using Duel.Core.AI;
using Duel.Core.Sim;
using VolleyDuel.Sim;

namespace VolleyDuel.AI
{
    public enum DefensePlan : byte
    {
        None,
        Block,
        Dig,
        FakeBlock,
    }

    /// <summary>
    /// The volleyball CPU. Each tick it looks at the match as it appeared ReactionFrames ago, through
    /// <see cref="Visibility"/> (it never sees RNG outcomes, hidden choices or the true flight of a decoy),
    /// extrapolates the visible ball like an eye would, and emits one controller input into the same sim
    /// a human plays. Presses are scheduled on absolute world frames with tier execution noise.
    /// </summary>
    public sealed partial class VolleyCpu : IController<GameState>
    {
        const int Never = int.MinValue / 2;

        readonly VolleySim sim;
        readonly Tuning T;
        readonly CpuTables tables;
        readonly SkillTuning K;
        readonly int me;
        readonly int opp;
        readonly Personality persona;
        readonly FighterDefinition def;
        readonly ReactionBuffer<GameState> history;
        readonly HabitModel attackHabits;
        readonly HabitModel defenseHabits;
        readonly HabitModel fakeHabits;
        Rng rng;

        // Scheduled outputs: the world frame each press should be stamped with.
        int hitPress = Never, hitHoldFrom = Never, hitRelease = Never, jumpPress = Never, feintPress = Never, trickPress = Never, finisherPress = Never;
        bool trickAmp = false;
        int aimStick;
        int aimFrom = Never, aimUntil = Never;
        bool hasMove;
        Fix64 moveX;
        bool stance;

        // Memory keyed by what was seen, so decisions stay stable across frames.
        int ringKey = Never;
        int ringNoise;
        int flightKey = Never;
        Fix64 readError;
        int rallyKey = Never;
        bool serveJump;
        int serveAt;
        int blockKey = Never;
        DefensePlan defense;
        int jumpKey = Never;
        int jumpFrame;
        Fix64 takeoffX;
        int setKey = Never;
        int setChoice; // 0 high, 1 quick, 2 dump
        int attackKey = Never;
        int attackPress;
        bool attackDecided;
        int lastRecordedFlight = Never;
        bool sawOppJump;
        SetType lastOppSet;

        public TierProfile Tier;

        public VolleyCpu(VolleySim sim, CpuTables tables, int player, TierProfile tier, ulong seed)
        {
            this.sim = sim;
            T = sim.T;
            this.tables = tables;
            K = tables.Skill;
            me = player;
            opp = 1 - player;
            def = sim.Def(player);
            persona = tables.Personalities[def.Index];
            Tier = tier;
            history = new ReactionBuffer<GameState>(64);
            attackHabits = new HabitModel(9, 3, tables.HabitDecay);
            defenseHabits = new HabitModel(3, 2, tables.HabitDecay);
            fakeHabits = new HabitModel(1, 2, tables.HabitDecay);
            rng = Rng.Create(seed, 1000 + (ulong)player);
        }

        public int Player => me;

        public FrameInput Decide(in GameState truth)
        {
            history.Push(truth);
            GameState seen = Perceive(truth);
            int next = truth.World + 1;
            hasMove = false;
            stance = false;
            Observe(seen);
            switch (seen.Phase)
            {
                case MatchPhase.PreServe:
                    if (seen.Server == me) ThinkServe(ref seen, next);
                    else MoveTo(VolleySim.FromNet(me, persona.ReceiveDepth));
                    break;
                case MatchPhase.Rally:
                    ThinkRally(ref seen, next);
                    break;
                case MatchPhase.Finisher:
                    ThinkFinisher(ref seen, next);
                    break;
            }
            GuardBlockPress(truth, next);
            return Output(truth, next);
        }

        // ================================================================== perception

