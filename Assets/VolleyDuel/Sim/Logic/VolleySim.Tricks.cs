using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    /// <summary>What a fighter committed to, for feint bait checks.</summary>
    public enum Commitment : byte
    {
        Block,
        Dive,
        Tip,
    }

    /// <summary>
    /// Signature Tricks, Finishers, Denial and jousts. One TRICK button carries a fighter's two offensive
    /// tricks and one defensive trick: the context decides which fires. Swiping up (AMP with TRICK)
    /// spends 100 Surge for the Amped version (spec: -3 frames start-up plus one added property).
    /// </summary>
    public sealed partial class VolleySim
    {
        // ------------------------------------------------------------------ feints

        /// <summary>
        /// Spec: fake spikes, fake sets and fake blocks each have a bait window. A commitment that starts
        /// inside the opponent's bait window bites: -15 Stamina and 8 extra frames of landing recovery.
        /// </summary>
        void CheckBite(ref GameState s, int committer, Commitment commitment)
        {
            int other = 1 - committer;
            ref FighterState feinter = ref GameState.Fighter(ref s, other);
            if (feinter.Bait == FeintKind.None) return;
            if (s.World < feinter.BaitStart || s.World > feinter.BaitEnd) return;
            bool bites;
            switch (commitment)
            {
                case Commitment.Block: bites = feinter.Bait == FeintKind.Tip || feinter.Bait == FeintKind.Dump; break;
                case Commitment.Dive: bites = feinter.Bait == FeintKind.Dump; break;
                default: bites = feinter.Bait == FeintKind.FakeBlock || feinter.Bait == FeintKind.Decoy; break;
            }
            if (!bites) return;
            FeintKind kind = feinter.Bait;
            feinter.Bait = FeintKind.None;
            ref FighterState f = ref GameState.Fighter(ref s, committer);
            int extra = f.ExtraRecovery + T.Feint.BiteRecovery;
            f.ExtraRecovery = (byte)(extra > 255 ? 255 : extra);
            SpendStamina(ref s, committer, Fix64.FromInt(T.Feint.BiteStamina));
            Emit(VolleyEvent.Bite, committer, (int)kind, 0, f.Pos.X, f.Pos.Y);
            GainSurge(ref s, other, T.Meters.GainBiteDrawn);
        }

        // ------------------------------------------------------------------ trick plumbing

        /// <summary>The trick definition (Amped when requested) if this fighter owns it.</summary>
        public TrickDefinition TrickFor(int player, TrickKind kind, bool amped)
        {
            if (kind == TrickKind.None) return null;
            TrickDefinition t = Def(player).Trick(kind);
            if (t == null) return null;
            return amped ? t.Amped : t;
        }

        bool WantsAmp(in FighterState f)
        {
            if (f.Input.IsHeld(InputButtons.Amp)) return true;
            int t = f.Input.PressFrame(InputButtons.Trick);
            int a = f.Input.PressFrame(InputButtons.Amp);
            if (t == InputTracker.Never || a == InputTracker.Never) return false;
            int d = t - a;
            return d <= T.Tricks.AmpWindow && d >= -T.Tricks.AmpWindow;
        }

        /// <summary>Amped if requested and affordable; spends the Surge and emits the trick event.</summary>
        bool BeginTrick(ref GameState s, int i, TrickKind kind, bool requestAmp)
        {
            bool amped = requestAmp && s.FighterAt(i).Surge >= T.Meters.CostAmped && SpendSurge(ref s, i, T.Meters.CostAmped);
            ref FighterState f = ref GameState.Fighter(ref s, i);
            f.Input.Consume(InputButtons.Trick);
            f.ActiveTrick = kind;
            f.ActiveTrickAmped = amped;
            Emit(VolleyEvent.Trick, i, (int)kind, amped ? 1 : 0, f.Pos.X, f.Pos.Y);
            return amped;
        }

        bool AffordsAmp(in GameState s, int i) => s.FighterAt(i).Surge >= T.Meters.CostAmped;

        // ------------------------------------------------------------------ serve trick

        bool TryServeTrick(ref GameState s, int i)
        {
            FighterDefinition d = Def(i);
            if (!d.Has(TrickKind.Afterburner)) return false;
            bool amped = BeginTrick(ref s, i, TrickKind.Afterburner, WantsAmp(s.FighterAt(i)));
            JumpToss(ref s, i, TrickFor(i, TrickKind.Afterburner, amped), amped);
            return true;
        }

        // ------------------------------------------------------------------ grounded tricks

        bool TryGroundTrick(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            bool wantAmp = WantsAmp(f);
            int touch = NextTouch(s, i);

            // Incoming ball: Rin's Ink Return (counter-dig) and Sweep (3 m slide).
            if (f.Ring == RingKind.Bump && touch == 1)
            {
                if (d.Has(TrickKind.InkReturn) && IsAttackTouch(s.LastSendKind) && f.RingReachable)
                {
                    return TryCommitGround(ref s, i, InputButtons.Trick, TrickKind.InkReturn, wantAmp && AffordsAmp(s, i));
                }
                if (d.Has(TrickKind.Sweep) && !f.RingReachable) return TryCommitSweep(ref s, i, wantAmp);
            }

            if (IsDefending(s, i))
            {
                TrickDefinition def = d.Defense;
                switch (def.Kind)
                {
                    case TrickKind.ShockwaveBlock:
                    case TrickKind.CommitBlock:
                        {
                            if (!NearNet(f)) return false;
                            bool amped = BeginTrick(ref s, i, def.Kind, wantAmp);
                            TrickDefinition used = amped ? def.Amped : def;
                            StartJump(ref s, i, def.Kind == TrickKind.CommitBlock ? JumpKind.CommitBlock : JumpKind.ShockwaveBlock, used);
                            ref FighterState g = ref GameState.Fighter(ref s, i);
                            g.ActiveTrick = def.Kind;
                            g.ActiveTrickAmped = amped;
                            g.LandInDigStance = used.Has(MoveFlags.LandInDigStance);
                            return true;
                        }
                    case TrickKind.Decoy:
                        {
                            bool amped = BeginTrick(ref s, i, TrickKind.Decoy, wantAmp);
                            TrickDefinition used = amped ? def.Amped : def;
                            ref FighterState g = ref GameState.Fighter(ref s, i);
                            g.DecoyUntil = s.World + used.DecoyFrames;
                            g.Bait = FeintKind.Decoy;
                            g.BaitStart = s.World;
                            g.BaitEnd = g.DecoyUntil;
                            Emit(VolleyEvent.Decoy, i, 0, used.DecoyFrames, VolleySim.FromNet(i, Fix64.FromRatio(4, 10)), Fix64.Zero);
                            return true;
                        }
                }
                return false;
            }

            // Our ball.
            if (f.Ring == RingKind.Set && touch == 2)
            {
                if (d.Has(TrickKind.QuickStroke)) return TryCommitGround(ref s, i, InputButtons.Trick, TrickKind.QuickStroke, wantAmp && AffordsAmp(s, i));
                if (d.Has(TrickKind.BlindSet)) return TryCommitGround(ref s, i, InputButtons.Trick, TrickKind.BlindSet, wantAmp && AffordsAmp(s, i));
            }
            if (d.Has(TrickKind.Slide) && touch == 3 && s.LastToucher == i && s.Ball.Mode == BallMode.Flight)
            {
                bool amped = BeginTrick(ref s, i, TrickKind.Slide, wantAmp);
                StartJump(ref s, i, JumpKind.Slide, TrickFor(i, TrickKind.Slide, amped));
                ref FighterState g = ref GameState.Fighter(ref s, i);
                g.ActiveTrick = TrickKind.Slide;
                g.ActiveTrickAmped = amped;
                return true;
            }
            return false;
        }

        /// <summary>Rin's Sweep: a dive with a 3 m reach at double speed; Amped pops up and counts as Good at worst.</summary>
        bool TryCommitSweep(ref GameState s, int i, bool wantAmp)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            TrickDefinition t = Def(i).Defense;
            int press = f.Input.PressFrame(InputButtons.Trick);
            TimingGrade grade = Grade(press - f.RingCenter, f.RingPerfect, f.RingGood);
            if (grade == TimingGrade.None) return false;
            Fix64 dist = Fix64.Abs(f.RingPoint.X - f.Pos.X);
            int framesLeft = f.RingCenter - s.World;
            if (framesLeft < 0) framesLeft = 0;
            Fix64 range = f.Gassed ? t.Range * T.Meters.GassedDiveScale : t.Range;
            Fix64 cover = T.Dive.Speed * 2 * T.Dt * framesLeft;
            if (dist > T.Reach.BumpReach + range || dist - T.Reach.BumpReach > cover) return false;
            bool amped = BeginTrick(ref s, i, TrickKind.Sweep, wantAmp);
            BeginCommit(ref s, i, NextTouch(s, i) == 3 ? TouchKind.Save : TouchKind.Bump, press, grade);
            ref FighterState g = ref GameState.Fighter(ref s, i);
            g.CommitTrick = TrickKind.Sweep;
            g.CommitAmped = amped;
            g.DiveTargetX = g.RingPoint.X;
            g.Action = FighterAction.Diving;
            g.ActionStart = s.World;
            g.ActionEnd = g.CommitContact + 2;
            SpendStamina(ref s, i, T.Meters.CostDive);
            Emit(VolleyEvent.Dive, i, 1, 0, g.RingPoint.X, g.RingPoint.Y);
            CheckBite(ref s, i, Commitment.Dive);
            return true;
        }

        /// <summary>Slide take-off: drifts toward the net and stops just short of it.</summary>
        Fix64 SlideDrift(ref GameState s, int i, Fix64 vy)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            TrickDefinition t = TrickFor(i, TrickKind.Slide, f.ActiveTrickAmped);
            if (t == null) return Fix64.Zero;
            int airFrames = 2 * (vy / T.GravityDt).CeilToInt();
            Fix64 room = Fix64.Abs(f.Pos.X) - T.Tricks.SlideNetMargin;
            Fix64 travel = Fix64.Clamp(t.Drift * t.DriftScale, Fix64.Zero, Fix64.Max(Fix64.Zero, room));
            // The slide moves the hitter's contact: the blocker's read is off by TimingShift frames,
            // and Amped hides the read until take-off.
            int opp = 1 - i;
            ref FighterState fb = ref GameState.Fighter(ref s, opp);
            fb.BlockRingCenter += t.TimingShift;
            if (t.Has(MoveFlags.HideTelegraph)) fb.BlockRingReveal = s.World;
            return travel / (T.Dt * airFrames) * Dir(i);
        }

        // ------------------------------------------------------------------ airborne tricks

        bool TryAirTrick(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            if (f.Ring != RingKind.Spike) return false;
            TrickKind kind = TrickKind.None;
            if (d.Has(TrickKind.PipeShot) && Fix64.Abs(f.Pos.X) >= d.Trick(TrickKind.PipeShot).MinFromNet) kind = TrickKind.PipeShot;
            else if (d.Has(TrickKind.KalariTwist)) kind = TrickKind.KalariTwist;
            else if (d.Has(TrickKind.OverpassHammer) && IsOverpassChance(s, i, f.RingPoint.X)) kind = TrickKind.OverpassHammer;
            if (kind == TrickKind.None) return false;
            int press = f.Input.PressFrame(InputButtons.Trick);
            if (Grade(press - f.RingCenter, f.RingPerfect, f.RingGood) == TimingGrade.None) return false;
            bool amped = BeginTrick(ref s, i, kind, WantsAmp(f));
            f.Charging = false;
            CommitAttack(ref s, i, AttackChoice.Spike, press, TrickFor(i, kind, amped), amped);
            return true;
        }

        /// <summary>A ball the opponent sent back on their first touch, tight to the net on this side.</summary>
        public bool IsOverpassChance(in GameState s, int i, Fix64 contactX)
        {
            TrickDefinition t = Def(i).Trick(TrickKind.OverpassHammer);
            if (t == null) return false;
            bool overpass = s.Ball.Has(BallFlags.Overpass) || (s.LastSender == 1 - i && s.LastSendKind == TouchKind.Bump && s.TouchesSinceCross == 0);
            return overpass && Fix64.Abs(contactX) <= t.MaxFromNet;
        }

        /// <summary>Kalari Twist (Amped): for a few frames the defender sees a deep swing's flight instead of the real one.</summary>
        void StartDecoyFlight(ref GameState s, int i, TrickDefinition trick, Fix64 speed)
        {
            var deep = new FixVec2(IntoOpponent(i, T.Tricks.DecoyFakeSpeedDeep), T.Court.BallRadius);
            s.Ball.DecoyVel = AimOverNet(s.Ball.Pos, deep, speed, T.Attack.NetClearance, i);
            s.Ball.DecoyUntil = s.World + trick.DecoyFrames;
            Emit(VolleyEvent.Decoy, i, 1, trick.DecoyFrames, s.Ball.Pos.X, s.Ball.Pos.Y);
        }

        // ------------------------------------------------------------------ block trick effects

        void ApplyBlockTrickEffects(ref GameState s, int blocker, int hitter, BlockOutcome outcome, int delta)
        {
            FighterState fb = s.FighterAt(blocker);
            if (fb.ActiveTrick != TrickKind.ShockwaveBlock) return;
            TrickDefinition t = TrickFor(blocker, TrickKind.ShockwaveBlock, fb.ActiveTrickAmped);
            ref FighterState fh = ref GameState.Fighter(ref s, hitter);
            // Spec: the shock ring knocks the hitter back on landing, delaying their next action.
            fh.PendingKnockback = (byte)(t.KnockbackFrames > 255 ? 255 : t.KnockbackFrames);
            if (t.IsAmped && outcome == BlockOutcome.Stuff && t.StaminaDrain.IsPositive)
            {
                // Amped: a Perfect block also drains 25 of their Stamina.
                fh.Stamina = Fix64.Max(Fix64.Zero, fh.Stamina - t.StaminaDrain);
                if (fh.Stamina.IsZero && !fh.Gassed)
                {
                    fh.Gassed = true;
                    Emit(VolleyEvent.Gassed, hitter);
                }
            }
        }

        // ------------------------------------------------------------------ jousts

        static bool IsAirAttack(in FighterState f) => f.Action == FighterAction.Airborne && (f.CommitKind == TouchKind.Spike || f.CommitKind == TouchKind.Tip || f.CommitKind == TouchKind.Roll);

        /// <summary>Both fighters contacting the ball above the net within a couple of frames: JOUST.</summary>
        bool TryJoust(ref GameState s)
        {
            FighterState a = s.F0, b = s.F1;
            if (!IsAirAttack(a) || !IsAirAttack(b)) return false;
            int first = a.CommitContact < b.CommitContact ? a.CommitContact : b.CommitContact;
            if (s.World != first) return false;
            int gap = a.CommitContact - b.CommitContact;
            if (gap < 0) gap = -gap;
            if (gap > T.Joust.FrameWindow) return false;
            if (Fix64.Abs(s.Ball.Pos.X) > T.Joust.NetWindow || s.Ball.Pos.Y < T.Court.NetHeight) return false;

            int winner;
            bool aWins = WinsJousts(a, 0), bWins = WinsJousts(b, 1);
            if (aWins != bWins) winner = aWins ? 0 : 1;
            else
            {
                int sa = JoustScore(ref s, 0), sb = JoustScore(ref s, 1);
                winner = sa == sb ? s.MatchRng.NextInt(2) : (sa > sb ? 0 : 1);
            }
            int loser = 1 - winner;
            FixVec2 at = s.Ball.Pos;
            Ballistics.SolveSpeed(at, new FixVec2(IntoOpponent(winner, Fix64.One), T.Court.BallRadius), T.Joust.Speed, T.Ball.Gravity, true, out FixVec2 v);
            Launch(ref s, winner, v, BallFlags.Attack | BallFlags.Unblockable, BallSpin.None);
            for (int i = 0; i < 2; i++)
            {
                ref FighterState f = ref GameState.Fighter(ref s, i);
                ClearCommit(ref f);
                f.Ring = RingKind.None;
            }
            s.Possession = (byte)winner;
            s.Touches = (byte)NextTouch(s, winner);
            s.LastToucher = (byte)winner;
            s.LastTouch = TouchKind.Joust;
            s.LastAttacker = (byte)winner;
            s.LastContactWorld = s.World;
            Emit(VolleyEvent.Contact, winner, (int)TouchKind.Joust, (int)TimingGrade.Perfect, at.X, at.Y);
            GainSurge(ref s, winner, T.Meters.GainJoust);
            Impact(ref s, Callout.Joust, winner, at);
            return true;
        }

        bool WinsJousts(in FighterState f, int i)
        {
            if (f.CommitTrick != TrickKind.OverpassHammer || !f.CommitAmped) return false;
            TrickDefinition t = TrickFor(i, TrickKind.OverpassHammer, true);
            return t != null && t.Has(MoveFlags.WinsJousts);
        }

        int JoustScore(ref GameState s, int i)
        {
            FighterDefinition d = Def(i);
            TimingGrade g = s.FighterAt(i).CommitGrade;
            int timing = g == TimingGrade.Perfect ? 3 : (g == TimingGrade.Good ? 1 : 0);
            return d.Power + d.Jump + timing + s.MatchRng.NextInt(4);
        }
    }
}
