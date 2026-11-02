using System.Linq;
using Duel.Core.Sim;
using NUnit.Framework;
using VolleyDuel.Sim;

namespace VolleyDuel.Tests
{
    public class DataTests
    {
        [Test]
        public void EveryFighterHasThirtySixPointsAndOneNine()
        {
            SimTables t = Rig.LoadTables();
            Assert.AreEqual(4, t.Fighters.Length, "roster of four");
            foreach (FighterDefinition f in t.Fighters)
            {
                int[] a = { f.Power, f.Jump, f.Speed, f.Control, f.Dig, f.Block };
                Assert.AreEqual(36, a.Sum(), f.Id + " budget");
                Assert.AreEqual(1, a.Count(v => v == 9), f.Id + " has a single 9 (its mastery)");
            }
            Assert.AreEqual(9, t.Fighters[t.FighterIndex("rafa")].Power, "Rafa's mastery is power");
            Assert.AreEqual(9, t.Fighters[t.FighterIndex("defne")].Block, "Defne is the wall");
            Assert.AreEqual(9, t.Fighters[t.FighterIndex("rin")].Dig, "Rin is the libero");
            Assert.AreEqual(9, t.Fighters[t.FighterIndex("arjun")].Control, "Arjun's mastery is control");
        }

        [Test]
        public void TwoMenTwoWomenWithRosterHeights()
        {
            SimTables t = Rig.LoadTables();
            Assert.AreEqual("1.98", t.Fighters[t.FighterIndex("rafa")].Height.ToString(2));
            Assert.AreEqual("1.93", t.Fighters[t.FighterIndex("defne")].Height.ToString(2));
            Assert.AreEqual("1.62", t.Fighters[t.FighterIndex("rin")].Height.ToString(2));
            Assert.AreEqual("1.84", t.Fighters[t.FighterIndex("arjun")].Height.ToString(2));
        }

        [Test]
        public void TwelveCourtsThreePerThemeWithHomeCourts()
        {
            SimTables t = Rig.LoadTables();
            Assert.AreEqual(12, t.Courts.Length);
            foreach (string theme in new[] { "arena", "school", "street", "beach" }) Assert.AreEqual(3, t.Courts.Count(c => c.Theme == theme), theme);
            Assert.AreEqual("rafa", t.Courts[t.CourtIndex("mosaic-promenade")].HomeOf);
            Assert.AreEqual("defne", t.Courts[t.CourtIndex("the-cauldron")].HomeOf);
            Assert.AreEqual("rin", t.Courts[t.CourtIndex("ouendan-gym")].HomeOf);
            Assert.AreEqual("arjun", t.Courts[t.CourtIndex("floodlight-village")].HomeOf);
            Assert.AreEqual(4, t.Courts.Count(c => c.LaunchSet), "one launch court per theme");
        }

        [TestCase(5, 4, 10)]
        [TestCase(9, 6, 14)]
        [TestCase(7, 5, 12)]
        [TestCase(3, 3, 8)]
        [TestCase(6, 4, 11)]
        public void RingWidthsFollowTheSpec(int attribute, int perfect, int good)
        {
            RingTuning r = Rig.LoadTables().Tuning.Rings;
            Assert.AreEqual(perfect, r.PerfectWidth(attribute), "Perfect is 4 frames, ±1 per 2 points from 5");
            Assert.AreEqual(good, r.GoodWidth(attribute), "Good is 10 frames, ±1 per point");
        }

        [Test]
        public void NothingDropsAddsTwoPerfectFramesToRinsDigs()
        {
            SimTables t = Rig.LoadTables();
            FighterDefinition rin = t.Fighters[t.FighterIndex("rin")];
            Assert.AreEqual(t.Tuning.Rings.PerfectWidth(rin.Dig) + 2, rin.PerfectDig);
        }

        [Test]
        public void SpikeSpeedIsSeventeenPlusPowerAboveFive()
        {
            SimTables t = Rig.LoadTables();
            Assert.AreEqual(Fix64.FromInt(21), t.Fighters[t.FighterIndex("rafa")].SpikeSpeed);
            Assert.AreEqual(Fix64.FromInt(17), t.Fighters[t.FighterIndex("arjun")].SpikeSpeed);
            Assert.AreEqual(Fix64.FromInt(15), t.Fighters[t.FighterIndex("rin")].SpikeSpeed);
        }

        [Test]
        public void DataHashFingerprintsTheData()
        {
            SimTables a = Rig.LoadTables();
            Assert.AreNotEqual(0UL, a.DataHash);
            Assert.AreEqual(SimVersion.Compute(a), SimVersion.Compute(a));
        }
    }

    public class ContactModelTests
    {
        static VolleySim Sim() => new Rig().Sim;

        [Test]
        public void PerfectAtNeutralAttributeIsControlled()
        {
            VolleySim s = Sim();
            Fix64 q = s.ContactQuality(TimingGrade.Perfect, 5, Fix64.Zero, Fix64.FromInt(100));
            Assert.AreEqual("1.00", q.ToString(2));
            Assert.AreEqual(ContactBand.Controlled, s.Band(q));
        }

