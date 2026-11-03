using System.IO;
using System.Xml;
using UnityEditor.Android;
#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

namespace Duel.Core.Editor
{
    /// <summary>
    /// Platform settings the radio bridges need, applied to every build so nobody edits generated projects
    /// by hand. Android: BLE permissions per API level, plus Wi-Fi for the LAN transport. iOS: the
    /// Bluetooth and local-network usage descriptions, the Swift version, and CoreBluetooth.
    /// Wi-Fi Aware entitlements are deliberately absent (they need Apple's approval; ask first).
    /// </summary>
    public sealed class DuelCoreAndroidManifest : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string ToolsNs = "http://schemas.android.com/tools";

        public int callbackOrder => 10;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath)) return;
            var doc = new XmlDocument();
            doc.Load(manifestPath);
            XmlElement manifest = doc.DocumentElement;
            if (manifest == null) return;
            if (!manifest.HasAttribute("xmlns:tools")) manifest.SetAttribute("xmlns:tools", ToolsNs);

            // Android 11 and older: legacy Bluetooth permissions and location for scanning.
            AddPermission(doc, manifest, "android.permission.BLUETOOTH", 30, null);
            AddPermission(doc, manifest, "android.permission.BLUETOOTH_ADMIN", 30, null);
            AddPermission(doc, manifest, "android.permission.ACCESS_FINE_LOCATION", 30, null);
            // Android 12+: scan (never used to derive location), advertise and connect.
            AddPermission(doc, manifest, "android.permission.BLUETOOTH_SCAN", 0, "neverForLocation");
            AddPermission(doc, manifest, "android.permission.BLUETOOTH_ADVERTISE", 0, null);
            AddPermission(doc, manifest, "android.permission.BLUETOOTH_CONNECT", 0, null);
            // LAN transport.
            AddPermission(doc, manifest, "android.permission.INTERNET", 0, null);
            AddPermission(doc, manifest, "android.permission.ACCESS_WIFI_STATE", 0, null);
            AddPermission(doc, manifest, "android.permission.CHANGE_WIFI_MULTICAST_STATE", 0, null);

            // BLE is optional: phones without it still play the CPU and LAN modes.
            XmlElement feature = doc.CreateElement("uses-feature");
            feature.SetAttribute("name", AndroidNs, "android.hardware.bluetooth_le");
            feature.SetAttribute("required", AndroidNs, "false");
            if (!HasNode(manifest, "uses-feature", "android.hardware.bluetooth_le")) manifest.AppendChild(feature);

            doc.Save(manifestPath);
        }

        static void AddPermission(XmlDocument doc, XmlElement manifest, string name, int maxSdk, string flags)
        {
            if (HasNode(manifest, "uses-permission", name)) return;
            XmlElement e = doc.CreateElement("uses-permission");
            e.SetAttribute("name", AndroidNs, name);
            if (maxSdk > 0) e.SetAttribute("maxSdkVersion", AndroidNs, maxSdk.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (flags != null)
            {
                e.SetAttribute("usesPermissionFlags", AndroidNs, flags);
                e.SetAttribute("targetApi", ToolsNs, "s");
            }
            manifest.AppendChild(e);
        }

        static bool HasNode(XmlElement manifest, string tag, string name)
        {
            foreach (XmlNode n in manifest.ChildNodes)
            {
                if (n is XmlElement e && e.Name == tag && e.GetAttribute("name", AndroidNs) == name) return true;
            }
            return false;
        }
    }

#if UNITY_IOS
    public static class DuelCoreIosPostprocess
    {
        [PostProcessBuild(10)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementDict root = plist.root;
            root.SetString("NSBluetoothAlwaysUsageDescription", "Bluetooth lets you host or join a court on a nearby phone.");
            root.SetString("NSLocalNetworkUsageDescription", "The local network lets you play a friend on the same Wi-Fi.");
            plist.WriteToFile(plistPath);

            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string framework = project.GetUnityFrameworkTargetGuid();
            string main = project.GetUnityMainTargetGuid();
            project.SetBuildProperty(framework, "SWIFT_VERSION", "5.0");
            project.SetBuildProperty(framework, "CLANG_ENABLE_MODULES", "YES");
            project.SetBuildProperty(main, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");
            project.SetBuildProperty(framework, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO");
            project.AddFrameworkToProject(framework, "CoreBluetooth.framework", false);

            // Automatic signing with the project's team on every target, so Xcode never stops at "requires
            // a provisioning profile". Unity only sets the team on the app target.
            string team = PlayerSettings.iOS.appleDeveloperTeamID;
            if (!string.IsNullOrEmpty(team))
            {
                foreach (string guid in new[] { main, framework, project.TargetGuidByName(PBXProject.GetUnityTestTargetName()) })
                {
                    if (string.IsNullOrEmpty(guid)) continue;
                    project.SetBuildProperty(guid, "DEVELOPMENT_TEAM", team);
                    project.SetBuildProperty(guid, "CODE_SIGN_STYLE", "Automatic");
                }
            }
            project.WriteToFile(projectPath);
        }
    }
#endif
}
