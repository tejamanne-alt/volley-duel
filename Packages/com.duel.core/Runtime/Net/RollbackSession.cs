using System;
using System.Collections.Generic;
using Duel.Core.Sim;

namespace Duel.Core.Net
{
    public sealed class NetConfig
    {
        /// <summary>Spec: 2 frames of input delay, adaptive 1-3 (chosen from RTT at the handshake).</summary>
        public int InputDelay = 2;
        public int MinInputDelay = 1;
        public int MaxInputDelay = 3;
        /// <summary>Spec: at most 8 frames of rollback; beyond that the leading peer slows.</summary>
        public int MaxRollback = 8;
        /// <summary>Spec: a 16-frame snapshot ring buffer.</summary>
        public int SnapshotFrames = 16;
        /// <summary>Spec: compare state hashes every 30 frames.</summary>
        public int HashInterval = 30;
        /// <summary>Spec: 3 seconds of silence pauses the match.</summary>
        public long PauseAfterMicros = 3000000;
        /// <summary>Spec: 20 seconds to reconnect, otherwise no contest.</summary>
        public long NoContestAfterMicros = 20000000;
        /// <summary>Frames of average lead over the remote before the leading peer waits a frame.</summary>
        public int TimeSyncLead = 1;
        public long TickMicros = 16667;
        /// <summary>Resend timeout for lost inputs before the first RTT sample.</summary>
        public long ResendBeforeRttMicros = 100000;
    }

    public enum SessionStatus : byte
    {
        Running,
        /// <summary>Did not advance this tick: time sync or the rollback budget is exhausted.</summary>
        Waiting,
        /// <summary>No packets for 3 s: the match is paused until the link returns.</summary>
        Paused,
        /// <summary>No packets for 20 s: no contest.</summary>
        NoContest,
    }

    /// <summary>Rollback and link measurements for the debug overlay and session log.</summary>
    public sealed class NetStats
    {
        public int Frames;
        public int Rollbacks;
        public int MaxRollbackDepth;
        /// <summary>Rollback depth histogram, index = frames resimulated (last bucket = MaxRollback or more).</summary>
        public readonly int[] RollbackHistogram = new int[10];
        public int Waits;
        public int Stalls;
        public int Desyncs;
        public int Resyncs;
        public int HashChecks;
        public int RttMicros;
        public int JitterMicros;
        public int LossPermille;
        public int PacketsReceived;
        public int PacketsSent;

        /// <summary>Rollback depth at the given percentile (0-100) over all advanced frames.</summary>
        public int RollbackPercentile(int percent)
        {
            int total = Frames;
            if (total <= 0) return 0;
            int noRollback = total - Rollbacks;
            int target = (total * percent + 99) / 100;
            if (target <= noRollback) return 0;
            int seen = noRollback;
            for (int d = 1; d < RollbackHistogram.Length; d++)
            {
                seen += RollbackHistogram[d];
                if (seen >= target) return d;
            }
            return RollbackHistogram.Length - 1;
        }
    }

    /// <summary>
    /// Peer-to-peer rollback over any <see cref="ITransport"/>. Each phone runs the same deterministic sim,
    /// sends only its inputs (20-byte packets with 8 frames of redundancy), predicts the remote input by
    /// repeating the last one, and when the real input arrives rolls back to the first mispredicted frame
    /// and resimulates. Confirmed frames are hash-checked every 30 frames; the host repairs a desync by
    /// sending its full state in chunks.
    /// </summary>
    public sealed class RollbackSession<TState> where TState : struct
    {
        const int RingSize = 256;
        const int RingMask = RingSize - 1;

        readonly ISimulation<TState> sim;
        readonly ITransport transport;
        readonly NetConfig cfg;
        readonly int local;
        readonly bool isHost;
        readonly int delay;

        TState state;
        int frame;

        readonly TState[] snapshots;
        readonly int[] snapshotFrame;

        readonly FrameInput[] localInputs = new FrameInput[RingSize];
        readonly int[] localFrameAt = new int[RingSize];
        readonly long[] localSentAt = new long[RingSize];
        readonly long[] localLastSent = new long[RingSize];
        readonly FrameInput[] remoteInputs = new FrameInput[RingSize];
        readonly int[] remoteFrameAt = new int[RingSize];
        readonly FrameInput[] remoteUsed = new FrameInput[RingSize];
        int localNext;
        int remoteContiguous;
        int remoteLatest;
        int mispredictedFrom = int.MaxValue;
        int remoteAck = -1;

