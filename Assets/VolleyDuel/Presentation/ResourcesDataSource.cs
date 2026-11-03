using System.IO;
using UnityEngine;
using VolleyDuel.Sim;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// Reads Resources/Data/*.json on device. It is the same text the headless tools read from disk, so the
    /// data hash in the sim version matches across editor, phones and the Balance Lab.
    /// </summary>
    public sealed class ResourcesDataSource : IDataSource
    {
        public string Read(string name)
        {
            TextAsset asset = Resources.Load<TextAsset>("Data/" + name);
            if (asset == null) throw new FileNotFoundException("Resources/Data/" + name + ".json is missing");
            return asset.text;
        }

        static SimTables cached;

        /// <summary>Tables are immutable, so one bake per app run serves every match.</summary>
        public static SimTables LoadTables()
        {
            if (cached == null) cached = SimTables.Load(new ResourcesDataSource());
            return cached;
        }
    }
}
