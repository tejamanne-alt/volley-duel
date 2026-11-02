using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Duel.Core.Sim
{
    /// <summary>
    /// A match saved as inputs: header (sim version, game config blob), both players' inputs
    /// per frame, and the expected final frame and hash. Inputs are run-length encoded and
    /// deflated, so a full match is a few kilobytes.
    /// </summary>
    public sealed class Replay
    {
        public const uint Magic = 0x4C455544; // "DUEL" little-endian
        public const ushort FormatVersion = 1;

        public ulong SimVersion;
        public byte[] Config = Array.Empty<byte>();
        public readonly List<FrameInput> P1 = new List<FrameInput>();
        public readonly List<FrameInput> P2 = new List<FrameInput>();
        public int FinalFrame;
        public ulong FinalHash;

        public int FrameCount => P1.Count;

        public void Add(FrameInput p1, FrameInput p2)
        {
            P1.Add(p1);
            P2.Add(p2);
        }

        public byte[] ToBytes()
        {
            using (var raw = new MemoryStream())
            {
                using (var w = new BinaryWriter(raw))
                {
                    w.Write(Magic);
                    w.Write(FormatVersion);
                    w.Write(SimVersion);
                    w.Write(FinalFrame);
                    w.Write(FinalHash);
                    w.Write(Config.Length);
                    w.Write(Config);
                    w.Write(P1.Count);
                    byte[] payload = EncodeInputs();
                    w.Write(payload.Length);
                    w.Write(payload);
                }
                return raw.ToArray();
            }
        }

        public static Replay FromBytes(byte[] bytes)
        {
            using (var raw = new MemoryStream(bytes))
            using (var r = new BinaryReader(raw))
            {
                if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a replay");
                ushort version = r.ReadUInt16();
                if (version != FormatVersion) throw new InvalidDataException("Unsupported replay format " + version);
                var replay = new Replay
                {
                    SimVersion = r.ReadUInt64(),
                    FinalFrame = r.ReadInt32(),
                    FinalHash = r.ReadUInt64(),
                };
                int configLength = r.ReadInt32();
                replay.Config = r.ReadBytes(configLength);
                int frames = r.ReadInt32();
                int payloadLength = r.ReadInt32();
                replay.DecodeInputs(r.ReadBytes(payloadLength), frames);
                return replay;
            }
        }

        byte[] EncodeInputs()
        {
            using (var packed = new MemoryStream())
            {
                using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, true))
                using (var w = new BinaryWriter(deflate))
                {
                    EncodeStream(w, P1);
                    EncodeStream(w, P2);
                }
                return packed.ToArray();
            }
        }

        static void EncodeStream(BinaryWriter w, List<FrameInput> inputs)
        {
            int i = 0;
            while (i < inputs.Count)
            {
                ushort bits = inputs[i].Bits;
                int run = 1;
                while (i + run < inputs.Count && inputs[i + run].Bits == bits) run++;
                WriteVarInt(w, run);
                w.Write(bits);
                i += run;
            }
        }

        void DecodeInputs(byte[] payload, int frames)
        {
            using (var packed = new MemoryStream(payload))
            using (var deflate = new DeflateStream(packed, CompressionMode.Decompress))
            using (var r = new BinaryReader(deflate))
            {
                DecodeStream(r, P1, frames);
                DecodeStream(r, P2, frames);
            }
        }

        static void DecodeStream(BinaryReader r, List<FrameInput> into, int frames)
        {
            while (into.Count < frames)
            {
                int run = ReadVarInt(r);
                var input = new FrameInput(r.ReadUInt16());
                for (int k = 0; k < run; k++) into.Add(input);
            }
        }

        static void WriteVarInt(BinaryWriter w, int value)
        {
            uint v = (uint)value;
            while (v >= 0x80)
            {
                w.Write((byte)(v | 0x80));
                v >>= 7;
            }
            w.Write((byte)v);
        }

        static int ReadVarInt(BinaryReader r)
        {
            int result = 0;
            int shift = 0;
            while (true)
            {
                byte b = r.ReadByte();
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift > 28) throw new InvalidDataException("Bad varint");
            }
        }

        /// <summary>
        /// Re-runs the replay from <paramref name="initial"/> and returns the final state.
        /// <paramref name="frameHashes"/> optionally receives the hash after every frame.
        /// </summary>
        public TState Play<TState>(ISimulation<TState> sim, TState initial, List<ulong> frameHashes = null) where TState : struct
        {
            var events = new SimEventBuffer();
            TState state = initial;
            for (int f = 0; f < FrameCount; f++)
            {
                events.Clear();
                state = sim.Step(state, P1[f], P2[f], events);
                if (frameHashes != null) frameHashes.Add(sim.Hash(state));
            }
            return state;
        }
    }
}
