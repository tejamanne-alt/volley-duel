using Duel.Core.Sim;
using NUnit.Framework;
using VolleyDuel.Sim;

namespace VolleyDuel.Tests
{
    /// <summary>Shared scenario builders for touches and the Read.</summary>
    static class Scenarios
    {
        /// <summary>Ball dropping straight onto the floor at <paramref name="x"/>, last played by <paramref name="lastToucher"/>.</summary>
        public static Rig DropOnFloor(string x, int lastToucher, TouchKind lastTouch = TouchKind.Spike, string f0 = "rafa", string f1 = "rafa")
        {
            var rig = new Rig(f0, f1);
            Fix64 bx = Fix64.Parse(x);
            rig.InRally(new FixVec2(bx, Fix64.FromInt(2)), new FixVec2(Fix64.Zero, -Fix64.One), lastToucher, lastTouch, VolleySim.SideOf(bx), 0);
            // Park both fighters far from the ball so nobody can play it.
            rig.Place(0, Fix64.Parse("-7"));
            rig.Place(1, Fix64.Parse("7"));
            return rig;
        }

        /// <summary>
        /// A set ball falling onto <paramref name="attacker"/>'s side, the attacker standing at the takeoff spot with a
        /// jump and swing scheduled so the swing meets the ball. Returns the planned contact frame.
        /// </summary>
        public static int SetUpSpike(Rig rig, int attacker, out int jumpFrame)
        {
            Fix64 ballX = VolleySim.FromNet(attacker, Fix64.Parse("0.8"));
            rig.InRally(new FixVec2(ballX, Fix64.Parse("4.6")), FixVec2.Zero, attacker, TouchKind.Set, attacker, 2, BallFlags.HighSet);
            Fix64 takeoff = ballX - rig.T.Reach.HandForward * VolleySim.Dir(attacker);
            rig.Place(attacker, takeoff);
            rig.Place(1 - attacker, VolleySim.FromNet(1 - attacker, Fix64.Parse("4.5")));
            jumpFrame = 0;
            int contact = 0;
            Fix64 best = Fix64.MaxValue;
            for (int j = rig.S.World + 2; j < rig.S.World + 40; j++)
            {
                if (rig.Sim.PlanJumpContact(rig.S, attacker, j, takeoff, out int cf, out FixVec2 cp, out Fix64 d2) && d2 < best)
                {
                    best = d2;
                    jumpFrame = j;
                    contact = cf;
                }
            }
            Assert.Greater(jumpFrame, 0, "a jump plan exists for the set");
            rig.PressAt(attacker, InputButtons.Jump, jumpFrame);
            return contact;
        }

        /// <summary>Schedules a block jump whose apex lands <paramref name="delta"/> frames after the hitter's contact.</summary>
        public static void ScheduleBlock(Rig rig, int blocker, int contact, int delta)
        {
            rig.Place(blocker, VolleySim.FromNet(blocker, Fix64.Parse("0.4")));
            Fix64 h = rig.Sim.CurrentJumpHeight(rig.S, blocker, true);
            int press = contact + delta - rig.T.Jump.BlockStartupFrames - rig.Sim.FramesToApex(h);
            rig.PressAt(blocker, InputButtons.Jump, press);
        }
    }

    public class RallyFlowTests
    {
        [Test]
        public void BallLandingInScoresForTheOtherSide()
        {
            Rig rig = Scenarios.DropOnFloor("3", 0);
            Assert.IsTrue(rig.RunUntilPoint());
            SimEvent p = rig.First(VolleyEvent.Point);
            Assert.AreEqual(0, p.Actor, "landed on P1's floor");
            Assert.AreEqual((int)PointReason.Kill, p.A);
            Assert.AreEqual(1, rig.S.Score0);
        }

        [Test]
        public void RallyWinnerServesNext()
        {
            Rig rig = Scenarios.DropOnFloor("-3", 1);
            rig.S.Server = 0;
            Assert.IsTrue(rig.RunUntilPoint());
            Assert.AreEqual(1, rig.S.Server, "rally scoring: the rally winner serves next");
            Assert.IsTrue(rig.RunUntil(r => r.S.Phase == MatchPhase.PreServe));
            Assert.AreEqual(FighterAction.ServeHold, rig.F(1).Action);
        }

