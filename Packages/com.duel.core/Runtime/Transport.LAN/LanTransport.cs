using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Duel.Core.Net;

namespace Duel.Core.Transport.LAN
{
    /// <summary>
    /// LAN transport, the second after BLE. Each phone uses one UDP socket and there is no server. Input
    /// packets are plain datagrams. Control messages carry sequence numbers, cumulative acks and
    /// retransmission, so they arrive reliably and in order. Discovery is pluggable
    /// (<see cref="ILanDiscovery"/>), and a guest can also join a known "ip:port". The socket is
    /// non-blocking and pumped from <see cref="Poll"/>, so there are no threads and every callback runs on
    /// the caller's thread.
    /// </summary>
    public sealed class LanTransport : ITransport
    {
        public const int DefaultGamePort = 47611;
        public const int ControlPayloadLimit = 512;
        public const int InputPayloadLimit = 64;

        const int ReorderWindow = 256;
        const int MaxUnacked = 4096;
        const long ConnectRetryMicros = 200000;
        const long ConnectTimeoutMicros = 10000000;
        const long HeartbeatMicros = 250000;
        const long PeerTimeoutMicros = 25000000;

        enum Kind : byte
        {
            Input = 1,
            Control = 2,
            ControlAck = 3,
            Connect = 4,
            Accept = 5,
            Busy = 6,
            Heartbeat = 7,
            HeartbeatEcho = 8,
            Bye = 9,
        }

        sealed class Pending
        {
            public ushort Seq;
            public byte[] Datagram;
            public long LastSent;
        }

        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly ILanDiscovery discovery;
        readonly int requestedPort;
        readonly IPAddress bindAddress;
        readonly byte[] receiveBuffer = new byte[2048];
        readonly byte[] sendBuffer = new byte[2048];
        readonly List<Pending> unacked = new List<Pending>();
        readonly byte[][] reorder = new byte[ReorderWindow][];
        readonly ushort[] reorderSeq = new ushort[ReorderWindow];
        readonly uint token;

        Socket socket;
        EndPoint anyEndPoint = new IPEndPoint(IPAddress.Any, 0);
        IPEndPoint peer;
        uint peerToken;
        string localName = string.Empty;
        string courtCode = string.Empty;
        bool isHost;
        TransportState state = TransportState.Idle;
        TransportState reported = TransportState.Idle;
        TransportStats stats;
        ushort sendSeq;
        ushort receiveNext;
        bool ackDue;
        long lastHeard;
        long lastHeartbeat;
        long connectStarted;
        long lastConnectSent;
        long srtt;
        long rttVar;
        uint dropRoll;

        /// <param name="discovery">How courts are advertised and found; null for join-by-address only.</param>
        /// <param name="gamePort">Host port (0 picks a free one, as tests do).</param>
        /// <param name="bindAddress">Defaults to every interface; tests pass loopback.</param>
        public LanTransport(ILanDiscovery discovery = null, int gamePort = DefaultGamePort, IPAddress bindAddress = null)
        {
            this.discovery = discovery;
            requestedPort = gamePort;
            this.bindAddress = bindAddress ?? IPAddress.Any;
            token = (uint)(Stopwatch.GetTimestamp() * 2654435761L) ^ (uint)Environment.TickCount;
            if (token == 0) token = 1;
        }

        public string Name => "LAN";

        public TransportState State => state;

        public TransportStats Stats => stats;

        public bool CanHost => true;

        /// <summary>Port the game socket is bound to (after Host or Join).</summary>
        public int LocalPort => socket != null ? ((IPEndPoint)socket.LocalEndPoint).Port : 0;

        /// <summary>Test hook: drop this many outgoing datagrams per thousand (exercises retransmission).</summary>
        public int DropPermille { get; set; }

        public int MaxPayload(TransportChannel channel) => channel == TransportChannel.Input ? InputPayloadLimit : ControlPayloadLimit;

        public event ReceiveHandler Received;
        public event Action<TransportState> StateChanged;
        public event Action<PeerInfo> PeerFound;