        GameState Perceive(in GameState truth)
        {
            ref readonly GameState delayed = ref history.Delayed(Tier.ReactionFrames);
            GameState v = Visibility.ForViewer(delayed, me);
            if (v.Ball.Mode == BallMode.Flight)
            {
                // The eye extrapolates the visible flight up to now.
                for (int w = delayed.World + 1; w <= truth.World; w++) sim.IntegrateBall(ref v.Ball, w);
            }
            v.World = truth.World;
            v.Tick = truth.Tick;
            // Proprioception: the CPU knows where its own body is now; its rings are as seen.
            FighterState mine = truth.FighterAt(me);
            FighterState seenMine = v.FighterAt(me);
            mine.Ring = seenMine.Ring;
            mine.RingCenter = seenMine.RingCenter;
            mine.RingPerfect = seenMine.RingPerfect;
            mine.RingGood = seenMine.RingGood;
            mine.RingPoint = seenMine.RingPoint;
            mine.RingReachable = seenMine.RingReachable;
            mine.RingDive = seenMine.RingDive;
            mine.BlockRingCenter = seenMine.BlockRingCenter;
            mine.BlockRingReveal = seenMine.BlockRingReveal;
            GameState.Fighter(ref v, me) = mine;
            return v;
        }

        /// <summary>Habit learning from what is visible: the opponent's attacks and their answers to ours.</summary>
        void Observe(in GameState seen)
        {
            FighterState watched = seen.FighterAt(opp);
            if (seen.Phase != MatchPhase.Rally || VolleySim.IsDefending(seen, opp) == false) sawOppJump = false;
            if (VolleySim.IsBlockJump(watched.Jump)) sawOppJump = true;
            if (seen.Ball.Mode != BallMode.Flight) return;
            if (seen.Ball.FlightStart == lastRecordedFlight) return;
            if (seen.Ball.Has(BallFlags.QuickSet) && seen.LastToucher == opp) lastOppSet = SetType.Quick;
            else if (seen.Ball.Has(BallFlags.HighSet) && seen.LastToucher == opp) lastOppSet = SetType.High;
            if (!Tier.LearnsHabits) return;
            lastRecordedFlight = seen.Ball.FlightStart;
            if (seen.LastToucher == opp)
            {
                int choice = seen.LastTouch == TouchKind.Spike ? 0 : (seen.LastTouch == TouchKind.Tip ? 1 : (seen.LastTouch == TouchKind.Roll ? 2 : -1));
                if (choice >= 0) attackHabits.Record(AttackContext(seen), choice);
            }
            else if (seen.LastToucher == me && (seen.LastTouch == TouchKind.Spike || seen.LastTouch == TouchKind.Tip || seen.LastTouch == TouchKind.Roll))
            {
                FighterState o = seen.FighterAt(opp);
                bool blocked = VolleySim.IsBlockJump(o.Jump);
                defenseHabits.Record(ScoreBucket(seen, opp), blocked ? 0 : 1);
                // A defender back on the floor by our contact after jumping was a fake hop.
                if (sawOppJump) fakeHabits.Record(0, blocked ? 0 : 1);
            }
        }

        int ScoreBucket(in GameState s, int player)
        {
            int margin = s.Score(player) - s.Score(1 - player);
            return margin < 0 ? 0 : (margin == 0 ? 1 : 2);
        }

        int AttackContext(in GameState s)
        {
            int set = lastOppSet == SetType.Quick ? 1 : (lastOppSet == SetType.High ? 2 : 0);
            return set * 3 + ScoreBucket(s, opp);
        }

        // ================================================================== serving

        void ThinkServe(ref GameState seen, int next)
        {
            FighterState m = seen.FighterAt(me);
            if (m.Action != FighterAction.ServeHold) return;
            if (rallyKey != seen.RallyStartWorld)
            {
                rallyKey = seen.RallyStartWorld;
                serveJump = rng.Chance(persona.JumpServe) && m.Stamina > T.Meters.GassedRecoverAt + 10;
                serveAt = seen.PhaseStart + rng.Range(K.ServeDelayMin, K.ServeDelayMax);
                ClearSchedule();
            }
            Fix64 zoneX = serveJump
                ? VolleySim.FromNet(me, T.Court.HalfLength + T.Serve.JumpApproachDistance + K.JumpServeBackoff * Fix64.FromRatio(3, 10))
                : VolleySim.FromNet(me, T.Serve.StandFromNet);
            MoveTo(zoneX);
            if (next < serveAt) return;
            bool inPlace = Fix64.Abs(m.Pos.X - zoneX) <= K.ArriveTolerance * 3;
            if (!inPlace && next < serveAt + 60) return;
            if (TryServeSpecial(ref seen, next)) return;
            if (serveJump) jumpPress = next;
            else hitPress = next;
            serveAt = int.MaxValue / 2;
        }