        [Test]
        public void GoodIsLooseAndLateIsAShank()
        {
            VolleySim s = Sim();
            Assert.AreEqual(ContactBand.Loose, s.Band(s.ContactQuality(TimingGrade.Good, 5, Fix64.Zero, Fix64.FromInt(100))));
            Assert.AreEqual(ContactBand.Shank, s.Band(s.ContactQuality(TimingGrade.Late, 5, Fix64.Zero, Fix64.FromInt(100))));
        }

        [Test]
        public void LateUnderPressureIsAMiss()
        {
            VolleySim s = Sim();
            Fix64 q = s.ContactQuality(TimingGrade.Late, 5, Fix64.Parse("0.1"), Fix64.FromInt(100));
            Assert.AreEqual("0.20", q.ToString(2));
            Assert.AreEqual(ContactBand.Miss, s.Band(q));
        }

        [Test]
        public void AttributeAddsThreeHundredthsPerPoint()
        {
            VolleySim s = Sim();
            Assert.AreEqual("0.77", s.ContactQuality(TimingGrade.Good, 9, Fix64.Zero, Fix64.FromInt(100)).ToString(2));
            Assert.AreEqual("0.59", s.ContactQuality(TimingGrade.Good, 3, Fix64.Zero, Fix64.FromInt(100)).ToString(2));
        }

        [TestCase(100, "1.000")]
        [TestCase(30, "1.000")]
        [TestCase(15, "0.925")]
        [TestCase(0, "0.850")]
        public void StaminaBelowThirtyCostsUpToFifteenHundredths(int stamina, string expected)
        {
            VolleySim s = Sim();
            Assert.AreEqual(expected, s.ContactQuality(TimingGrade.Perfect, 5, Fix64.Zero, Fix64.FromInt(stamina)).ToString(3));
        }

        [TestCase(10, "0.000")]
        [TestCase(14, "0.000")]
        [TestCase(24, "0.175")]
        [TestCase(34, "0.350")]
        [TestCase(40, "0.350")]
        public void PressureRisesWithBallSpeedUpToThirtyFiveHundredths(int speed, string expected)
        {
            Assert.AreEqual(expected, Sim().SpeedPressure(Fix64.FromInt(speed)).ToString(3));
        }

        [Test]
        public void BandThresholdsMatchTheSpec()
        {
            VolleySim s = Sim();
            Assert.AreEqual(ContactBand.Controlled, s.Band(Fix64.Parse("0.85")));
            Assert.AreEqual(ContactBand.Loose, s.Band(Fix64.Parse("0.84")));
            Assert.AreEqual(ContactBand.Loose, s.Band(Fix64.Parse("0.55")));
            Assert.AreEqual(ContactBand.Shank, s.Band(Fix64.Parse("0.54")));
            Assert.AreEqual(ContactBand.Shank, s.Band(Fix64.Parse("0.30")));
            Assert.AreEqual(ContactBand.Miss, s.Band(Fix64.Parse("0.29")));
        }

        [TestCase(-2, TimingGrade.Perfect)]
        [TestCase(1, TimingGrade.Perfect)]
        [TestCase(-3, TimingGrade.Good)]
        [TestCase(2, TimingGrade.Good)]
        [TestCase(-5, TimingGrade.Good)]
        [TestCase(4, TimingGrade.Good)]
        [TestCase(-6, TimingGrade.Late)]
        [TestCase(5, TimingGrade.Late)]
        [TestCase(-11, TimingGrade.Late)]
        [TestCase(10, TimingGrade.Late)]
        [TestCase(-12, TimingGrade.None)]
        [TestCase(11, TimingGrade.None)]
        public void RingWindowsAtNeutralAttribute(int delta, TimingGrade expected)
        {
            Assert.AreEqual(expected, Sim().Grade(delta, 4, 10));
        }
    }

    public class ScoringRuleTests
    {
        static ScoringRules Rules => Rig.LoadTables().Tuning.Scoring;

        [TestCase(7, 5, true)]
        [TestCase(7, 6, false)]
        [TestCase(8, 6, true)]
        [TestCase(8, 7, false)]
        [TestCase(8, 8, false)]
        [TestCase(9, 8, true)]
        [TestCase(6, 0, false)]
        public void FirstToSevenWinByTwoCappedAtNine(int own, int other, bool won)
        {
            Assert.AreEqual(won, Rules.SetWon(own, other));
        }

        [Test]
        public void AtEightAllTheNextPointWins()
        {
            Assert.IsTrue(Rules.SetPoint(8, 8));
            Assert.IsTrue(Rules.SetPoint(8, 7));
            Assert.IsTrue(Rules.SetPoint(6, 4));
            Assert.IsTrue(Rules.SetPoint(6, 5), "6-5: the next point makes 7-5");
            Assert.IsFalse(Rules.SetPoint(6, 6));
            Assert.IsFalse(Rules.SetPoint(5, 6));
        }

        [Test]
        public void BestOfThreeSets()
        {
            Assert.AreEqual(2, Rules.SetsToWin);
            Assert.AreEqual(2, Rules.FinisherPoints);
        }
    }
}
