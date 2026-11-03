using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using VolleyDuel.Presentation;
using Debug = UnityEngine.Debug;

namespace VolleyDuel.EditorTools
{
    /// <summary>
    /// The app's identity and platform defaults, applied automatically on every editor load and again
    /// before every build, so nobody fixes Player Settings or Xcode by hand:
    /// - Bundle and package id <see cref="ApplicationId"/> on iOS and Android.
    /// - iOS automatic signing with the <see cref="AppleTeamName"/> developer team. The Team ID comes from
    ///   <see cref="AppleTeamId"/> when set. Otherwise it comes from the team field (OU) of the "Apple
    ///   Development: Teja Manne" certificate in this Mac's keychain, the one Xcode created for the
    ///   account, and failing that from Unity's Preferences.
    /// - Landscape only, IL2CPP ARM64, the Input System backend on, and the graybox base materials.
    /// Change identity here, not in Player Settings: these values win on the next load or build.
    /// </summary>
    [InitializeOnLoad]
    public static class BuildIdentity
    {
        public const string CompanyName = "Astradite";
        public const string ProductName = "Volley Duel";
        public const string ApplicationId = "com.astradite.volleyduel";

        /// <summary>The Apple developer account (team) name, as Xcode's Team menu shows it.</summary>
        public const string AppleTeamName = "Teja Manne";

        /// <summary>
        /// The signing certificate Xcode chose for that team. It is preferred when the keychain holds
        /// certificates for more than one team under the same name (a personal team and a paid one).
        /// </summary>
        public const string AppleCertificate = "Apple Development: Teja Manne (BX42AX95QW)";

        /// <summary>
        /// Optional: pin the 10-character Team ID (developer.apple.com, Account, Membership details). Leave
        /// empty to read it from the keychain certificate, which works on any Mac signed in to the account.
        /// </summary>
        public const string AppleTeamId = "";

        const string MaterialFolder = "Assets/VolleyDuel/Resources/Graybox";
        static string resolvedTeam;
        static bool teamLookedUp;