        long Now => clock.ElapsedTicks * 1000000L / Stopwatch.Frequency;

        public void Host(string code, string displayName)
        {
            isHost = true;
            courtCode = code ?? string.Empty;
            localName = displayName ?? string.Empty;
            OpenSocket(requestedPort);
            state = TransportState.Advertising;
            discovery?.Advertise(new LanBeacon { CourtCode = courtCode, DisplayName = localName, GamePort = LocalPort });
        }

        public void StartScan()
        {
            if (discovery == null) return;
            state = TransportState.Scanning;
            discovery.StartBrowsing(OnPeerFound);
        }

        public void StopScan()
        {
            discovery?.StopBrowsing();
            if (state == TransportState.Scanning) state = TransportState.Idle;
        }

        /// <summary>Join a court found by discovery, or any "ip:port" typed or scanned from a code.</summary>
        public void Join(PeerInfo target)
        {
            if (!TryParseEndPoint(target.Id, out IPEndPoint endPoint))
            {
                state = TransportState.Failed;
                return;
            }
            discovery?.StopBrowsing();
            isHost = false;
            OpenSocket(0);
            peer = endPoint;
            connectStarted = Now;
            lastConnectSent = long.MinValue / 2;
            state = TransportState.Connecting;
        }

        public void Send(ReadOnlySpan<byte> data, TransportChannel channel)
        {
            if (state != TransportState.Connected || peer == null) return;
            if (channel == TransportChannel.Input)
            {
                if (data.Length > InputPayloadLimit) throw new ArgumentException("input payload too large");
                sendBuffer[0] = (byte)Kind.Input;
                data.CopyTo(sendBuffer.AsSpan(1));
                SendDatagram(sendBuffer, data.Length + 1);
                return;
            }
            if (data.Length > ControlPayloadLimit) throw new ArgumentException("control payload too large");
            if (unacked.Count >= MaxUnacked)
            {
                // The peer stopped acknowledging for minutes: the link is gone.
                state = TransportState.Failed;
                return;
            }
            var datagram = new byte[data.Length + 3];
            datagram[0] = (byte)Kind.Control;
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(1), sendSeq);
            data.CopyTo(datagram.AsSpan(3));
            unacked.Add(new Pending { Seq = sendSeq, Datagram = datagram, LastSent = Now });
            sendSeq++;
            SendDatagram(datagram, datagram.Length);
        }

        public void Poll()
        {
            long now = Now;
            discovery?.Poll(now);
            if (socket != null)
            {
                Receive(now);
                if (state == TransportState.Connecting) ConnectStep(now);
                if (state == TransportState.Connected)
                {
                    Retransmit(now);
                    if (ackDue) SendAck();
                    if (now - lastHeartbeat >= HeartbeatMicros) SendHeartbeat(now);
                    if (now - lastHeard > PeerTimeoutMicros) state = TransportState.Disconnected;
                }
            }
            if (state != reported)
            {
                reported = state;
                StateChanged?.Invoke(state);
            }
        }

        public void Disconnect()
        {
            if (socket != null && peer != null && state == TransportState.Connected)
            {
                sendBuffer[0] = (byte)Kind.Bye;
                for (int i = 0; i < 3; i++) SendRaw(sendBuffer, 1, peer);
            }
            discovery?.StopAdvertising();
            discovery?.StopBrowsing();
            state = TransportState.Disconnected;
            CloseSocket();
        }

        public void Dispose()
        {
            Disconnect();
            discovery?.Dispose();
        }

        // ------------------------------------------------------------------ socket

        void OpenSocket(int port)
        {
            CloseSocket();
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            socket.Bind(new IPEndPoint(bindAddress, port));
            unacked.Clear();
            for (int i = 0; i < ReorderWindow; i++) reorder[i] = null;
            sendSeq = 0;
            receiveNext = 0;
            peer = null;
            lastHeard = Now;
            stats = default;
            stats.Mtu = 1200;
        }

        void CloseSocket()
        {
            socket?.Dispose();
            socket = null;
        }

