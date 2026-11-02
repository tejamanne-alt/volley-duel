using System;
using Duel.Core.Sim;

namespace Duel.Core.AI
{
    /// <summary>A CPU player: turns what it sees into one controller input per tick.</summary>
    public interface IController<TState> where TState : struct
    {
        FrameInput Decide(in TState state);
    }

    /// <summary>How the CPU spends its meter, from Rookie (rarely) to Legend (optimal).</summary>
    public enum SurgePolicy : byte
    {
        Rarely,
        WhenFull,
        WithPurpose,
        SavesForDenials,
        Optimal,
    }

    /// <summary>
    /// One difficulty tier. Spec columns: reaction, landing read, execution, reads you, learns habits,
    /// Surge use. Values are Fix64 so CPU behavior is reproducible on every machine.
    /// </summary>
    public struct TierProfile
    {
        public string Name;
        /// <summary>Frames between something appearing on screen and the CPU reacting to it.</summary>
        public int ReactionFrames;
        /// <summary>Error in judging the landing point (meters), shrinking to zero as the ball falls.</summary>
        public Fix64 LandingReadError;
        /// <summary>Standard deviation of press timing, in frames.</summary>
        public Fix64 ExecutionSigma;
        /// <summary>Chance of picking the counter to the predicted opponent choice.</summary>
        public Fix64 ReadsYou;
        public bool LearnsHabits;
        public SurgePolicy Surge;

        /// <summary>Linear blend between two tiers (Rival mode); t in [0, 1].</summary>
        public static TierProfile Lerp(TierProfile a, TierProfile b, Fix64 t)
        {
            t = Fix64.Clamp01(t);
            return new TierProfile
            {
                Name = t < Fix64.Half ? a.Name : b.Name,
                ReactionFrames = Fix64.Lerp(a.ReactionFrames, b.ReactionFrames, t).RoundToInt(),
                LandingReadError = Fix64.Lerp(a.LandingReadError, b.LandingReadError, t),
                ExecutionSigma = Fix64.Lerp(a.ExecutionSigma, b.ExecutionSigma, t),
                ReadsYou = Fix64.Lerp(a.ReadsYou, b.ReadsYou, t),
                LearnsHabits = t < Fix64.Half ? a.LearnsHabits : b.LearnsHabits,
                Surge = t < Fix64.Half ? a.Surge : b.Surge,
            };
        }
    }

    /// <summary>
    /// Ring buffer of past states, so the CPU acts on the world as it looked
    /// <see cref="TierProfile.ReactionFrames"/> ago, never on the present.
    /// </summary>
    public sealed class ReactionBuffer<TState> where TState : struct
    {
        readonly TState[] ring;
        int head;
        int count;

        public ReactionBuffer(int capacity)
        {
            ring = new TState[Math.Max(2, capacity + 1)];
        }

        public int Capacity => ring.Length - 1;

        public void Push(in TState state)
        {
            head = (head + 1) % ring.Length;
            ring[head] = state;
            if (count < ring.Length) count++;
        }

        /// <summary>The state from <paramref name="framesAgo"/> pushes ago (clamped to the oldest kept).</summary>
        public ref readonly TState Delayed(int framesAgo)
        {
            if (count == 0) throw new InvalidOperationException("ReactionBuffer is empty");
            if (framesAgo >= count) framesAgo = count - 1;
            if (framesAgo < 0) framesAgo = 0;
            int index = (head - framesAgo) % ring.Length;
            if (index < 0) index += ring.Length;
            return ref ring[index];
        }

        public void Clear()
        {
            count = 0;
            head = 0;
        }
    }

    /// <summary>
    /// Rolling counts of an opponent's choices per context (set type, depth, score bucket...), with
    /// exponential decay so recent habits weigh more. Fixed-size, allocation-free, deterministic.
    /// </summary>
    public sealed class HabitModel
    {
        readonly Fix64[] counts;
        readonly int contexts;
        readonly int choices;
        readonly Fix64 decay;

        public HabitModel(int contexts, int choices, Fix64 decay)
        {
            this.contexts = contexts;
            this.choices = choices;
            this.decay = decay;
            counts = new Fix64[contexts * choices];
        }

        public int Contexts => contexts;

        public int Choices => choices;

        public void Record(int context, int choice)
        {
            if ((uint)context >= (uint)contexts || (uint)choice >= (uint)choices) return;
            int row = context * choices;
            for (int c = 0; c < choices; c++) counts[row + c] = counts[row + c] * decay;
            counts[row + choice] += Fix64.One;
        }

        public Fix64 Count(int context, int choice) => counts[context * choices + choice];

        public Fix64 Total(int context)
        {
            Fix64 t = Fix64.Zero;
            for (int c = 0; c < choices; c++) t += counts[context * choices + c];
            return t;
        }

        /// <summary>Most frequent choice in a context, or -1 when nothing is known yet.</summary>
        public int MostLikely(int context, Fix64 minimumEvidence)
        {
            if (Total(context) < minimumEvidence) return -1;
            int best = 0;
            for (int c = 1; c < choices; c++)
            {
                if (counts[context * choices + c] > counts[context * choices + best]) best = c;
            }
            return best;
        }

        /// <summary>Share of a choice in a context (0 when unknown).</summary>
        public Fix64 Share(int context, int choice)
        {
            Fix64 t = Total(context);
            return t.IsZero ? Fix64.Zero : Count(context, choice) / t;
        }

        public void Clear() => Array.Clear(counts, 0, counts.Length);
    }

    /// <summary>Utility scoring: pick the option with the highest score after seeded noise.</summary>
    public static class Utility
    {
        public static int Pick(ReadOnlySpan<Fix64> scores, ref Rng rng, Fix64 noise)
        {
            int best = -1;
            Fix64 bestScore = Fix64.MinValue;
            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i] == Fix64.MinValue) continue; // unavailable option
                Fix64 v = scores[i] + (noise.IsZero ? Fix64.Zero : rng.Symmetric(noise));
                if (v > bestScore)
                {
                    bestScore = v;
                    best = i;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Rival mode: nudges a blend between tiers after each set so the player's win rate drifts toward
    /// a target (spec: 50%).
    /// </summary>
    public sealed class RivalAdjuster
    {
        readonly Fix64 step;
        public Fix64 Blend { get; private set; }

        public RivalAdjuster(Fix64 initialBlend, Fix64 step)
        {
            Blend = Fix64.Clamp01(initialBlend);
            this.step = step;
        }

        /// <summary>Call after each set with the player's margin (positive = player won by that many).</summary>
        public void AfterSet(int playerMargin)
        {
            if (playerMargin > 0) Blend = Fix64.Clamp01(Blend + step * Math.Min(playerMargin, 4) / 2);
            else if (playerMargin < 0) Blend = Fix64.Clamp01(Blend - step * Math.Min(-playerMargin, 4) / 2);
        }
    }
}
