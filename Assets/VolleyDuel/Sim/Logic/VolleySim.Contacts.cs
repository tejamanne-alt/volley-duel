using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    public sealed partial class VolleySim
    {
        // ================================================================== planning (rings)

        void UpdatePlans(ref GameState s)
        {
            UpdatePlan(ref s, 0);
            UpdatePlan(ref s, 1);
        }

        void UpdatePlan(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            if (f.Action == FighterAction.Committed || f.Action == FighterAction.Diving) return;
            if (f.CommitKind != TouchKind.None && f.CommitContact >= s.World) return;
            RingKind previous = f.Ring;
            f.Ring = RingKind.None;
            f.RingReachable = false;
            f.RingDive = false;
            if (s.Ball.Mode != BallMode.Flight || s.Phase != MatchPhase.Rally) return;
            if (previous != RingKind.None && f.RingFlight == s.Ball.FlightStart && s.World >= f.RingCenter &&
                s.World <= f.RingCenter + (f.RingGood - 1) / 2 + T.Rings.LateBand)
            {
                // Past the ideal frame the ball is below the planned height, so prediction finds no new
                // crossing; the ring stays open (same flight) until its late window closes.
                f.Ring = previous;
                if (previous == RingKind.Spike || previous == RingKind.Serve)
                {
                    f.RingReachable = true; // airborne reach is checked against the hand at contact
                }
                else
                {
                    // A late press connects at once, so the ball must be within reach right now.
                    Fix64 reach = previous == RingKind.Set ? T.Reach.SetReach : T.Reach.BumpReach;
                    f.RingReachable = Fix64.Abs(s.Ball.Pos.X - f.Pos.X) <= reach && s.Ball.Pos.Y >= T.Reach.MinContactHeight;
                }
                return;
            }

            if (f.Action == FighterAction.ServeToss)
            {
                PlanFloatServe(ref s, i);
                return;
            }
            if (f.Action == FighterAction.Airborne)
            {
                if (IsBlockJump(f.Jump) || f.Jump == JumpKind.FakeHop || f.SwungThisJump) return;
                if (s.Ball.Has(BallFlags.Serve) && s.Possession != i && f.Jump != JumpKind.JumpServe) return; // no attacking the serve
                PlanAirborne(ref s, i);
                return;
            }
            if (f.Action == FighterAction.ServeHold || f.Action == FighterAction.ServeApproach) return;
            PlanGrounded(ref s, i);
        }

        public static bool IsBlockJump(JumpKind kind) => kind == JumpKind.Block || kind == JumpKind.CommitBlock || kind == JumpKind.ShockwaveBlock;

        /// <summary>Touch number this player's next contact would be (1-based, block touches excluded).</summary>
        public static int NextTouch(in GameState s, int player) => (s.Possession == player ? s.Touches : 0) + 1;

        void PlanGrounded(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            int touch = NextTouch(s, i);
            RingKind kind = RingKind.Bump;
            Fix64 reach = T.Reach.BumpReach;
            int frame;
            FixVec2 point;
            Fix64 speed;
            bool found = false;
            if (touch == 2)
            {
                kind = RingKind.Set;
                reach = T.Reach.SetReach;
                found = PredictDescent(ref s, d.Height + T.Reach.SetHeightAboveHead, i, out frame, out point, out speed);
                if (!found)
                {
                    reach = T.Reach.BumpReach;
                    found = PredictDescent(ref s, T.Reach.BumpHeight, i, out frame, out point, out speed);
                }
            }
            else
            {
                found = PredictDescent(ref s, T.Reach.BumpHeight, i, out frame, out point, out speed);
            }
            if (!found) return;

            int perfect, good;
            if (kind == RingKind.Set)
            {
                perfect = d.PerfectControl;
                good = d.GoodControl;
            }
            else
            {
                perfect = d.PerfectDig;
                good = d.GoodDig;
                if (touch == 1 && f.Action == FighterAction.DigStance) good += T.Rings.DigStanceGoodBonus;
                if (touch == 1 && s.Ball.Has(BallFlags.RingShrink)) good -= RingShrinkFrames(s.LastSender);
            }
            SetRing(ref s, i, kind, frame, point, perfect, good);

            int ready = s.World;
            if (f.Action == FighterAction.Landing || f.Action == FighterAction.Recovery || f.Action == FighterAction.Floor ||
                f.Action == FighterAction.Knockback || f.Action == FighterAction.JumpStartup)
            {
                if (f.ActionEnd > ready) ready = f.ActionEnd;
            }
            int framesLeft = frame - ready;
            if (framesLeft < 0) framesLeft = 0;
            Fix64 dist = Fix64.Abs(point.X - f.Pos.X);
            Fix64 step = T.Reach.CommitStepSpeed * T.Dt * framesLeft;
            f.RingReachable = dist <= reach + step;
            if (!f.RingReachable && touch == 1)
            {
                Fix64 range = DiveRange(ref s, i);
                Fix64 diveCover = T.Dive.Speed * T.Dt * framesLeft;
                f.RingDive = dist <= reach + range && dist - reach <= diveCover;
                // Nothing Drops: once per rally, anything inside her dive range is a save.
                if (d.GuaranteedFloorSave && !f.FloorSaveUsed) f.RingDive = dist <= reach + range;
            }
        }

        int RingShrinkFrames(byte server)
        {
            if (server > 1) return 0;
            TrickDefinition t = Def(server).Trick(TrickKind.Afterburner);
            return t == null ? 0 : t.Amped.RingShrink;
        }

        Fix64 DiveRange(ref GameState s, int i)
        {
            FighterState f = s.FighterAt(i);
            Fix64 range = Def(i).DiveRange;
            if (f.Gassed) range = range * T.Meters.GassedDiveScale; // spec: dive range -30% while Gassed
            return range;
        }

        void PlanFloatServe(ref GameState s, int i)
        {
            FighterDefinition d = Def(i);
            Fix64 height = d.StandingReach;
            if (!PredictDescent(ref s, height, i, out int frame, out FixVec2 point, out Fix64 speed)) return;
            SetRing(ref s, i, RingKind.Serve, frame, point, d.PerfectPower, d.GoodPower);
            ref FighterState f = ref GameState.Fighter(ref s, i);
            f.RingReachable = true;
        }

        void PlanAirborne(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            if (!PredictSpikeContact(ref s, i, out int frame, out FixVec2 point)) return;
            RingKind kind = f.Jump == JumpKind.JumpServe ? RingKind.Serve : RingKind.Spike;
            int perfect = d.PerfectPower;
            int good = d.GoodPower;
            if (kind == RingKind.Spike && s.RunArmed(i)) perfect = (T.OnRun.PerfectScale * perfect).RoundToInt();
            SetRing(ref s, i, kind, frame, point, perfect, good);
            f.RingReachable = true;
        }

        void SetRing(ref GameState s, int i, RingKind kind, int center, FixVec2 point, int perfect, int good)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            if (Config.SimpleOf(i))
            {
                perfect = (T.Rings.SimpleModeScale * perfect).RoundToInt();
                good = (T.Rings.SimpleModeScale * good).RoundToInt();
            }
            if (perfect < T.Rings.MinPerfect) perfect = T.Rings.MinPerfect;
            if (good < T.Rings.MinGood) good = T.Rings.MinGood;
            if (good < perfect) good = perfect;
            f.Ring = kind;
            f.RingCenter = center;
            f.RingPoint = point;
            f.RingFlight = s.Ball.FlightStart;
            f.RingPerfect = (byte)perfect;
            f.RingGood = (byte)good;
        }

        // ================================================================== prediction

        /// <summary>
        /// Finds the first world frame at which the ball, descending, reaches <paramref name="height"/> on
        /// <paramref name="side"/>'s half. False if it leaves that half, lands first or clips the net cord.
        /// </summary>
        public bool PredictDescent(ref GameState s, Fix64 height, int side, out int frame, out FixVec2 point, out Fix64 speed)
        {
            frame = 0;
            point = FixVec2.Zero;
            speed = Fix64.Zero;
            if (s.Ball.Mode != BallMode.Flight) return false;
            BallState b = s.Ball;
            Fix64 r = T.Court.BallRadius;
            for (int k = 1; k <= T.Ball.PredictionHorizon; k++)
            {
                FixVec2 prev = b.Pos;
                NetResult net = IntegrateBall(ref b, s.World + k);
                if (net == NetResult.Cord) return false;
                if (net == NetResult.Crossed && SideOf(prev.X) == side) return false;
                if (b.Vel.Y.IsNegative && b.Pos.Y <= height && prev.Y > height)
                {
                    if (SideOf(b.Pos.X) != side && Fix64.Abs(b.Pos.X) > T.Reach.NetReachOver) return false;
                    frame = s.World + k;
                    point = b.Pos;
                    speed = b.Vel.Length;
                    return true;
                }
                if (b.Pos.Y <= r) return false;
            }
            return false;
        }

        /// <summary>Closest approach between the airborne fighter's spiking hand and the ball before landing.</summary>
        public bool PredictSpikeContact(ref GameState s, int i, out int frame, out FixVec2 point)
        {
            frame = 0;
            point = FixVec2.Zero;
            FighterState f = s.FighterAt(i);
            BallState b = s.Ball;
            Fix64 bestD2 = Fix64.MaxValue;
            int bestFrame = 0;
            FixVec2 bestPoint = FixVec2.Zero;
            FixVec2 feet = f.Pos;
            Fix64 vy = f.Vel.Y;
            Fix64 r2 = T.Reach.SpikeReachRadius * T.Reach.SpikeReachRadius;
            for (int k = 1; k <= 90; k++)
            {
                NetResult net = IntegrateBall(ref b, s.World + k);
                if (net == NetResult.Cord) break;
                feet.X += f.Vel.X * T.Dt;
                feet.Y += vy * T.Dt - T.HalfGravityDt2;
                vy -= T.GravityDt;
                if (feet.Y <= Fix64.Zero) break;
                FixVec2 hand = SpikeHand(feet, i);
                Fix64 d2 = FixVec2.DistanceSquared(hand, b.Pos);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestFrame = s.World + k;
                    bestPoint = b.Pos;
                }
                if (b.Pos.Y <= T.Court.BallRadius) break;
            }
            if (bestD2 > r2) return false;
            // The contact must be on the attacker's side, or just over the net (jousts, tight balls).
            if (SideOf(bestPoint.X) != i && Fix64.Abs(bestPoint.X) > T.Reach.NetReachOver) return false;
            frame = bestFrame;
            point = bestPoint;
            return true;
        }

        // ================================================================== commits

        /// <summary>Timing grade of a press <paramref name="delta"/> frames from the ring center, or None if outside the ring.</summary>
        public TimingGrade Grade(int delta, int perfect, int good)
        {
            if (delta >= -(perfect / 2) && delta <= (perfect - 1) / 2) return TimingGrade.Perfect;
            if (delta >= -(good / 2) && delta <= (good - 1) / 2) return TimingGrade.Good;
            int late = T.Rings.LateBand;
            if (delta >= -(good / 2) - late && delta <= (good - 1) / 2 + late) return TimingGrade.Late;
            return TimingGrade.None;
        }

        /// <summary>Earliest press offset (relative to the ring center) that still connects.</summary>
        public int EarliestDelta(int good) => -(good / 2) - T.Rings.LateBand;

        bool StickToward(in FighterState f, int i, Fix64 x)
        {
            int stick = EffectiveStick(f);
            if (stick == 0) return false;
            int worldDir = stick * Dir(i);
            Fix64 dx = x - f.Pos.X;
            return (worldDir > 0 && dx.IsPositive) || (worldDir < 0 && dx.IsNegative);
        }

        bool TryCommitGround(ref GameState s, int i, InputButtons button, TrickKind trick = TrickKind.None, bool amped = false)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            if (f.Ring == RingKind.None || f.Ring == RingKind.Spike) return false;
            bool feint = button == InputButtons.Feint;
            if (feint && f.Ring != RingKind.Set) return false;
            int press = f.Input.PressFrame(button);
            int delta = press - f.RingCenter;
            TimingGrade grade = Grade(delta, f.RingPerfect, f.RingGood);
            if (grade == TimingGrade.None)
            {
                if (delta > 0) f.Input.Consume(button); // too late for this ball
                return false;                         // too early: stays buffered until it expires
            }

            int touch = NextTouch(s, i);
            TouchKind kind;
            if (f.Ring == RingKind.Serve) kind = TouchKind.FloatServe;
            else if (f.Ring == RingKind.Set) kind = feint ? TouchKind.Dump : (f.RingPoint.Y < d.Height ? TouchKind.BumpSet : TouchKind.Set);
            else kind = touch == 3 ? TouchKind.Save : TouchKind.Bump;

            bool dive = false;
            if (kind != TouchKind.FloatServe)
            {
                if (f.RingReachable)
                {
                    dive = false;
                }
                else if (f.RingDive && StickToward(f, i, f.RingPoint.X))
                {
                    dive = true;
                }
                else
                {
                    return false; // keep buffered: the player may still get into reach
                }
            }

            f.Input.Consume(button);
            BeginCommit(ref s, i, kind, press, grade);
            if (trick != TrickKind.None)
            {
                amped = BeginTrick(ref s, i, trick, amped);
                f.CommitTrick = trick;
                f.CommitAmped = amped;
            }
            if (kind == TouchKind.FloatServe) return true;
            f.DiveTargetX = f.RingPoint.X;
            if (dive)
            {
                f.Action = FighterAction.Diving;
                f.ActionStart = s.World;
                f.ActionEnd = f.CommitContact + 2;
                SpendStamina(ref s, i, T.Meters.CostDive);
                Emit(VolleyEvent.Dive, i, 0, 0, f.RingPoint.X, f.RingPoint.Y);
                CheckBite(ref s, i, Commitment.Dive);
            }
            else
            {
                f.Action = FighterAction.Committed;
                f.ActionStart = s.World;
            }
            if (kind == TouchKind.Dump)
            {
                f.Bait = FeintKind.Dump;
                f.BaitStart = press;
                f.BaitEnd = f.CommitContact;
            }
            return true;
        }

        void BeginCommit(ref GameState s, int i, TouchKind kind, int press, TimingGrade grade)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            f.CommitKind = kind;
            f.CommitPress = press;
            f.CommitGrade = grade;
            f.CommitContact = f.RingCenter > s.World ? f.RingCenter : s.World;
            int lockAt = press + T.Rings.LockFrames + d.LateLockExtra;
            f.CommitLock = lockAt < f.CommitContact ? lockAt : f.CommitContact;
            Emit(VolleyEvent.Commit, i, (int)kind, (int)grade, f.RingPoint.X, f.RingPoint.Y);
        }

        void HandleAirInputs(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            int buffer = T.Rings.BufferFrames;
            if (IsBlockJump(f.Jump) || f.Jump == JumpKind.FakeHop) return;

            if (f.CommitKind != TouchKind.None)
            {
                // Before the choice locks, a different attack button switches the swing (Sleight locks later).
                if (s.World < f.CommitLock)
                {
                    if (f.Input.Buffered(InputButtons.Feint, s.World, buffer) && f.Choice != AttackChoice.Tip && f.Choice != AttackChoice.Roll)
                    {
                        f.Input.Consume(InputButtons.Feint);
                        f.Choice = AttackChoice.Tip;
                        f.CommitKind = TouchKind.Tip;
                    }
                    else if (f.Input.Buffered(InputButtons.Hit, s.World, buffer) && f.Choice != AttackChoice.Spike)
                    {
                        f.Input.Consume(InputButtons.Hit);
                        f.Choice = AttackChoice.Spike;
                        f.CommitKind = f.Jump == JumpKind.JumpServe ? TouchKind.JumpServe : TouchKind.Spike;
                    }
                }
                return;
            }
            if (f.SwungThisJump) return;

            if (f.Input.Buffered(InputButtons.Trick, s.World, buffer) && TryAirTrick(ref s, i)) return;
            if (!f.Charging && f.Input.Buffered(InputButtons.Hit, s.World, buffer))
            {
                f.Charging = true;
                f.ChargeStart = f.Input.PressFrame(InputButtons.Hit);
                f.Input.Consume(InputButtons.Hit);
            }
            if (f.Charging)
            {
                int release = f.Input.ReleaseFrame(InputButtons.Hit);
                if (release != InputTracker.Never && release >= f.ChargeStart)
                {
                    f.Input.ConsumeRelease(InputButtons.Hit);
                    f.Charging = false;
                    CommitAttack(ref s, i, AttackChoice.Spike, release, null, false);
                    return;
                }
            }
            if (f.Jump != JumpKind.JumpServe && f.Input.Buffered(InputButtons.Feint, s.World, buffer))
            {
                int press = f.Input.PressFrame(InputButtons.Feint);
                f.Input.Consume(InputButtons.Feint);
                f.Charging = false;
                CommitAttack(ref s, i, AttackChoice.Tip, press, null, false);
            }
        }

        /// <summary>Commits an airborne swing. A swing outside any ring whiffs and uses up the jump.</summary>
        void CommitAttack(ref GameState s, int i, AttackChoice choice, int press, TrickDefinition trick, bool amped)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            f.SwungThisJump = true;
            TimingGrade grade = TimingGrade.None;
            if (f.Ring == RingKind.Spike || f.Ring == RingKind.Serve) grade = Grade(press - f.RingCenter, f.RingPerfect, f.RingGood);
            if (grade == TimingGrade.None)
            {
                Emit(VolleyEvent.Whiff, i, (int)choice, 0, f.Pos.X, f.Pos.Y);
                return;
            }
            TouchKind kind = choice == AttackChoice.Tip ? TouchKind.Tip : (f.Jump == JumpKind.JumpServe ? TouchKind.JumpServe : TouchKind.Spike);
            f.Choice = choice;
            f.CommitTrick = trick != null ? trick.Kind : (f.Jump == JumpKind.JumpServe ? f.CommitTrick : TrickKind.None);
            f.CommitAmped = amped || (f.Jump == JumpKind.JumpServe && f.ActiveTrickAmped);
            int charge = press - f.ChargeStart;
            if (choice != AttackChoice.Spike) charge = 0;
            if (charge < 0) charge = 0;
            if (charge > d.ChargeFrames) charge = d.ChargeFrames;
            f.Charge = (byte)charge;
            BeginCommit(ref s, i, kind, press, grade);
            if (trick != null && trick.Kind == TrickKind.KalariTwist) f.CommitLock = f.CommitContact;
            if (f.ActiveTrick == TrickKind.QuickStroke && f.ActiveTrickAmped)
            {
                // Amped Quick Stroke: short or deep is decided on the last frame.
                TrickDefinition stroke = TrickFor(i, TrickKind.QuickStroke, true);
                if (stroke != null && stroke.Has(MoveFlags.LateAim)) f.CommitLock = f.CommitContact;
            }
            if (choice == AttackChoice.Tip)
            {
                f.Bait = FeintKind.Tip;
                f.BaitStart = press;
                f.BaitEnd = f.CommitContact;
                CheckBite(ref s, i, Commitment.Tip);
            }
        }

        // ================================================================== contact resolution

        void ResolveContacts(ref GameState s)
        {
            for (int i = 0; i < 2; i++)
            {
                ref FighterState f = ref GameState.Fighter(ref s, i);
                if (f.CommitKind != TouchKind.None && s.World == f.CommitLock && f.CommitLock < f.CommitContact) LockChoice(ref s, i);
            }
            if (TryJoust(ref s)) return;
            for (int i = 0; i < 2; i++)
            {
                ref FighterState f = ref GameState.Fighter(ref s, i);
                if (f.CommitKind == TouchKind.None || f.CommitContact != s.World) continue;
                ResolveTouch(ref s, i);
                if (s.Phase != MatchPhase.Rally) return;
            }
        }

        void LockChoice(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            f.CommitAim = (sbyte)f.Input.AimStickX(s.World, T.Rings.LockFrames);
            if (f.Choice == AttackChoice.Tip && f.CommitAim <= -T.Attack.AimStick)
            {
                f.Choice = AttackChoice.Roll;
                f.CommitKind = TouchKind.Roll;
            }
        }

        bool InReach(ref GameState s, int i, TouchKind kind)
        {
            FighterState f = s.FighterAt(i);
            FighterDefinition d = Def(i);
            FixVec2 ball = s.Ball.Pos;
            if (s.Ball.Mode != BallMode.Flight) return false;
            Fix64 slack = Fix64.FromRatio(1, 10);
            switch (kind)
            {
                case TouchKind.Spike:
                case TouchKind.Tip:
                case TouchKind.Roll:
                case TouchKind.JumpServe:
                    {
                        if (f.Action != FighterAction.Airborne) return false;
                        Fix64 r = T.Reach.SpikeReachRadius + slack;
                        return FixVec2.DistanceSquared(SpikeHand(f.Pos, i), ball) <= r * r;
                    }
                case TouchKind.FloatServe:
                    return Fix64.Abs(ball.X - f.Pos.X) <= T.Reach.SetReach + slack && ball.Y >= T.Reach.MinContactHeight;
                case TouchKind.Set:
                case TouchKind.Dump:
                    // A late setter still gets to the ball, low off the forearms if need be (see ResolveTouch).
                    return Fix64.Abs(ball.X - f.Pos.X) <= T.Reach.SetReach + slack && ball.Y >= T.Reach.MinContactHeight && ball.Y <= d.StandingReach + Fix64.One;
                default:
                    return Fix64.Abs(ball.X - f.Pos.X) <= T.Reach.BumpReach + slack && ball.Y >= T.Reach.MinContactHeight && ball.Y <= d.Height + Fix64.Half;
            }
        }

        static bool IsDigTouch(TouchKind kind) => kind == TouchKind.Bump || kind == TouchKind.Save;

        static bool IsAttackTouch(TouchKind kind) => kind == TouchKind.Spike || kind == TouchKind.Tip || kind == TouchKind.Roll || kind == TouchKind.Dump || kind == TouchKind.StandingHit;

        static bool IsServeTouch(TouchKind kind) => kind == TouchKind.FloatServe || kind == TouchKind.JumpServe;

        int AttributeFor(int i, TouchKind kind)
        {
            FighterDefinition d = Def(i);
            switch (kind)
            {
                case TouchKind.Bump:
                case TouchKind.Save:
                    return d.Dig;
                case TouchKind.Spike:
                case TouchKind.StandingHit:
                case TouchKind.FloatServe:
                case TouchKind.JumpServe:
                    return d.Power;
                default:
                    return d.Control;
            }
        }

        public Fix64 TimingValue(TimingGrade grade)
        {
            switch (grade)
            {
                case TimingGrade.Perfect: return T.Contact.TimingPerfect;
                case TimingGrade.Good: return T.Contact.TimingGood;
                default: return T.Contact.TimingLate;
            }
        }

        /// <summary>Spec contact model: Q = T + 0.03 (A - 5) - P - S.</summary>
        public Fix64 ContactQuality(TimingGrade grade, int attribute, Fix64 pressure, Fix64 stamina)
        {
            Fix64 q = TimingValue(grade) + T.Contact.AttributeSlope * (attribute - 5) - pressure;
            if (stamina < T.Contact.StaminaPenaltyBelow)
            {
                q -= T.Contact.StaminaPenaltyMax * (T.Contact.StaminaPenaltyBelow - stamina) / T.Contact.StaminaPenaltyBelow;
            }
            return q;
        }

        /// <summary>Incoming pressure from ball speed, up to the spec's 0.35.</summary>
        public Fix64 SpeedPressure(Fix64 speed) => T.Contact.PressureMax * Fix64.InverseLerp(T.Contact.PressureSpeedLow, T.Contact.PressureSpeedHigh, speed);

        public ContactBand Band(Fix64 q)
        {
            if (q >= T.Contact.Controlled) return ContactBand.Controlled;
            if (q >= T.Contact.Loose) return ContactBand.Loose;
            if (q >= T.Contact.Shank) return ContactBand.Shank;
            return ContactBand.Miss;
        }

        void ResolveTouch(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            if (f.CommitLock >= s.World) LockChoice(ref s, i);
            TouchKind kind = f.CommitKind;
            bool airborne = f.Action == FighterAction.Airborne;
            bool diving = f.Action == FighterAction.Diving;

            bool guaranteedSave = false;
            if (!InReach(ref s, i, kind))
            {
                // Nothing Drops: one floor save per rally on any ball inside her dive range.
                Fix64 gap = Fix64.Abs(s.Ball.Pos.X - f.Pos.X);
                bool rescue = diving && d.GuaranteedFloorSave && !f.FloorSaveUsed && s.Ball.Pos.Y >= T.Reach.MinContactHeight &&
                    gap <= T.Reach.BumpReach + DiveRange(ref s, i) + (f.ActiveTrick == TrickKind.Sweep ? d.Defense.Range : Fix64.Zero);
                if (!rescue)
                {
                    Emit(VolleyEvent.Whiff, i, (int)kind, 0, f.Pos.X, f.Pos.Y);
                    EndTouch(ref s, i, airborne, diving);
                    return;
                }
                f.Pos.X = s.Ball.Pos.X;
                f.FloorSaveUsed = true;
                guaranteedSave = true;
            }

            if (kind == TouchKind.Set && s.Ball.Pos.Y < d.Height - Fix64.FromRatio(3, 10))
            {
                kind = TouchKind.BumpSet;
                f.CommitKind = kind;
            }
            if (kind == TouchKind.Set && f.CommitTrick == TrickKind.BlindSet && f.CommitAmped && f.Input.IsHeld(InputButtons.Feint))
            {
                // Amped Blind Set: he can dump it over instead, decided on the last frame.
                TrickDefinition blind = TrickFor(i, TrickKind.BlindSet, true);
                if (blind != null && blind.Has(MoveFlags.DumpOption)) kind = TouchKind.Dump;
            }

            int touch = NextTouch(s, i);
            if (touch >= 4)
            {
                Fault(ref s, i, FaultKind.FourHits);
                return;
            }

            TimingGrade grade = f.CommitGrade;
            if (diving && f.ActiveTrick == TrickKind.Sweep && f.ActiveTrickAmped && grade == TimingGrade.Late) grade = TimingGrade.Good;
            bool firstTouchOfAttack = touch == 1 && !IsServeTouch(kind);
            Fix64 incoming = s.Ball.Vel.Length;
            Fix64 pressure = Fix64.Zero;
            if (firstTouchOfAttack && !airborne)
            {
                pressure = SpeedPressure(incoming);
                if (s.Ball.Has(BallFlags.HeavyBall)) pressure += T.Contact.HeavyBallPressure;
                if (WasInDigStance(ref s, i)) pressure = Fix64.Max(Fix64.Zero, pressure - T.Rings.DigStancePressureRelief);
            }
            Fix64 q = ContactQuality(grade, AttributeFor(i, kind), pressure, f.Stamina);
            if (kind == TouchKind.BumpSet) q -= T.Contact.BumpSetPenalty;
            ContactBand band = Band(q);

            // Masteries that rewrite the band.
            if (firstTouchOfAttack && IsDigTouch(kind))
            {
                if (s.Ball.Has(BallFlags.HeavyBall))
                {
                    // Heavy Ball: any dig short of Perfect is shanked, and every dig costs Stamina.
                    if (grade != TimingGrade.Perfect && band < ContactBand.Shank) band = ContactBand.Shank;
                    FighterDefinition hitter = Def(1 - i);
                    SpendStamina(ref s, i, hitter.HeavyBallDigStamina);
                }
                if (s.Ball.Has(BallFlags.PerfectOnly) && grade != TimingGrade.Perfect) band = ContactBand.Miss;
                if (diving && d.GuaranteedFloorSave && (guaranteedSave || !f.FloorSaveUsed) && band >= ContactBand.Shank)
                {
                    band = ContactBand.Loose; // Nothing Drops: the save keeps the ball alive
                    f.FloorSaveUsed = true;
                }
            }

            if (band == ContactBand.Miss)
            {
                Emit(VolleyEvent.Contact, i, (int)kind, (int)grade | ((int)band << 4), s.Ball.Pos.X, s.Ball.Pos.Y);
                EndTouch(ref s, i, airborne, diving);
                return;
            }

            FixVec2 at = s.Ball.Pos;
            TrickKind trick = f.CommitTrick;
            bool amped = f.CommitAmped;
            TouchKind recorded = kind;
            switch (kind)
            {
                case TouchKind.Bump when trick == TrickKind.InkReturn:
                    LaunchInkReturn(ref s, i, q, band, amped, incoming);
                    recorded = TouchKind.Spike;
                    break;
                case TouchKind.Bump: LaunchPass(ref s, i, q, band); break;
                case TouchKind.Save: LaunchSave(ref s, i, q, band); break;
                case TouchKind.Set:
                case TouchKind.BumpSet: LaunchSet(ref s, i, q, band, grade); break;
                case TouchKind.Dump: LaunchDump(ref s, i, q, band); break;
                case TouchKind.Spike: LaunchSpike(ref s, i, q, band); break;
                case TouchKind.Tip:
                case TouchKind.Roll: LaunchTip(ref s, i, q, band, kind == TouchKind.Roll); break;
                case TouchKind.FloatServe: LaunchServe(ref s, i, q, band, false); break;
                case TouchKind.JumpServe: LaunchServe(ref s, i, q, band, true); break;
            }
            if (s.Phase != MatchPhase.Rally) return;

            s.Possession = (byte)i;
            s.Touches = (byte)(IsServeTouch(kind) ? 0 : touch);
            s.TouchesSinceCross = (byte)(s.TouchesSinceCross + 1);
            s.LastToucher = (byte)i;
            s.LastTouch = recorded;
            s.LastContactWorld = s.World;
            if (IsAttackTouch(recorded) || IsServeTouch(recorded)) s.LastAttacker = (byte)i;
            int payload = (int)grade | ((int)band << 4) | ((int)trick << 8) | (amped ? 1 << 15 : 0);
            Emit(VolleyEvent.Contact, i, (int)kind, payload, at.X, at.Y);

            // Highlights.
            if (firstTouchOfAttack && IsDigTouch(kind))
            {
                if (diving)
                {
                    GainSurge(ref s, i, T.Meters.GainFloorSave);
                    Impact(ref s, Callout.Pancake, i, at);
                }
                else if (grade == TimingGrade.Perfect && s.LastSendKind != TouchKind.None && IsAttackTouch(s.LastSendKind))
                {
                    GainSurge(ref s, i, T.Meters.GainPerfectDig);
                    Impact(ref s, Callout.Dug, i, at);
                    f.PerfectDigFrame = s.World;
                }
            }
            if (f.BlockedLast && !IsServeTouch(kind)) f.BlockedLast = false;
            if (IsAttackTouch(kind) && s.RunArmed(i)) s.SetRunArmed(i, false);
            EndTouch(ref s, i, airborne, diving);
        }

        bool WasInDigStance(ref GameState s, int i)
        {
            FighterState f = s.FighterAt(i);
            return f.Action == FighterAction.DigStance || (f.Action == FighterAction.Committed && f.Input.StickX <= -T.Movement.DigStanceStick);
        }

        void EndTouch(ref GameState s, int i, bool airborne, bool diving)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            ClearCommit(ref f);
            f.Ring = RingKind.None;
            if (airborne) return;
            if (diving)
            {
                f.Action = FighterAction.Floor;
                f.ActionStart = s.World;
                f.ActionEnd = s.World + T.Dive.Recovery;
                CheckCenterLine(ref s, i);
                return;
            }
            f.Action = FighterAction.Recovery;
            f.ActionStart = s.World;
            f.ActionEnd = s.World + T.Reach.TouchRecovery;
        }

        // ================================================================== launches

        void Launch(ref GameState s, int i, FixVec2 velocity, BallFlags flags, BallSpin spin)
        {
            s.Ball.Vel = velocity;
            s.Ball.Mode = BallMode.Flight;
            s.Ball.FlightStart = s.World;
            s.Ball.LaunchSide = (byte)i;
            s.Ball.Flags = flags;
            s.Ball.Spin = spin;
            s.Ball.CrossedNet = false;
            s.Ball.DipGravity = Fix64.Zero;
            s.Ball.Nudge0 = Fix64.Zero;
            s.Ball.Nudge1 = Fix64.Zero;
            s.Ball.Nudge2 = Fix64.Zero;
            s.Ball.PendingBlock = BlockOutcome.None;
            s.Ball.PendingBlockFrame = int.MinValue / 2;
            s.Ball.DecoyUntil = int.MinValue / 2;
            s.Ball.Wind = CurrentWind(ref s);
        }

        Fix64 LooseFactor(Fix64 q) => Fix64.Clamp01((T.Contact.Controlled - q) / (T.Contact.Controlled - T.Contact.Loose));

        FixVec2 ShankVelocity(ref GameState s, int i, bool forward)
        {
            Fix64 speed = s.MatchRng.Range(T.Contact.ShankSpeedMin, T.Contact.ShankSpeedMax);
            Fix64 vx = forward ? speed * s.MatchRng.Range(Fix64.FromRatio(1, 5), Fix64.One) * Dir(i) : s.MatchRng.Symmetric(speed);
            Fix64 vy = speed * s.MatchRng.Range(Fix64.FromRatio(1, 5), Fix64.One);
            return new FixVec2(vx, vy);
        }

        void LaunchPass(ref GameState s, int i, Fix64 q, ContactBand band)
        {
            FighterDefinition d = Def(i);
            if (band == ContactBand.Shank)
            {
                Launch(ref s, i, ShankVelocity(ref s, i, false), BallFlags.None, BallSpin.None);
                return;
            }
            Fix64 x = FromNet(i, T.Pass.TargetFromNet);
            Fix64 apex = T.Pass.PeakHeight;
            if (band == ContactBand.Loose)
            {
                Fix64 k = LooseFactor(q);
                x += s.MatchRng.Symmetric(T.Contact.LooseAimError * k);
                apex += s.MatchRng.Symmetric(T.Contact.LooseHeightError * k);
            }
            var target = new FixVec2(x, d.Height + T.Reach.SetHeightAboveHead);
            Ballistics.SolveApex(s.Ball.Pos, apex, target, T.Ball.Gravity, out FixVec2 v);
            Launch(ref s, i, v, BallFlags.None, BallSpin.None);
        }

        /// <summary>Rin's Ink Return: the dig goes straight back over the net and catches a hitter still landing.</summary>
        void LaunchInkReturn(ref GameState s, int i, Fix64 q, ContactBand band, bool amped, Fix64 incoming)
        {
            TrickDefinition t = TrickFor(i, TrickKind.InkReturn, amped);
            if (band == ContactBand.Shank)
            {
                Launch(ref s, i, ShankVelocity(ref s, i, true), BallFlags.Attack, BallSpin.None);
                return;
            }
            Fix64 speed = Fix64.Max(T.Tricks.InkReturnMinSpeed, incoming * t.SpeedScale);
            Fix64 x = IntoOpponent(i, t.TargetFromNet);
            if (band == ContactBand.Loose) x += s.MatchRng.Symmetric(T.Contact.LooseAimError * LooseFactor(q));
            FixVec2 v = AimOverNet(s.Ball.Pos, new FixVec2(x, T.Court.BallRadius), speed, T.Attack.NetClearance, i);
            BallFlags flags = BallFlags.Attack;
            if (t.Has(MoveFlags.Unblockable)) flags |= BallFlags.Unblockable;
            Launch(ref s, i, v, flags, BallSpin.None);
            CheckBlock(ref s, i);
        }

        void LaunchSave(ref GameState s, int i, Fix64 q, ContactBand band)
        {
            if (band == ContactBand.Shank)
            {
                Launch(ref s, i, ShankVelocity(ref s, i, true), BallFlags.None, BallSpin.None);
                return;
            }
            Fix64 x = IntoOpponent(i, T.Pass.SaveDepth);
            if (band == ContactBand.Loose) x += s.MatchRng.Symmetric(T.Contact.LooseAimError * LooseFactor(q));
            Ballistics.SolveApex(s.Ball.Pos, T.Pass.SavePeak, new FixVec2(x, T.Court.BallRadius), T.Ball.Gravity, out FixVec2 v);
            Launch(ref s, i, v, BallFlags.FreeBall, BallSpin.None);
        }

        void LaunchSet(ref GameState s, int i, Fix64 q, ContactBand band, TimingGrade grade)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            if (band == ContactBand.Shank)
            {
                Launch(ref s, i, ShankVelocity(ref s, i, false), BallFlags.WeakSet, BallSpin.None);
                return;
            }
            TrickKind trick = f.CommitTrick;
            bool quick = f.CommitAim >= T.Set.QuickStick && f.CommitKind == TouchKind.Set;
            bool back = !quick && f.CommitAim <= -T.Set.QuickStick && f.CommitKind == TouchKind.Set;
            if (trick == TrickKind.QuickStroke)
            {
                // Quick Stroke: a low self-set into an instant attack with no power penalty.
                quick = true;
                back = false;
                f.ActiveTrick = TrickKind.QuickStroke;
                f.ActiveTrickAmped = f.CommitAmped;
            }
            Fix64 fromNet = quick ? T.Set.QuickTargetFromNet : (back ? T.Set.BackTargetFromNet : T.Set.HighTargetFromNet);
            Fix64 apex = T.Court.NetHeight + (quick ? T.Set.QuickPeakAboveNet : (back ? T.Set.BackPeakAboveNet : T.Set.HighPeakAboveNet));
            Fix64 x = FromNet(i, fromNet);
            if (band == ContactBand.Loose)
            {
                Fix64 k = LooseFactor(q) * d.SetErrorScale;
                x += s.MatchRng.Symmetric(T.Contact.LooseAimError * k);
                apex += s.MatchRng.Symmetric(T.Contact.LooseHeightError * k);
            }
            Fix64 hit = IdealHitHeight(i);
            var target = apex > hit ? new FixVec2(x, hit) : new FixVec2(x, apex);
            Ballistics.SolveApex(s.Ball.Pos, apex, target, T.Ball.Gravity, out FixVec2 v);
            BallFlags flags = quick ? BallFlags.QuickSet : BallFlags.HighSet;
            if (grade == TimingGrade.Late) flags |= BallFlags.WeakSet; // spec: a missed set ring allows only a weak hit or a tip
            bool hidden = trick == TrickKind.BlindSet;
            if (hidden) flags |= BallFlags.HideTelegraph;
            Launch(ref s, i, v, flags, BallSpin.None);
            ArmBlockRing(ref s, i, quick, hidden, f.CommitPress, trick);
        }

        /// <summary>
        /// Sets the opponent's block read ring: centered on this hitter's ideal contact with the set, revealed
        /// from the set commit (quick sets reveal later; hidden sets only once the ball leaves the hands).
        /// </summary>
        void ArmBlockRing(ref GameState s, int setter, bool quick, bool hidden, int commitPress, TrickKind trick)
        {
            int blocker = 1 - setter;
            ref FighterState fb = ref GameState.Fighter(ref s, blocker);
            Fix64 hit = IdealHitHeight(setter);
            int center = s.World + 30;
            BallState b = s.Ball;
            Fix64 bestHeight = Fix64.MinValue;
            for (int k = 1; k <= 150; k++)
            {
                FixVec2 prev = b.Pos;
                IntegrateBall(ref b, s.World + k);
                if (b.Pos.Y > bestHeight)
                {
                    bestHeight = b.Pos.Y;
                    center = s.World + k;
                }
                if (b.Vel.Y.IsNegative && b.Pos.Y <= hit && prev.Y > hit)
                {
                    center = s.World + k;
                    break;
                }
                if (b.Vel.Y.IsNegative && b.Pos.Y < hit) break;
            }
            int reveal = hidden ? s.World : commitPress;
            if (quick)
            {
                int cut = T.Set.QuickReadCut;
                TrickDefinition t = trick != TrickKind.None ? Def(setter).Trick(trick) : null;
                if (t != null && t.ReadCut > cut) cut = t.ReadCut;
                reveal += cut;
            }
            fb.BlockRingCenter = center;
            fb.BlockRingReveal = reveal;
        }

        void LaunchDump(ref GameState s, int i, Fix64 q, ContactBand band)
        {
            if (band == ContactBand.Shank)
            {
                Launch(ref s, i, ShankVelocity(ref s, i, true), BallFlags.Attack, BallSpin.None);
                return;
            }
            Fix64 x = IntoOpponent(i, T.Attack.TipFromNet + Fix64.Half);
            if (band == ContactBand.Loose) x += s.MatchRng.Symmetric(T.Contact.LooseAimError * LooseFactor(q) / 2);
            Fix64 apex = Fix64.Max(s.Ball.Pos.Y, T.Court.NetHeight + Fix64.FromRatio(6, 10)) + Fix64.FromRatio(2, 10);
            Ballistics.SolveApex(s.Ball.Pos, apex, new FixVec2(x, T.Court.BallRadius), T.Ball.Gravity, out FixVec2 v);
            Launch(ref s, i, v, BallFlags.Attack, BallSpin.None);
        }

        Fix64 AttackDepth(int aim)
        {
            if (aim >= T.Attack.AimStick) return T.Attack.ShortFromNet;
            if (aim <= -T.Attack.AimStick) return T.Attack.DeepFromNet;
            return T.Attack.MidFromNet;
        }

        void LaunchSpike(ref GameState s, int i, Fix64 q, ContactBand band)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            TrickDefinition trick = TrickFor(i, f.CommitTrick, f.CommitAmped);
            Fix64 speed = d.SpikeSpeed;
            if (f.Charge > 0) speed = speed * (Fix64.One + T.Attack.ChargeBonus * f.Charge / d.ChargeFrames);
            bool noQuickPenalty = trick != null && trick.Has(MoveFlags.NoQuickPenalty);
            if (f.ActiveTrick == TrickKind.QuickStroke)
            {
                TrickDefinition stroke = TrickFor(i, TrickKind.QuickStroke, f.ActiveTrickAmped);
                if (stroke != null && stroke.Has(MoveFlags.NoQuickPenalty)) noQuickPenalty = true;
                f.ActiveTrick = TrickKind.None;
            }
            if (s.Ball.Has(BallFlags.QuickSet) && !noQuickPenalty) speed = speed * T.Set.QuickPowerScale;
            if (s.Ball.Has(BallFlags.WeakSet)) speed = speed * T.Set.MissedPowerScale;
            if (trick != null) speed = speed * trick.SpeedScale;

            BallFlags flags = BallFlags.Attack;
            if (d.HeavyBall) flags |= BallFlags.HeavyBall;
            if (trick != null)
            {
                if (trick.Has(MoveFlags.Unblockable)) flags |= BallFlags.Unblockable;
                if (trick.Has(MoveFlags.CantStuff)) flags |= BallFlags.CantStuff;
                if (trick.Has(MoveFlags.PerfectOnly)) flags |= BallFlags.PerfectOnly;
            }

            if (band == ContactBand.Shank)
            {
                Fix64 vx = speed * s.MatchRng.Range(Fix64.FromRatio(1, 4), Fix64.One) * Dir(i);
                Fix64 vy = speed * s.MatchRng.Range(-Fix64.Half, Fix64.FromRatio(1, 4));
                Launch(ref s, i, new FixVec2(vx, vy), flags, BallSpin.None);
                CheckBlock(ref s, i);
                return;
            }

            Fix64 depth = AttackDepth(f.CommitAim);
            if (trick != null && trick.TargetFromNet.IsPositive) depth = trick.TargetFromNet;
            Fix64 x = IntoOpponent(i, depth);
            if (band == ContactBand.Loose) x += s.MatchRng.Symmetric(T.Contact.LooseAimError * LooseFactor(q));
            FixVec2 v = AimOverNet(s.Ball.Pos, new FixVec2(x, T.Court.BallRadius), speed, T.Attack.NetClearance, i);
            Launch(ref s, i, v, flags, BallSpin.None);
            if (trick != null && trick.Kind == TrickKind.KalariTwist && trick.DecoyFrames > 0) StartDecoyFlight(ref s, i, trick, speed);
            CheckBlock(ref s, i);
        }

        /// <summary>
        /// Velocity of the given speed from <paramref name="from"/> to <paramref name="target"/> on the low arc,
        /// raised to clear the net by <paramref name="clearance"/> when the low arc would not.
        /// </summary>
        public FixVec2 AimOverNet(FixVec2 from, FixVec2 target, Fix64 speed, Fix64 clearance, int i)
        {
            Fix64 g = T.Ball.Gravity;
            Fix64 minHeight = T.Court.NetHeight + T.Court.BallRadius + clearance;
            if (Ballistics.SolveSpeed(from, target, speed, g, true, out FixVec2 v) && ClearsNet(from, v, minHeight)) return v;
            var via = new FixVec2(Fix64.Zero, minHeight);
            // Push the target deeper until a clean arc exists (contact far from the net or too low).
            for (int k = 0; k < 8; k++)
            {
                if (Fix64.Abs(target.X) > Fix64.Abs(from.X) || SideOf(target.X) != SideOf(from.X))
                {
                    if (Ballistics.SolveThrough(from, via, target, g, out v)) return v;
                }
                target.X += Fix64.Half * Dir(i);
            }
            Ballistics.SolveApex(from, Fix64.Max(from.Y, minHeight) + Fix64.Half, target, g, out v);
            return v;
        }

        bool ClearsNet(FixVec2 from, FixVec2 v, Fix64 minHeight)
        {
            if (!Ballistics.HeightAt(from, v, T.Ball.Gravity, Fix64.Zero, out Fix64 h, out Fix64 t)) return false;
            return h >= minHeight;
        }

        void LaunchTip(ref GameState s, int i, Fix64 q, ContactBand band, bool roll)
        {
            FighterDefinition d = Def(i);
            if (band == ContactBand.Shank)
            {
                Launch(ref s, i, ShankVelocity(ref s, i, true), BallFlags.Attack, BallSpin.None);
                return;
            }
            Fix64 depth = roll ? T.Attack.RollFromNet : T.Attack.TipFromNet;
            Fix64 x = IntoOpponent(i, depth);
            if (band == ContactBand.Loose) x += s.MatchRng.Symmetric(T.Contact.LooseAimError * LooseFactor(q) / 2);
            var target = new FixVec2(x, T.Court.BallRadius);
            // Spec: tips travel at 4-6 m/s (finer touch = softer), lobbed over the block; rolls are off-speed and deep.
            Fix64 speed = roll ? T.Attack.RollSpeed : T.Attack.TipSpeedMax - (T.Attack.TipSpeedMax - T.Attack.TipSpeedMin) * Fix64.FromRatio(d.Control, 10);
            Fix64 clearHeight = T.Court.NetHeight + (roll ? T.Attack.RollLift : T.Attack.TipLift);
            FixVec2 v;
            bool ok = false;
            for (int k = 0; k < 12 && !ok; k++)
            {
                ok = Ballistics.SolveSpeed(s.Ball.Pos, target, speed, T.Ball.Gravity, false, out v) && ClearsNet(s.Ball.Pos, v, clearHeight);
                if (!ok) speed += Fix64.FromRatio(1, 4);
            }
            if (!ok || !Ballistics.SolveSpeed(s.Ball.Pos, target, speed, T.Ball.Gravity, false, out v))
            {
                Fix64 apex = roll ? s.Ball.Pos.Y + T.Attack.RollLift : Fix64.Max(s.Ball.Pos.Y, clearHeight);
                Ballistics.SolveApex(s.Ball.Pos, apex, target, T.Ball.Gravity, out v);
            }
            Launch(ref s, i, v, BallFlags.Attack, BallSpin.None);
            // Spec: a tip into a grounded defender is a free ball and gives the defender 20 Surge (read tip).
            int defender = 1 - i;
            FighterState fd = s.FighterAt(defender);
            if (fd.Action != FighterAction.Airborne && fd.Action != FighterAction.JumpStartup)
            {
                GainSurge(ref s, defender, T.Meters.GainReadTip);
                Impact(ref s, Callout.ReadIt, defender, fd.Pos);
                s.Ball.Flags |= BallFlags.FreeBall;
            }
        }

        void LaunchServe(ref GameState s, int i, Fix64 q, ContactBand band, bool jump)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            // Spec: serve foot fault. A standing serve is struck from behind the end line.
            if (!jump && Fix64.Abs(f.Pos.X) < T.Court.HalfLength)
            {
                Fault(ref s, i, FaultKind.FootFault);
                return;
            }
            TrickDefinition trick = TrickFor(i, f.CommitTrick, f.CommitAmped);
            Fix64 speed = jump ? d.JumpServeSpeed : d.FloatServeSpeed;
            if (jump && f.Charge > 0) speed = speed * (Fix64.One + T.Attack.ChargeBonus * f.Charge / d.ChargeFrames);
            if (trick != null) speed = speed * trick.SpeedScale;
            BallFlags flags = BallFlags.Serve;
            if (jump && d.HeavyBall) flags |= BallFlags.HeavyBall;
            if (trick != null && trick.IsAmped && trick.RingShrink > 0) flags |= BallFlags.RingShrink;
            BallSpin spin = jump ? BallSpin.Topspin : BallSpin.Float;
            bool setPoint = IsSetPointServe(ref s);
            if (setPoint) flags |= BallFlags.SetPointSlow;

            FixVec2 v;
            if (band == ContactBand.Shank)
            {
                Fix64 vx = speed * s.MatchRng.Range(Fix64.FromRatio(1, 3), Fix64.One) * Dir(i);
                Fix64 vy = speed * s.MatchRng.Range(-Fix64.FromRatio(1, 5), Fix64.FromRatio(1, 3));
                v = new FixVec2(vx, vy);
            }
            else
            {
                Fix64 depth = f.CommitAim >= T.Attack.AimStick ? T.Serve.ShortFromNet : (f.CommitAim <= -T.Attack.AimStick ? T.Serve.DeepFromNet : T.Serve.MidFromNet);
                Fix64 x = IntoOpponent(i, depth);
                if (band == ContactBand.Loose) x += s.MatchRng.Symmetric(T.Contact.LooseAimError * LooseFactor(q));
                v = AimOverNet(s.Ball.Pos, new FixVec2(x, T.Court.BallRadius), speed, jump ? T.Serve.JumpClearance : T.Serve.FloatClearance, i);
            }
            Launch(ref s, i, v, flags, spin);
            if (jump)
            {
                Fix64 dip = T.Ball.TopspinDipGravity;
                if (trick != null) dip = dip * trick.DipScale;
                s.Ball.DipGravity = dip;
            }
            else
            {
                s.Ball.Nudge0 = T.Ball.FloatWobbleNudges > 0 ? s.MatchRng.Symmetric(T.Ball.FloatWobbleMax) : Fix64.Zero;
                s.Ball.Nudge1 = T.Ball.FloatWobbleNudges > 1 ? s.MatchRng.Symmetric(T.Ball.FloatWobbleMax) : Fix64.Zero;
                s.Ball.Nudge2 = T.Ball.FloatWobbleNudges > 2 ? s.MatchRng.Symmetric(T.Ball.FloatWobbleMax) : Fix64.Zero;
            }
            if (setPoint)
            {
                s.SlowDiv = (byte)T.Flow.SetPointSlowDiv;
                s.SlowFrames = 0;
                s.SlowPhase = 0;
                Emit(VolleyEvent.SlowMotion, i, T.Flow.SetPointSlowDiv, 0);
            }
            f.ActiveTrick = TrickKind.None;
            if (!jump)
            {
                f.Action = FighterAction.Recovery;
                f.ActionEnd = s.World + T.Reach.TouchRecovery;
            }
        }
    }
}
