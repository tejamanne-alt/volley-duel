using System.IO;

namespace VolleyDuel.Sim
{
    /// <summary>
    /// Immutable per-match settings agreed before frame 0 (handshake, replay header). Everything
    /// here must be identical on both phones.
    /// </summary>
    public struct MatchConfig
    {
        public const byte Version = 1;

        public byte Fighter0;
        public byte Fighter1;
        public byte Court;
        /// <summary>2 for best of 3 sets, 3 for best of 5.</summary>
        public byte SetsToWin;
        public uint Seed;
        public bool Simple0;
        public bool Simple1;
        /// <summary>Casual Local Conditions toggle (sand drag, snow slide, beach wind).</summary>
        public bool LocalConditions;

        public static MatchConfig Create(int fighter0, int fighter1, int court, uint seed, int setsToWin = 2)
        {
            return new MatchConfig
            {
                Fighter0 = (byte)fighter0,
                Fighter1 = (byte)fighter1,
                Court = (byte)court,
                SetsToWin = (byte)setsToWin,
                Seed = seed,
            };
        }

        public int FighterOf(int player) => player == 0 ? Fighter0 : Fighter1;

        public bool SimpleOf(int player) => player == 0 ? Simple0 : Simple1;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Version);
                w.Write(Fighter0);
                w.Write(Fighter1);
                w.Write(Court);
                w.Write(SetsToWin);
                w.Write(Seed);
                w.Write(Simple0);
                w.Write(Simple1);
                w.Write(LocalConditions);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static MatchConfig FromBytes(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
            using (var r = new BinaryReader(ms))
            {
                byte version = r.ReadByte();
                if (version != Version) throw new InvalidDataException("MatchConfig version " + version);
                return new MatchConfig
                {
                    Fighter0 = r.ReadByte(),
                    Fighter1 = r.ReadByte(),
                    Court = r.ReadByte(),
                    SetsToWin = r.ReadByte(),
                    Seed = r.ReadUInt32(),
                    Simple0 = r.ReadBoolean(),
                    Simple1 = r.ReadBoolean(),
                    LocalConditions = r.ReadBoolean(),
                };
            }
        }
    }
}