        readonly Dictionary<int, ulong> localHashes = new Dictionary<int, ulong>();
        readonly Dictionary<int, ulong> remoteHashes = new Dictionary<int, ulong>();
        int nextHashFrame;

        // Full-state repair.
        byte[] incomingState;
        int incomingFrame = -1;
        int incomingChunks;
        bool[] incomingHave;
        readonly byte[] scratch;

        long now;
        long lastReceive;
        long advantageSum;
        int advantageSamples;
        int syncTicks;
        int packetsExpected;
        int newestReceived;
        int gapResentThrough = -1;
        readonly byte[] packetBuffer = new byte[InputPacket.Size];
        readonly byte[] chunkBuffer = new byte[NetProtocol.StateChunkBytes + 32];
        readonly byte[] hashBuffer = new byte[16];

        public RollbackSession(ISimulation<TState> sim, TState initial, int localPlayer, bool isHost, ITransport transport, NetConfig config, int inputDelay, long nowMicros)
        {
            this.sim = sim;
            this.transport = transport;
            cfg = config ?? new NetConfig();
            local = localPlayer;
            this.isHost = isHost;
            delay = Math.Max(cfg.MinInputDelay, Math.Min(cfg.MaxInputDelay, inputDelay));
            state = initial;
            frame = sim.FrameOf(initial);
            snapshots = new TState[cfg.SnapshotFrames];
            snapshotFrame = new int[cfg.SnapshotFrames];
            for (int i = 0; i < snapshotFrame.Length; i++) snapshotFrame[i] = -1;
            for (int i = 0; i < RingSize; i++)
            {
                localFrameAt[i] = -1;
                remoteFrameAt[i] = -1;
            }
            // Both peers agree that frames [0, delay) carry neutral input.
            for (int f = frame; f < frame + delay; f++)
            {
                SetLocal(f, FrameInput.Neutral, nowMicros);
                SetRemote(f, FrameInput.Neutral);
            }
            localNext = frame + delay;
            remoteContiguous = frame + delay - 1;
            remoteLatest = remoteContiguous;
            remoteAck = remoteContiguous;
            nextHashFrame = ((frame / cfg.HashInterval) + 1) * cfg.HashInterval;
            scratch = new byte[sim.MaxStateBytes];
            now = nowMicros;
            lastReceive = nowMicros;
            transport.Received += OnReceive;
        }

        public ref readonly TState State => ref state;

        public int Frame => frame;

        public int InputDelay => delay;

        public int LocalPlayer => local;

        public SessionStatus Status { get; private set; }

        public NetStats Stats { get; } = new NetStats();

        /// <summary>Events produced by the last <see cref="Tick"/>, including re-simulated frames.</summary>
        public SimEventBuffer Events { get; } = new SimEventBuffer();

        /// <summary>First re-simulated frame in the last tick, or -1 if there was no rollback.</summary>
        public int ResimulatedFrom { get; private set; } = -1;

        /// <summary>Highest frame whose inputs are all known on both sides.</summary>
        public int ConfirmedFrame => Math.Min(remoteContiguous, frame - 1);

        /// <summary>Raised with the frame number when hashes disagree.</summary>
        public event Action<int> DesyncDetected;

        /// <summary>Raised on the guest after it adopted the host's state to repair a desync.</summary>
        public event Action<int> Resynced;

        /// <summary>Raised when a local input is bound to a frame (replays and reference checks).</summary>
        public event Action<int, FrameInput> LocalInputCommitted;

        /// <summary>Called by dev builds on a desync with both serialized states (local, frame) for diffing.</summary>
        public Action<int, byte[]> DumpState;

        /// <summary>
        /// One app tick at 60 Hz: pump the transport, take the local input, send the input packet and advance
        /// (with a rollback if needed). Returns whether the sim advanced.
        /// </summary>
        public SessionStatus Tick(FrameInput localInput, long nowMicros)
        {
            now = nowMicros;
            Events.Clear();
            ResimulatedFrom = -1;
            transport.Poll();
            ApplyIncomingState();

            long silence = now - lastReceive;
            if (silence > cfg.NoContestAfterMicros)
            {
                Status = SessionStatus.NoContest;
                return Status;
            }
            if (silence > cfg.PauseAfterMicros)
            {
                Status = SessionStatus.Paused;
                SendInputs();
                return Status;
            }

            if (ShouldWaitForTimeSync())
            {
                Stats.Waits++;
                SendInputs();
                Status = SessionStatus.Waiting;
                return Status;
            }
            if (frame - remoteContiguous > cfg.MaxRollback)
            {
                // Out of prediction budget: the leading peer slows one frame at a time.
                Stats.Stalls++;
                SendInputs();
                Status = SessionStatus.Waiting;
                return Status;
            }

            if (localNext <= frame + delay)
            {
                SetLocal(localNext, localInput, now);
                LocalInputCommitted?.Invoke(localNext, localInput);
                localNext++;
            }
            SendInputs();

            if (mispredictedFrom < frame) Rollback(mispredictedFrom);
            StepFrame(frame);
            frame++;
            Stats.Frames++;
            CheckHashes();
            Status = SessionStatus.Running;
            return Status;
        }

