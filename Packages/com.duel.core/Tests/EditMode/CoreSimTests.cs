using System;
using System.Text;
using Duel.Core.Sim;
using Duel.Core.Sim.Data;
using NUnit.Framework;

namespace Duel.Core.Tests
{
    public class RngTests
    {
        [Test]
        public void SameSeedSameSequence()
        {
            var a = Rng.Create(42, 7);
            var b = Rng.Create(42, 7);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void StreamsDiffer()
        {
            var a = Rng.Create(42, 1);
            var b = Rng.Create(42, 2);
            int same = 0;
            for (int i = 0; i < 100; i++) if (a.NextUInt() == b.NextUInt()) same++;
            Assert.Less(same, 3);
        }

        [Test]
        public void KnownAnswer()
        {
            // Locks the generator: changing it silently would invalidate every replay.
            var r = Rng.Create(42, 0);
            uint[] got = { r.NextUInt(), r.NextUInt(), r.NextUInt(), r.NextUInt() };
            var again = Rng.Create(42, 0);
            for (int i = 0; i < got.Length; i++) Assert.AreEqual(got[i], again.NextUInt());
            Assert.AreEqual(KnownAnswerValues.Rng42First, got[0], "PCG32 output changed: replays recorded earlier will no longer match");
        }

        [Test]
        public void NextIntIsInRangeAndRoughlyUniform()
        {
            var r = Rng.Create(5, 5);
            int[] buckets = new int[6];
            for (int i = 0; i < 60000; i++)
            {
                int v = r.NextInt(6);
                Assert.That(v, Is.InRange(0, 5));
                buckets[v]++;
            }
            foreach (int b in buckets) Assert.That(b, Is.InRange(9400, 10600));
        }

        [Test]
        public void FixRangeAndChance()
        {
            var r = Rng.Create(9, 9);
            int hits = 0;
            for (int i = 0; i < 10000; i++)
            {
                Fix64 v = r.Range(Fix64.FromInt(-2), Fix64.FromInt(3));
                Assert.That(v.Raw, Is.InRange(Fix64.FromInt(-2).Raw, Fix64.FromInt(3).Raw - 1));
                if (r.Chance(Fix64.Parse("0.25"))) hits++;
            }
            Assert.That(hits, Is.InRange(2300, 2700));
        }

        [Test]
        public void GaussianHasRoughlyRequestedSpread()
        {
            var r = Rng.Create(3, 3);
            long sumSq = 0;
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                int v = r.Gaussian(Fix64.FromInt(10)).RoundToInt();
                sumSq += v * v;
            }
            double sd = Math.Sqrt((double)sumSq / n);
            Assert.That(sd, Is.InRange(9.0, 11.0));
        }
    }

    public static class KnownAnswerValues
    {
        // First PCG32 output of Rng.Create(42, 0); recorded when the generator was introduced.
        public const uint Rng42First = 1875661987u;
    }

    public class HashAndSerializationTests
    {
        [Test]
        public void FnvKnownAnswers()
        {
            Assert.AreEqual(StateHash.OffsetBasis, StateHash.Fnv1a(ReadOnlySpan<byte>.Empty));
            Assert.AreEqual(0xaf63dc4c8601ec8cUL, StateHash.Fnv1a(Encoding.ASCII.GetBytes("a")));
            Assert.AreEqual(0x85944171f73967e8UL, StateHash.Fnv1a(Encoding.ASCII.GetBytes("foobar")));
        }

