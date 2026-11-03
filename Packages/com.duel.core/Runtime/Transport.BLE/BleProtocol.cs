using System;
using System.Buffers.Binary;
using System.Text;

namespace Duel.Core.Transport.BLE
{
    /// <summary>
    /// GATT layout (spec): one custom service with two characteristics.
    /// INPUT: write without response (guest to host) and notify (host to guest). It carries the 20-byte
    /// input packet as is, which fits the default ATT payload.
    /// CONTROL: write with response (guest to host) and indicate (host to guest). It carries control
    /// messages split into fragments that fit the negotiated MTU.
    /// The same constants are mirrored in Plugins/iOS/DuelBle.swift and Plugins/Android/DuelBle.kt.
    /// </summary>
    public static class BleUuids
    {
        public const string Service = "6A1D0000-5C3E-4F7B-9B1E-2D8C4F0A7E31";
        public const string Input = "6A1D0001-5C3E-4F7B-9B1E-2D8C4F0A7E31";
        public const string Control = "6A1D0002-5C3E-4F7B-9B1E-2D8C4F0A7E31";
    }

    /// <summary>
    /// How a court announces itself. iOS peripherals can only advertise a local name and service UUIDs,
    /// so the code travels in the local name ("VD-AB12"). Android cannot set a local name without
    /// renaming the phone, so it puts the 4 code bytes in service data for the service UUID, inside the
    /// scan response. Scanners accept either.
    /// </summary>
    public static class CourtAdvert
    {
        public const string NamePrefix = "VD-";
        public const int CodeLength = 4;

        public static string LocalName(string courtCode) => NamePrefix + Normalize(courtCode);

        public static byte[] ServiceData(string courtCode) => Encoding.ASCII.GetBytes(Normalize(courtCode));

        /// <summary>Court code from a scanned advert, or null if it is not one of ours.</summary>
        public static string Parse(string localName, byte[] serviceData)
        {
            if (!string.IsNullOrEmpty(localName) && localName.StartsWith(NamePrefix, StringComparison.Ordinal) && localName.Length >= NamePrefix.Length + CodeLength)
            {
                return localName.Substring(NamePrefix.Length, CodeLength);
            }
            if (serviceData != null && serviceData.Length >= CodeLength)
            {
                for (int i = 0; i < CodeLength; i++)
                {
                    if (!IsCodeChar(serviceData[i])) return null;
                }
                return Encoding.ASCII.GetString(serviceData, 0, CodeLength);
            }
            return null;
        }

        /// <summary>Upper-case letters and digits, padded or cut to 4.</summary>
        public static string Normalize(string code)
        {
            var sb = new StringBuilder(CodeLength);
            if (code != null)
            {
                foreach (char c in code)
                {
                    char u = char.ToUpperInvariant(c);
                    if (IsCodeChar((byte)u) && u < 128) sb.Append(u);
                    if (sb.Length == CodeLength) break;
                }
            }
            while (sb.Length < CodeLength) sb.Append('0');
            return sb.ToString();
        }

        static bool IsCodeChar(byte b) => (b >= (byte)'0' && b <= (byte)'9') || (b >= (byte)'A' && b <= (byte)'Z');
    }

    /// <summary>
    /// Splits control messages into fragments of one ATT payload each. The one-byte header holds bit 7
    /// for the last fragment and bits 0-6 for the message number mod 128. CONTROL is reliable and
    /// ordered, so a message number change mid-message only happens across a reconnect, and the partial
    /// message is dropped.
    /// </summary>
    public sealed class ControlFragmenter
    {
        public const int HeaderBytes = 1;
        public const int MaxMessageBytes = 4096;
        byte messageNumber;

        /// <summary>Calls <paramref name="emit"/> once per fragment.</summary>
        public void Split(ReadOnlySpan<byte> message, int maxFragmentBytes, Action<byte[]> emit)
        {
            if (maxFragmentBytes <= HeaderBytes) throw new ArgumentOutOfRangeException(nameof(maxFragmentBytes));
            if (message.Length > MaxMessageBytes) throw new ArgumentException("control message too large");
            int body = maxFragmentBytes - HeaderBytes;
            int offset = 0;
            do
            {
                int n = Math.Min(body, message.Length - offset);
                bool last = offset + n >= message.Length;
                var fragment = new byte[n + HeaderBytes];
                fragment[0] = (byte)((last ? 0x80 : 0) | (messageNumber & 0x7F));
                message.Slice(offset, n).CopyTo(fragment.AsSpan(HeaderBytes));
                emit(fragment);
                offset += n;
            }
            while (offset < message.Length);
            messageNumber = (byte)((messageNumber + 1) & 0x7F);
        }

        public void Reset() => messageNumber = 0;
    }

    /// <summary>Rebuilds control messages from fragments.</summary>
    public sealed class ControlReassembler
    {
        readonly byte[] buffer = new byte[ControlFragmenter.MaxMessageBytes];
        int length;
        int current = -1;

