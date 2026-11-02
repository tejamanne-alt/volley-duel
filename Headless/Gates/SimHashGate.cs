using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using VolleyDuel.Sim;

namespace VolleyDuel.Gates
{
    /// <summary>
    /// The sim version hash in SimCodeHash.g.cs must match the sim sources. Both phones compare it
    /// in the handshake, so a stale value would let mismatched builds play each other.
    /// Regenerate with `python3 scripts/sim_hash.py --write` (scripts/check.sh does it).
    /// </summary>
    public class SimHashGate
    {
        static readonly string[] Dirs = { "Packages/com.duel.core/Runtime/Sim", "Assets/VolleyDuel/Sim" };

        [Test]
        public void SimCodeHashMatchesSources()
        {
            string root = OriginalIpGate.RepoRoot();
            var files = Dirs.SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
                .Where(p => Path.GetFileName(p) != "SimCodeHash.g.cs")
                .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();
            using (var sha = SHA256.Create())
            {
                var all = new StringBuilder();
                foreach (string rel in files)
                {
                    string text = File.ReadAllText(Path.Combine(root, rel)).Replace("\r\n", "\n");
                    all.Append(rel).Append('\n').Append(text).Append('\n');
                }
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(all.ToString()));
                ulong value = System.BitConverter.ToUInt64(digest, 0);
                Assert.AreEqual(value, SimCodeHash.Value, "SimCodeHash.g.cs is stale: run python3 scripts/sim_hash.py --write");
            }
        }
    }
}
