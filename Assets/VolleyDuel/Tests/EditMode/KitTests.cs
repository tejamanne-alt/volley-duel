using Duel.Core.Sim;
using NUnit.Framework;
using VolleyDuel.Sim;

namespace VolleyDuel.Tests
{
    /// <summary>Signature Tricks, masteries, Finishers and Denial.</summary>
    public class KitTests
    {
        static double Speed(Rig rig) => double.Parse(rig.S.Ball.Vel.Length.ToString(3), System.Globalization.CultureInfo.InvariantCulture);

        static Rig SpikeWithTrick(string attacker, string defender, bool amped, string fromNet = "0.8", int stick = 0)
        {
            var rig = new Rig(attacker, defender);
            if (amped) rig.F(0).Surge = 100;
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump, fromNet);
            rig.PressAt(0, InputButtons.Trick, contact);
            if (amped) rig.PressAt(0, InputButtons.Amp, contact);
            rig.StickAt(0, stick, contact - 4, contact + 6);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact) || r.Has(VolleyEvent.Whiff), 300));
            Assert.IsTrue(rig.Has(VolleyEvent.Trick), "trick fired");
            Assert.IsTrue(rig.Has(VolleyEvent.Contact), "trick met the ball");
            return rig;
        }

        [Test]
        public void AmpedTricksCostOneSurgeBar()
        {
            Rig rig = SpikeWithTrick("arjun", "rafa", true);
            SimEvent t = rig.First(VolleyEvent.Trick);
            Assert.AreEqual((int)TrickKind.KalariTwist, t.A);
            Assert.AreEqual(1, t.B, "Amped");
            Assert.AreEqual(0, rig.F(0).Surge, "spent 100");
            Rig plain = SpikeWithTrick("arjun", "rafa", false);
            Assert.AreEqual(0, plain.First(VolleyEvent.Trick).B, "no swipe, no Amp");
        }

        [Test]
        public void KalariTwistDrivesShortAndAmpedShowsADecoyFlight()
        {
            Rig rig = SpikeWithTrick("arjun", "rafa", true, "0.8", -7);
            Assert.IsTrue(rig.Sim.PredictLanding(rig.S, out int f, out Fix64 x));
            Assert.Less(Fix64.Abs(x), Fix64.Parse("2.6"), "a deep swing turned into a steep, short spike");
            Assert.IsTrue(rig.Has(VolleyEvent.Decoy));
            GameState seen = Visibility.ForViewer(rig.S, 1);
            Assert.AreNotEqual(rig.S.Ball.Vel, seen.Ball.Vel, "the defender sees the decoy flight");
            Assert.AreEqual(rig.S.Ball.Vel, Visibility.ForViewer(rig.S, 0).Ball.Vel, "the hitter sees the real one");
        }

        [Test]
        public void PipeShotFromDeepLandsOnTheEndLine()
        {
            Rig rig = SpikeWithTrick("rafa", "rin", false, "3.0");
            Assert.AreEqual((int)TrickKind.PipeShot, rig.First(VolleyEvent.Trick).A);
            Assert.IsTrue(rig.Sim.PredictLanding(rig.S, out int f, out Fix64 x));
            Assert.That(double.Parse(x.ToString(2), System.Globalization.CultureInfo.InvariantCulture), Is.InRange(5.0, 6.105));
        }

        [Test]
        public void AmpedPipeShotCannotBeStuffed()
        {
            Rig rig = SpikeWithTrick("rafa", "rin", true, "3.0");
            Assert.IsTrue(rig.S.Ball.Has(BallFlags.CantStuff));
        }

        [Test]
        public void OverpassHammerIsUnblockableAndOnlyAPerfectDigSurvives()
        {
            var rig = new Rig("defne", "rin");
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump);
            // The ball came back over on Rin's first touch.
            rig.S.LastSender = 1;
            rig.S.LastSendKind = TouchKind.Bump;
            rig.S.TouchesSinceCross = 0;
            rig.S.Ball.Flags |= BallFlags.Overpass;
            rig.PressAt(0, InputButtons.Trick, contact);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact), 300));
            Assert.AreEqual((int)TrickKind.OverpassHammer, rig.First(VolleyEvent.Trick).A);
            Assert.IsTrue(rig.S.Ball.Has(BallFlags.Unblockable));
            Assert.IsTrue(rig.S.Ball.Has(BallFlags.PerfectOnly));
        }

        [Test]
        public void ShockwaveBlockDelaysTheHittersLanding()
        {
            var rig = new Rig("arjun", "rafa");
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump);
            rig.Place(1, VolleySim.FromNet(1, Fix64.Parse("0.4")));
            TrickDefinition t = rig.Def(1).Defense;
            int press = contact - t.Startup - rig.Sim.FramesToApex(rig.Def(1).BlockJumpHeight);
            rig.PressAt(1, InputButtons.Trick, press);
            rig.PressAt(0, InputButtons.Hit, contact - 1, 1);
            rig.StickAt(0, 7, contact - 4, contact + 6);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.BlockTouch), 400));
            Assert.AreEqual((int)TrickKind.ShockwaveBlock, rig.First(VolleyEvent.Trick).A);
            Assert.AreEqual(t.KnockbackFrames, (int)rig.F(0).PendingKnockback, "the hitter's next landing is delayed by 10 frames");
            // Landing during play adds it to the recovery.
            rig.F(0).Pos.Y = Fix64.Parse("0.01");
            rig.F(0).Vel = new FixVec2(Fix64.Zero, -Fix64.One);
            rig.S.Phase = MatchPhase.Rally;
            rig.S.HitStop = 0;
            rig.S.SlowDiv = 1;
            rig.Run(2);
            SimEvent land = rig.All(VolleyEvent.Land).Find(e => e.Actor == 0);
            Assert.AreEqual(rig.T.Jump.LandingRecovery + rig.Def(0).LandingRecoveryExtra + t.KnockbackFrames, land.A);
        }

        [Test]
        public void CommitBlockHangsOverTheNetAndCoversBothTimings()
        {
            var rig = new Rig("arjun", "defne");
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump, "0.8", "6.4");
            rig.Place(1, VolleySim.FromNet(1, Fix64.Parse("0.4")));
            TrickDefinition t = rig.Def(1).Defense;
            // Jump early: apex well before the contact, inside the hang.
            int press = contact - t.Startup - rig.Sim.FramesToApex(rig.Def(1).BlockJumpHeight) - t.HangFrames / 2;
            rig.PressAt(1, InputButtons.Trick, press);
            rig.PressAt(0, InputButtons.Hit, contact - 1, 1);
            rig.StickAt(0, 7, contact - 4, contact + 6);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.BlockTouch) || r.Has(VolleyEvent.Point), 400));
            Assert.AreEqual((int)BlockOutcome.Stuff, rig.First(VolleyEvent.BlockTouch).A, "the hang turns an early jump into a stuff");
        }

        [Test]
        public void SlideShiftsTheBlockersReadByFourFrames()
        {
            var rig = new Rig("defne", "rafa");
            rig.InRally(new FixVec2(Fix64.Parse("-1.0"), Fix64.Parse("4.6")), FixVec2.Zero, 0, TouchKind.Set, 0, 2, BallFlags.HighSet);
            rig.Place(0, Fix64.Parse("-2.0"));
            rig.F(1).BlockRingCenter = rig.S.World + 50;
            rig.F(1).BlockRingReveal = rig.S.World;
            int before = rig.F(1).BlockRingCenter;
            rig.PressAt(0, InputButtons.Trick, rig.S.World + 1);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Takeoff), 60));
            Assert.AreEqual(JumpKind.Slide, rig.F(0).Jump);
            Assert.AreEqual(before + rig.Def(0).OffenseA.TimingShift, rig.F(1).BlockRingCenter);
            Assert.Greater(VolleySim.TowardNet(0, rig.F(0).Vel.X), Fix64.Zero, "drifts toward the net");
        }

        [Test]
        public void QuickStrokeSetsLowWithoutThePowerPenalty()
        {
            var rig = new Rig("rin", "rafa");
            rig.InRally(new FixVec2(Fix64.Parse("-1.1"), Fix64.FromInt(4)), FixVec2.Zero, 0, TouchKind.Bump, 0, 1);
            rig.Place(0, Fix64.Parse("-1.1"));
            rig.Step();
            rig.PressAt(0, InputButtons.Trick, rig.F(0).RingCenter);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact), 200));
            Assert.AreEqual((int)TrickKind.QuickStroke, rig.First(VolleyEvent.Trick).A);
            Assert.IsTrue(rig.S.Ball.Has(BallFlags.QuickSet));
            Assert.AreEqual(TrickKind.QuickStroke, rig.F(0).ActiveTrick, "carried into the attack, which skips the x0.85");
        }

        [Test]
        public void BlindSetHidesTheReadUntilTheBallLeavesTheHands()
        {
            var rig = new Rig("arjun", "rafa");
            rig.InRally(new FixVec2(Fix64.Parse("-1.1"), Fix64.FromInt(4)), FixVec2.Zero, 0, TouchKind.Bump, 0, 1);
            rig.Place(0, Fix64.Parse("-1.1"));
            rig.Step();
            int center = rig.F(0).RingCenter;
            rig.PressAt(0, InputButtons.Trick, center - 3);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact), 200));
            SimEvent c = rig.First(VolleyEvent.Contact);
            Assert.IsTrue(rig.S.Ball.Has(BallFlags.HideTelegraph));
            Assert.GreaterOrEqual(rig.F(1).BlockRingReveal, c.Frame - 1, "no read before the contact");
        }

        [Test]
        public void InkReturnSendsTheDigStraightBackOverTheNet()
        {
            var rig = new Rig("rafa", "rin");
            rig.InRally(new FixVec2(Fix64.Parse("-0.8"), Fix64.Parse("3.2")), new FixVec2(Fix64.FromInt(14), -Fix64.FromInt(5)), 0, TouchKind.Spike, 0, 3, BallFlags.Attack);
            rig.S.LastSendKind = TouchKind.Spike;
            rig.Place(0, Fix64.FromInt(-2));
            rig.Step();
            Assert.AreEqual(RingKind.Bump, rig.F(1).Ring);
            rig.Place(1, rig.F(1).RingPoint.X);
            rig.Step();
            rig.PressAt(1, InputButtons.Trick, rig.F(1).RingCenter);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact), 200));
            Assert.AreEqual((int)TrickKind.InkReturn, rig.First(VolleyEvent.Trick).A);
            Assert.Greater(VolleySim.TowardNet(1, rig.S.Ball.Vel.X), Fix64.Zero, "heading back toward the hitter's side");
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.NetCross), 200), "back over the net on the first touch");
        }

        [Test]
        public void SweepReachesBallsBeyondANormalDive()
        {
            var rig = new Rig("rafa", "rin");
            rig.InRally(new FixVec2(Fix64.Parse("-1"), Fix64.FromInt(3)), new FixVec2(Fix64.FromInt(9), Fix64.Zero), 0, TouchKind.Spike, 0, 3, BallFlags.Attack);
            rig.S.LastSendKind = TouchKind.Spike;
            rig.Step();
            Assert.AreEqual(RingKind.Bump, rig.F(1).Ring);
            Fix64 target = rig.F(1).RingPoint.X;
            // Stand 2.9 m away: out of a normal dive, inside the 3 m Sweep.
            rig.Place(1, target + Fix64.Parse("2.9"));
            rig.Step();
            Assert.IsFalse(rig.F(1).RingDive, "a normal dive cannot reach it");
            int press = rig.F(1).RingCenter - 9;
            rig.PressAt(1, InputButtons.Trick, press);
            rig.StickAt(1, 7, press - 1, press + 40);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact) || r.Has(VolleyEvent.Point), 200));
            Assert.AreEqual((int)TrickKind.Sweep, rig.First(VolleyEvent.Trick).A);
            Assert.IsTrue(rig.Has(VolleyEvent.Contact), "Sweep got there");
        }

        [Test]
        public void DecoyReadsAsABlockAndBaitsATip()
        {
            var rig = new Rig("rafa", "arjun");
            int contact = Scenarios.SetUpSpike(rig, 0, out int jump);
            rig.PressAt(1, InputButtons.Trick, contact - 8);
            rig.PressAt(0, InputButtons.Feint, contact);
            rig.StickAt(0, 4, contact - 4, contact + 6);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Decoy), 300));
            GameState seen = Visibility.ForViewer(rig.S, 0);
            Assert.AreEqual(JumpKind.Block, seen.F1.Jump, "the hitter sees a mirage at the net");
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact), 300));
            Assert.IsTrue(rig.Has(VolleyEvent.Bite), "tipping into the mirage bites");
        }

        [Test]
        public void HeavyBallShanksAnyDigShortOfPerfectAndCostsStamina()
        {
            var rig = new Rig("rafa", "arjun");
            rig.InRally(new FixVec2(Fix64.Parse("-0.8"), Fix64.Parse("3.2")), new FixVec2(Fix64.FromInt(14), -Fix64.FromInt(5)), 0, TouchKind.Spike, 0, 3, BallFlags.Attack | BallFlags.HeavyBall);
            rig.S.LastSendKind = TouchKind.Spike;
            rig.Place(0, Fix64.FromInt(-2));
            rig.Step();
            rig.Place(1, rig.F(1).RingPoint.X);
            rig.Step();
            Fix64 before = rig.F(1).Stamina;
            rig.PressAt(1, InputButtons.Hit, rig.F(1).RingCenter - 4);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact), 200));
            SimEvent c = rig.First(VolleyEvent.Contact);
            Assert.AreEqual(TimingGrade.Good, (TimingGrade)(c.B & 0xF));
            Assert.AreEqual(ContactBand.Shank, (ContactBand)((c.B >> 4) & 0xF), "Good dig on a Heavy Ball is shanked");
            Assert.Less(rig.F(1).Stamina, before, "every dig against Heavy Ball costs Stamina");
        }

        [Test]
        public void NothingDropsGuaranteesOneFloorSavePerRally()
        {
            var rig = new Rig("rafa", "rin");
            rig.InRally(new FixVec2(Fix64.Parse("-1"), Fix64.FromInt(3)), new FixVec2(Fix64.FromInt(9), Fix64.Zero), 0, TouchKind.Spike, 0, 3, BallFlags.Attack);
            rig.S.LastSendKind = TouchKind.Spike;
            rig.Step();
            Fix64 target = rig.F(1).RingPoint.X;
            // 2.4 m away and pressing at the last moment: no dive could cover that in time,
            // but it is inside her dive range, so Nothing Drops saves it once.
            int press = rig.F(1).RingCenter - 1;
            rig.RunToWorld(press - 2);
            rig.Place(1, target + Fix64.Parse("2.4"));
            rig.PressAt(1, InputButtons.Hit, press);
            rig.StickAt(1, 7, press - 1, press + 40);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Contact) || r.Has(VolleyEvent.Point), 200));
            Assert.IsTrue(rig.F(1).FloorSaveUsed, "used the rally's guaranteed save");
            Assert.IsTrue(rig.HasCallout(Callout.Pancake));
            SimEvent c = rig.First(VolleyEvent.Contact);
            Assert.AreNotEqual(ContactBand.Miss, (ContactBand)((c.B >> 4) & 0xF));
        }

        static Rig FinisherOnServe(int defenderSurge)
        {
            var rig = new Rig("rafa", "rin").ToServe(0);
            rig.F(0).Surge = 300;
            rig.F(1).Surge = (short)defenderSurge;
            rig.PressAt(0, InputButtons.Finisher, rig.S.World + 2);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.FinisherStart), 30));
            return rig;
        }

        [Test]
        public void SoundBarrierFiresOnHisServeAndScoresTwo()
        {
            Rig rig = FinisherOnServe(0);
            Assert.AreEqual((int)FinisherKind.SoundBarrier, rig.First(VolleyEvent.FinisherStart).A);
            Assert.AreEqual(0, rig.F(0).Surge, "costs 3 bars");
            Assert.AreEqual(MatchPhase.Finisher, rig.S.Phase);
            Assert.IsTrue(rig.RunUntilPoint(400));
            SimEvent p = rig.First(VolleyEvent.Point);
            Assert.AreEqual((int)PointReason.Finisher, p.A);
            Assert.AreEqual(2, p.B);
            Assert.AreEqual(2, rig.S.Score0);
        }

        [Test]
        public void DenialInsideTheWindowDigsTheFinisherUp()
        {
            Rig rig = FinisherOnServe(150);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.DenialWindow), 300));
            FinisherState fin = rig.S.Fin;
            rig.PressAt(1, InputButtons.Finisher, fin.DenialStart + 1);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.Denied), 300));
            Assert.AreEqual(50, rig.F(1).Surge, "a Denial costs 1 bar");
            Assert.AreEqual(MatchPhase.Rally, rig.S.Phase, "the rally continues");
            Assert.AreEqual(1, rig.S.Possession, "as a free ball on the defender's side");
            Assert.IsTrue(rig.HasCallout(Callout.Denied));
            Assert.AreEqual(0, rig.S.Score0);
        }

        [Test]
        public void DenialOutsideTheWindowFailsAndThereIsOnlyOneAttempt()
        {
            Rig rig = FinisherOnServe(150);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.DenialWindow), 300));
            FinisherState fin = rig.S.Fin;
            rig.PressAt(1, InputButtons.Finisher, fin.DenialStart - 3);
            rig.PressAt(1, InputButtons.Finisher, fin.DenialStart + 1);
            Assert.IsTrue(rig.RunUntilPoint(400));
            Assert.IsFalse(rig.Has(VolleyEvent.Denied));
            Assert.AreEqual(150 + rig.T.Meters.GainScoredOn, rig.F(1).Surge, "a failed attempt costs nothing (plus the scored-on bonus)");
            Assert.AreEqual((int)PointReason.Finisher, rig.First(VolleyEvent.Point).A);
        }

        [Test]
        public void NoDenialWithoutABarOfSurge()
        {
            Rig rig = FinisherOnServe(90);
            Assert.IsTrue(rig.RunUntil(r => r.S.Phase == MatchPhase.Finisher && r.S.World >= r.S.Fin.DenialStart, 300));
            rig.PressAt(1, InputButtons.Finisher, rig.S.Fin.DenialStart + 1);
            Assert.IsTrue(rig.RunUntilPoint(400));
            Assert.IsFalse(rig.Has(VolleyEvent.DenialWindow));
            Assert.IsFalse(rig.Has(VolleyEvent.Denied));
        }

        [Test]
        public void FinisherNeedsItsTriggerMoment()
        {
            // Arjun's Maya fires only on his second touch; at the serve nothing happens.
            var rig = new Rig("arjun", "rin").ToServe(0);
            rig.F(0).Surge = 300;
            rig.PressAt(0, InputButtons.Finisher, rig.S.World + 2);
            rig.Run(20);
            Assert.IsFalse(rig.Has(VolleyEvent.FinisherStart));

            var set = new Rig("arjun", "rin");
            set.F(0).Surge = 300;
            set.InRally(new FixVec2(Fix64.Parse("-1.1"), Fix64.FromInt(4)), FixVec2.Zero, 0, TouchKind.Bump, 0, 1);
            set.Place(0, Fix64.Parse("-1.1"));
            set.Step();
            Assert.IsTrue(set.Sim.FinisherReady(set.S, 0), "second touch");
            set.PressAt(0, InputButtons.Finisher, set.S.World + 1);
            Assert.IsTrue(set.RunUntil(r => r.Has(VolleyEvent.FinisherStart), 20));
            Assert.AreEqual((int)FinisherKind.Maya, set.First(VolleyEvent.FinisherStart).A);
        }

        [Test]
        public void FinisherTwoPointsClampAtTheSetCap()
        {
            Rig rig = FinisherOnServe(0);
            rig.S.Score0 = 8;
            rig.S.Score1 = 8;
            Assert.IsTrue(rig.RunUntilPoint(400));
            Assert.AreEqual(9, rig.S.Score0);
            Assert.IsTrue(rig.RunUntil(r => r.Has(VolleyEvent.SetWon), 300));
        }
    }
}