        /// <summary>Feeds one fragment; returns true with the whole message when it was the last one.</summary>
        public bool Add(ReadOnlySpan<byte> fragment, out ReadOnlySpan<byte> message)
        {
            message = default;
            if (fragment.Length < ControlFragmenter.HeaderBytes) return false;
            int number = fragment[0] & 0x7F;
            bool last = (fragment[0] & 0x80) != 0;
            if (number != current)
            {
                current = number;
                length = 0;
            }
            ReadOnlySpan<byte> body = fragment.Slice(ControlFragmenter.HeaderBytes);
            if (length + body.Length > buffer.Length)
            {
                current = -1;
                length = 0;
                return false;
            }
            body.CopyTo(buffer.AsSpan(length));
            length += body.Length;
            if (!last) return false;
            message = new ReadOnlySpan<byte>(buffer, 0, length);
            current = -1;
            length = 0;
            return true;
        }

        public void Reset()
        {
            current = -1;
            length = 0;
        }
    }

    /// <summary>Kinds of event the native bridges report (values mirrored in Swift and Kotlin).</summary>
    public enum BleEventKind : byte
    {
        /// <summary>Value: 1 powered on, 0 off, -1 unauthorized, -2 unsupported.</summary>
        Adapter = 1,
        /// <summary>PeerId, Name (local name), Value (RSSI dBm), Data (service data, may be empty).</summary>
        PeerFound = 2,
        /// <summary>PeerId, Value (max ATT payload for CONTROL), Value2 (max ATT payload for INPUT).</summary>
        Connected = 3,
        /// <summary>PeerId, Value (platform reason code).</summary>
        Disconnected = 4,
        /// <summary>Channel, Data.</summary>
        Received = 5,
        /// <summary>Value (max ATT payload for CONTROL), Value2 (INPUT).</summary>
        MtuChanged = 6,
        /// <summary>Value (connection interval in microseconds).</summary>
        ConnectionInterval = 7,
        /// <summary>Value (error code), Name (message).</summary>
        Error = 8,
        /// <summary>Advertising started (Value 1) or failed (Value = -error).</summary>
        Advertising = 9,
    }

    /// <summary>One event from a native bridge, decoded on the main thread.</summary>
    public struct BleEvent
    {
        public BleEventKind Kind;
        public byte Channel;
        public int Value;
        public int Value2;
        public string PeerId;
        public string Name;
        public byte[] Data;
    }

    /// <summary>
    /// The byte format both native bridges use to hand events to C#:
    /// u8 kind, u8 channel, i32 value, i32 value2, u16 + UTF-8 peer id, u16 + UTF-8 name, u16 + data.
    /// A batch (Android polling) is a sequence of u16-length-prefixed events.
    /// </summary>
    public static class BleEventCodec
    {
        public static int Encode(in BleEvent e, Span<byte> dst)
        {
            int p = 0;
            dst[p++] = (byte)e.Kind;
            dst[p++] = e.Channel;
            BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(p), e.Value);
            p += 4;
            BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(p), e.Value2);
            p += 4;
            p += WriteBlock(dst.Slice(p), e.PeerId != null ? Encoding.UTF8.GetBytes(e.PeerId) : Array.Empty<byte>());
            p += WriteBlock(dst.Slice(p), e.Name != null ? Encoding.UTF8.GetBytes(e.Name) : Array.Empty<byte>());
            p += WriteBlock(dst.Slice(p), e.Data ?? Array.Empty<byte>());
            return p;
        }

        public static bool TryDecode(ReadOnlySpan<byte> src, out BleEvent e)
        {
            e = default;
            if (src.Length < 16) return false;
            int p = 0;
            e.Kind = (BleEventKind)src[p++];
            e.Channel = src[p++];
            e.Value = BinaryPrimitives.ReadInt32LittleEndian(src.Slice(p));
            p += 4;
            e.Value2 = BinaryPrimitives.ReadInt32LittleEndian(src.Slice(p));
            p += 4;
            if (!ReadBlock(src, ref p, out byte[] id) || !ReadBlock(src, ref p, out byte[] name) || !ReadBlock(src, ref p, out byte[] data)) return false;
            e.PeerId = id.Length > 0 ? Encoding.UTF8.GetString(id) : null;
            e.Name = name.Length > 0 ? Encoding.UTF8.GetString(name) : null;
            e.Data = data;
            return true;
        }

        /// <summary>Decodes a batch of u16-length-prefixed events.</summary>
        public static int DecodeBatch(ReadOnlySpan<byte> batch, Action<BleEvent> each)
        {
            int p = 0, count = 0;
            while (p + 2 <= batch.Length)
            {
                int n = BinaryPrimitives.ReadUInt16LittleEndian(batch.Slice(p));
                p += 2;
                if (p + n > batch.Length) break;
                if (TryDecode(batch.Slice(p, n), out BleEvent e))
                {
                    each(e);
                    count++;
                }
                p += n;
            }
            return count;
        }

        static int WriteBlock(Span<byte> dst, byte[] bytes)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(dst, (ushort)bytes.Length);
            bytes.CopyTo(dst.Slice(2));
            return 2 + bytes.Length;
        }

        static bool ReadBlock(ReadOnlySpan<byte> src, ref int p, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (p + 2 > src.Length) return false;
            int n = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(p));
            p += 2;
            if (p + n > src.Length) return false;
            bytes = src.Slice(p, n).ToArray();
            p += n;
            return true;
        }
    }
}