        // ================================================================== rally

        void ThinkRally(ref GameState seen, int next)
        {
            FighterState m = seen.FighterAt(me);
            if (m.Action == FighterAction.ServeToss)
            {
                PressOnRing(ref seen, next, InputButtons.Hit, ServeAim());
                return;
            }
            if (m.Action == FighterAction.ServeApproach) return;
            if (m.Action == FighterAction.Airborne)
            {
                ThinkAirborne(ref seen, next);
                return;
            }
            bool defending = VolleySim.IsDefending(seen, me);
            int touch = VolleySim.NextTouch(seen, me);
            if (touch >= 4) return; // a fourth touch is a fault; let it go
            if (m.Ring == RingKind.Bump && (touch == 1 || touch == 3))
            {
                if (touch == 3 && TryApproach(ref seen, next)) return;
                Receive(ref seen, next, touch);
                return;
            }
            if (m.Ring == RingKind.Set)
            {
                SetBall(ref seen, next);
                return;
            }
            if (!defending && touch == 3 && TryApproach(ref seen, next)) return;
            if (defending)
            {
                Defend(ref seen, next);
                return;
            }
            // Our ball but no ring yet: drift under where it will come down.
            if (sim.PredictLanding(seen, out int f, out Fix64 x) && VolleySim.SideOf(x) == me) MoveTo(sim.ClampGrounded(me, x));
        }

        int ServeAim()
        {
            Fix64 roll = rng.NextFix();
            if (roll < persona.ShortAim) return FrameInput.StickMax - 1;
            if (roll > Fix64.One - persona.DeepAim) return -(FrameInput.StickMax - 1);
            return 0;
        }

        /// <summary>Schedules a press for the ring the CPU currently sees, once per ring.</summary>
        void PressOnRing(ref GameState seen, int next, InputButtons button, int aim)
        {
            FighterState m = seen.FighterAt(me);
            if (m.Ring == RingKind.None) return;
            if (ringKey != m.RingCenter)
            {
                ringKey = m.RingCenter;
                ringNoise = rng.Gaussian(Tier.ExecutionSigma).RoundToInt();
                int press = m.RingCenter + ringNoise;
                if (press < next) press = next;
                if (button == InputButtons.Hit) hitPress = press;
                else if (button == InputButtons.Feint) feintPress = press;
                aimStick = aim;
                aimFrom = press - 2;
                aimUntil = m.RingCenter + T.Rings.LockFrames + 1;
                if (aimUntil < press + T.Rings.LockFrames + 1) aimUntil = press + T.Rings.LockFrames + 1;
            }
        }

        void Receive(ref GameState seen, int next, int touch)
        {
            FighterState m = seen.FighterAt(me);
            Fix64 target = EstimateContactX(ref seen, T.Reach.BumpHeight, m.RingCenter);
            MoveTo(target);
            Fix64 gap = Fix64.Abs(target - m.Pos.X);
            int framesLeft = m.RingCenter - next;
            Fix64 walk = def.WalkSpeed * T.Dt * (framesLeft > 0 ? framesLeft : 0);
            bool needDive = gap > T.Reach.BumpReach + walk && gap <= T.Reach.BumpReach + def.DiveRange;
            if (needDive && framesLeft <= 14)
            {
                // Commit to the dive: stick toward the ball with the press.
                aimStick = VolleySim.TowardNet(me, target - m.Pos.X).IsPositive ? FrameInput.StickMax : -FrameInput.StickMax;
                aimFrom = next;
                aimUntil = m.RingCenter + 2;
            }
            else if (touch == 1 && gap <= K.ArriveTolerance * 2 && framesLeft <= K.StanceLead && seen.Ball.Vel.LengthSquared > Fix64.FromInt(100))
            {
                stance = true;
            }
            PressOnRing(ref seen, next, InputButtons.Hit, needDive ? aimStick : 0);
            if (needDive && hitPress != Never) aimUntil = hitPress + 2;
        }