        void Rollback(int from)
        {
            int depth = frame - from;
            int slot = SnapshotSlot(from);
            if (snapshotFrame[slot] != from)
            {
                // Outside the snapshot ring: cannot happen while the rollback budget holds.
                mispredictedFrom = int.MaxValue;
                return;
            }
            state = snapshots[slot];
            ResimulatedFrom = from;
            for (int f = from; f < frame; f++) StepFrame(f);
            mispredictedFrom = int.MaxValue;
            Stats.Rollbacks++;
            if (depth > Stats.MaxRollbackDepth) Stats.MaxRollbackDepth = depth;
            Stats.RollbackHistogram[Math.Min(depth, Stats.RollbackHistogram.Length - 1)]++;
        }

        void StepFrame(int f)
        {
            int slot = SnapshotSlot(f);
            snapshots[slot] = state;
            snapshotFrame[slot] = f;
            FrameInput mine = LocalAt(f);
            FrameInput theirs = RemoteKnown(f) ? remoteInputs[f & RingMask] : PredictRemote(f);
            remoteUsed[f & RingMask] = theirs;
            if (local == 0) state = sim.Step(state, mine, theirs, Events);
            else state = sim.Step(state, theirs, mine, Events);
        }

        int SnapshotSlot(int f) => ((f % snapshots.Length) + snapshots.Length) % snapshots.Length;

        bool TryGetSnapshot(int f, out TState s)
        {
            int slot = SnapshotSlot(f);
            if (snapshotFrame[slot] == f)
            {
                s = snapshots[slot];
                return true;
            }
            s = default;
            return false;
        }

        // ------------------------------------------------------------------ inputs

        void SetLocal(int f, FrameInput input, long sentAt)
        {
            localInputs[f & RingMask] = input;
            localFrameAt[f & RingMask] = f;
            localSentAt[f & RingMask] = sentAt;
        }

        FrameInput LocalAt(int f) => localFrameAt[f & RingMask] == f ? localInputs[f & RingMask] : FrameInput.Neutral;

        bool RemoteKnown(int f) => remoteFrameAt[f & RingMask] == f;

        void SetRemote(int f, FrameInput input)
        {
            remoteInputs[f & RingMask] = input;
            remoteFrameAt[f & RingMask] = f;
        }

        /// <summary>Spec: predict by repeating the last input.</summary>
        FrameInput PredictRemote(int f)
        {
            for (int g = Math.Min(f - 1, remoteLatest); g >= 0 && g > f - RingSize; g--)
            {
                if (RemoteKnown(g)) return remoteInputs[g & RingMask];
            }
            return FrameInput.Neutral;
        }

        void SendInputs()
        {
            int latest = localNext - 1;
            SendWindow(latest);
            // Inputs the remote still lacks that have dropped out of the newest window (a dropout or a burst
            // of losses): once they have gone unacknowledged for longer than a round trip, resend the window
            // right after the remote's ack. Up to two extra windows per tick, so a gap closes quickly while the
            // steady state stays at one 20-byte packet per tick.
            int gapStart = remoteAck + 1;
            for (int extra = 0; extra < 2; extra++)
            {
                if (gapStart < 0 || gapStart > latest - InputPacket.Redundancy) break;
                if (localFrameAt[gapStart & RingMask] != gapStart) break;
                if (now - localLastSent[gapStart & RingMask] <= ResendAfterMicros()) break;
                SendWindow(gapStart + InputPacket.Redundancy - 1);
                gapResentThrough = Math.Max(gapResentThrough, gapStart + InputPacket.Redundancy - 1);
                gapStart += InputPacket.Redundancy;
            }
        }