        static BuildIdentity()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                Apply();
            };
        }

        /// <summary>Applies every default. Only writes settings that differ, so it is cheap to repeat.</summary>
        public static void Apply()
        {
            if (PlayerSettings.companyName != CompanyName) PlayerSettings.companyName = CompanyName;
            if (PlayerSettings.productName != ProductName) PlayerSettings.productName = ProductName;
            SetId(NamedBuildTarget.iOS);
            SetId(NamedBuildTarget.Android);

            // Landscape only (contract).
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation) PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            if (PlayerSettings.allowedAutorotateToPortrait) PlayerSettings.allowedAutorotateToPortrait = false;
            if (PlayerSettings.allowedAutorotateToPortraitUpsideDown) PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            if (!PlayerSettings.allowedAutorotateToLandscapeLeft) PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            if (!PlayerSettings.allowedAutorotateToLandscapeRight) PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // iOS: automatic signing with the team; iOS 15+.
            if (!PlayerSettings.iOS.appleEnableAutomaticSigning) PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            string team = ResolveAppleTeamId(out string source);
            if (!string.IsNullOrEmpty(team) && PlayerSettings.iOS.appleDeveloperTeamID != team)
            {
                PlayerSettings.iOS.appleDeveloperTeamID = team;
                Debug.Log("Volley Duel: iOS signing team set to " + AppleTeamName + " (" + team + ", from " + source + ").");
            }
            if (PlayerSettings.iOS.targetOSVersionString != "15.0") PlayerSettings.iOS.targetOSVersionString = "15.0";

            // Android: IL2CPP ARM64, Android 8.0+.
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP) PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64) PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel26) PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            EnableInputSystem();
            EnsureGrayboxMaterials();
        }

        static void SetId(NamedBuildTarget target)
        {
            if (PlayerSettings.GetApplicationIdentifier(target) != ApplicationId) PlayerSettings.SetApplicationIdentifier(target, ApplicationId);
        }

        // ------------------------------------------------------------------ Apple team

        /// <summary>The Team ID to sign with, and where it came from; null when it cannot be found.</summary>
        public static string ResolveAppleTeamId(out string source)
        {
            if (!string.IsNullOrEmpty(AppleTeamId))
            {
                source = "BuildIdentity.AppleTeamId";
                return AppleTeamId;
            }
            if (!teamLookedUp)
            {
                teamLookedUp = true;
                resolvedTeam = TeamIdFromKeychain(AppleTeamName);
            }
            if (!string.IsNullOrEmpty(resolvedTeam))
            {
                source = "the Apple Development certificate for " + AppleTeamName;
                return resolvedTeam;
            }
            string preference = EditorPrefs.GetString("DefaultiOSAutomaticSignTeamId", string.Empty);
            if (!string.IsNullOrEmpty(preference))
            {
                source = "Unity Preferences";
                return preference;
            }
            source = null;
            return null;
        }

        /// <summary>
        /// Reads the team from the keychain: an Apple Development certificate's subject carries the Team ID
        /// in its OU field (the ID in brackets after the name is the person's, not the team's).
        /// </summary>
        static string TeamIdFromKeychain(string teamName)
        {
            if (Application.platform != RuntimePlatform.OSXEditor) return null;
            string team = FirstTeamId(AppleCertificate, out _);
            if (team != null) return team;
            team = FirstTeamId("Apple Development: " + teamName, out string others);
            if (team != null && others.Length > 0)
            {
                Debug.LogWarning("Volley Duel: the keychain has Apple Development certificates for teams " + team + others + " under " + teamName + ". Using " + team + "; set BuildIdentity.AppleTeamId to pick another.");
            }
            return team;
        }

        /// <summary>Team ID (subject OU) of the first keychain certificate whose name contains <paramref name="name"/>.</summary>
        static string FirstTeamId(string name, out string others)
        {
            others = string.Empty;
            string pem = Run("/usr/bin/security", "find-certificate -a -p -c \"" + name + "\"", null);
            if (string.IsNullOrEmpty(pem)) return null;
            const string End = "-----END CERTIFICATE-----";
            string first = null;
            int start = 0;
            while (true)
            {
                int stop = pem.IndexOf(End, start, StringComparison.Ordinal);
                if (stop < 0) return first;
                string certificate = pem.Substring(start, stop + End.Length - start);
                start = stop + End.Length;
                string subject = Run("/usr/bin/openssl", "x509 -noout -subject", certificate);
                Match m = TeamField.Match(subject ?? string.Empty);
                if (!m.Success) continue;
                string id = m.Groups[1].Value;
                if (first == null) first = id;
                else if (id != first && !others.Contains(id)) others += ", " + id;
            }
        }

        /// <summary>The OU field of a certificate subject, in either openssl output style.</summary>
        internal static readonly Regex TeamField = new Regex(@"\bOU\s*=\s*([A-Z0-9]{10})\b");

        static string Run(string file, string arguments, string input)
        {
            try
            {
                var info = new ProcessStartInfo(file, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = input != null,
                    CreateNoWindow = true,
                };
                using (Process p = Process.Start(info))
                {
                    if (p == null) return null;
                    if (input != null)
                    {
                        p.StandardInput.Write(input);
                        p.StandardInput.Close();
                    }
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    return output;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ input and materials

        /// <summary>
        /// Turns the Input System backend on ("Both" keeps the legacy API working). The game runs under
        /// either setting; this enables gamepads. Takes effect after an editor restart.
        /// </summary>
        static void EnableInputSystem()
        {
            UnityEngine.Object[] settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settings.Length == 0) return;
            var so = new SerializedObject(settings[0]);
            SerializedProperty handler = so.FindProperty("activeInputHandler");
            if (handler == null || handler.intValue != 0) return;
            handler.intValue = 2;
            so.ApplyModifiedProperties();
            Debug.LogWarning("Volley Duel: Active Input Handling set to Both (adds gamepad support). Restart the editor for it to take effect.");
        }

        /// <summary>
        /// Graybox base materials in Resources, using the active pipeline's shaders. A material in Resources is
        /// what includes its shader in a build; see <see cref="Graybox"/>.
        /// </summary>
        public static void EnsureGrayboxMaterials()
        {
            bool srp = GraphicsSettings.currentRenderPipeline != null;
            EnsureMaterial(MaterialFolder + "/GrayboxLit.mat", srp ? "Universal Render Pipeline/Lit" : "Standard");
            EnsureMaterial(MaterialFolder + "/GrayboxUnlit.mat", srp ? "Universal Render Pipeline/Unlit" : "Unlit/Color");
        }

        static void EnsureMaterial(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) return;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Directory.CreateDirectory(MaterialFolder);
                AssetDatabase.CreateAsset(new Material(shader) { color = Color.white }, path);
                AssetDatabase.SaveAssets();
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }
        }
    }

    /// <summary>Re-applies the defaults right before every build and reports the signing team for iOS.</summary>
    public sealed class BuildIdentityPreprocess : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            BuildIdentity.Apply();
            if (report.summary.platform != BuildTarget.iOS) return;
            if (string.IsNullOrEmpty(PlayerSettings.iOS.appleDeveloperTeamID))
            {
                Debug.LogWarning("Volley Duel: no Apple Team ID found for " + BuildIdentity.AppleTeamName + ". Sign in to the account in Xcode (Settings, Accounts) once, or set BuildIdentity.AppleTeamId, then build again.");
            }
            else
            {
                Debug.Log("Volley Duel: signing " + BuildIdentity.ApplicationId + " with team " + PlayerSettings.iOS.appleDeveloperTeamID + " (automatic).");
            }
        }
    }
}
