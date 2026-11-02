using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace VolleyDuel.Gates
{
    /// <summary>
    /// Contract rule 1: no names from the benchmark game or its developer anywhere in code,
    /// assets or text. Banned terms are stored only as FNV-1a hashes of their lowercase words
    /// and word pairs, so this file does not itself contain them.
    /// </summary>
    public class OriginalIpGate
    {
        static readonly HashSet<ulong> BannedHashes = new HashSet<ulong>
        {
            0x0b7abb167c700033UL, // two-word franchise name
            0x48c30789502b248fUL, // franchise name without a space
            0x823157195cd9680eUL, // abbreviated sequel name
            0x63f486404695a6d8UL, // developer name
            0x823197195cd9d4ceUL, // abbreviated sequel name
            0x823156195cd9665bUL, // abbreviated sequel name
        };

        static readonly string[] TextExtensions =
        {
            ".cs", ".md", ".json", ".txt", ".asmdef", ".swift", ".kt", ".py", ".sh", ".html", ".js", ".css",
            ".xml", ".plist", ".gradle", ".yml", ".yaml", ".props", ".csproj", ".sln", ".uss", ".uxml", ".shader", ".hlsl",
        };

        static readonly string[] SkipDirectories = { "/.git/", "/bin/", "/obj/", "/Library/", "/Temp/", "/Logs/", "/artifacts/", "/node_modules/" };

        public static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "CLAUDE.md"))) dir = Path.GetDirectoryName(dir);
            if (dir == null) throw new DirectoryNotFoundException("Repo root (CLAUDE.md) not found above " + TestContext.CurrentContext.TestDirectory);
            return dir;
        }

        [Test]
        public void NoBenchmarkNamesInRepo()
        {
            string root = RepoRoot();
            var hits = new List<string>();
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (SkipDirectories.Any(d => normalized.Contains(d))) continue;
                if (!TextExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                string[] words = Tokenize(File.ReadAllText(file));
                for (int i = 0; i < words.Length; i++)
                {
                    if (BannedHashes.Contains(Fnv(words[i])) || (i + 1 < words.Length && BannedHashes.Contains(Fnv(words[i] + " " + words[i + 1]))))
                    {
                        hits.Add(Path.GetRelativePath(root, file) + " (word " + i + ")");
                    }
                }
            }
            Assert.IsEmpty(hits, "Banned benchmark names found (contract rule 1):\n" + string.Join("\n", hits.Distinct()));
        }

        [Test]
        public void TokenizerSplitsOnNonAlphanumerics()
        {
            CollectionAssert.AreEqual(new[] { "rin", "inkline", "takeda", "6" }, Tokenize("Rin \"Inkline\" Takeda-6"));
        }

        static string[] Tokenize(string text)
        {
            var words = new List<string>();
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (sb.Length > 0)
                {
                    words.Add(sb.ToString());
                    sb.Clear();
                }
            }
            if (sb.Length > 0) words.Add(sb.ToString());
            return words.ToArray();
        }

        static ulong Fnv(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                h ^= b;
                h = unchecked(h * 1099511628211UL);
            }
            return h;
        }
    }
}
