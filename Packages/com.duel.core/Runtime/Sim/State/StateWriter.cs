using System;
using System.Buffers.Binary;

namespace Duel.Core.Sim
{
    /// <summary>
    /// Little-endian writer for snapshots. Game states serialize field by field through this
    /// writer, so the byte image (and therefore the hash) never depends on struct layout,
    /// padding, runtime or CPU architecture.
    /// </summary>
    public ref struct StateWriter
    {
        readonly Span<byte> buffer;
        int position;

        public StateWriter(Span<byte> buffer)
        {
            this.buffer = buffer;
            position = 0;
        }

        public int Position => position;

        public ReadOnlySpan<byte> Written => buffer.Slice(0, position);

        public void WriteByte(byte v)
        {
            buffer[position++] = v;
        }

        public void WriteSByte(sbyte v) => WriteByte((byte)v);

        public void WriteBool(bool v) => WriteByte(v ? (byte)1 : (byte)0);

        public void WriteUInt16(ushort v)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(position, 2), v);
            position += 2;
        }

        public void WriteInt16(short v) => WriteUInt16((ushort)v);

        public void WriteInt32(int v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(position, 4), v);
            position += 4;
        }

        public void WriteUInt32(uint v)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(position, 4), v);
            position += 4;
        }

        public void WriteInt64(long v)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(position, 8), v);
            position += 8;
        }

        public void WriteUInt64(ulong v)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(position, 8), v);
            position += 8;
        }

        public void WriteFix(Fix64 v) => WriteInt64(v.Raw);

        public void WriteVec(FixVec2 v)
        {
            WriteInt64(v.X.Raw);
            WriteInt64(v.Y.Raw);
        }

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            bytes.CopyTo(buffer.Slice(position));
            position += bytes.Length;
        }
    }

    /// <summary>Little-endian reader matching <see cref="StateWriter"/>.</summary>
    public ref struct StateReader
    {
        readonly ReadOnlySpan<byte> buffer;
        int position;

        public StateReader(ReadOnlySpan<byte> buffer)
        {
            this.buffer = buffer;
            position = 0;
        }

        public int Position => position;

        public byte ReadByte() => buffer[position++];

        public sbyte ReadSByte() => (sbyte)ReadByte();

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUInt16()
        {
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(position, 2));
            position += 2;
            return v;
        }

        public short ReadInt16() => (short)ReadUInt16();

        public int ReadInt32()
        {
            int v = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(position, 4));
            position += 4;
            return v;
        }

        public uint ReadUInt32()
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(position, 4));
            position += 4;
            return v;
        }

        public long ReadInt64()
        {
            long v = BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(position, 8));
            position += 8;
            return v;
        }

        public ulong ReadUInt64()
        {
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(position, 8));
            position += 8;
            return v;
        }

        public Fix64 ReadFix() => Fix64.FromRaw(ReadInt64());

        public FixVec2 ReadVec()
        {
            long x = ReadInt64();
            long y = ReadInt64();
            return new FixVec2(Fix64.FromRaw(x), Fix64.FromRaw(y));
        }

        public void ReadBytes(Span<byte> destination)
        {
            buffer.Slice(position, destination.Length).CopyTo(destination);
            position += destination.Length;
        }
    }
}