        Fix64 EstimateContactX(ref GameState seen, Fix64 height, int ringCenter)
        {
            if (flightKey != seen.Ball.FlightStart)
            {
                flightKey = seen.Ball.FlightStart;
                readError = rng.Symmetric(Tier.LandingReadError);
            }
            if (!sim.PredictDescent(ref seen, height, me, out int frame, out FixVec2 point, out Fix64 speed)) return seen.FighterAt(me).Pos.X;
            int total = frame - seen.Ball.FlightStart;
            int left = frame - seen.World;
            Fix64 fraction = total <= 0 ? Fix64.Zero : Fix64.Clamp01(Fix64.FromRatio(left, total));
            return sim.ClampGrounded(me, point.X + readError * fraction);
        }

        void SetBall(ref GameState seen, int next)
        {
            FighterState m = seen.FighterAt(me);
            Fix64 target = EstimateContactX(ref seen, def.Height, m.RingCenter);
            MoveTo(target);
            if (setKey != m.RingCenter)
            {
                setKey = m.RingCenter;
                FighterState o = seen.FighterAt(opp);
                Fix64 dumpChance = persona.Dump;
                if (VolleySim.TowardNet(opp, -o.Pos.X) > Fix64.FromInt(4)) dumpChance = dumpChance * 2; // opponent sitting deep
                Fix64 quickChance = persona.QuickSet;
                int oppBlocks = defenseHabits.MostLikely(ScoreBucket(seen, opp), tables.HabitMinimumEvidence);
                if (oppBlocks == 0) quickChance = Fix64.Min(Fix64.One, quickChance + Fix64.FromRatio(2, 10));
                if (rng.Chance(dumpChance)) setChoice = 2;
                else setChoice = rng.Chance(quickChance) ? 1 : 0;
            }
            if (setChoice == 2) PressOnRing(ref seen, next, InputButtons.Feint, 0);
            else PressOnRing(ref seen, next, InputButtons.Hit, setChoice == 1 ? FrameInput.StickMax : 0);
        }

        /// <summary>Plans and runs the approach for our own set: where to stand and when to jump.</summary>
        bool TryApproach(ref GameState seen, int next)
        {
            FighterState m = seen.FighterAt(me);
            if (seen.Ball.Mode != BallMode.Flight || seen.LastToucher != me) return false;
            if (jumpKey != seen.Ball.FlightStart)
            {
                jumpKey = seen.Ball.FlightStart;
                if (!PlanApproach(ref seen, out jumpFrame, out takeoffX))
                {
                    jumpFrame = Never;
                    return false;
                }
                // Hitters vary their tempo a little so the block has to read them (timing battle).
                jumpFrame += rng.Gaussian(Tier.ExecutionSigma).RoundToInt() + rng.Range(-K.TempoVariation, K.TempoVariation + 1);
            }
            if (jumpFrame == Never) return false;
            if (next > jumpFrame + T.Rings.BufferFrames && m.Action != FighterAction.JumpStartup) return false; // missed it: fall back
            MoveTo(takeoffX);
            if (next >= jumpFrame && jumpPress < jumpFrame) jumpPress = next;
            return true;
        }

