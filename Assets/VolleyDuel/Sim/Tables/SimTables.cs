using System;
using System.Collections.Generic;
using System.IO;
using Duel.Core.Sim;
using Duel.Core.Sim.Data;

namespace VolleyDuel.Sim
{
    /// <summary>Supplies data file text by logical name ("tuning", "fighters/rafa", ...).</summary>
    public interface IDataSource
    {
        string Read(string name);
    }

    /// <summary>Reads data from a folder on disk (headless tools, tests and the Unity editor).</summary>
    public sealed class DirectoryDataSource : IDataSource
    {
        readonly string root;

        public DirectoryDataSource(string root)
        {
            this.root = root;
        }

        public string Read(string name) => File.ReadAllText(Path.Combine(root, name + ".json"));

        /// <summary>Finds Assets/VolleyDuel/Resources/Data above <paramref name="startDirectory"/>.</summary>
        public static DirectoryDataSource FindFrom(string startDirectory)
        {
            string dir = startDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "Assets", "VolleyDuel", "Resources", "Data");
                if (File.Exists(Path.Combine(candidate, "manifest.json"))) return new DirectoryDataSource(candidate);
                dir = Path.GetDirectoryName(dir);
            }
            throw new DirectoryNotFoundException("Assets/VolleyDuel/Resources/Data not found above " + startDirectory);
        }
    }

    /// <summary>
    /// All immutable sim tables, baked once from data. <see cref="DataHash"/> fingerprints the
    /// exact data text so two builds with different tuning refuse to play each other.
    /// </summary>
    public sealed class SimTables
    {
        public readonly Tuning Tuning;
        public readonly FighterDefinition[] Fighters;
        public readonly CourtDefinition[] Courts;
        public readonly ulong DataHash;
        /// <summary>Raw CPU data (tiers and personalities), baked by VolleyDuel.AI; not part of the sim hash.</summary>
        public readonly JsonNode CpuData;

        SimTables(Tuning tuning, FighterDefinition[] fighters, CourtDefinition[] courts, ulong dataHash, JsonNode cpu)
        {
            Tuning = tuning;
            Fighters = fighters;
            Courts = courts;
            DataHash = dataHash;
            CpuData = cpu;
        }

        public static SimTables Load(IDataSource source)
        {
            ulong hash = StateHash.OffsetBasis;
            JsonNode manifest = Parse(source, "manifest", ref hash);
            string tuningName = manifest.Str("tuning");
            var tuning = new Tuning(Parse(source, tuningName, ref hash));

            var fighters = new List<FighterDefinition>();
            foreach (JsonNode id in manifest["fighters"].Items)
            {
                JsonNode doc = Parse(source, "fighters/" + id.AsString(), ref hash);
                var def = new FighterDefinition(fighters.Count, doc, tuning);
                if (def.Id != id.AsString()) throw doc.Fail("id '" + def.Id + "' does not match its file name");
                fighters.Add(def);
            }

            var courts = new List<CourtDefinition>();
            foreach (JsonNode id in manifest["courts"].Items)
            {
                JsonNode doc = Parse(source, "courts/" + id.AsString(), ref hash);
                courts.Add(new CourtDefinition(courts.Count, doc));
            }

            JsonNode cpu = null;
            string cpuName = manifest.Str("cpu", null);
            if (cpuName != null)
            {
                ulong ignored = 0;
                cpu = Parse(source, cpuName, ref ignored);
            }
            return new SimTables(tuning, fighters.ToArray(), courts.ToArray(), hash, cpu);
        }

        static JsonNode Parse(IDataSource source, string name, ref ulong hash)
        {
            string text = source.Read(name);
            if (text == null) throw new FileNotFoundException("Data file not found: " + name);
            text = text.Replace("\r\n", "\n");
            hash = StateHash.AddString(hash, name);
            hash = StateHash.AddString(hash, text);
            return JsonNode.Parse(text, name);
        }

        public int FighterIndex(string id)
        {
            for (int i = 0; i < Fighters.Length; i++)
            {
                if (Fighters[i].Id == id) return i;
            }
            throw new ArgumentException("Unknown fighter '" + id + "'");
        }

        public int CourtIndex(string id)
        {
            for (int i = 0; i < Courts.Length; i++)
            {
                if (Courts[i].Id == id) return i;
            }
            throw new ArgumentException("Unknown court '" + id + "'");
        }
    }
}