        void SendWindow(int latest)
        {
            InputPacket p = NetProtocol.BuildInputPacket(latest, remoteContiguous, LocalAt);
            p.Write(packetBuffer);
            transport.Send(packetBuffer, TransportChannel.Input);
            Stats.PacketsSent++;
            for (int f = Math.Max(0, latest - InputPacket.Redundancy + 1); f <= latest; f++) localLastSent[f & RingMask] = now;
        }

        long ResendAfterMicros()
        {
            long rtt = Stats.RttMicros > 0 ? Stats.RttMicros : cfg.ResendBeforeRttMicros;
            return rtt + 2L * Stats.JitterMicros + 2L * cfg.TickMicros;
        }

        void OnReceive(ReadOnlySpan<byte> data, TransportChannel channel)
        {
            lastReceive = now;
            if (channel == TransportChannel.Input)
            {
                if (InputPacket.TryRead(data, out InputPacket p)) OnInputPacket(p);
                return;
            }
            OnControl(data);
        }

        void OnInputPacket(in InputPacket p)
        {
            Stats.PacketsReceived++;
            int latest = FrameWire.Expand(p.LatestFrame, Math.Max(remoteLatest, frame));
            int ack = FrameWire.Expand(p.AckFrame, localNext);
            // Only the sender's newest window says where it is now; resent gap windows and late packets do not.
            bool newest = latest >= remoteLatest;
            if (latest > remoteLatest)
            {
                packetsExpected += latest - remoteLatest;
                remoteLatest = latest;
            }
            for (int k = 0; k < InputPacket.Redundancy; k++)
            {
                int f = latest - (InputPacket.Redundancy - 1) + k;
                if (f < 0 || f <= remoteContiguous - RingSize / 2 || RemoteKnown(f)) continue;
                var input = new FrameInput(p.Input(k));
                SetRemote(f, input);
                if (f < frame && remoteUsed[f & RingMask] != input && f < mispredictedFrom) mispredictedFrom = f;
            }
            while (RemoteKnown(remoteContiguous + 1)) remoteContiguous++;

            if (ack > remoteAck && ack < localNext)
            {
                // RTT: first acknowledgement of a frame we sent. As in Karn's rule, no sample when the frames it
                // covers needed a gap resend or sat in the window through a stall or pause: the time would
                // include the outage, not the link.
                int slot = ack & RingMask;
                bool clean = gapResentThrough <= remoteAck && localLastSent[slot] - localSentAt[slot] <= (InputPacket.Redundancy + 1) * cfg.TickMicros;
                if (clean && localFrameAt[slot] == ack && localSentAt[slot] > 0) Sample(now - localSentAt[slot]);
                remoteAck = ack;
            }
            if (!newest) return;
            newestReceived++;
            if (packetsExpected > 0)
            {
                Stats.LossPermille = Math.Max(0, Math.Min(1000, 1000 - newestReceived * 1000 / Math.Max(newestReceived, packetsExpected)));
            }

            // Time sync: how far ahead of the remote are we? (their latest input frame is their frame + delay)
            int rttFrames = (int)(Stats.RttMicros / cfg.TickMicros);
            int remoteNow = latest - delay + (rttFrames + 1) / 2;
            advantageSum += frame - remoteNow;
            advantageSamples++;
        }

        void Sample(long rtt)
        {
            int r = (int)Math.Min(int.MaxValue, Math.Max(0, rtt));
            if (Stats.RttMicros == 0) Stats.RttMicros = r;
            int diff = Math.Abs(r - Stats.RttMicros);
            Stats.RttMicros += (r - Stats.RttMicros) / 8;
            Stats.JitterMicros += (diff - Stats.JitterMicros) / 8;
        }

        bool ShouldWaitForTimeSync()
        {
            // Every 30 ticks: if we ran on average more than TimeSyncLead frames ahead of the remote, wait one frame.
            if (++syncTicks < 30) return false;
            syncTicks = 0;
            if (advantageSamples == 0) return false;
            long average = advantageSum * 16 / advantageSamples;
            advantageSum = 0;
            advantageSamples = 0;
            return average > cfg.TimeSyncLead * 16;
        }

        // ------------------------------------------------------------------ desync detection and repair

        void CheckHashes()
        {
            int confirmed = ConfirmedFrame;
            while (nextHashFrame <= confirmed + 1)
            {
                int h = nextHashFrame;
                nextHashFrame += cfg.HashInterval;
                if (!TryGetSnapshot(h, out TState snap))
                {
                    if (frame == h) snap = state;
                    else continue;
                }
                ulong hash = sim.Hash(snap);
                localHashes[h] = hash;
                Stats.HashChecks++;
                var w = new ControlWriter(hashBuffer, ControlType.Hash);
                w.I32(h);
                w.U64(hash);
                transport.Send(w.Written, TransportChannel.Control);
                Compare(h);
                Prune(localHashes, h);
            }
        }

