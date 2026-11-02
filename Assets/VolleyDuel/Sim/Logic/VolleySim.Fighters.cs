using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    public sealed partial class VolleySim
    {
        void UpdateFighter(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            if (f.Action != FighterAction.Airborne && f.Input.Buffered(InputButtons.Finisher, s.World, T.Rings.BufferFrames) && TryFinisher(ref s, i)) return;
            switch (f.Action)
            {
                case FighterAction.JumpStartup:
                    if (s.World >= f.ActionEnd) Takeoff(ref s, i);
                    break;
                case FighterAction.Airborne:
                    StepAirborne(ref s, i);
                    break;
                case FighterAction.Landing:
                case FighterAction.Recovery:
                case FighterAction.Floor:
                case FighterAction.Knockback:
                    if (s.World >= f.ActionEnd) f.Action = FighterAction.Neutral;
                    break;
                case FighterAction.Committed:
                    StepCommitted(ref s, i);
                    break;
                case FighterAction.Diving:
                    StepDiving(ref s, i);
                    break;
                case FighterAction.ServeApproach:
                    StepServeApproach(ref s, i);
                    break;
            }
            if (s.Phase != MatchPhase.Rally && s.Phase != MatchPhase.PreServe) return;

            switch (f.Action)
            {
                case FighterAction.Neutral:
                case FighterAction.DigStance:
                    Move(ref s, i);
                    if (s.Phase == MatchPhase.Rally) HandleGroundInputs(ref s, i);
                    else HandleReceiverPreServe(ref s, i);
                    break;
                case FighterAction.Airborne:
                    if (s.Phase == MatchPhase.Rally) HandleAirInputs(ref s, i);
                    break;
                case FighterAction.ServeHold:
                    StepServeHold(ref s, i);
                    break;
                case FighterAction.ServeToss:
                    HandleServeToss(ref s, i);
                    break;
            }

            // Spec: Stamina refills while grounded and idle (free to act), plus 40 at each dead ball.
            if (f.Action == FighterAction.Neutral || f.Action == FighterAction.DigStance || f.Action == FighterAction.ServeHold)
            {
                RefillStamina(ref s, i, T.Meters.RefillPerFrame);
            }
        }

        // ------------------------------------------------------------------ movement

        /// <summary>True when the ball is the opponent's to play (in flight on their side or held by their server).</summary>
        public static bool IsDefending(in GameState s, int player)
        {
            if (s.Ball.Mode == BallMode.Held) return s.Ball.Holder != player;
            return SideOf(s.Ball.Pos.X) != player;
        }

        int EffectiveStick(in FighterState f)
        {
            int stick = f.Input.StickX;
            if (stick <= T.Movement.Deadzone && stick >= -T.Movement.Deadzone) return 0;
            return stick;
        }

        void Move(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            int stick = EffectiveStick(f);
            bool defending = IsDefending(s, i);
            if (defending && stick <= -T.Movement.DigStanceStick) f.Action = FighterAction.DigStance;
            else if (f.Action == FighterAction.DigStance) f.Action = FighterAction.Neutral;

            Fix64 speed = d.WalkSpeed;
            if (stick < 0) speed = speed * d.BackpedalScale;
            if (f.Action == FighterAction.DigStance) speed = speed * T.Movement.DigStanceScale;
            if (Config.LocalConditions && court.Sand) speed = speed * T.Conditions.SandDragScale;
            Fix64 target = speed * stick / FrameInput.StickMax * Dir(i);
            Fix64 accel = d.WalkSpeed / T.Movement.AccelFrames;
            if (Config.LocalConditions && court.Snow) accel = accel * T.Conditions.SnowAccelScale;
            f.Vel.X = Approach(f.Vel.X, target, accel);
            f.Vel.Y = Fix64.Zero;
            f.Pos.X += f.Vel.X * T.Dt;
            f.Pos.X = ClampGrounded(i, f.Pos.X);
        }

        static Fix64 Approach(Fix64 value, Fix64 target, Fix64 step)
        {
            if (value < target) return Fix64.Min(target, value + step);
            if (value > target) return Fix64.Max(target, value - step);
            return value;
        }

        public Fix64 ClampGrounded(int player, Fix64 x)
        {
            Fix64 near = T.Court.GroundedNetGap;
            Fix64 far = T.Court.OuterLimit;
            return player == 0 ? Fix64.Clamp(x, -far, -near) : Fix64.Clamp(x, near, far);
        }

        Fix64 ClampServiceZone(int player, Fix64 x)
        {
            Fix64 line = T.Court.HalfLength + Fix64.FromRatio(1, 50);
            Fix64 back = T.Court.HalfLength + T.Court.ServiceDepth;
            return player == 0 ? Fix64.Clamp(x, -back, -line) : Fix64.Clamp(x, line, back);
        }

        bool NearNet(in FighterState f) => Fix64.Abs(f.Pos.X) <= T.Block.RangeFromNet;

        // ------------------------------------------------------------------ grounded inputs

        void HandleGroundInputs(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            int buffer = T.Rings.BufferFrames;
            if (f.Input.Buffered(InputButtons.Finisher, s.World, buffer) && TryFinisher(ref s, i)) return;
            if (f.Input.Buffered(InputButtons.Trick, s.World, buffer) && TryGroundTrick(ref s, i)) return;
            if (f.Input.Buffered(InputButtons.Jump, s.World, buffer))
            {
                f.Input.Consume(InputButtons.Jump);
                bool block = IsDefending(s, i) && NearNet(f);
                StartJump(ref s, i, block ? JumpKind.Block : JumpKind.Approach);
                return;
            }
            if (f.Input.Buffered(InputButtons.Hit, s.World, buffer) && TryCommitGround(ref s, i, InputButtons.Hit)) return;
            if (f.Input.Buffered(InputButtons.Feint, s.World, buffer))
            {
                if (IsDefending(s, i) && NearNet(f))
                {
                    f.Input.Consume(InputButtons.Feint);
                    StartJump(ref s, i, JumpKind.FakeHop);
                    return;
                }
                TryCommitGround(ref s, i, InputButtons.Feint);
            }
        }

        void HandleReceiverPreServe(ref GameState s, int i)
        {
            // The receiver may move and sink into a stance before the serve, nothing else.
            ref FighterState f = ref GameState.Fighter(ref s, i);
            if (f.Input.Buffered(InputButtons.Jump, s.World, T.Rings.BufferFrames)) f.Input.Consume(InputButtons.Jump);
        }

        // ------------------------------------------------------------------ jumps

        void StartJump(ref GameState s, int i, JumpKind kind, TrickDefinition trick = null)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            int startup = kind == JumpKind.Block || kind == JumpKind.FakeHop || kind == JumpKind.CommitBlock || kind == JumpKind.ShockwaveBlock
                ? T.Jump.BlockStartupFrames
                : T.Jump.StartupFrames;
            if (trick != null) startup = trick.Startup;
            Fix64 height;
            switch (kind)
            {
                case JumpKind.Block:
                case JumpKind.CommitBlock:
                case JumpKind.ShockwaveBlock:
                    height = d.BlockJumpHeight;
                    break;
                case JumpKind.FakeHop:
                    height = T.Jump.FakeHopHeight;
                    break;
                default:
                    height = d.JumpHeight;
                    break;
            }
            if (f.Gassed) height = height * T.Meters.GassedJumpScale;
            f.Action = FighterAction.JumpStartup;
            f.Jump = kind;
            f.ActionStart = s.World;
            f.ActionEnd = s.World + startup;
            f.JumpHeight = height;
            f.SwungThisJump = false;
            f.Charging = false;
            ClearCommit(ref f);
            if (kind != JumpKind.FakeHop) SpendStamina(ref s, i, T.Meters.CostJump);
            Emit(VolleyEvent.Jump, i, (int)kind, 0, f.Pos.X, f.Pos.Y);
            if (kind == JumpKind.FakeHop)
            {
                f.Bait = FeintKind.FakeBlock;
                f.BaitStart = s.World;
                f.BaitEnd = s.World + T.Feint.FakeBlockBaitFrames;
            }
            if (kind == JumpKind.Block || kind == JumpKind.CommitBlock || kind == JumpKind.ShockwaveBlock) CheckBite(ref s, i, Commitment.Block);
        }

        void Takeoff(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            f.Action = FighterAction.Airborne;
            Fix64 vy = Fix64.Sqrt(2 * T.Ball.Gravity * f.JumpHeight);
            Fix64 vx = Fix64.Zero;
            if (f.Jump == JumpKind.Approach || f.Jump == JumpKind.JumpServe)
            {
                vx = Fix64.Clamp(f.Vel.X * T.Jump.ApproachCarry, -T.Jump.MaxDrift, T.Jump.MaxDrift);
            }
            else if (f.Jump == JumpKind.Slide)
            {
                vx = SlideDrift(ref s, i, vy);
            }
            f.Vel = new FixVec2(vx, vy);
            f.ApexFrame = s.World + (vy / T.GravityDt).CeilToInt();
            if (f.Jump == JumpKind.CommitBlock) f.HangUntil = int.MinValue / 2;
            Emit(VolleyEvent.Takeoff, i, (int)f.Jump, 0, f.Pos.X, f.Pos.Y);
        }

        void StepAirborne(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            if (f.Jump == JumpKind.CommitBlock && f.HangUntil > s.World && f.Vel.Y <= Fix64.Zero)
            {
                // Commit Block: hands held over the net at the apex for the hang window.
                f.Vel.Y = Fix64.Zero;
            }
            else
            {
                f.Pos.Y += f.Vel.Y * T.Dt - T.HalfGravityDt2;
                f.Vel.Y -= T.GravityDt;
                if (f.Jump == JumpKind.CommitBlock && f.HangUntil == int.MinValue / 2 && f.Vel.Y <= Fix64.Zero)
                {
                    TrickDefinition commit = TrickFor(i, TrickKind.CommitBlock, f.ActiveTrickAmped);
                    if (commit != null) f.HangUntil = s.World + commit.HangFrames;
                }
            }
            f.Pos.X += f.Vel.X * T.Dt;
            Fix64 far = T.Court.OuterLimit;
            f.Pos.X = Fix64.Clamp(f.Pos.X, -far, far);
            if (s.Phase == MatchPhase.Rally && Fix64.Abs(f.Pos.X) < T.Court.NetTouchDistance)
            {
                Fault(ref s, i, FaultKind.NetTouch);
                return;
            }
            if (f.Pos.Y <= Fix64.Zero) Land(ref s, i);
        }

        void Land(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            JumpKind jump = f.Jump;
            f.Pos.Y = Fix64.Zero;
            f.Vel = FixVec2.Zero;
            int recovery = T.Jump.LandingRecovery + d.LandingRecoveryExtra + f.ExtraRecovery;
            if (jump == JumpKind.FakeHop) recovery = T.Jump.FakeHopRecovery + f.ExtraRecovery;
            if (f.PendingKnockback > 0)
            {
                // Shockwave Block: the shock ring delays the hitter's next action on landing.
                recovery += f.PendingKnockback;
                Emit(VolleyEvent.Knockback, i, f.PendingKnockback);
                f.PendingKnockback = 0;
            }
            f.ExtraRecovery = 0;
            f.Jump = JumpKind.None;
            f.Charging = false;
            f.SwungThisJump = false;
            if (jump == JumpKind.ShockwaveBlock || jump == JumpKind.CommitBlock || jump == JumpKind.Slide || jump == JumpKind.JumpServe) f.ActiveTrick = TrickKind.None;
            if (f.CommitContact > s.World || f.CommitKind == TouchKind.Spike || f.CommitKind == TouchKind.Tip || f.CommitKind == TouchKind.Roll || f.CommitKind == TouchKind.JumpServe)
            {
                ClearCommit(ref f);
            }
            if (f.LandInDigStance)
            {
                f.LandInDigStance = false;
                f.Action = FighterAction.DigStance;
            }
            else
            {
                f.Action = FighterAction.Landing;
                f.ActionStart = s.World;
                f.ActionEnd = s.World + recovery;
            }
            Emit(VolleyEvent.Land, i, recovery, 0, f.Pos.X, Fix64.Zero);
            CheckCenterLine(ref s, i);
        }

        void CheckCenterLine(ref GameState s, int i)
        {
            if (s.Phase != MatchPhase.Rally) return;
            FighterState f = s.FighterAt(i);
            if (TowardNet(i, f.Pos.X) > T.Court.CenterLineTolerance) Fault(ref s, i, FaultKind.CenterLine);
        }

        void StepCommitted(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            Fix64 step = T.Reach.CommitStepSpeed * T.Dt;
            f.Pos.X = ClampGrounded(i, Approach(f.Pos.X, f.DiveTargetX, step));
            f.Vel = FixVec2.Zero;
            if (s.World > f.CommitContact + 1)
            {
                // The ball never arrived (rally changed): stand back up.
                ClearCommit(ref f);
                f.Action = FighterAction.Neutral;
            }
        }

        void StepDiving(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            Fix64 step = T.Dive.Speed * T.Dt;
            if (f.ActiveTrick == TrickKind.Sweep) step = step * 2;
            Fix64 x = Approach(f.Pos.X, f.DiveTargetX, step);
            // Dives may slide under the net: a foot across the center line is a fault, checked on the floor.
            Fix64 far = T.Court.OuterLimit;
            f.Pos.X = Fix64.Clamp(x, -far, far);
            if (s.World > f.CommitContact + 1 && f.CommitKind != TouchKind.None) ClearCommit(ref f);
            if (s.World >= f.ActionEnd)
            {
                f.Action = FighterAction.Floor;
                f.ActionStart = s.World;
                f.ActionEnd = s.World + T.Dive.Recovery;
                ClearCommit(ref f);
                CheckCenterLine(ref s, i);
            }
        }

        // ------------------------------------------------------------------ serving

        void StepServeHold(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            int stick = EffectiveStick(f);
            Fix64 speed = d.WalkSpeed * Fix64.FromRatio(1, 2);
            Fix64 target = speed * stick / FrameInput.StickMax * Dir(i);
            f.Vel.X = Approach(f.Vel.X, target, d.WalkSpeed / T.Movement.AccelFrames);
            f.Pos.X = ClampServiceZone(i, f.Pos.X + f.Vel.X * T.Dt);
            if (s.Phase != MatchPhase.PreServe) return;

            int buffer = T.Rings.BufferFrames;
            if (f.Input.Buffered(InputButtons.Finisher, s.World, buffer) && TryFinisher(ref s, i)) return;
            if (f.Input.Buffered(InputButtons.Trick, s.World, buffer) && TryServeTrick(ref s, i)) return;
            if (f.Input.Buffered(InputButtons.Jump, s.World, buffer))
            {
                f.Input.Consume(InputButtons.Jump);
                JumpToss(ref s, i, null, false);
                return;
            }
            if (f.Input.Buffered(InputButtons.Hit, s.World, buffer) || s.World - s.PhaseStart >= T.Serve.TimeoutFrames)
            {
                f.Input.Consume(InputButtons.Hit);
                FloatToss(ref s, i);
            }
        }

        void StartRallyFromToss(ref GameState s, int i)
        {
            SetPhase(ref s, MatchPhase.Rally);
            s.Possession = (byte)i;
            s.Touches = 0;
            s.LastToucher = (byte)i;
            s.LastTouch = TouchKind.None;
            s.Ball.Mode = BallMode.Flight;
            s.Ball.FlightStart = s.World;
            s.Ball.LaunchSide = (byte)i;
            s.Ball.Flags = BallFlags.Serve;
            s.Ball.Spin = BallSpin.None;
            s.Ball.CrossedNet = false;
        }

        void FloatToss(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            StartRallyFromToss(ref s, i);
            s.Ball.Pos = HandPosition(ref s, i);
            s.Ball.Vel = new FixVec2(Fix64.Zero, Fix64.Sqrt(2 * T.Ball.Gravity * T.Serve.FloatTossHeight));
            f.Action = FighterAction.ServeToss;
            f.ActionStart = s.World;
            f.Vel = FixVec2.Zero;
            Emit(VolleyEvent.Toss, i, 0, 0, s.Ball.Pos.X, s.Ball.Pos.Y);
        }

        void JumpToss(ref GameState s, int i, TrickDefinition trick, bool amped)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            FighterDefinition d = Def(i);
            StartRallyFromToss(ref s, i);
            FixVec2 hand = HandPosition(ref s, i);
            Fix64 tossVy = Fix64.Sqrt(2 * T.Ball.Gravity * T.Serve.JumpTossHeight);
            Fix64 jumpHeight = f.Gassed ? d.JumpHeight * T.Meters.GassedJumpScale : d.JumpHeight;
            Fix64 jumpVy = Fix64.Sqrt(2 * T.Ball.Gravity * jumpHeight);
            int upFrames = (jumpVy / T.GravityDt).CeilToInt();
            Fix64 hitHeight = d.StandingReach + jumpHeight;
            Ballistics.TimeToHeight(hand.Y, tossVy, T.Ball.Gravity, hitHeight, out Fix64 tHit);
            int hitFrames = (tHit / T.Dt).RoundToInt();
            int approachFrames = hitFrames - upFrames;
            if (approachFrames < 6) approachFrames = 6;
            Fix64 approachSpeed = T.Serve.JumpApproachDistance / (T.Dt * approachFrames);
            Fix64 airVx = Fix64.Clamp(approachSpeed * Dir(i) * T.Jump.ApproachCarry, -T.Jump.MaxDrift, T.Jump.MaxDrift);
            Fix64 takeoffX = f.Pos.X + T.Serve.JumpApproachDistance * Dir(i);
            Fix64 contactX = takeoffX + airVx * T.Dt * upFrames + T.Reach.HandForward * Dir(i);
            s.Ball.Pos = hand;
            s.Ball.Vel = new FixVec2((contactX - hand.X) / (T.Dt * hitFrames), tossVy);
            f.Action = FighterAction.ServeApproach;
            f.ActionStart = s.World;
            f.ActionEnd = s.World + approachFrames;
            f.Vel = new FixVec2(approachSpeed * Dir(i), Fix64.Zero);
            f.JumpHeight = jumpHeight;
            f.Jump = JumpKind.JumpServe;
            f.CommitTrick = trick != null ? trick.Kind : TrickKind.None;
            f.ActiveTrick = trick != null ? trick.Kind : TrickKind.None;
            f.ActiveTrickAmped = amped;
            SpendStamina(ref s, i, T.Meters.CostJumpServe);
            Emit(VolleyEvent.Toss, i, 1, 0, s.Ball.Pos.X, s.Ball.Pos.Y);
        }

        void StepServeApproach(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            f.Pos.X += f.Vel.X * T.Dt;
            if (s.World < f.ActionEnd) return;
            // Spec: serve foot fault. A jump serve must take off from behind the end line.
            if (Fix64.Abs(f.Pos.X) < T.Court.HalfLength)
            {
                Fault(ref s, i, FaultKind.FootFault);
                return;
            }
            f.Jump = JumpKind.JumpServe;
            f.SwungThisJump = false;
            f.Charging = false;
            Takeoff(ref s, i);
        }

        void HandleServeToss(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            if (f.Input.Buffered(InputButtons.Hit, s.World, T.Rings.BufferFrames)) TryCommitGround(ref s, i, InputButtons.Hit);
        }

        // ------------------------------------------------------------------ dead ball

        void SettleFighter(ref GameState s, int i)
        {
            ref FighterState f = ref GameState.Fighter(ref s, i);
            if (f.Action == FighterAction.Airborne)
            {
                f.Pos.Y += f.Vel.Y * T.Dt - T.HalfGravityDt2;
                f.Vel.Y -= T.GravityDt;
                f.Pos.X += f.Vel.X * T.Dt;
                if (f.Pos.Y <= Fix64.Zero)
                {
                    f.Pos.Y = Fix64.Zero;
                    f.Vel = FixVec2.Zero;
                    f.Action = FighterAction.Landing;
                    f.ActionEnd = s.World + T.Jump.LandingRecovery;
                    f.Jump = JumpKind.None;
                    Emit(VolleyEvent.Land, i, 0, 0, f.Pos.X, Fix64.Zero);
                }
                return;
            }
            if (f.Action == FighterAction.JumpStartup || f.Action == FighterAction.Committed || f.Action == FighterAction.ServeToss ||
                f.Action == FighterAction.ServeApproach || f.Action == FighterAction.ServeHold || f.Action == FighterAction.DigStance)
            {
                f.Action = FighterAction.Neutral;
                f.Jump = JumpKind.None;
            }
            else if (f.Action == FighterAction.Diving)
            {
                f.Action = FighterAction.Floor;
                f.ActionEnd = s.World + T.Dive.Recovery / 2;
            }
            else if (f.Action != FighterAction.Neutral && s.World >= f.ActionEnd)
            {
                f.Action = FighterAction.Neutral;
            }
            f.Vel = FixVec2.Zero;
            ClearCommit(ref f);
            f.Charging = false;
            f.Ring = RingKind.None;
        }
    }
}