        [Test]
        public void BallOutLongIsAPointAgainstTheLastToucher()
        {
            Rig rig = Scenarios.DropOnFloor("6.5", 0);
            Assert.IsTrue(rig.RunUntilPoint());
            Assert.AreEqual(0, rig.First(VolleyEvent.BallLanded).A, "out");
            Assert.AreEqual(1, rig.First(VolleyEvent.Point).Actor);
        }

        [Test]
        public void BallTouchingTheLineIsIn()
        {
            Rig rig = Scenarios.DropOnFloor("6.08", 0);
            Assert.IsTrue(rig.RunUntilPoint());
            Assert.AreEqual(1, rig.First(VolleyEvent.BallLanded).A, "a ball touching the line is in");
            Assert.AreEqual(0, rig.First(VolleyEvent.Point).Actor);
        }

        [Test]
        public void WinningTheSetAtSevenMovesToTheNextSetWithTheFirstReceiverServing()
        {
            Rig rig = Scenarios.DropOnFloor("3", 0);
            rig.S.Score0 = 6;
            rig.S.Score1 = 3;
            rig.S.FirstServerThisSet = 0;
            rig.F(1).Surge = 120;
            rig.F(0).Stamina = Fix64.FromInt(20);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.SetWon)));
            Assert.AreEqual(1, rig.S.SetsWon0);
            Assert.IsTrue(rig.RunUntil(r => r.S.Phase == MatchPhase.PreServe && r.S.SetIndex == 1));
            Assert.AreEqual(0, rig.S.Score0);
            Assert.AreEqual(1, rig.S.Server, "whoever received first in one set serves first in the next");
            Assert.AreEqual(rig.T.Meters.StaminaMax, rig.F(0).Stamina, "Stamina resets each set");
            Assert.GreaterOrEqual(rig.F(1).Surge, 120, "Surge carries over between sets");
        }

        [Test]
        public void SecondSetWinEndsABestOfThree()
        {
            Rig rig = Scenarios.DropOnFloor("3", 0);
            rig.S.SetsWon0 = 1;
            rig.S.Score0 = 8;
            rig.S.Score1 = 8;
            Assert.IsTrue(rig.RunUntil(r => r.S.Phase == MatchPhase.MatchOver));
            Assert.AreEqual(0, rig.S.Winner);
            Assert.AreEqual(9, rig.S.Score0, "at 8-8 the next point wins, capped at 9");
            Assert.IsTrue(rig.Has(VolleyEvent.MatchWon));
        }

        [Test]
        public void DroppedServeTossLosesThePoint()
        {
            var rig = new Rig().ToServe(0);
            rig.PressAt(0, InputButtons.Hit, rig.S.World + 2);
            Assert.IsTrue(rig.RunUntilPoint());
            Assert.IsTrue(rig.Has(VolleyEvent.Toss));
            Assert.AreEqual(1, rig.First(VolleyEvent.Point).Actor);
        }

        [Test]
        public void FloatServeOnTheRingCrossesTheNet()
        {
            var rig = new Rig().ToServe(0);
            rig.PressAt(0, InputButtons.Hit, rig.S.World + 2);
            Assert.IsTrue(rig.RunUntil(r => r.F(0).Ring == RingKind.Serve));
            rig.PressAt(0, InputButtons.Hit, rig.F(0).RingCenter);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact)));
            SimEvent c = rig.First(VolleyEvent.Contact);
            Assert.AreEqual((int)TouchKind.FloatServe, c.A);
            Assert.AreEqual(TimingGrade.Perfect, (TimingGrade)(c.B & 0xF));
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.NetCross)));
            Assert.AreEqual(1, rig.S.Possession, "crossing the net hands the receiver a fresh count");
            Assert.AreEqual(0, rig.S.Touches);
        }
    }

    public class FaultTests
    {
        [Test]
        public void DriftingIntoTheNetIsANetTouch()
        {
            Rig rig = Scenarios.DropOnFloor("3", 1);
            rig.S.Ball.Pos = new FixVec2(Fix64.FromInt(4), Fix64.FromInt(6));
            rig.F(0).Action = FighterAction.Airborne;
            rig.F(0).Jump = JumpKind.Approach;
            rig.F(0).Pos = new FixVec2(Fix64.Parse("-0.4"), Fix64.Parse("0.4"));
            rig.F(0).Vel = new FixVec2(Fix64.Parse("1.3"), Fix64.FromInt(2));
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Fault), 120));
            Assert.AreEqual((int)FaultKind.NetTouch, rig.First(VolleyEvent.Fault).A);
            Assert.AreEqual(1, rig.First(VolleyEvent.Point).Actor);
        }

        [Test]
        public void LandingAcrossTheCenterLineIsAFault()
        {
            Rig rig = Scenarios.DropOnFloor("3", 1);
            rig.S.Ball.Pos = new FixVec2(Fix64.FromInt(4), Fix64.FromInt(6));
            rig.F(0).Action = FighterAction.Airborne;
            rig.F(0).Jump = JumpKind.Approach;
            rig.F(0).Pos = new FixVec2(Fix64.Parse("0.3"), Fix64.Parse("0.02"));
            rig.F(0).Vel = new FixVec2(Fix64.Zero, -Fix64.One);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Fault), 30));
            Assert.AreEqual((int)FaultKind.CenterLine, rig.First(VolleyEvent.Fault).A);
        }

        [Test]
        public void FourthTouchIsAFault()
        {
            var rig = new Rig();
            rig.InRally(new FixVec2(Fix64.Parse("-3"), Fix64.FromInt(3)), FixVec2.Zero, 0, TouchKind.Save, 0, 3);
            rig.Place(0, Fix64.Parse("-3"));
            rig.Place(1, Fix64.FromInt(4));
            Assert.IsTrue(rig.RunUntil(r => r.F(0).Ring != RingKind.None, 60));
            rig.PressAt(0, InputButtons.Hit, rig.F(0).RingCenter);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Fault), 200));
            Assert.AreEqual((int)FaultKind.FourHits, rig.First(VolleyEvent.Fault).A);
            Assert.AreEqual(1, rig.First(VolleyEvent.Point).Actor);
        }

        [Test]
        public void JumpServeTakingOffInsideTheCourtIsAFootFault()
        {
            var rig = new Rig().ToServe(0);
            rig.Place(0, Fix64.Parse("-6.2"), FighterAction.ServeHold);
            rig.PressAt(0, InputButtons.Jump, rig.S.World + 2);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Fault), 200));
            Assert.AreEqual((int)FaultKind.FootFault, rig.First(VolleyEvent.Fault).A);
        }

        [Test]
        public void JumpServeFromDeepInTheZoneIsLegal()
        {
            var rig = new Rig().ToServe(0);
            rig.Place(0, Fix64.Parse("-6.95"), FighterAction.ServeHold);
            rig.PressAt(0, InputButtons.Jump, rig.S.World + 2);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Takeoff), 200));
            Assert.IsFalse(rig.Has(VolleyEvent.Fault));
        }
    }

    public class TouchTests
    {
        [Test]
        public void ReceivePopsTheBallUpOnOwnSideTowardTheSettingSpot()
        {
            var rig = new Rig("rafa", "rin");
            rig.InRally(new FixVec2(Fix64.FromInt(-2), Fix64.Parse("3.5")), new FixVec2(Fix64.FromInt(6), Fix64.One), 0, TouchKind.FloatServe, 0, 0, BallFlags.Serve);
            rig.Place(0, Fix64.FromInt(-6));
            rig.Step();
            Assert.AreEqual(RingKind.Bump, rig.F(1).Ring, "receiver gets a bump ring");
            rig.Place(1, rig.F(1).RingPoint.X);
            rig.Step();
            rig.PressAt(1, InputButtons.Hit, rig.F(1).RingCenter);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact), 200));
            SimEvent c = rig.First(VolleyEvent.Contact);
            Assert.AreEqual((int)TouchKind.Bump, c.A);
            Assert.AreEqual(ContactBand.Controlled, (ContactBand)((c.B >> 4) & 0xF));
            Assert.AreEqual(1, rig.S.Touches);
            Fix64 apex = rig.Sim.PredictApex(rig.S, out int f);
            Assert.LessOrEqual(Fix64.Abs(apex - rig.T.Pass.PeakHeight), Fix64.Parse("0.03"), "pass peaks at the tuned height");
            Assert.IsTrue(rig.Sim.PredictDescent(ref rig.S, rig.Def(1).Height, 1, out int df, out FixVec2 pt, out Fix64 sp), "comes down on own side");
            Assert.That((pt.X - rig.T.Pass.TargetFromNet).ToString(1), Is.EqualTo("0.0"), "toward the setting spot");
        }

        static Rig SetScenario(string fighter, int stick, int pressOffset)
        {
            var rig = new Rig(fighter, "rafa");
            rig.InRally(new FixVec2(Fix64.Parse("-1.6"), Fix64.FromInt(4)), FixVec2.Zero, 0, TouchKind.Bump, 0, 1);
            rig.Place(0, Fix64.Parse("-1.6"));
            rig.Step();
            Assert.AreEqual(RingKind.Set, rig.F(0).Ring);
            int c = rig.F(0).RingCenter;
            rig.PressAt(0, InputButtons.Hit, c + pressOffset);
            rig.StickAt(0, stick, c - 3, c + 8);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact), 200));
            return rig;
        }

        [Test]
        public void QuickSetPeaksSixtyCentimetersAboveTheNet()
        {
            Rig rig = SetScenario("arjun", 7, 0);
            Assert.IsTrue(rig.S.Ball.Has(BallFlags.QuickSet));
            Fix64 apex = rig.Sim.PredictApex(rig.S, out int f);
            Fix64 expected = rig.T.Court.NetHeight + rig.T.Set.QuickPeakAboveNet;
            Assert.LessOrEqual(Fix64.Abs(apex - expected), Fix64.Parse("0.03"), "apex " + apex + " vs " + expected);
        }

        [Test]
        public void HighSetPeaksThreeMetersAboveTheNet()
        {
            Rig rig = SetScenario("arjun", 0, 0);
            Assert.IsTrue(rig.S.Ball.Has(BallFlags.HighSet));
            Fix64 apex = rig.Sim.PredictApex(rig.S, out int f);
            Fix64 expected = rig.T.Court.NetHeight + rig.T.Set.HighPeakAboveNet;
            Assert.LessOrEqual(Fix64.Abs(apex - expected), Fix64.Parse("0.03"), "apex " + apex + " vs " + expected);
            Assert.AreEqual(2, rig.S.Touches);
        }

        [Test]
        public void MissedSetRingAllowsOnlyAWeakHit()
        {
            Rig rig = SetScenario("arjun", 0, 7);
            SimEvent c = rig.First(VolleyEvent.Contact);
            Assert.AreEqual(TimingGrade.Late, (TimingGrade)(c.B & 0xF));
            Assert.AreEqual((int)TouchKind.BumpSet, c.A, "a late setter plays it low");
            Assert.IsTrue(rig.S.Ball.Has(BallFlags.WeakSet));
            Assert.IsFalse(rig.Has(VolleyEvent.Whiff));
        }

        static Rig Spike(int stick, int chargeFrames, InputButtons button = InputButtons.Hit)
        {
            var rig = new Rig("rafa", "rin");
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump);
            if (button == InputButtons.Hit) rig.PressAt(0, InputButtons.Hit, contact - chargeFrames, chargeFrames);
            else rig.PressAt(0, button, contact);
            rig.StickAt(0, stick, contact - 4, contact + 6);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact) || r.Has(VolleyEvent.Whiff), 300));
            Assert.IsTrue(rig.Has(VolleyEvent.Contact), "the swing met the ball");
            return rig;
        }

        [Test]
        public void SpikeLeavesAtTheFightersSpikeSpeed()
        {
            Rig rig = Spike(-7, 1);
            SimEvent c = rig.First(VolleyEvent.Contact);
            Assert.AreEqual((int)TouchKind.Spike, c.A);
            double speed = double.Parse(rig.S.Ball.Vel.Length.ToString(3), System.Globalization.CultureInfo.InvariantCulture);
            double expected = 21 * (1 + 0.15 / 8);
            Assert.That(speed, Is.EqualTo(expected).Within(0.3), "17 m/s + 1 per Power point above 5 (Rafa, Power 9)");
        }

        [Test]
        public void FullChargeAddsFifteenPercent()
        {
            Rig rig = Spike(-7, 12);
            double speed = double.Parse(rig.S.Ball.Vel.Length.ToString(3), System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(speed, Is.EqualTo(21 * 1.15).Within(0.3), "Heavy Ball: Rafa reaches full charge in 8 frames");
            Assert.IsTrue(rig.S.Ball.Has(BallFlags.HeavyBall));
        }

        [Test]
        public void TipTravelsAtFourToSixMetersPerSecond()
        {
            Rig rig = Spike(4, 0, InputButtons.Feint);
            SimEvent c = rig.First(VolleyEvent.Contact);
            Assert.AreEqual((int)TouchKind.Tip, c.A);
            double speed = double.Parse(rig.S.Ball.Vel.Length.ToString(3), System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(speed, Is.InRange(4.0, 6.5));
        }
    }
}