        bool PlanApproach(ref GameState seen, out int jump, out Fix64 x)
        {
            jump = 0;
            x = Fix64.Zero;
            Fix64 hitHeight = sim.IdealHitHeight(me);
            int contactFrame;
            FixVec2 point;
            if (!sim.PredictDescent(ref seen, hitHeight, me, out contactFrame, out point, out Fix64 speed))
            {
                Fix64 apex = sim.PredictApex(seen, out contactFrame);
                if (apex < T.Court.NetHeight) return false;
                BallState b = seen.Ball;
                for (int w = seen.World + 1; w <= contactFrame; w++) sim.IntegrateBall(ref b, w);
                point = b.Pos;
                if (VolleySim.SideOf(point.X) != me) return false;
            }
            Fix64 h = sim.CurrentJumpHeight(seen, me, false);
            int up = sim.FramesToApex(h);
            jump = contactFrame - T.Jump.StartupFrames - up;
            x = sim.ClampGrounded(me, point.X - T.Reach.HandForward * VolleySim.Dir(me));
            if (jump <= seen.World) jump = seen.World + 1;
            // Refine: try a few jump frames and keep the closest pass.
            Fix64 best = Fix64.MaxValue;
            int bestJump = jump;
            for (int dj = -4; dj <= 4; dj++)
            {
                int j = jump + dj;
                if (j <= seen.World) continue;
                if (sim.PlanJumpContact(seen, me, j, x, out int cf, out FixVec2 cp, out Fix64 d2) && d2 < best)
                {
                    best = d2;
                    bestJump = j;
                }
            }
            if (best == Fix64.MaxValue) return false;
            jump = bestJump;
            return true;
        }

        void ThinkAirborne(ref GameState seen, int next)
        {
            FighterState m = seen.FighterAt(me);
            if (VolleySim.IsBlockJump(m.Jump) || m.Jump == JumpKind.FakeHop) return;
            if (m.Ring != RingKind.Spike && m.Ring != RingKind.Serve) return;
            if (attackKey != m.RingCenter)
            {
                attackKey = m.RingCenter;
                attackDecided = false;
                ringNoise = rng.Gaussian(Tier.ExecutionSigma).RoundToInt();
                int press = m.RingCenter + ringNoise;
                if (press < next) press = next;
                attackPress = press;
                if (m.Jump == JumpKind.JumpServe)
                {
                    SwingSpike(next, press, ServeAim());
                    attackDecided = true;
                    return;
                }
                if (TryAttackTrick(ref seen, next, press))
                {
                    attackDecided = true;
                    return;
                }
                // Wind up a spike by default; the spike-or-tip call is made at the last moment.
                SwingSpike(next, press, 0);
            }
            if (attackDecided || next < attackPress - 3) return;
            attackDecided = true;
            AttackChoice choice = ChooseAttack(ref seen, out int aim);
            if (choice == AttackChoice.Spike)
            {
                aimStick = aim;
                return;
            }
            // Switch to a tip or roll: FEINT commits it while the spike is still cocked.
            feintPress = attackPress;
            hitRelease = attackPress + 12;
            aimStick = choice == AttackChoice.Roll ? -FrameInput.StickMax : FrameInput.StickMax / 2;
            aimFrom = attackPress - 2;
            aimUntil = m.RingCenter + T.Rings.LockFrames + 2;
        }

        void SwingSpike(int next, int release, int aim)
        {
            int holdFrom = release - def.ChargeFrames;
            if (holdFrom < next) holdFrom = next;
            if (holdFrom >= release) holdFrom = release - 1;
            if (holdFrom < next)
            {
                // No time to wind up: tap.
                holdFrom = next;
                release = next + 1;
            }
            hitHoldFrom = holdFrom;
            hitRelease = release;
            aimStick = aim;
            aimFrom = release - 2;
            aimUntil = release + T.Rings.LockFrames + 2;
        }

