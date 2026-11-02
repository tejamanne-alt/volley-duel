using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    public sealed partial class VolleySim
    {
        /// <summary>The ball touched the floor: in or out, attribute the point.</summary>
        void BallLanded(ref GameState s)
        {
            ref BallState b = ref s.Ball;
            Fix64 x = b.Pos.X;
            b.Pos.Y = T.Court.BallRadius;
            // Spec: a ball touching the line is in.
            bool inBounds = Fix64.Abs(x) <= T.Court.HalfLength + T.Court.BallRadius;
            int sideLanded = SideOf(x);
            Emit(VolleyEvent.BallLanded, s.LastToucher, inBounds ? 1 : 0, 0, x, b.Pos.Y);

            int winner;
            if (inBounds) winner = 1 - sideLanded;
            else winner = s.LastToucher <= 1 ? 1 - s.LastToucher : 1 - sideLanded;
            int loser = 1 - winner;

            PointReason reason = PointReason.Landed;
            Callout callout = Callout.None;
            bool winnerSent = s.LastSender == winner;
            bool loserFailedFirstTouch = s.LastToucher == loser && s.TouchesSinceCross <= 1 && winnerSent;
            TouchKind send = s.LastSendKind;
            if (s.LastTouch == TouchKind.Block && s.LastToucher == loser && !inBounds)
            {
                reason = PointReason.Tooled; // off the blocker's hands and out
            }
            else if (s.LastTouch == TouchKind.Block && s.LastToucher == winner && inBounds)
            {
                reason = PointReason.Stuff;
            }
            else if (s.LastToucher == winner && inBounds)
            {
                if (IsServeTouch(s.LastTouch)) reason = PointReason.Ace;
                else if (IsAttackTouch(s.LastTouch)) reason = PointReason.Kill;
                else if (s.LastTouch == TouchKind.Joust) reason = PointReason.Joust;
            }
            else if (loserFailedFirstTouch)
            {
                if (IsServeTouch(send)) reason = PointReason.Ace;
                else if (IsAttackTouch(send)) reason = PointReason.Kill;
                else reason = PointReason.Error;
            }
            else if (s.LastToucher == loser)
            {
                reason = PointReason.Error;
            }

            if (reason == PointReason.Ace)
            {
                callout = Callout.Ace;
                GainSurge(ref s, winner, T.Meters.GainAce);
            }
            else if (reason == PointReason.Kill || reason == PointReason.Tooled)
            {
                callout = reason == PointReason.Kill ? Callout.Kill : Callout.None;
                GainSurge(ref s, winner, T.Meters.GainKill);
            }
            b.Mode = BallMode.Dead;
            b.Vel = new FixVec2(b.Vel.X * Fix64.FromRatio(6, 10), -b.Vel.Y * T.Ball.FloorRestitution);
            AwardPoint(ref s, winner, reason, 1);
            if (callout != Callout.None) Impact(ref s, callout, winner, b.Pos);
        }

        void Fault(ref GameState s, int faulter, FaultKind kind)
        {
            Emit(VolleyEvent.Fault, faulter, (int)kind, 0, s.FighterAt(faulter).Pos.X, s.FighterAt(faulter).Pos.Y);
            if (s.Ball.Mode == BallMode.Flight || s.Ball.Mode == BallMode.Held) s.Ball.Mode = BallMode.Dead;
            AwardPoint(ref s, 1 - faulter, PointReason.Fault, 1);
        }

        /// <summary>Scores a rally (or a Finisher) and moves to the dead ball.</summary>
        void AwardPoint(ref GameState s, int winner, PointReason reason, int points)
        {
            int loser = 1 - winner;
            int score = s.Score(winner) + points;
            if (score > T.Scoring.SetCap) score = T.Scoring.SetCap;
            s.SetScore(winner, score);
            s.LastPointWinner = (byte)winner;
            s.LastPointReason = reason;
            Emit(VolleyEvent.Point, winner, (int)reason, points);

            // Spec: On a Run — three straight points arm the next attack; breaking a run pays a full bar.
            if (s.Streak(loser) >= T.OnRun.Streak)
            {
                GainSurge(ref s, winner, T.OnRun.BreakerSurge);
                Emit(VolleyEvent.RunBroken, winner, T.OnRun.BreakerSurge);
            }
            s.SetStreak(loser, 0);
            s.SetRunArmed(loser, false);
            int streak = s.Streak(winner) + 1;
            s.SetStreak(winner, streak > 250 ? 250 : streak);
            if (streak >= T.OnRun.Streak)
            {
                s.SetRunArmed(winner, true);
                if (streak == T.OnRun.Streak) Emit(VolleyEvent.OnARun, winner);
            }
            GainSurge(ref s, loser, T.Meters.GainScoredOn);

            // Spec: Stamina refills 40 at each dead ball; the rally winner serves next.
            RefillStamina(ref s, 0, T.Meters.RefillDeadBall);
            RefillStamina(ref s, 1, T.Meters.RefillDeadBall);
            s.Server = (byte)winner;
            s.RallyIndex++;
            if (s.SlowFrames == 0 && s.SlowDiv > 1) s.SlowDiv = 1;
            for (int i = 0; i < 2; i++)
            {
                ref FighterState f = ref GameState.Fighter(ref s, i);
                f.Ring = RingKind.None;
                f.Charging = false;
            }
            SetPhase(ref s, MatchPhase.DeadBall);
        }

        void AfterDeadBall(ref GameState s)
        {
            int w = s.LastPointWinner;
            if (w <= 1 && T.Scoring.SetWon(s.Score(w), s.Score(1 - w)))
            {
                s.AddSetWon(w);
                Emit(VolleyEvent.SetWon, w, s.SetIndex, s.SetsWon(w));
                if (s.SetsWon(w) >= setsToWin)
                {
                    s.Winner = (byte)w;
                    SetPhase(ref s, MatchPhase.MatchOver);
                    Emit(VolleyEvent.MatchWon, w, s.SetsWon0, s.SetsWon1);
                    return;
                }
                SetPhase(ref s, MatchPhase.SetBreak);
                return;
            }
            BeginServe(ref s);
        }
    }
}
