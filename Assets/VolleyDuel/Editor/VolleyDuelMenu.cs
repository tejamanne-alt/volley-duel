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
    /// "Volley Duel/Setup Project" configures player settings, URP and TextMeshPro.
    /// "Volley Duel/Build Scenes" builds the one app scene.
    /// "Volley Duel/Validate Data" bakes the JSON tables.
    /// "Volley Duel/Print Sim Version" prints the handshake fingerprint.
    /// "Volley Duel/Build/*" makes review builds.
    /// </summary>
    public static class VolleyDuelMenu
    {
        public const string ScenePath = "Assets/VolleyDuel/Scenes/Main.unity";
        const string PipelinePath = "Assets/VolleyDuel/Settings/VolleyDuelURP.asset";

        [MenuItem("Volley Duel/Setup Project")]
        public static void SetupProject()
        {
            PlayerSettings.companyName = "Volley Duel";
            PlayerSettings.productName = "Volley Duel";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.volleyduel.game");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.volleyduel.game");

            // Landscape only (contract).
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // IL2CPP and ARM64 on both phones; Android 8.0+ (BLE peripheral support is broad from API 26).
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.runInBackground = false;

            EnsurePipeline();
            ImportTextMeshPro();
            AssetDatabase.SaveAssets();
            Debug.Log("Volley Duel: project settings applied (landscape, IL2CPP ARM64, URP, TextMeshPro).");
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

        static void ImportTextMeshPro()
        {
            if (AssetDatabase.IsValidFolder("Assets/TextMesh Pro")) return;
            TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
        }

        [MenuItem("Volley Duel/Build Scenes")]
        public static void BuildScenes()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera cam = cameraObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.11f, 0.14f);
            cameraObject.AddComponent<DuelingCamera>();
            cameraObject.transform.position = new Vector3(0f, 2f, -20f);

            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            new GameObject("Volley Duel").AddComponent<GameApp>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Volley Duel: built " + ScenePath);
        }

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