        /// <summary>The Read from the attacker's side: spike, tip or roll, and depth.</summary>
        AttackChoice ChooseAttack(ref GameState seen, out int aim)
        {
            FighterState o = seen.FighterAt(opp);
            bool blocking = VolleySim.IsBlockJump(o.Jump) && (o.Action == FighterAction.Airborne || o.Action == FighterAction.JumpStartup);
            Fix64 oppDepth = Fix64.Abs(o.Pos.X);
            bool weakSet = seen.Ball.Has(BallFlags.WeakSet);
            Fix64 tip = persona.Tip;
            Fix64 roll = persona.Roll;
            if (blocking)
            {
                // A visible jump might be a fake hop: tip into it only as far as we believe it is real.
                Fix64 fakeShare = fakeHabits.Share(0, 1);
                Fix64 believe = Fix64.One - fakeShare;
                tip += Fix64.FromRatio(5, 10) * believe;
                roll += Fix64.FromRatio(2, 10) * believe;
            }
            if (weakSet) tip += Fix64.FromRatio(3, 10);
            int predicted = defenseHabits.MostLikely(ScoreBucket(seen, opp), tables.HabitMinimumEvidence);
            if (predicted >= 0 && rng.Chance(Tier.ReadsYou))
            {
                if (predicted == 0) tip += Fix64.FromRatio(3, 10); // they block: tip behind it
                else tip = tip / 2;                                // they dig: hit through them
            }
            Fix64 r = rng.NextFix() * (Fix64.One + tip + roll);
            AttackChoice choice;
            if (r < tip) choice = AttackChoice.Tip;
            else if (r < tip + roll) choice = AttackChoice.Roll;
            else choice = AttackChoice.Spike;
            if (choice == AttackChoice.Tip && oppDepth < Fix64.FromRatio(25, 10) && !blocking) choice = AttackChoice.Roll;
            // Depth: hit away from where the defender stands.
            if (oppDepth > Fix64.FromRatio(42, 10)) aim = FrameInput.StickMax - 1;
            else if (oppDepth < Fix64.FromRatio(30, 10)) aim = -(FrameInput.StickMax - 1);
            else
            {
                Fix64 a = rng.NextFix();
                aim = a < persona.ShortAim ? FrameInput.StickMax - 1 : (a > Fix64.One - persona.DeepAim ? -(FrameInput.StickMax - 1) : 0);
            }
            return choice;
        }

        // ================================================================== defense

        void Defend(ref GameState seen, int next)
        {
            FighterState m = seen.FighterAt(me);
            bool attackComing = m.BlockRingCenter > seen.World - 2 && m.BlockRingCenter < seen.World + 200;
            if (!attackComing)
            {
                MoveTo(VolleySim.FromNet(me, persona.DigDepth));
                return;
            }
            if (blockKey != m.BlockRingCenter)
            {
                blockKey = m.BlockRingCenter;
                defense = ChooseDefense(ref seen);
                ScheduleDefense(ref seen, next);
            }
            switch (defense)
            {
                case DefensePlan.Block:
                    MoveTo(VolleySim.FromNet(me, K.BlockSpot));
                    break;
                case DefensePlan.FakeBlock:
                    if (next < feintPress + 2) MoveTo(VolleySim.FromNet(me, K.BlockSpot));
                    else MoveTo(VolleySim.FromNet(me, K.ShortDigDepth));
                    break;
                default:
                    MoveTo(VolleySim.FromNet(me, DigDepthFor(ref seen)));
                    if (next >= m.BlockRingCenter - K.StanceLead && Fix64.Abs(m.Pos.X - moveX) <= K.ArriveTolerance * 2) stance = true;
                    break;
            }
        }

        Fix64 DigDepthFor(ref GameState seen)
        {
            int predicted = attackHabits.MostLikely(AttackContext(seen), tables.HabitMinimumEvidence);
            if (predicted == 1) return K.ShortDigDepth + Fix64.Half;
            return Fix64.Lerp(persona.DigDepth, K.DeepDigDepth, Fix64.Half);
        }

        /// <summary>The Read from the defender's side, with habit prediction and the tier's read chance.</summary>
        DefensePlan ChooseDefense(ref GameState seen)
        {
            int predicted = attackHabits.MostLikely(AttackContext(seen), tables.HabitMinimumEvidence);
            if (predicted < 0) predicted = 0; // prior: most attacks are spikes
            if (rng.Chance(Tier.ReadsYou)) return predicted == 0 ? DefensePlan.Block : DefensePlan.Dig;
            Fix64 r = rng.NextFix();
            if (r < persona.FakeBlock) return DefensePlan.FakeBlock;
            if (r < persona.FakeBlock + persona.Block) return DefensePlan.Block;
            return DefensePlan.Dig;
        }