        void Compare(int h)
        {
            if (!localHashes.TryGetValue(h, out ulong mine) || !remoteHashes.TryGetValue(h, out ulong theirs)) return;
            remoteHashes.Remove(h);
            if (mine == theirs) return;
            Stats.Desyncs++;
            DesyncDetected?.Invoke(h);
            if (DumpState != null && TryGetSnapshot(h, out TState snap))
            {
                int n = sim.Serialize(snap, scratch);
                DumpState(h, scratch.AsSpan(0, n).ToArray());
            }
            if (isHost) SendFullState();
        }

        static void Prune(Dictionary<int, ulong> d, int newest)
        {
            if (d.Count < 32) return;
            var old = new List<int>();
            foreach (int k in d.Keys) if (k < newest - 600) old.Add(k);
            foreach (int k in old) d.Remove(k);
        }

        /// <summary>Host: send the latest confirmed state in chunks so the guest can adopt it.</summary>
        void SendFullState()
        {
            int g = ConfirmedFrame + 1;
            if (!TryGetSnapshot(g, out TState snap))
            {
                if (g != frame) return;
                snap = state;
            }
            int n = sim.Serialize(snap, scratch);
            int chunk = NetProtocol.StateChunkBytes;
            int count = (n + chunk - 1) / chunk;
            Span<byte> msg = chunkBuffer;
            for (int i = 0; i < count; i++)
            {
                int len = Math.Min(chunk, n - i * chunk);
                var w = new ControlWriter(msg, ControlType.StateChunk);
                w.I32(g);
                w.U16((ushort)i);
                w.U16((ushort)count);
                w.U16((ushort)n);
                w.Bytes(scratch.AsSpan(i * chunk, len));
                transport.Send(w.Written, TransportChannel.Control);
            }
        }

        void OnControl(ReadOnlySpan<byte> data)
        {
            var r = new ControlReader(data);
            switch (r.Type)
            {
                case ControlType.Hash:
                    {
                        int h = r.I32();
                        ulong hash = r.U64();
                        if (!r.Ok) return;
                        remoteHashes[h] = hash;
                        Compare(h);
                        Prune(remoteHashes, h);
                        break;
                    }
                case ControlType.StateChunk:
                    {
                        if (isHost) return;
                        int g = r.I32();
                        int index = r.U16();
                        int count = r.U16();
                        int total = r.U16();
                        byte[] bytes = r.Bytes();
                        if (!r.Ok || count <= 0 || index >= count) return;
                        if (incomingFrame != g)
                        {
                            incomingFrame = g;
                            incomingState = new byte[total];
                            incomingHave = new bool[count];
                            incomingChunks = 0;
                        }
                        if (incomingHave[index]) return;
                        incomingHave[index] = true;
                        incomingChunks++;
                        Array.Copy(bytes, 0, incomingState, index * NetProtocol.StateChunkBytes, bytes.Length);
                        break;
                    }
            }
        }

        /// <summary>Guest: once every chunk of the host's state arrived, adopt it and resimulate forward.</summary>
        void ApplyIncomingState()
        {
            if (incomingFrame < 0 || incomingHave == null || incomingChunks < incomingHave.Length) return;
            int g = incomingFrame;
            TState adopted = sim.Deserialize(incomingState);
            incomingFrame = -1;
            incomingHave = null;
            if (g > frame) return;
            if (g == frame)
            {
                state = adopted;
            }
            else
            {
                int slot = SnapshotSlot(g);
                snapshots[slot] = adopted;
                snapshotFrame[slot] = g;
                if (g < mispredictedFrom) mispredictedFrom = g;
            }
            localHashes.Clear();
            remoteHashes.Clear();
            Stats.Resyncs++;
            Resynced?.Invoke(g);
        }

        /// <summary>Hash this peer computed for a confirmed checkpoint frame (multiples of HashInterval).</summary>
        public bool TryGetConfirmedHash(int checkpoint, out ulong hash) => localHashes.TryGetValue(checkpoint, out hash);

        /// <summary>Stop listening to the transport (after the match or on no contest).</summary>
        public void Detach() => transport.Received -= OnReceive;
    }
}
