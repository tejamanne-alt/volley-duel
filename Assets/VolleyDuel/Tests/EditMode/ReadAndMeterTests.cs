using Duel.Core.Sim;
using NUnit.Framework;
using VolleyDuel.Sim;

namespace VolleyDuel.Tests
{
    /// <summary>The Read: spike or tip against block or dig.</summary>
    public class ReadTests
    {
        static Rig SpikeIntoBlock(string attacker, string blocker, int delta, int stick = 7)
        {
            var rig = new Rig(attacker, blocker);
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump);
            Scenarios.ScheduleBlock(rig, 1, contact, delta);
            rig.PressAt(0, InputButtons.Hit, contact - 1, 1);
            rig.StickAt(0, stick, contact - 4, contact + 6);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Point), 600));
            return rig;
        }

        [Test]
        public void SpikeIntoAPerfectBlockIsStuffed()
        {
            Rig rig = SpikeIntoBlock("arjun", "rafa", 0);
            Assert.IsTrue(rig.Has(VolleyEvent.BlockTouch), "the spike met the hands");
            Assert.AreEqual((int)BlockOutcome.Stuff, rig.First(VolleyEvent.BlockTouch).A);
            SimEvent p = rig.First(VolleyEvent.Point);
            Assert.AreEqual(1, p.Actor);
            Assert.AreEqual((int)PointReason.Stuff, p.A);
            Assert.IsTrue(rig.HasCallout(Callout.Stuffed));
            Assert.GreaterOrEqual(rig.F(1).Surge, rig.T.Meters.GainStuff, "stuff block +50 Surge");
        }

        [Test]
        public void SpikeIntoAGoodBlockSoftensIntoAFreeBallForTheBlocker()
        {
            var rig = new Rig("arjun", "rafa");
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump);
            Scenarios.ScheduleBlock(rig, 1, contact, 4);
            rig.PressAt(0, InputButtons.Hit, contact - 1, 1);
            rig.StickAt(0, 7, contact - 4, contact + 6);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.BlockTouch), 400));
            Assert.AreEqual((int)BlockOutcome.Soft, rig.First(VolleyEvent.BlockTouch).A);
            rig.Run(2);
            Assert.AreEqual(1, rig.S.Possession, "free ball on the blocker's side");
            Assert.AreEqual(0, rig.S.Touches, "a block touch is free");
            Assert.AreEqual((int)TouchKind.Block, (int)rig.S.LastTouch);
        }

        [Test]
        public void LateBlockGetsTooledForAPointToTheHitter()
        {
            Rig rig = SpikeIntoBlock("arjun", "rafa", 9);
            Assert.AreEqual((int)BlockOutcome.Tooled, rig.First(VolleyEvent.BlockTouch).A);
            SimEvent p = rig.First(VolleyEvent.Point);
            Assert.AreEqual(0, p.Actor);
            Assert.AreEqual((int)PointReason.Tooled, p.A);
            Assert.IsTrue(rig.HasCallout(Callout.Tooled));
        }

        [Test]
        public void FortressGoodBlocksStuff()
        {
            Rig rig = SpikeIntoBlock("arjun", "defne", 5);
            Assert.AreEqual((int)BlockOutcome.Stuff, rig.First(VolleyEvent.BlockTouch).A, "Defne's Good blocks stuff the ball");
        }

        [Test]
        public void TipDropsBehindAnAirborneBlocker()
        {
            var rig = new Rig("arjun", "rafa");
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump);
            Scenarios.ScheduleBlock(rig, 1, contact, 0);
            rig.PressAt(0, InputButtons.Feint, contact);
            rig.StickAt(0, 4, contact - 4, contact + 6);
            Assert.IsTrue(rig.RunUntilPoint(600));
            Assert.IsFalse(rig.Has(VolleyEvent.BlockTouch), "tips go over the block");
            SimEvent p = rig.First(VolleyEvent.Point);
            Assert.AreEqual(0, p.Actor);
            Assert.AreEqual((int)PointReason.Kill, p.A);
            Assert.IsFalse(rig.HasCallout(Callout.ReadIt), "the defender was in the air: no read");
        }

        [Test]
        public void TipIntoAGroundedDefenderIsAReadTip()
        {
            var rig = new Rig("arjun", "rafa");
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump);
            rig.PressAt(0, InputButtons.Feint, contact);
            rig.StickAt(0, 4, contact - 4, contact + 6);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact), 300));
            Assert.IsTrue(rig.HasCallout(Callout.ReadIt));
            Assert.AreEqual(rig.T.Meters.GainReadTip, rig.F(1).Surge, "defender gains 20 Surge");
            Assert.IsTrue(rig.S.Ball.Has(BallFlags.FreeBall));
        }

        [Test]
        public void BlockJumpInsideATipsBaitWindowBites()
        {
            var rig = new Rig("arjun", "rin");
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump);
            rig.PressAt(0, InputButtons.Feint, contact - 3);
            rig.StickAt(0, 4, contact - 6, contact + 6);
            // Rin reacts to the swing by jumping inside the fake-spike bait window.
            rig.Place(1, VolleySim.FromNet(1, Fix64.Parse("0.4")));
            rig.PressAt(1, InputButtons.Jump, contact - 1);
            Fix64 before = rig.F(1).Stamina;
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Bite), 300));
            SimEvent bite = rig.First(VolleyEvent.Bite);
            Assert.AreEqual(1, bite.Actor);
            Assert.AreEqual((int)FeintKind.Tip, bite.A);
            Assert.AreEqual(rig.T.Meters.GainBiteDrawn, rig.F(0).Surge, "feinter gains 10 Surge");
            Assert.LessOrEqual(rig.F(1).Stamina, before - Fix64.FromInt(rig.T.Feint.BiteStamina) - rig.T.Meters.CostJump + Fix64.One, "bite costs 15 Stamina on top of the jump");
            Assert.AreEqual(rig.T.Feint.BiteRecovery, (int)rig.F(1).ExtraRecovery, "and 8 frames of landing recovery");
        }
    }

    public class MeterTests
    {
        [Test]
        public void JumpingCostsStaminaScaledByFighter()
        {
            var rig = new Rig("rin", "rafa");
            rig.InRally(new FixVec2(Fix64.FromInt(3), Fix64.FromInt(6)), FixVec2.Zero, 1, TouchKind.Set, 1, 2);
            rig.Place(0, Fix64.FromInt(-3));
            rig.PressAt(0, InputButtons.Jump, rig.S.World + 1);
            rig.Run(2);
            Assert.AreEqual(rig.T.Meters.StaminaMax - rig.T.Meters.CostJump, rig.F(0).Stamina, "jump costs 12");

            var rafa = new Rig("rafa", "rin");
            rafa.InRally(new FixVec2(Fix64.FromInt(3), Fix64.FromInt(6)), FixVec2.Zero, 1, TouchKind.Set, 1, 2);
            rafa.Place(0, Fix64.FromInt(-3));
            rafa.PressAt(0, InputButtons.Jump, rafa.S.World + 1);
            rafa.Run(2);
            Assert.Less(rafa.F(0).Stamina, rig.F(0).Stamina, "Rafa has the hungriest Stamina");
        }

        [Test]
        public void StaminaRefillsWhileGroundedAndAtEachDeadBall()
        {
            Rig rig = Scenarios.DropOnFloor("3", 0);
            rig.F(0).Stamina = Fix64.FromInt(10);
            rig.F(1).Stamina = Fix64.FromInt(10);
            rig.Run(30);
            Assert.That(rig.F(0).Stamina.ToString(0), Is.EqualTo("20"), "20 per second while grounded and idle");
            Assert.IsTrue(rig.RunUntilPoint());
            Assert.GreaterOrEqual(rig.F(1).Stamina, Fix64.FromInt(10) + rig.T.Meters.RefillDeadBall, "plus 40 at each dead ball");
        }

        [Test]
        public void GassedAtZeroLowersJumpsAndDivesUntilThirty()
        {
            var rig = new Rig("rin", "rafa");
            rig.InRally(new FixVec2(Fix64.FromInt(3), Fix64.FromInt(6)), FixVec2.Zero, 1, TouchKind.Set, 1, 2);
            rig.F(0).Stamina = Fix64.FromInt(5);
            rig.Place(0, Fix64.FromInt(-3));
            Fix64 fresh = rig.Sim.CurrentJumpHeight(rig.S, 0, false);
            rig.PressAt(0, InputButtons.Jump, rig.S.World + 1);
            rig.Run(2);
            Assert.IsTrue(rig.F(0).Gassed);
            Assert.IsTrue(rig.Has(VolleyEvent.Gassed));
            Assert.AreEqual((fresh * rig.T.Meters.GassedJumpScale).ToString(3), rig.Sim.CurrentJumpHeight(rig.S, 0, false).ToString(3), "the next jump is 25% lower while Gassed");
            rig.F(0).Stamina = Fix64.FromInt(29);
            rig.F(0).Action = FighterAction.Neutral;
            rig.F(0).Pos.Y = Fix64.Zero;
            rig.Run(12);
            Assert.IsFalse(rig.F(0).Gassed, "recovers once Stamina refills to 30");
        }

        [Test]
        public void AceGivesTheServerFortyAndTheReceiverTen()
        {
            Rig rig = Scenarios.DropOnFloor("3", 0, TouchKind.FloatServe);
            rig.S.LastSendKind = TouchKind.FloatServe;
            Assert.IsTrue(rig.RunUntilPoint());
            Assert.AreEqual((int)PointReason.Ace, rig.First(VolleyEvent.Point).A);
            Assert.AreEqual(rig.T.Meters.GainAce, rig.F(0).Surge);
            Assert.AreEqual(rig.T.Meters.GainScoredOn, rig.F(1).Surge);
            Assert.IsTrue(rig.HasCallout(Callout.Ace));
        }

        [Test]
        public void KillGivesThirty()
        {
            Rig rig = Scenarios.DropOnFloor("3", 0);
            Assert.IsTrue(rig.RunUntilPoint());
            Assert.AreEqual(rig.T.Meters.GainKill, rig.F(0).Surge);
            Assert.IsTrue(rig.HasCallout(Callout.Kill));
        }

        [Test]
        public void SurgeCapsAtThreeHundred()
        {
            Rig rig = Scenarios.DropOnFloor("3", 0);
            rig.F(0).Surge = 290;
            Assert.IsTrue(rig.RunUntilPoint());
            Assert.AreEqual(rig.T.Meters.SurgeMax, rig.F(0).Surge);
        }

        [Test]
        public void SimpleModeHalvesSurgeGainAndWidensRings()
        {
            var rig = new Rig("rafa", "rafa", 7, simple0: true);
            rig.InRally(new FixVec2(Fix64.FromInt(3), Fix64.FromInt(2)), new FixVec2(Fix64.Zero, -Fix64.One), 0, TouchKind.Spike, 1, 0);
            rig.Place(0, Fix64.FromInt(-7));
            rig.Place(1, Fix64.FromInt(7));
            Assert.IsTrue(rig.RunUntilPoint());
            Assert.AreEqual(rig.T.Meters.GainKill / 2, rig.F(0).Surge);

            var ring = new Rig("rafa", "rafa", 7, simple0: true);
            ring.InRally(new FixVec2(Fix64.Parse("-1.6"), Fix64.FromInt(4)), FixVec2.Zero, 0, TouchKind.Bump, 0, 1);
            ring.Place(0, Fix64.Parse("-1.6"));
            ring.Step();
            FighterDefinition d = ring.Def(0);
            Assert.Greater(ring.F(0).RingGood, d.GoodControl, "Simple mode widens the rings");
        }

        [Test]
        public void ThreeStraightPointsPutYouOnARunAndBreakingItPaysABar()
        {
            Rig rig = Scenarios.DropOnFloor("3", 0);
            rig.S.Streak0 = 2;
            Assert.IsTrue(rig.RunUntilPoint());
            Assert.IsTrue(rig.Has(VolleyEvent.OnARun));
            Assert.IsTrue(rig.S.RunArmed0, "next attack gets the wider Perfect window");

            Rig breaker = Scenarios.DropOnFloor("-3", 1);
            breaker.S.Streak0 = 4;
            breaker.S.RunArmed0 = true;
            Assert.IsTrue(breaker.RunUntilPoint());
            Assert.IsTrue(breaker.Has(VolleyEvent.RunBroken));
            Assert.AreEqual(breaker.T.OnRun.BreakerSurge + breaker.T.Meters.GainKill, breaker.F(1).Surge, "breaker gains a full bar (plus the kill)");
            Assert.IsFalse(breaker.S.RunArmed0);
        }
    }

    public class WorldClockTests
    {
        [Test]
        public void KillFreezesTheWorldForEightFrames()
        {
            Rig rig = Scenarios.DropOnFloor("3", 0);
            Assert.IsTrue(rig.RunUntilPoint());
            int world = rig.S.World;
            int tick = rig.S.Tick;
            rig.Run(rig.T.Impacts.For(Callout.Kill).HitStop);
            Assert.AreEqual(world, rig.S.World, "world frozen during hit-stop");
            Assert.AreEqual(tick + 8, rig.S.Tick, "ticks keep counting for the netcode");
            rig.Run(1);
            Assert.AreEqual(world + 1, rig.S.World);
        }

        [Test]
        public void StuffBlockRunsAtHalfSpeedAfterItsHitStop()
        {
            var rig = new Rig("arjun", "rafa");
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump);
            Scenarios.ScheduleBlock(rig, 1, contact, 0);
            rig.PressAt(0, InputButtons.Hit, contact - 1, 1);
            rig.StickAt(0, 7, contact - 4, contact + 6);
            Assert.IsTrue(rig.RunUntil(r => r.HasCallout(Callout.Stuffed), 400));
            rig.Run(12);
            int world = rig.S.World;
            rig.Run(8);
            Assert.AreEqual(world + 4, rig.S.World, "half speed: one world frame every two ticks");
        }

        [Test]
        public void SetPointServeFliesAtThirdSpeedUntilItCrosses()
        {
            var rig = new Rig().ToServe(0);
            rig.S.Score0 = 6;
            rig.S.Score1 = 2;
            rig.PressAt(0, InputButtons.Hit, rig.S.World + 2);
            Assert.IsTrue(rig.RunUntil(r => r.F(0).Ring == RingKind.Serve));
            rig.PressAt(0, InputButtons.Hit, rig.F(0).RingCenter);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact)));
            Assert.AreEqual(3, rig.S.SlowDiv);
            int world = rig.S.World;
            rig.Run(9);
            Assert.AreEqual(world + 3, rig.S.World, "third speed while the serve is in flight");
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.NetCross), 600));
            Assert.AreEqual(1, rig.S.SlowDiv, "back to full speed once over the net");
        }
    }
}
