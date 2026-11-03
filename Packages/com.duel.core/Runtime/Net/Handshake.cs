using System;

namespace Duel.Core.Net
{
    /// <summary>What both phones agreed before frame 0.</summary>
    public sealed class MatchAgreement
    {
        public bool IsHost;
        /// <summary>Spec: seed = host seed XOR guest seed.</summary>
        public uint Seed;
        /// <summary>Game match config (opaque to Duel Core), identical on both phones.</summary>
        public byte[] Config;
        public int InputDelay;
        public int RttMicros;
        public string RemoteName;
    }

    public enum HandshakePhase : byte
    {
        WaitingForPeer,
        Exchanging,
        Measuring,
        Ready,
        Rejected,
    }

    /// <summary>
    /// Session handshake (spec): protocol version; sim version (a build hash of sim code and data tables
    /// that refuses mismatched builds); match config; seed = host seed XOR guest seed; RTT samples that pick
    /// the input delay (1-3 frames). The host decides the final config from the guest's request.
    /// </summary>
    public sealed class DuelHandshake
    {
        public const int PingSamples = 8;

        readonly ITransport transport;
        readonly bool host;
        readonly ulong simVersion;
        readonly uint localSeed;
        readonly byte[] localConfig;
        readonly string localName;
        readonly Func<byte[], byte[]> hostMergesGuestRequest;
        readonly NetConfig netConfig;
        readonly long[] pingSent = new long[PingSamples];
        readonly long[] rttSamples = new long[PingSamples];
        int pongs;
        int pingsSent;
        long now;
        uint remoteSeed;
        byte[] agreedConfig;
        string remoteName = string.Empty;

        /// <param name="localConfig">Host: its default config. Guest: its request (fighter choice and so on).</param>
        /// <param name="hostMergesGuestRequest">Host only: builds the final config from the guest's request.</param>
        public DuelHandshake(ITransport transport, bool host, ulong simVersion, uint localSeed, byte[] localConfig, string localName, Func<byte[], byte[]> hostMergesGuestRequest = null, NetConfig netConfig = null)
        {
            this.transport = transport;
            this.host = host;
            this.simVersion = simVersion;
            this.localSeed = localSeed;
            this.localConfig = localConfig ?? Array.Empty<byte>();
            this.localName = localName ?? string.Empty;
            this.hostMergesGuestRequest = hostMergesGuestRequest;
            this.netConfig = netConfig ?? new NetConfig();
            transport.Received += OnReceive;
            transport.StateChanged += OnState;
            Phase = HandshakePhase.WaitingForPeer;
        }

        public HandshakePhase Phase { get; private set; }

        public RejectReason Rejection { get; private set; }

        public MatchAgreement Result { get; private set; }

        public void Poll(long nowMicros)
        {
            now = nowMicros;
            transport.Poll();
            if (Phase == HandshakePhase.WaitingForPeer && transport.State == TransportState.Connected) OnState(TransportState.Connected);
            if (host && Phase == HandshakePhase.Measuring && pingsSent < PingSamples && (pingsSent == 0 || pongs >= pingsSent))
            {
                SendPing();
            }
        }

        void OnState(TransportState s)
        {
            if (s != TransportState.Connected || Phase != HandshakePhase.WaitingForPeer) return;
            Phase = HandshakePhase.Exchanging;
            if (!host)
            {
                Span<byte> buf = stackalloc byte[256];
                var w = new ControlWriter(buf, ControlType.Hello);
                w.U16(NetProtocol.Version);
                w.U64(simVersion);
                w.U32(localSeed);
                w.Bytes(localConfig);
                w.Text(localName);
                transport.Send(w.Written, TransportChannel.Control);
            }
        }

