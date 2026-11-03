using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using VolleyDuel.Presentation;
using VolleyDuel.Sim;
using VolleyDuel.UI;

namespace VolleyDuel.EditorTools
{
    /// <summary>
    /// Editor entry points. Scenes and settings are always made through these, never by hand-editing YAML.
    /// Identity, signing and platform defaults apply automatically (<see cref="BuildIdentity"/>).
    /// "Volley Duel/Setup Project" re-applies them and also sets URP as the render pipeline.
    /// "Volley Duel/Build Scenes" rebuilds the one app scene (it is also created automatically).
    /// "Volley Duel/Validate Data" bakes the JSON tables.
    /// "Volley Duel/Print Sim Version" prints the handshake fingerprint.
    /// "Volley Duel/Build/*" makes review builds.
    /// </summary>
    public static class VolleyDuelMenu
    {
        public const string ScenePath = SceneSetup.ScenePath;
        const string PipelinePath = "Assets/VolleyDuel/Settings/VolleyDuelURP.asset";

        [MenuItem("Volley Duel/Setup Project")]
        public static void SetupProject()
        {
            EnsurePipeline();
            BuildIdentity.Apply();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.runInBackground = false;
            AssetDatabase.SaveAssets();
            string team = PlayerSettings.iOS.appleDeveloperTeamID;
            Debug.Log("Volley Duel: project set up. " + BuildIdentity.ApplicationId + ", landscape, IL2CPP ARM64, URP, iOS team " + (string.IsNullOrEmpty(team) ? "not found (see BuildIdentity.AppleTeamId)" : team) + ".");
        }

        static void EnsurePipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PipelinePath));
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, PipelinePath.Replace(".asset", "_Renderer.asset"));
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
        }

        [MenuItem("Volley Duel/Build Scenes")]
        public static void BuildScenes() => SceneSetup.Rebuild();

        [MenuItem("Volley Duel/Validate Data")]
        public static void ValidateData()
        {
            try
            {
                SimTables tables = SimTables.Load(new DirectoryDataSource(Path.Combine(Application.dataPath, "VolleyDuel", "Resources", "Data")));
                Debug.Log("Volley Duel: data OK. " + tables.Fighters.Length + " fighters, " + tables.Courts.Length + " courts, data hash " + tables.DataHash.ToString("X16"));
            }
            catch (Exception e)
            {
                Debug.LogError("Volley Duel: data invalid: " + e.Message);
            }
        }

        [MenuItem("Volley Duel/Print Sim Version")]
        public static void PrintSimVersion()
        {
            SimTables tables = SimTables.Load(new DirectoryDataSource(Path.Combine(Application.dataPath, "VolleyDuel", "Resources", "Data")));
            Debug.Log("Volley Duel: sim version " + SimVersion.Compute(tables).ToString("X16") + " (code " + SimCodeHash.Value.ToString("X16") + ", data " + tables.DataHash.ToString("X16") + ")");
        }

        [MenuItem("Volley Duel/Build/Android APK (review)")]
        public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/Android/VolleyDuel.apk");

        [MenuItem("Volley Duel/Build/iOS Xcode project (review)")]
        public static void BuildIos() => Build(BuildTarget.iOS, "Builds/iOS");

        /// <summary>Command line: -executeMethod VolleyDuel.EditorTools.VolleyDuelMenu.BuildFromCommandLine -buildTarget Android</summary>
        public static void BuildFromCommandLine()
        {
            SetupProject();
            BuildScenes();
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS) BuildIos();
            else BuildAndroid();
        }

        static void Build(BuildTarget target, string path)
        {
            if (!File.Exists(ScenePath)) BuildScenes();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = BuildOptions.Development,
            };
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("Volley Duel: " + target + " build " + report.summary.result + " -> " + path);
        }
    }
}
