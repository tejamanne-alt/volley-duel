namespace Duel.Core.Sim
{
    /// <summary>
    /// Local frame loop: owns the current state, steps it with both players' inputs and
    /// optionally records a replay. Used for CPU matches, Training and the Balance Lab.
    /// Network play uses the rollback session in Duel.Core.Net instead.
    /// </summary>
    public sealed class SimLoop<TState> where TState : struct
    {
        readonly ISimulation<TState> sim;
        readonly SimEventBuffer events = new SimEventBuffer();
        TState state;
        TState previous;

        public SimLoop(ISimulation<TState> sim, TState initial, Replay recorder = null)
        {
            this.sim = sim;
            state = initial;
            previous = initial;
            Recorder = recorder;
        }

        public ISimulation<TState> Simulation => sim;

        /// <summary>State after the last tick.</summary>
        public ref readonly TState State => ref state;

        /// <summary>State before the last tick (for presentation interpolation).</summary>
        public ref readonly TState Previous => ref previous;

        /// <summary>Events produced by the last tick.</summary>
        public SimEventBuffer Events => events;

        public Replay Recorder { get; }

        public int Frame => sim.FrameOf(state);

        public bool IsFinished => sim.IsFinished(state);

        public void Tick(FrameInput p1, FrameInput p2)
        {
            events.Clear();
            previous = state;
            state = sim.Step(state, p1, p2, events);
            if (Recorder != null) Recorder.Add(p1, p2);
        }

        /// <summary>Replaces the current state (Training resets, desync recovery tests).</summary>
        public void Reset(TState newState)
        {
            state = newState;
            previous = newState;
            events.Clear();
        }

        /// <summary>Writes the final frame and hash into the recorder.</summary>
        public void FinishRecording()
        {
            if (Recorder == null) return;
            Recorder.FinalFrame = sim.FrameOf(state);
            Recorder.FinalHash = sim.Hash(state);
        }
    }

    /// <summary>
    /// Converts elapsed real time into fixed 60 Hz ticks. Time is passed in integer
    /// microseconds so this stays usable inside float-free assemblies.
    /// </summary>
    public sealed class TickClock
    {
        public const int TicksPerSecond = 60;
        const long MicrosPerTick = 1000000L / TicksPerSecond; // 16666 us; remainder carried below
        const long MicrosPerSecond = 1000000L;

        long accumulator;
        readonly int maxTicksPerUpdate;

        public TickClock(int maxTicksPerUpdate = 4)
        {
            this.maxTicksPerUpdate = maxTicksPerUpdate;
        }

        /// <summary>Adds elapsed time and returns how many ticks to run now.</summary>
        public int Advance(long elapsedMicros)
        {
            if (elapsedMicros < 0) elapsedMicros = 0;
            accumulator += elapsedMicros * TicksPerSecond; // in units of 1/60 microsecond
            long ticks = accumulator / MicrosPerSecond;
            if (ticks > maxTicksPerUpdate)
            {
                ticks = maxTicksPerUpdate;
                accumulator = 0;
            }
            else
            {
                accumulator -= ticks * MicrosPerSecond;
            }
            return (int)ticks;
        }

        /// <summary>Fraction of the next tick already elapsed, in 1/1000ths (for interpolation).</summary>
        public int AlphaPermille => (int)(accumulator * 1000 / MicrosPerSecond);

        public static long MicrosPerTickApprox => MicrosPerTick;
    }
}