        void OnReceive(ReadOnlySpan<byte> data, TransportChannel channel)
        {
            if (channel != TransportChannel.Control || Phase == HandshakePhase.Ready || Phase == HandshakePhase.Rejected) return;
            var r = new ControlReader(data);
            switch (r.Type)
            {
                case ControlType.Hello when host:
                    {
                        ushort protocol = r.U16();
                        ulong version = r.U64();
                        remoteSeed = r.U32();
                        byte[] request = r.Bytes();
                        remoteName = r.Text();
                        if (!r.Ok) return;
                        if (protocol != NetProtocol.Version) { Reject(RejectReason.ProtocolVersion); return; }
                        if (version != simVersion) { Reject(RejectReason.SimVersion); return; }
                        agreedConfig = hostMergesGuestRequest != null ? hostMergesGuestRequest(request) : localConfig;
                        if (agreedConfig == null) { Reject(RejectReason.BadConfig); return; }
                        Span<byte> buf = stackalloc byte[256];
                        var w = new ControlWriter(buf, ControlType.Welcome);
                        w.U16(NetProtocol.Version);
                        w.U64(simVersion);
                        w.U32(localSeed);
                        w.Bytes(agreedConfig);
                        w.Text(localName);
                        transport.Send(w.Written, TransportChannel.Control);
                        Phase = HandshakePhase.Measuring;
                        break;
                    }
                case ControlType.Welcome when !host:
                    {
                        ushort protocol = r.U16();
                        ulong version = r.U64();
                        remoteSeed = r.U32();
                        agreedConfig = r.Bytes();
                        remoteName = r.Text();
                        if (!r.Ok) return;
                        if (protocol != NetProtocol.Version || version != simVersion)
                        {
                            Rejection = protocol != NetProtocol.Version ? RejectReason.ProtocolVersion : RejectReason.SimVersion;
                            Phase = HandshakePhase.Rejected;
                            return;
                        }
                        Phase = HandshakePhase.Measuring;
                        break;
                    }
                case ControlType.Reject:
                    Rejection = (RejectReason)r.U8();
                    Phase = HandshakePhase.Rejected;
                    break;
                case ControlType.Ping when !host:
                    {
                        ushort id = r.U16();
                        Span<byte> buf = stackalloc byte[8];
                        var w = new ControlWriter(buf, ControlType.Pong);
                        w.U16(id);
                        transport.Send(w.Written, TransportChannel.Control);
                        break;
                    }
                case ControlType.Pong when host:
                    {
                        ushort id = r.U16();
                        if (id >= PingSamples || pingSent[id] == 0) return;
                        rttSamples[pongs++] = now - pingSent[id];
                        if (pongs == PingSamples) SendStart();
                        break;
                    }
                case ControlType.Start when !host:
                    {
                        int inputDelay = r.U8();
                        int rtt = r.I32();
                        if (!r.Ok) return;
                        Finish(inputDelay, rtt);
                        break;
                    }
            }
        }

        void SendPing()
        {
            int id = pingsSent++;
            pingSent[id] = now == 0 ? 1 : now;
            Span<byte> buf = stackalloc byte[8];
            var w = new ControlWriter(buf, ControlType.Ping);
            w.U16((ushort)id);
            transport.Send(w.Written, TransportChannel.Control);
        }

        void SendStart()
        {
            Array.Sort(rttSamples);
            long median = rttSamples[PingSamples / 2];
            // Delay covers the one-way trip; spec: 2 frames, adaptive 1-3.
            int oneWayFrames = (int)((median / 2 + netConfig.TickMicros - 1) / netConfig.TickMicros);
            int inputDelay = Math.Max(netConfig.MinInputDelay, Math.Min(netConfig.MaxInputDelay, oneWayFrames + 1));
            Span<byte> buf = stackalloc byte[16];
            var w = new ControlWriter(buf, ControlType.Start);
            w.U8((byte)inputDelay);
            w.I32((int)Math.Min(int.MaxValue, median));
            transport.Send(w.Written, TransportChannel.Control);
            Finish(inputDelay, (int)Math.Min(int.MaxValue, median));
        }

        void Reject(RejectReason reason)
        {
            Span<byte> buf = stackalloc byte[4];
            var w = new ControlWriter(buf, ControlType.Reject);
            w.U8((byte)reason);
            transport.Send(w.Written, TransportChannel.Control);
            Rejection = reason;
            Phase = HandshakePhase.Rejected;
        }

        void Finish(int inputDelay, int rtt)
        {
            Result = new MatchAgreement
            {
                IsHost = host,
                Seed = localSeed ^ remoteSeed,
                Config = agreedConfig,
                InputDelay = inputDelay,
                RttMicros = rtt,
                RemoteName = remoteName,
            };
            Phase = HandshakePhase.Ready;
            transport.Received -= OnReceive;
            transport.StateChanged -= OnState;
        }
    }
}