        void SendDatagram(byte[] datagram, int length)
        {
            stats.PacketsSent++;
            stats.BytesSent += length;
            if (DropPermille > 0)
            {
                dropRoll = dropRoll * 1664525u + 1013904223u;
                if ((dropRoll >> 8) % 1000 < (uint)DropPermille) return;
            }
            SendRaw(datagram, length, peer);
        }

        void SendRaw(byte[] datagram, int length, IPEndPoint to)
        {
            try
            {
                socket.SendTo(datagram, 0, length, SocketFlags.None, to);
            }
            catch (SocketException)
            {
                // Wi-Fi blips surface here; the session's silence timers decide what that means.
            }
        }

        void Receive(long now)
        {
            while (true)
            {
                int n;
                EndPoint from = anyEndPoint;
                try
                {
                    if (socket.Available <= 0) return;
                    n = socket.ReceiveFrom(receiveBuffer, ref from);
                }
                catch (SocketException)
                {
                    return;
                }
                if (n <= 0) continue;
                var source = (IPEndPoint)from;
                Handle(new ReadOnlySpan<byte>(receiveBuffer, 0, n), source, now);
            }
        }

        void Handle(ReadOnlySpan<byte> d, IPEndPoint from, long now)
        {
            var kind = (Kind)d[0];
            if (kind == Kind.Connect)
            {
                OnConnect(d, from, now);
                return;
            }
            if (peer == null || !peer.Equals(from)) return;
            lastHeard = now;
            stats.PacketsReceived++;
            stats.BytesReceived += d.Length;
            switch (kind)
            {
                case Kind.Accept:
                    if (state == TransportState.Connecting && d.Length >= 5 && BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(1)) == token)
                    {
                        state = TransportState.Connected;
                    }
                    break;
                case Kind.Busy:
                    if (state == TransportState.Connecting) state = TransportState.Failed;
                    break;
                case Kind.Input:
                    if (state == TransportState.Connected) Received?.Invoke(d.Slice(1), TransportChannel.Input);
                    break;
                case Kind.Control:
                    if (state == TransportState.Connected && d.Length >= 3) OnControl(BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(1)), d.Slice(3));
                    break;
                case Kind.ControlAck:
                    if (d.Length >= 3) OnAck(BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(1)));
                    break;
                case Kind.Heartbeat:
                    if (d.Length >= 5)
                    {
                        sendBuffer[0] = (byte)Kind.HeartbeatEcho;
                        d.Slice(1, 4).CopyTo(sendBuffer.AsSpan(1));
                        SendDatagram(sendBuffer, 5);
                    }
                    break;
                case Kind.HeartbeatEcho:
                    if (d.Length >= 5) SampleRtt((uint)now - BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(1)));
                    break;
                case Kind.Bye:
                    state = TransportState.Disconnected;
                    break;
            }
        }

        // ------------------------------------------------------------------ connection

        void ConnectStep(long now)
        {
            if (now - connectStarted > ConnectTimeoutMicros)
            {
                state = TransportState.Failed;
                return;
            }
            if (now - lastConnectSent < ConnectRetryMicros) return;
            lastConnectSent = now;
            sendBuffer[0] = (byte)Kind.Connect;
            BinaryPrimitives.WriteUInt32LittleEndian(sendBuffer.AsSpan(1), token);
            BinaryPrimitives.WriteUInt16LittleEndian(sendBuffer.AsSpan(5), NetProtocol.Version);
            SendRaw(sendBuffer, 7, peer);
        }

        void OnConnect(ReadOnlySpan<byte> d, IPEndPoint from, long now)
        {
            if (!isHost || d.Length < 7) return;
            uint guestToken = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(1));
            bool fresh = peer == null && (state == TransportState.Advertising || state == TransportState.Idle);
            bool repeat = peer != null && peer.Equals(from) && guestToken == peerToken;
            if (fresh)
            {
                peer = from;
                peerToken = guestToken;
                lastHeard = now;
                state = TransportState.Connected;
                discovery?.StopAdvertising();
            }
            if (fresh || repeat)
            {
                sendBuffer[0] = (byte)Kind.Accept;
                BinaryPrimitives.WriteUInt32LittleEndian(sendBuffer.AsSpan(1), guestToken);
                SendRaw(sendBuffer, 5, from);
                return;
            }
            // A second phone tried to join a full court.
            sendBuffer[0] = (byte)Kind.Busy;
            SendRaw(sendBuffer, 1, from);
        }

        // ------------------------------------------------------------------ reliable control channel

        void OnControl(ushort seq, ReadOnlySpan<byte> payload)
        {
            ackDue = true;
            int ahead = (short)(seq - receiveNext);
            if (ahead < 0 || ahead >= ReorderWindow) return;
            int slot = seq % ReorderWindow;
            if (ahead > 0)
            {
                if (reorder[slot] == null)
                {
                    reorder[slot] = payload.ToArray();
                    reorderSeq[slot] = seq;
                }
                return;
            }
            Received?.Invoke(payload, TransportChannel.Control);
            receiveNext++;
            while (true)
            {
                int next = receiveNext % ReorderWindow;
                byte[] buffered = reorder[next];
                if (buffered == null || reorderSeq[next] != receiveNext) break;
                reorder[next] = null;
                Received?.Invoke(buffered, TransportChannel.Control);
                receiveNext++;
            }
        }

        void SendAck()
        {
            ackDue = false;
            sendBuffer[0] = (byte)Kind.ControlAck;
            BinaryPrimitives.WriteUInt16LittleEndian(sendBuffer.AsSpan(1), receiveNext);
            SendDatagram(sendBuffer, 3);
        }

        void OnAck(ushort nextExpected)
        {
            int removed = 0;
            while (removed < unacked.Count && (short)(unacked[removed].Seq - nextExpected) < 0) removed++;
            if (removed > 0) unacked.RemoveRange(0, removed);
        }

        void Retransmit(long now)
        {
            long rto = srtt > 0 ? Math.Max(30000, Math.Min(1000000, srtt + 4 * rttVar)) : 100000;
            for (int i = 0; i < unacked.Count; i++)
            {
                Pending p = unacked[i];
                if (now - p.LastSent < rto) continue;
                p.LastSent = now;
                SendDatagram(p.Datagram, p.Datagram.Length);
            }
        }

        void SendHeartbeat(long now)
        {
            lastHeartbeat = now;
            sendBuffer[0] = (byte)Kind.Heartbeat;
            BinaryPrimitives.WriteUInt32LittleEndian(sendBuffer.AsSpan(1), (uint)now);
            SendDatagram(sendBuffer, 5);
        }

        void SampleRtt(uint rtt)
        {
            long r = Math.Min(rtt, 5000000u);
            if (srtt == 0)
            {
                srtt = r;
                rttVar = r / 2;
            }
            else
            {
                rttVar += (Math.Abs(r - srtt) - rttVar) / 4;
                srtt += (r - srtt) / 8;
            }
            stats.RttMicros = (int)srtt;
            stats.JitterMicros = (int)rttVar;
        }

        // ------------------------------------------------------------------ discovery

        void OnPeerFound(PeerInfo info) => PeerFound?.Invoke(info);

        /// <summary>Parses "a.b.c.d:port" (port defaults to <see cref="DefaultGamePort"/>).</summary>
        public static bool TryParseEndPoint(string text, out IPEndPoint endPoint)
        {
            endPoint = null;
            if (string.IsNullOrEmpty(text)) return false;
            int colon = text.LastIndexOf(':');
            string host = colon >= 0 ? text.Substring(0, colon) : text;
            int port = DefaultGamePort;
            if (colon >= 0 && !int.TryParse(text.Substring(colon + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port)) return false;
            if (port <= 0 || port > 65535 || !IPAddress.TryParse(host, out IPAddress address)) return false;
            endPoint = new IPEndPoint(address, port);
            return true;
        }

        /// <summary>Formats an endpoint the way <see cref="TryParseEndPoint"/> reads it.</summary>
        public static string FormatEndPoint(IPAddress address, int port) => address + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