        void ScheduleDefense(ref GameState seen, int next)
        {
            FighterState m = seen.FighterAt(me);
            int noise = rng.Gaussian(Tier.ExecutionSigma * K.BlockTimingScale).RoundToInt();
            if (defense == DefensePlan.Block || defense == DefensePlan.FakeBlock)
            {
                // Only block if the net is reachable before the jump has to start.
                Fix64 spot = VolleySim.FromNet(me, K.BlockSpot);
                Fix64 h = sim.CurrentJumpHeight(seen, me, true);
                int press = m.BlockRingCenter - T.Jump.BlockStartupFrames - sim.FramesToApex(h) + noise;
                if (defense == DefensePlan.FakeBlock) press = m.BlockRingCenter - 16 + noise;
                Fix64 travel = Fix64.Abs(m.Pos.X - spot);
                Fix64 reach = def.WalkSpeed * T.Dt * (press - next);
                if (travel > reach + T.Block.RangeFromNet - K.BlockSpot)
                {
                    defense = DefensePlan.Dig;
                    return;
                }
                if (defense == DefensePlan.Block && TryDefenseTrick(ref seen, next, press)) return;
                if (defense == DefensePlan.Block) jumpPress = press < next ? next : press;
                else feintPress = press < next ? next : press;
            }
        }

        /// <summary>Cancels a scheduled block or fake hop if the CPU is not at the net when it must jump.</summary>
        void GuardBlockPress(in GameState truth, int next)
        {
            if (defense != DefensePlan.Block && defense != DefensePlan.FakeBlock) return;
            if (next != jumpPress && next != feintPress) return;
            FighterState mine = truth.FighterAt(me);
            if (Fix64.Abs(mine.Pos.X) <= T.Block.RangeFromNet) return;
            if (next == jumpPress) jumpPress = Never;
            if (next == feintPress) feintPress = Never;
            defense = DefensePlan.Dig;
        }

        // ================================================================== output

        void MoveTo(Fix64 x)
        {
            hasMove = true;
            moveX = x;
        }

        void ClearSchedule()
        {
            hitPress = hitHoldFrom = hitRelease = jumpPress = feintPress = trickPress = finisherPress = Never;
            aimFrom = aimUntil = Never;
        }

        FrameInput Output(in GameState truth, int next)
        {
            InputButtons b = InputButtons.None;
            if (next == hitPress) b |= InputButtons.Hit;
            if (hitHoldFrom != Never && next >= hitHoldFrom && next < hitRelease) b |= InputButtons.Hit;
            if (next == jumpPress) b |= InputButtons.Jump;
            if (next == feintPress) b |= InputButtons.Feint;
            if (next == trickPress)
            {
                b |= InputButtons.Trick;
                if (trickAmp) b |= InputButtons.Amp;
            }
            if (next == finisherPress) b |= InputButtons.Finisher;

            int stick = 0;
            FighterState mine = truth.FighterAt(me);
            if (next >= aimFrom && next <= aimUntil)
            {
                stick = aimStick;
            }
            else if (hasMove)
            {
                Fix64 dx = VolleySim.TowardNet(me, moveX - mine.Pos.X);
                if (Fix64.Abs(dx) > K.ArriveTolerance)
                {
                    stick = (dx * K.StickGain).RoundToInt();
                    if (stick > FrameInput.StickMax) stick = FrameInput.StickMax;
                    if (stick < -FrameInput.StickMax) stick = -FrameInput.StickMax;
                    if (stick > 0 && stick < 2) stick = 2;
                    if (stick < 0 && stick > -2) stick = -2;
                }
                else if (stance)
                {
                    stick = -FrameInput.StickMax;
                }
            }
            else if (stance)
            {
                stick = -FrameInput.StickMax;
            }
            return FrameInput.Create(stick, 0, b);
        }
    }
}
