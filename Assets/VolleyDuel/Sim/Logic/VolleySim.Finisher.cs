using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    /// <summary>
    /// Signature Finishers: 3 Surge bars, each fighter's own trigger moment, a 120-180 frame cinematic
    /// worth 2 points. A defender holding at least 1 bar gets one Denial ring; hitting it digs the
    /// Finisher up and the rally continues as a free ball on the defender's side.
    /// </summary>
    public sealed partial class VolleySim
    {
        /// <summary>Whether this fighter's Finisher could fire right now (for HUD lighting and the CPU).</summary>
        public bool FinisherReady(in GameState s, int i)
        {
            FighterState f = s.FighterAt(i);
            if (f.Surge < T.Meters.CostFinisher) return false;
            FinisherDefinition fin = Def(i).Finisher;
            switch (fin.Trigger)
            {
                case FinisherTrigger.OwnServe:
                    return s.Phase == MatchPhase.PreServe && f.Action == FighterAction.ServeHold;
                case FinisherTrigger.AfterOwnBlock:
                    return s.Phase == MatchPhase.Rally && f.BlockedLast && s.Ball.Mode == BallMode.Flight && SideOf(s.Ball.Pos.X) == i && f.Ring != RingKind.None;
                case FinisherTrigger.PerfectDig:
                    return s.Phase == MatchPhase.Rally && s.World - f.PerfectDigFrame <= T.Finisher.IppitsuWindow;
                case FinisherTrigger.SecondTouch:
                    return s.Phase == MatchPhase.Rally && NextTouch(s, i) == 2 && f.Ring == RingKind.Set && s.LastToucher == i;
            }
            return false;
        }

        bool TryFinisher(ref GameState s, int i)
        {
            if (!FinisherReady(s, i)) return false;
            ref FighterState f = ref GameState.Fighter(ref s, i);
            f.Input.Consume(InputButtons.Finisher);
            StartFinisher(ref s, i);
            return true;
        }

        void StartFinisher(ref GameState s, int i)
        {
            FinisherDefinition fin = Def(i).Finisher;
            SpendSurge(ref s, i, T.Meters.CostFinisher);
            FixVec2 from = s.Ball.Mode == BallMode.Held ? HandPosition(ref s, i) : s.Ball.Pos;
            int jitter = s.MatchRng.Range(-T.Finisher.DenialJitter, T.Finisher.DenialJitter + 1);
            int denialStart = s.World + fin.DenialStart + jitter;
            if (denialStart < s.World + T.Finisher.DenialLead + 1) denialStart = s.World + T.Finisher.DenialLead + 1;
            if (denialStart + fin.DenialFrames >= s.World + fin.Frames) denialStart = s.World + fin.Frames - fin.DenialFrames - 1;
            s.Fin = new FinisherState
            {
                Kind = fin.Kind,
                User = (byte)i,
                Start = s.World,
                End = s.World + fin.Frames,
                DenialStart = denialStart,
                DenialEnd = denialStart + fin.DenialFrames - 1,
                From = from,
                To = new FixVec2(IntoOpponent(i, T.Finisher.TargetFromNet), T.Court.BallRadius),
            };
            SetPhase(ref s, MatchPhase.Finisher);
            if (s.SlowDiv > 1 && s.SlowFrames == 0) s.SlowDiv = 1;
            for (int k = 0; k < 2; k++)
            {
                ref FighterState f = ref GameState.Fighter(ref s, k);
                f.Action = FighterAction.Locked;
                f.Vel = FixVec2.Zero;
                f.Pos.Y = Fix64.Zero;
                f.Jump = JumpKind.None;
                ClearCommit(ref f);
                f.Ring = RingKind.None;
                f.Charging = false;
            }
            s.Ball.Mode = BallMode.Scripted;
            s.Ball.Pos = from;
            s.Ball.Vel = FixVec2.Zero;
            s.Ball.PendingBlock = BlockOutcome.None;
            Emit(VolleyEvent.FinisherStart, i, (int)fin.Kind, fin.Frames, from.X, from.Y);
        }

        void StepFinisher(ref GameState s)
        {
            ref FinisherState fin = ref s.Fin;
            int user = fin.User;
            int defender = 1 - user;
            int total = fin.End - fin.Start;
            int t = s.World - fin.Start;
            Fix64 u = Fix64.FromRatio(t, total);
            // Scripted flight for presentation: a high arc into the defender's court.
            Fix64 arc = 4 * T.Finisher.ScriptPeak * u * (Fix64.One - u);
            s.Ball.Pos = new FixVec2(Fix64.Lerp(fin.From.X, fin.To.X, u), Fix64.Lerp(fin.From.Y, fin.To.Y, u) + arc);

            ref FighterState fd = ref GameState.Fighter(ref s, defender);
            bool canDeny = fd.Surge >= T.Meters.CostDenial;
            if (canDeny && s.World == fin.DenialStart - T.Finisher.DenialLead)
            {
                // The Denial ring appears; its window is DenialStart..DenialEnd.
                fd.Ring = RingKind.Denial;
                fd.RingCenter = (fin.DenialStart + fin.DenialEnd) / 2;
                fd.RingPerfect = (byte)(fin.DenialEnd - fin.DenialStart + 1);
                fd.RingGood = fd.RingPerfect;
                fd.RingPoint = s.Ball.Pos;
                Emit(VolleyEvent.DenialWindow, defender, fin.DenialEnd - fin.DenialStart + 1, fin.DenialStart - s.World);
            }
            if (canDeny && !fin.Attempted && fd.Input.Buffered(InputButtons.Finisher, s.World, T.Rings.BufferFrames))
            {
                int press = fd.Input.PressFrame(InputButtons.Finisher);
                fd.Input.Consume(InputButtons.Finisher);
                if (press >= fin.Start)
                {
                    // Spec: one Denial ring per Finisher. The first DENY press is the attempt.
                    fin.Attempted = true;
                    if (press >= fin.DenialStart && press <= fin.DenialEnd)
                    {
                        Deny(ref s, defender);
                        return;
                    }
                }
            }
            if (s.World < fin.End) return;

            // The Finisher lands: 2 points.
            FinisherKind kind = fin.Kind;
            s.Ball.Mode = BallMode.Dead;
            s.Ball.Pos = fin.To;
            s.Ball.Vel = new FixVec2(Fix64.FromInt(Dir(user)), Fix64.FromInt(3));
            s.LastToucher = (byte)user;
            s.LastTouch = TouchKind.Finisher;
            Emit(VolleyEvent.FinisherScored, user, T.Scoring.FinisherPoints, (int)kind, fin.To.X, fin.To.Y);
            s.Fin = default;
            for (int k = 0; k < 2; k++) GameState.Fighter(ref s, k).Action = FighterAction.Neutral;
            fd.Ring = RingKind.None;
            AwardPoint(ref s, user, PointReason.Finisher, T.Scoring.FinisherPoints);
        }

        void Deny(ref GameState s, int defender)
        {
            int user = s.Fin.User;
            SpendSurge(ref s, defender, T.Meters.CostDenial);
            ref FighterState fd = ref GameState.Fighter(ref s, defender);
            ref FighterState fu = ref GameState.Fighter(ref s, user);
            Emit(VolleyEvent.Denied, defender, (int)s.Fin.Kind, 0, fd.Pos.X, fd.Pos.Y);
            s.Fin = default;
            SetPhase(ref s, MatchPhase.Rally);
            fd.Action = FighterAction.Recovery;
            fd.ActionEnd = s.World + T.Reach.TouchRecovery;
            fd.Ring = RingKind.None;
            fu.Action = FighterAction.Landing;
            fu.ActionEnd = s.World + T.Jump.LandingRecovery * 2;
            // Dug up: a free ball popping up on the defender's side; the dig was their first touch.
            var at = new FixVec2(fd.Pos.X + Fix64.FromRatio(2, 10) * Dir(defender), Fix64.One);
            s.Ball.Pos = at;
            Launch(ref s, defender, new FixVec2(-Fix64.FromRatio(1, 2) * Dir(defender), T.Finisher.DeniedPop), BallFlags.FreeBall, BallSpin.None);
            s.Possession = (byte)defender;
            s.Touches = 1;
            s.TouchesSinceCross = 1;
            s.LastToucher = (byte)defender;
            s.LastTouch = TouchKind.Bump;
            s.LastContactWorld = s.World;
            Impact(ref s, Callout.Denied, defender, at);
        }
    }
}
