using System;
using System.Buffers.Binary;
using System.Text;
using Duel.Core.Sim;

namespace Duel.Core.Net
{
    /// <summary>
    /// The 20-byte input packet (spec): u16 latest frame, u16 acknowledged frame, then the sender's last
    /// 8 inputs (frames latest-7 .. latest). The overlap absorbs dropped packets without retransmission.
    /// Frames travel as u16 and are re-expanded relative to the receiver's own frame counter.
    /// </summary>
    public struct InputPacket
    {
        public const int Size = 20;
        public const int Redundancy = 8;

        public ushort LatestFrame;
        public ushort AckFrame;
        public ushort I0, I1, I2, I3, I4, I5, I6, I7;

        public ushort Input(int k)
        {
            switch (k)
            {
                case 0: return I0;
                case 1: return I1;
                case 2: return I2;
                case 3: return I3;
                case 4: return I4;
                case 5: return I5;
                case 6: return I6;
                default: return I7;
            }
        }

        public void SetInput(int k, ushort v)
        {
            switch (k)
            {
                case 0: I0 = v; break;
                case 1: I1 = v; break;
                case 2: I2 = v; break;
                case 3: I3 = v; break;
                case 4: I4 = v; break;
                case 5: I5 = v; break;
                case 6: I6 = v; break;
                default: I7 = v; break;
            }
        }

        public void Write(Span<byte> dst)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(dst, LatestFrame);
            BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(2), AckFrame);
            for (int k = 0; k < Redundancy; k++) BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(4 + 2 * k), Input(k));
        }

        public static bool TryRead(ReadOnlySpan<byte> src, out InputPacket p)
        {
            p = default;
            if (src.Length != Size) return false;
            p.LatestFrame = BinaryPrimitives.ReadUInt16LittleEndian(src);
            p.AckFrame = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(2));
            for (int k = 0; k < Redundancy; k++) p.SetInput(k, BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(4 + 2 * k)));
            return true;
        }
    }

    /// <summary>Serial-number arithmetic for 16-bit frame numbers.</summary>
    public static class FrameWire
    {
        public static ushort Encode(int frame) => (ushort)(frame & 0xFFFF);

        /// <summary>The full frame closest to <paramref name="reference"/> whose low 16 bits are <paramref name="wire"/>.</summary>
        public static int Expand(ushort wire, int reference)
        {
            int delta = (short)(wire - (ushort)(reference & 0xFFFF));
            return reference + delta;
        }
    }

    public enum ControlType : byte
    {
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        Ping = 4,
        Pong = 5,
        Start = 6,
        Hash = 7,
        StateChunk = 8,
        Pause = 9,
        Resume = 10,
        Bye = 11,
        Rematch = 12,
        RematchAccept = 13,
    }

    public enum RejectReason : byte
    {
        None = 0,
        ProtocolVersion = 1,
        SimVersion = 2,
        Busy = 3,
        BadConfig = 4,
    }

    /// <summary>Little-endian writer/reader for control messages (variable length, reliable channel).</summary>
    public ref struct ControlWriter
    {
        readonly Span<byte> buffer;
        int position;

        public ControlWriter(Span<byte> buffer, ControlType type)
        {
            this.buffer = buffer;
            position = 0;
            U8((byte)type);
        }

        public int Length => position;

        public ReadOnlySpan<byte> Written => buffer.Slice(0, position);

        public void U8(byte v) => buffer[position++] = v;

        public void U16(ushort v)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(position), v);
            position += 2;
        }

        public void U32(uint v)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(position), v);
            position += 4;
        }

        public void I32(int v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(position), v);
            position += 4;
        }

        public void U64(ulong v)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(position), v);
            position += 8;
        }

        public void Bytes(ReadOnlySpan<byte> v)
        {
            U16((ushort)v.Length);
            v.CopyTo(buffer.Slice(position));
            position += v.Length;
        }

        public void Text(string s)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(s ?? string.Empty);
            if (utf8.Length > 64) Array.Resize(ref utf8, 64);
            Bytes(utf8);
        }
    }

    public ref struct ControlReader
    {
        readonly ReadOnlySpan<byte> buffer;
        int position;

        public ControlReader(ReadOnlySpan<byte> buffer)
        {
            this.buffer = buffer;
            position = 0;
            Type = buffer.Length > 0 ? (ControlType)buffer[0] : 0;
            position = 1;
        }

        public ControlType Type { get; }

        public bool Ok => position <= buffer.Length;

        public byte U8() => position < buffer.Length ? buffer[position++] : Fail8();

        public ushort U16()
        {
            if (position + 2 > buffer.Length) return (ushort)Fail8();
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(position));
            position += 2;
            return v;
        }

        public uint U32()
        {
            if (position + 4 > buffer.Length) return Fail8();
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(position));
            position += 4;
            return v;
        }

        public int I32() => (int)U32();

        public ulong U64()
        {
            if (position + 8 > buffer.Length) return Fail8();
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(position));
            position += 8;
            return v;
        }

        public byte[] Bytes()
        {
            int n = U16();
            if (position + n > buffer.Length)
            {
                Fail8();
                return Array.Empty<byte>();
            }
            byte[] v = buffer.Slice(position, n).ToArray();
            position += n;
            return v;
        }

        public string Text() => Encoding.UTF8.GetString(Bytes());

        byte Fail8()
        {
            position = buffer.Length + 1;
            return 0;
        }
    }

    /// <summary>Wire constants shared by every transport.</summary>
    public static class NetProtocol
    {
        /// <summary>Bump when packet layouts or the handshake change.</summary>
        public const ushort Version = 1;

        /// <summary>Payload bytes per full-state chunk; fits the default 20-byte BLE ATT payload budget with headers when the MTU is larger.</summary>
        public const int StateChunkBytes = 160;

        /// <summary>Encodes a local input history into an input packet.</summary>
        public static InputPacket BuildInputPacket(int latestFrame, int ackFrame, Func<int, FrameInput> inputAt)
        {
            var p = new InputPacket { LatestFrame = FrameWire.Encode(latestFrame), AckFrame = FrameWire.Encode(ackFrame) };
            for (int k = 0; k < InputPacket.Redundancy; k++)
            {
                int f = latestFrame - (InputPacket.Redundancy - 1) + k;
                p.SetInput(k, f >= 0 ? inputAt(f).Bits : (ushort)0);
            }
            return p;
        }
    }
}
