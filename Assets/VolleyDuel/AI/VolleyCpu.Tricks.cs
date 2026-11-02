using Duel.Core.AI;
using Duel.Core.Sim;
using VolleyDuel.Sim;

namespace VolleyDuel.AI
{
    /// <summary>CPU use of Signature Tricks, Finishers and Denials, shaped by the tier's Surge policy.</summary>
    public sealed partial class VolleyCpu
    {
        int denialKey = Never;
        int finisherKey = Never;

        /// <summary>Spend a bar on the Amped version? Spec Surge use: Rookie rarely spends ... Legend optimal.</summary>
        bool WantAmp(in GameState seen)
        {
            int surge = seen.FighterAt(me).Surge;
            int bar = T.Meters.CostAmped;
            if (surge < bar) return false;
            int oppSurge = seen.FighterAt(opp).Surge;
            switch (Tier.Surge)
            {
                case SurgePolicy.Rarely: return false;
                case SurgePolicy.WhenFull: return surge >= T.Meters.SurgeMax;
                case SurgePolicy.WithPurpose: return surge >= 2 * bar || IsBigPoint(seen);
                case SurgePolicy.SavesForDenials: return surge - bar >= bar || oppSurge < 2 * bar;
                default: return (surge - bar >= bar || oppSurge < 2 * bar) && surge < T.Meters.CostFinisher;
            }
        }

        bool IsBigPoint(in GameState s) => T.Scoring.SetPoint(s.Score(me), s.Score(opp)) || T.Scoring.SetPoint(s.Score(opp), s.Score(me));

        bool WantFinisher(in GameState seen)
        {
            switch (Tier.Surge)
            {
                case SurgePolicy.Rarely: return rng.Chance(Fix64.FromRatio(3, 10));
                case SurgePolicy.Optimal:
                    // Fire into an opponent who cannot deny; otherwise mix it up so the Denial must be earned.
                    return seen.FighterAt(opp).Surge < T.Meters.CostDenial || rng.Chance(Fix64.Half);
                default: return true;
            }
        }

        bool TryFinisherNow(in GameState seen, int next)
        {
            if (finisherKey == seen.RallyStartWorld) return false;
            if (!sim.FinisherReady(seen, me)) return false;
            finisherKey = seen.RallyStartWorld;
            if (!WantFinisher(seen)) return false;
            finisherPress = next;
            return true;
        }

        bool TryServeSpecial(ref GameState seen, int next)
        {
            if (TryFinisherNow(seen, next)) return true;
            if (serveJump && def.Has(TrickKind.Afterburner) && rng.Chance(persona.Trick))
            {
                trickPress = next;
                trickAmp = WantAmp(seen);
                return true;
            }
            return false;
        }

        /// <summary>Airborne with a spike ring: Pipe Shot from deep, Kalari Twist, or the Overpass Hammer.</summary>
        bool TryAttackTrick(ref GameState seen, int next, int press)
        {
            FighterState m = seen.FighterAt(me);
            TrickKind kind = TrickKind.None;
            if (def.Has(TrickKind.PipeShot) && Fix64.Abs(m.Pos.X) >= def.Trick(TrickKind.PipeShot).MinFromNet) kind = TrickKind.PipeShot;
            else if (def.Has(TrickKind.OverpassHammer) && sim.IsOverpassChance(seen, me, m.RingPoint.X)) kind = TrickKind.OverpassHammer;
            else if (def.Has(TrickKind.KalariTwist) && rng.Chance(persona.Trick)) kind = TrickKind.KalariTwist;
            if (kind == TrickKind.None) return false;
            trickPress = press;
            trickAmp = WantAmp(seen);
            return true;
        }

        /// <summary>Planning a block: Shockwave Block, Commit Block, or Arjun's Decoy instead of a fake hop.</summary>
        bool TryDefenseTrick(ref GameState seen, int next, int press)
        {
            FighterState m = seen.FighterAt(me);
            TrickDefinition t = def.Defense;
            if (!rng.Chance(persona.Trick)) return false;
            bool amp = WantAmp(seen);
            TrickDefinition used = amp ? t.Amped : t;
            switch (t.Kind)
            {
                case TrickKind.ShockwaveBlock:
                    trickPress = press - (used.Startup - T.Jump.BlockStartupFrames);
                    break;
                case TrickKind.CommitBlock:
                    trickPress = press - (used.Startup - T.Jump.BlockStartupFrames) - used.HangFrames / 2;
                    break;
                case TrickKind.Decoy:
                    trickPress = m.BlockRingCenter - 22;
                    defense = DefensePlan.Dig;
                    break;
                default:
                    return false;
            }
            if (trickPress < next) trickPress = next;
            trickAmp = amp;
            return true;
        }

        /// <summary>Rin's counters on an incoming attack: Ink Return when standing, Sweep when diving.</summary>
        bool TryDigTrick(ref GameState seen, int next, bool needDive)
        {
            if (!rng.Chance(persona.Trick)) return false;
            FighterState m = seen.FighterAt(me);
            bool attack = seen.LastSendKind == TouchKind.Spike || seen.LastSendKind == TouchKind.Roll;
            if (needDive && def.Has(TrickKind.Sweep)) { }
            else if (!needDive && attack && def.Has(TrickKind.InkReturn)) { }
            else return false;
            trickPress = m.RingCenter + rng.Gaussian(Tier.ExecutionSigma).RoundToInt();
            if (trickPress < next) trickPress = next;
            trickAmp = WantAmp(seen);
            return true;
        }

        /// <summary>Set tricks: Quick Stroke (Rin) and Blind Set (Arjun) replace HIT on the set ring.</summary>
        bool TrySetTrick(ref GameState seen, int next)
        {
            if (!def.Has(TrickKind.QuickStroke) && !def.Has(TrickKind.BlindSet)) return false;
            if (!rng.Chance(persona.Trick)) return false;
            FighterState m = seen.FighterAt(me);
            trickPress = m.RingCenter + rng.Gaussian(Tier.ExecutionSigma).RoundToInt();
            if (trickPress < next) trickPress = next;
            trickAmp = WantAmp(seen);
            return true;
        }

        void ThinkFinisher(ref GameState seen, int next)
        {
            FighterState m = seen.FighterAt(me);
            if (seen.Fin.User == me || m.Ring != RingKind.Denial) return;
            if (denialKey == m.RingCenter) return;
            denialKey = m.RingCenter;
            if (m.Surge < T.Meters.CostDenial) return;
            int press = m.RingCenter + rng.Gaussian(Tier.ExecutionSigma * K.DenialTimingScale).RoundToInt();
            finisherPress = press < next ? next : press;
        }
    }
}