        [Test]
        public void WriterReaderRoundTrip()
        {
            var buffer = new byte[128];
            var w = new StateWriter(buffer);
            w.WriteByte(250);
            w.WriteSByte(-5);
            w.WriteBool(true);
            w.WriteInt16(-1234);
            w.WriteUInt16(65000);
            w.WriteInt32(-123456789);
            w.WriteUInt32(4000000000u);
            w.WriteInt64(long.MinValue + 3);
            w.WriteUInt64(ulong.MaxValue - 9);
            w.WriteFix(Fix64.Parse("-2.35"));
            w.WriteVec(new FixVec2(Fix64.Parse("1.5"), Fix64.Parse("-6")));
            int written = w.Position;

            var r = new StateReader(buffer);
            Assert.AreEqual(250, r.ReadByte());
            Assert.AreEqual(-5, r.ReadSByte());
            Assert.IsTrue(r.ReadBool());
            Assert.AreEqual(-1234, r.ReadInt16());
            Assert.AreEqual(65000, r.ReadUInt16());
            Assert.AreEqual(-123456789, r.ReadInt32());
            Assert.AreEqual(4000000000u, r.ReadUInt32());
            Assert.AreEqual(long.MinValue + 3, r.ReadInt64());
            Assert.AreEqual(ulong.MaxValue - 9, r.ReadUInt64());
            Assert.AreEqual(Fix64.Parse("-2.35"), r.ReadFix());
            Assert.AreEqual(new FixVec2(Fix64.Parse("1.5"), Fix64.Parse("-6")), r.ReadVec());
            Assert.AreEqual(written, r.Position);
            // Little-endian on the wire regardless of host.
            Assert.AreEqual(0x2E, buffer[3]);
            Assert.AreEqual(0xFB, buffer[4]);
        }

        [Test]
        public void RngSerializes()
        {
            var a = Rng.Create(1, 2);
            a.NextUInt();
            var buffer = new byte[16];
            var w = new StateWriter(buffer);
            a.Write(ref w);
            var r = new StateReader(buffer);
            var b = new Rng();
            b.Read(ref r);
            Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }
    }

    public class InputTests
    {
        [Test]
        public void FrameInputPacksIntoSixteenBits()
        {
            for (int x = -7; x <= 7; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    var input = FrameInput.Create(x, y, InputButtons.Hit | InputButtons.Finisher);
                    Assert.AreEqual(x, input.StickX);
                    Assert.AreEqual(y, input.StickY);
                    Assert.IsTrue(input.Has(InputButtons.Hit));
                    Assert.IsTrue(input.Has(InputButtons.Finisher));
                    Assert.IsFalse(input.Has(InputButtons.Jump));
                    Assert.AreEqual(0, input.Bits & 0xF000, "reserved bits stay zero");
                }
            }
            Assert.AreEqual(7, FrameInput.Create(99, 0, InputButtons.None).StickX);
            Assert.AreEqual(-7, FrameInput.Create(-99, 0, InputButtons.None).StickX);
        }

        [Test]
        public void TrackerBuffersPressesAndReleases()
        {
            var t = InputTracker.Create();
            t.Record(FrameInput.Create(0, 0, InputButtons.None), 1);
            t.Record(FrameInput.Create(0, 0, InputButtons.Hit), 2);
            t.Record(FrameInput.Create(0, 0, InputButtons.Hit), 3);
            Assert.AreEqual(2, t.PressFrame(InputButtons.Hit));
            Assert.IsTrue(t.Buffered(InputButtons.Hit, 7, 6));
            Assert.IsFalse(t.Buffered(InputButtons.Hit, 8, 6), "six-frame buffer");
            t.Record(FrameInput.Create(0, 0, InputButtons.None), 4);
            Assert.AreEqual(4, t.ReleaseFrame(InputButtons.Hit));
            t.Consume(InputButtons.Hit);
            Assert.IsFalse(t.Buffered(InputButtons.Hit, 4, 6));
        }

