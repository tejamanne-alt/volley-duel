using System;
using System.Collections.Generic;
using System.Diagnostics;
using Duel.Core.Net;
using VolleyDuel.Sim;

namespace VolleyDuel.UI
{
    /// <summary>
    /// Host or join a court over any transport, run the session handshake and hand back a
    /// <see cref="NetMatchRunner"/>. The host decides the final match config from the guest's request
    /// (the guest's fighter and Simple mode). Seed = host seed XOR guest seed. A rematch reuses the
    /// connection with a fresh handshake.
    /// </summary>
    public sealed class NetLobby : IDisposable
    {
        public enum Phase
        {
            Idle,
            Hosting,
            Scanning,
            Connecting,
            Handshaking,
            Ready,
            Failed,
        }

        const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        readonly SimTables tables;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random entropy = new Random(Environment.TickCount);
        DuelHandshake handshake;
        MatchConfig hostConfig;
        byte[] guestRequest = Array.Empty<byte>();
        bool isHost;
        bool wantRematch;
        bool peerWantsRematch;
        string localName = "Player";

        public NetLobby(ITransport transport, SimTables tables)
        {
            Transport = transport;
            this.tables = tables;
            transport.PeerFound += OnPeerFound;
            transport.Received += OnReceived;
        }

        public ITransport Transport { get; }

        public Phase State { get; private set; }

        public string Error { get; private set; } = string.Empty;

        public string CourtCode { get; private set; } = string.Empty;

        public readonly List<PeerInfo> Courts = new List<PeerInfo>();

        public MatchAgreement Agreement { get; private set; }

        public long NowMicros => clock.ElapsedTicks * 1000000L / Stopwatch.Frequency;

        public void Host(int fighter, int court, int setsToWin, bool simple, string name)
        {
            isHost = true;
            localName = name;
            hostConfig = MatchConfig.Create(fighter, 0, court, 0, setsToWin);
            hostConfig.Simple0 = simple;
            var code = new char[4];
            for (int i = 0; i < code.Length; i++) code[i] = CodeAlphabet[entropy.Next(CodeAlphabet.Length)];
            CourtCode = new string(code);
            Transport.Host(CourtCode, name);
            State = Transport.State == TransportState.Failed ? Phase.Failed : Phase.Hosting;
            BeginHandshake();
        }

        public void Scan()
        {
            Courts.Clear();
            Transport.StartScan();
            State = Phase.Scanning;
        }

        public void Join(PeerInfo court, int fighter, bool simple, string name)
        {
            isHost = false;
            localName = name;
            guestRequest = new[] { (byte)fighter, (byte)(simple ? 1 : 0) };
            Transport.Join(court);
            State = Phase.Connecting;
            BeginHandshake();
        }

        void BeginHandshake()
        {
            Agreement = null;
            uint seed = (uint)entropy.Next() ^ ((uint)entropy.Next() << 16);
            byte[] local = isHost ? hostConfig.ToBytes() : guestRequest;
            handshake = new DuelHandshake(Transport, isHost, SimVersion.Compute(tables), seed, local, localName, isHost ? (Func<byte[], byte[]>)MergeGuestRequest : null);
        }

        byte[] MergeGuestRequest(byte[] request)
        {
            MatchConfig c = hostConfig;
            if (request.Length >= 1)
            {
                if (request[0] >= tables.Fighters.Length) return null;
                c.Fighter1 = request[0];
            }
            if (request.Length >= 2) c.Simple1 = request[1] != 0;
            return c.ToBytes();
        }

        /// <summary>Pump the transport and the handshake; call every frame while in the lobby.</summary>
        public void Poll()
        {
            if (handshake == null)
            {
                Transport.Poll();
            }
            else
            {
                handshake.Poll(NowMicros);
                if (handshake.Phase == HandshakePhase.Exchanging || handshake.Phase == HandshakePhase.Measuring) State = Phase.Handshaking;
                if (handshake.Phase == HandshakePhase.Ready)
                {
                    Agreement = handshake.Result;
                    handshake = null;
                    State = Phase.Ready;
                }
                else if (handshake.Phase == HandshakePhase.Rejected)
                {
                    Error = handshake.Rejection == RejectReason.SimVersion ? "The other phone runs a different build. Update both phones." : "The court refused the match (" + handshake.Rejection + ").";
                    handshake = null;
                    State = Phase.Failed;
                }
            }
            if (Transport.State == TransportState.Failed && State != Phase.Ready)
            {
                State = Phase.Failed;
                if (Error.Length == 0) Error = "The connection failed.";
            }
        }

        /// <summary>The agreed match: both phones build identical sims from it.</summary>
        public VolleySim CreateSim()
        {
            MatchConfig config = MatchConfig.FromBytes(Agreement.Config);
            config.Seed = Agreement.Seed;
            return new VolleySim(tables, config);
        }

        public NetMatchRunner CreateRunner()
        {
            var runner = new NetMatchRunner(CreateSim(), Transport, Agreement, NowMicros);
            State = Phase.Idle;
            return runner;
        }

        /// <summary>Instant rematch on the same connection: both phones ask, then a fresh handshake runs.</summary>
        public void RequestRematch()
        {
            wantRematch = true;
            Span<byte> msg = stackalloc byte[1];
            msg[0] = (byte)ControlType.Rematch;
            Transport.Send(msg, TransportChannel.Control);
            TryStartRematch();
        }

        public bool PeerWantsRematch => peerWantsRematch;

        void TryStartRematch()
        {
            if (!wantRematch || !peerWantsRematch) return;
            wantRematch = false;
            peerWantsRematch = false;
            State = Phase.Handshaking;
            BeginHandshake();
        }

        void OnReceived(ReadOnlySpan<byte> data, TransportChannel channel)
        {
            if (channel != TransportChannel.Control || data.Length == 0 || data[0] != (byte)ControlType.Rematch) return;
            peerWantsRematch = true;
            TryStartRematch();
        }

        void OnPeerFound(PeerInfo peer)
        {
            for (int i = 0; i < Courts.Count; i++)
            {
                if (Courts[i].Id == peer.Id)
                {
                    Courts[i] = peer;
                    return;
                }
            }
            Courts.Add(peer);
        }

        public void Dispose()
        {
            Transport.PeerFound -= OnPeerFound;
            Transport.Received -= OnReceived;
            Transport.Dispose();
        }
    }
}
