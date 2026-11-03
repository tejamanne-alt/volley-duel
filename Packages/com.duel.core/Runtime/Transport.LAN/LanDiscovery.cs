using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Duel.Core.Net;

namespace Duel.Core.Transport.LAN
{
    /// <summary>What a hosting phone advertises on the LAN.</summary>
    public struct LanBeacon
    {
        public string CourtCode;
        public string DisplayName;
        public int GamePort;
    }

    /// <summary>
    /// How courts are found on a LAN. Broadcast beacons (below) work on Android, desktop and the editor.
    /// iOS needs Apple's multicast entitlement for broadcast, so iOS discovery goes through Bonjour or
    /// join-by-address instead (DECISIONS N5).
    /// </summary>
    public interface ILanDiscovery : IDisposable
    {
        void Advertise(LanBeacon beacon);

        void StopAdvertising();

        void StartBrowsing(Action<PeerInfo> found);

        void StopBrowsing();

        /// <summary>Pumped by the transport's Poll on the caller's thread.</summary>
        void Poll(long nowMicros);
    }

    /// <summary>
    /// UDP broadcast beacons: the host sends a small beacon twice a second, and browsers listen on the
    /// discovery port. A beacon is 4 bytes of magic, the u16 protocol, the u16 game port, the 4-byte court
    /// code and a name of up to 32 bytes. Sockets are non-blocking and use no threads.
    /// </summary>
    public sealed class BroadcastDiscovery : ILanDiscovery
    {
        public const int DefaultDiscoveryPort = 47610;
        const uint Magic = 0x4E414C44; // "DLAN"
        const long BeaconIntervalMicros = 500000;

        readonly int discoveryPort;
        readonly List<IPEndPoint> targets = new List<IPEndPoint>();
        readonly byte[] buffer = new byte[256];
        readonly List<string> seen = new List<string>();
        Socket sender;
        Socket listener;
        byte[] beacon;
        int beaconLength;
        long lastBeacon = long.MinValue / 2;
        Action<PeerInfo> onFound;

        /// <param name="discoveryPort">Port beacons go to (tests use a private one).</param>
        /// <param name="extraTargets">Unicast targets besides the broadcast address (loopback in tests, known peers).</param>
        /// <param name="broadcast">Send to 255.255.255.255 as well.</param>
        public BroadcastDiscovery(int discoveryPort = DefaultDiscoveryPort, IEnumerable<IPEndPoint> extraTargets = null, bool broadcast = true)
        {
            this.discoveryPort = discoveryPort;
            if (broadcast) targets.Add(new IPEndPoint(IPAddress.Broadcast, discoveryPort));
            if (extraTargets != null) targets.AddRange(extraTargets);
        }

        public void Advertise(LanBeacon b)
        {
            string code = (b.CourtCode ?? string.Empty).PadRight(4).Substring(0, 4);
            byte[] name = Encoding.UTF8.GetBytes(b.DisplayName ?? string.Empty);
            int nameLength = Math.Min(32, name.Length);
            beacon = new byte[13 + nameLength];
            BinaryPrimitives.WriteUInt32LittleEndian(beacon, Magic);
            BinaryPrimitives.WriteUInt16LittleEndian(beacon.AsSpan(4), NetProtocol.Version);
            BinaryPrimitives.WriteUInt16LittleEndian(beacon.AsSpan(6), (ushort)b.GamePort);
            Encoding.ASCII.GetBytes(code, 0, 4, beacon, 8);
            beacon[12] = (byte)nameLength;
            Array.Copy(name, 0, beacon, 13, nameLength);
            beaconLength = beacon.Length;
            if (sender == null)
            {
                sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false, EnableBroadcast = true };
            }
            lastBeacon = long.MinValue / 2;
        }

        public void StopAdvertising()
        {
            beacon = null;
            sender?.Dispose();
            sender = null;
        }

        public void StartBrowsing(Action<PeerInfo> found)
        {
            onFound = found;
            seen.Clear();
            if (listener != null) return;
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
        }

        public void StopBrowsing()
        {
            onFound = null;
            listener?.Dispose();
            listener = null;
        }

        public void Poll(long nowMicros)
        {
            if (beacon != null && sender != null && nowMicros - lastBeacon >= BeaconIntervalMicros)
            {
                lastBeacon = nowMicros;
                foreach (IPEndPoint t in targets)
                {
                    try
                    {
                        sender.SendTo(beacon, 0, beaconLength, SocketFlags.None, t);
                    }
                    catch (SocketException)
                    {
                        // No route for broadcast on this network; unicast targets may still work.
                    }
                }
            }
            if (listener == null) return;
            while (true)
            {
                int n;
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    if (listener.Available <= 0) return;
                    n = listener.ReceiveFrom(buffer, ref from);
                }
                catch (SocketException)
                {
                    return;
                }
                if (TryReadBeacon(new ReadOnlySpan<byte>(buffer, 0, n), ((IPEndPoint)from).Address, out PeerInfo peer))
                {
                    if (seen.Contains(peer.Id)) continue;
                    seen.Add(peer.Id);
                    onFound?.Invoke(peer);
                }
            }
        }

        /// <summary>Parses a beacon; the peer id is the sender's address with the advertised game port.</summary>
        public static bool TryReadBeacon(ReadOnlySpan<byte> d, IPAddress source, out PeerInfo peer)
        {
            peer = default;
            if (d.Length < 13 || BinaryPrimitives.ReadUInt32LittleEndian(d) != Magic) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(4)) != NetProtocol.Version) return false;
            int port = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(6));
            int nameLength = d[12];
            if (d.Length < 13 + nameLength || port == 0) return false;
            peer = new PeerInfo
            {
                Id = LanTransport.FormatEndPoint(source, port),
                CourtCode = Encoding.ASCII.GetString(d.Slice(8, 4).ToArray()).Trim(),
                DisplayName = Encoding.UTF8.GetString(d.Slice(13, nameLength).ToArray()),
                SignalDbm = 0,
            };
            return true;
        }

        public void Dispose()
        {
            StopAdvertising();
            StopBrowsing();
        }
    }
}
