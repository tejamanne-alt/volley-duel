using System;

namespace Duel.Core.Sim
{
    /// <summary>
    /// A deterministic two-player game: a pure function from (state, inputs) to the next state.
    /// Implementations must be integer-only (see the static gate), hold no hidden mutable state,
    /// and produce byte-identical results everywhere. The netcode, replays, CPU harness and
    /// Balance Lab are written against this interface and know nothing about the game.
    /// </summary>
    public interface ISimulation<TState> where TState : struct
    {
        /// <summary>Upper bound of <see cref="Serialize"/> output, in bytes.</summary>
        int MaxStateBytes { get; }

        /// <summary>Advances one 60 Hz tick. Appends that tick's events to <paramref name="events"/>.</summary>
        TState Step(in TState state, FrameInput p1, FrameInput p2, SimEventBuffer events);

        /// <summary>Tick counter of the state (number of Steps applied since the start).</summary>
        int FrameOf(in TState state);

        /// <summary>True once the match is over and further steps change nothing.</summary>
        bool IsFinished(in TState state);

        int Serialize(in TState state, Span<byte> destination);

        TState Deserialize(ReadOnlySpan<byte> source);

        /// <summary>Hash of the serialized state.</summary>
        ulong Hash(in TState state);
    }

    /// <summary>Hashing helper for <see cref="ISimulation{TState}"/> implementations.</summary>
    public static class SimulationHash
    {
        [ThreadStatic] static byte[] scratch;

        public static ulong Of<TState>(ISimulation<TState> sim, in TState state) where TState : struct
        {
            if (scratch == null || scratch.Length < sim.MaxStateBytes) scratch = new byte[sim.MaxStateBytes];
            int n = sim.Serialize(state, scratch);
            return StateHash.Fnv1a(new ReadOnlySpan<byte>(scratch, 0, n));
        }
    }
}
