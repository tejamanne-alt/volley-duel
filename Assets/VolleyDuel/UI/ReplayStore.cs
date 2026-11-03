using System;
using System.Collections.Generic;
using System.IO;
using Duel.Core.Sim;
using UnityEngine;

namespace VolleyDuel.UI
{
    /// <summary>Every match is saved as inputs (a few kilobytes) under persistentDataPath/Replays.</summary>
    public static class ReplayStore
    {
        static string Folder => Path.Combine(Application.persistentDataPath, "Replays");

        public static string Save(Replay replay, string label)
        {
            Directory.CreateDirectory(Folder);
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + label + ".replay";
            string path = Path.Combine(Folder, name);
            File.WriteAllBytes(path, replay.ToBytes());
            return path;
        }

        public static List<string> List()
        {
            var files = new List<string>();
            if (!Directory.Exists(Folder)) return files;
            files.AddRange(Directory.GetFiles(Folder, "*.replay"));
            files.Sort(StringComparer.Ordinal);
            files.Reverse();
            return files;
        }

        public static Replay Load(string path) => Replay.FromBytes(File.ReadAllBytes(path));
    }

    /// <summary>Session log for netplay debugging: link and sync numbers only, no personal data.</summary>
    public static class SessionLog
    {
        public static void Write(string line)
        {
            try
            {
                File.AppendAllText(Path.Combine(Application.persistentDataPath, "session.log"), DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + " " + line + "\n");
            }
            catch (IOException)
            {
                // Logging must never break a match.
            }
        }
    }
}