        [Test]
        public void AimUsesRecentDirectionWithinWindow()
        {
            var t = InputTracker.Create();
            t.Record(FrameInput.Create(5, 0, InputButtons.None), 10);
            t.Record(FrameInput.Create(0, 0, InputButtons.Hit), 11);
            Assert.AreEqual(5, t.AimStickX(13, 3));
            Assert.AreEqual(0, t.AimStickX(14, 3));
            t.Record(FrameInput.Create(-3, 0, InputButtons.Hit), 12);
            Assert.AreEqual(-3, t.AimStickX(12, 3));
        }
    }

    public class EventBufferTests
    {
        [Test]
        public void SequencesRestartPerFrame()
        {
            var b = new SimEventBuffer();
            b.Add(5, 1, 0);
            b.Add(5, 2, 1);
            b.Add(6, 1, 0);
            Assert.AreEqual(0, b[0].Sequence);
            Assert.AreEqual(1, b[1].Sequence);
            Assert.AreEqual(0, b[2].Sequence);
            Assert.AreNotEqual(b[0].Key, b[2].Key);
            for (int i = 0; i < 200; i++) b.Add(7, 3, 0);
            Assert.AreEqual(203, b.Count);
        }
    }

    public class ReplayTests
    {
        [Test]
        public void RoundTripsAndCompresses()
        {
            var replay = new Replay { SimVersion = 0x1234, Config = new byte[] { 1, 2, 3 }, FinalFrame = 9999, FinalHash = 42 };
            var rng = Rng.Create(1, 1);
            var p1 = FrameInput.Neutral;
            var p2 = FrameInput.Neutral;
            for (int f = 0; f < 10000; f++)
            {
                if (rng.NextInt(8) == 0) p1 = new FrameInput((ushort)rng.NextInt(4096));
                if (rng.NextInt(8) == 0) p2 = new FrameInput((ushort)rng.NextInt(4096));
                replay.Add(p1, p2);
            }
            byte[] bytes = replay.ToBytes();
            Replay back = Replay.FromBytes(bytes);
            Assert.AreEqual(replay.SimVersion, back.SimVersion);
            Assert.AreEqual(replay.FinalFrame, back.FinalFrame);
            Assert.AreEqual(replay.FinalHash, back.FinalHash);
            CollectionAssert.AreEqual(replay.Config, back.Config);
            Assert.AreEqual(replay.FrameCount, back.FrameCount);
            for (int f = 0; f < replay.FrameCount; f++)
            {
                Assert.AreEqual(replay.P1[f], back.P1[f]);
                Assert.AreEqual(replay.P2[f], back.P2[f]);
            }
            Assert.Less(bytes.Length, 10000 * 4 / 2, "replay is compressed");
        }
    }

    public class JsonTests
    {
        [Test]
        public void ParsesDataDocument()
        {
            string text = "{\n  // tuning note\n  \"netHeight\": 2.35,\n  \"name\": \"Rin \\\"Inkline\\\" Takeda\\u00e9\",\n  \"list\": [1, -2, 3.5],\n  \"flag\": true,\n  \"none\": null,\n  \"nested\": { \"frames\": 12 }\n}";
            JsonNode root = JsonNode.Parse(text, "test");
            Assert.AreEqual(Fix64.Parse("2.35"), root.Fix("netHeight"));
            Assert.AreEqual("Rin \"Inkline\" Takedaé", root.Str("name"));
            Assert.AreEqual(3, root["list"].Count);
            Assert.AreEqual(-2, root["list"][1].AsInt());
            Assert.AreEqual(Fix64.Parse("3.5"), root["list"][2].AsFix());
            Assert.IsTrue(root.Bool("flag", false));
            Assert.AreEqual(JsonKind.Null, root["none"].Kind);
            Assert.AreEqual(12, root["nested"].Int("frames"));
            Assert.AreEqual(7, root.Int("missing", 7));
            Assert.AreEqual("$.nested.frames", root["nested"]["frames"].Path);
        }

        [TestCase("{\"a\": 1e3}")]
        [TestCase("{\"a\": 1,}")]
        [TestCase("{\"a\": 1, \"a\": 2}")]
        [TestCase("[1, 2")]
        [TestCase("{\"a\" 1}")]
        public void RejectsMalformedData(string text)
        {
            Assert.Throws<FormatException>(() => JsonNode.Parse(text, "bad"));
        }

        [Test]
        public void MissingRequiredKeyNamesThePath()
        {
            JsonNode root = JsonNode.Parse("{\"ball\": {}}", "t");
            var ex = Assert.Throws<FormatException>(() => root["ball"].Fix("radius"));
            StringAssert.Contains("$.ball", ex.Message);
            StringAssert.Contains("radius", ex.Message);
        }
    }

    public class TickClockTests
    {
        [Test]
        public void SixtyTicksPerSecond()
        {
            var clock = new TickClock(maxTicksPerUpdate: 100);
            int ticks = 0;
            for (int i = 0; i < 1000; i++) ticks += clock.Advance(1000); // 1 ms steps
            Assert.AreEqual(60, ticks);
        }

        [Test]
        public void ClampsCatchUp()
        {
            var clock = new TickClock(maxTicksPerUpdate: 4);
            Assert.AreEqual(4, clock.Advance(1000000));
        }
    }
}
