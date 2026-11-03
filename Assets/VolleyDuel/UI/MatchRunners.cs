using System;
using Duel.Core.AI;
using Duel.Core.Net;
using Duel.Core.Sim;
using VolleyDuel.AI;
using VolleyDuel.Sim;

namespace VolleyDuel.UI
{
    /// <summary>What the match screen needs from any way of running a match (local, netplay, replay).</summary>
    public interface IMatchRunner : IDisposable
    {
        VolleySim Sim { get; }

        /// <summary>The player this phone shows and controls.</summary>
        int LocalPlayer { get; }

        GameState Truth { get; }

        /// <summary>Events from the ticks run by the last <see cref="Advance"/>, including resimulated ones.</summary>
        SimEventBuffer FrameEvents { get; }

        int ResimulatedFrom { get; }

        bool Finished { get; }

        /// <summary>Connection status for the HUD (empty when fine).</summary>
        string Status { get; }

        /// <summary>Run as many fixed 60 Hz ticks as the elapsed real time asks for.</summary>
        void Advance(long elapsedMicros, Func<FrameInput> sampleLocal);
    }

    /// <summary>One phone: a human against the CPU (or CPU against CPU for attract mode), recorded as a replay.</summary>
    public sealed class LocalMatchRunner : IMatchRunner
    {
        readonly SimLoop<GameState> loop;
        readonly VolleyCpu[] cpu = new VolleyCpu[2];
        readonly TickClock clock = new TickClock();
        readonly SimEventBuffer frameEvents = new SimEventBuffer();
        readonly RivalMode rival;
        readonly int rivalCpu = -1;
        int setsSeen;

        public LocalMatchRunner(VolleySim sim, CpuTables cpuTables, bool human0, bool human1, int tier0, int tier1, bool rivalMode, ulong cpuSeed)
        {
            Sim = sim;
            LocalPlayer = human1 && !human0 ? 1 : 0;
            var replay = new Replay { SimVersion = SimVersion.Compute(sim.Tables), Config = sim.Config.ToBytes() };
            loop = new SimLoop<GameState>(sim, sim.CreateInitialState(), replay);
            if (!human0) cpu[0] = new VolleyCpu(sim, cpuTables, 0, cpuTables.Tiers[tier0], cpuSeed * 2 + 1);
            if (!human1) cpu[1] = new VolleyCpu(sim, cpuTables, 1, cpuTables.Tiers[tier1], cpuSeed * 2 + 2);
            if (rivalMode && (human0 != human1))
            {
                rivalCpu = human0 ? 1 : 0;
                rival = new RivalMode(cpuTables, rivalCpu == 1 ? tier1 : tier0);
                rival.Apply(cpu[rivalCpu]);
            }
        }

        public VolleySim Sim { get; }

        public int LocalPlayer { get; }

        public GameState Truth => loop.State;

        public SimEventBuffer FrameEvents => frameEvents;

        public int ResimulatedFrom => -1;

        public bool Finished => loop.IsFinished;

        public string Status => string.Empty;

        public bool Paused { get; set; }

        public Replay Replay => loop.Recorder;

        public void Advance(long elapsedMicros, Func<FrameInput> sampleLocal)
        {
            frameEvents.Clear();
            int ticks = clock.Advance(Paused ? 0 : elapsedMicros);
            for (int i = 0; i < ticks && !loop.IsFinished; i++)
            {
                FrameInput human = sampleLocal();
                FrameInput p0 = cpu[0] != null ? cpu[0].Decide(loop.State) : human;
                FrameInput p1 = cpu[1] != null ? cpu[1].Decide(loop.State) : human;
                loop.Tick(p0, p1);
                loop.Events.CopyTo(frameEvents);
                TrackSets();
            }
            if (loop.IsFinished) loop.FinishRecording();
        }

        void TrackSets()
        {
            if (rival == null) return;
            GameState s = loop.State;
            int sets = s.SetsWon0 + s.SetsWon1;
            if (sets == setsSeen) return;
            setsSeen = sets;
            // Rival mode: nudge the CPU between sets toward a 50% win rate for the human.
            int human = 1 - rivalCpu;
            rival.AfterSet(s.SetsWon(human) - s.SetsWon(rivalCpu));
            rival.Apply(cpu[rivalCpu]);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Two phones over any transport, through the rollback session.</summary>
    public sealed class NetMatchRunner : IMatchRunner
    {
        readonly RollbackSession<GameState> session;
        readonly ITransport transport;
        readonly TickClock clock = new TickClock();
        readonly SimEventBuffer frameEvents = new SimEventBuffer();
        long now;
        int resimulatedFrom = -1;

        public NetMatchRunner(VolleySim sim, ITransport transport, MatchAgreement agreement, long nowMicros)
        {
            Sim = sim;
            this.transport = transport;
            LocalPlayer = agreement.IsHost ? 0 : 1;
            now = nowMicros;
            session = new RollbackSession<GameState>(sim, sim.CreateInitialState(), LocalPlayer, agreement.IsHost, transport, new NetConfig(), agreement.InputDelay, nowMicros);
        }

        public VolleySim Sim { get; }

        public int LocalPlayer { get; }

        public GameState Truth => session.State;

        public SimEventBuffer FrameEvents => frameEvents;

        public int ResimulatedFrom => resimulatedFrom;

        public bool Finished => Sim.IsFinished(session.State) && session.ConfirmedFrame >= session.Frame - 1;

        public RollbackSession<GameState> Session => session;

        public string Status
        {
            get
            {
                switch (session.Status)
                {
                    case SessionStatus.Paused: return "Connection lost. Waiting for the other phone...";
                    case SessionStatus.NoContest: return "No contest: the other phone did not come back.";
                    default: return string.Empty;
                }
            }
        }

        public void Advance(long elapsedMicros, Func<FrameInput> sampleLocal)
        {
            frameEvents.Clear();
            resimulatedFrom = -1;
            int ticks = clock.Advance(elapsedMicros);
            for (int i = 0; i < ticks; i++)
            {
                now += TickClock.MicrosPerTickApprox;
                session.Tick(sampleLocal(), now);
                session.Events.CopyTo(frameEvents);
                if (session.ResimulatedFrom >= 0 && (resimulatedFrom < 0 || session.ResimulatedFrom < resimulatedFrom)) resimulatedFrom = session.ResimulatedFrom;
            }
            if (ticks == 0) transport.Poll();
        }

        public void Dispose() => session.Detach();
    }

    /// <summary>Plays a saved replay (inputs only) through the same sim.</summary>
    public sealed class ReplayRunner : IMatchRunner
    {
        readonly SimLoop<GameState> loop;
        readonly Replay replay;
        readonly TickClock clock = new TickClock();
        readonly SimEventBuffer frameEvents = new SimEventBuffer();
        int index;

        public ReplayRunner(VolleySim sim, Replay replay)
        {
            Sim = sim;
            this.replay = replay;
            loop = new SimLoop<GameState>(sim, sim.CreateInitialState());
        }

        public VolleySim Sim { get; }

        public int LocalPlayer => 0;

        public GameState Truth => loop.State;

        public SimEventBuffer FrameEvents => frameEvents;

        public int ResimulatedFrom => -1;

        public bool Finished => index >= replay.FrameCount || loop.IsFinished;

        public string Status => string.Empty;

        public void Advance(long elapsedMicros, Func<FrameInput> sampleLocal)
        {
            frameEvents.Clear();
            int ticks = clock.Advance(elapsedMicros);
            for (int i = 0; i < ticks && !Finished; i++, index++)
            {
                loop.Tick(replay.P1[index], replay.P2[index]);
                loop.Events.CopyTo(frameEvents);
            }
        }

        public void Dispose()
        {
        }
    }
}
