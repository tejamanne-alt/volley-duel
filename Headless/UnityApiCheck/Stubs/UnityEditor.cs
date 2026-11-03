#pragma warning disable CS0067, CS1591, CA1822, CA2211, CA1805, IDE0060
using System;
using UnityEngine;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute { public MenuItem(string itemName) { } }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute { }

    public static class EditorApplication
    {
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall;
        public static bool isPlayingOrWillChangePlaymode => false;
    }

    public static class EditorPrefs { public static string GetString(string key, string defaultValue) => defaultValue; }

    public static class EditorUtility { public static void SetDirty(UnityEngine.Object target) { } }

    public enum BuildTarget { StandaloneWindows64 = 19, iOS = 9, Android = 13 }

    [Flags]
    public enum BuildOptions { None = 0, Development = 1 }

    public enum ScriptingImplementation { Mono2x = 0, IL2CPP = 1 }

    public enum UIOrientation { Portrait = 0, PortraitUpsideDown = 1, LandscapeRight = 2, LandscapeLeft = 3, AutoRotation = 4 }

    [Flags]
    public enum AndroidArchitecture { None = 0, ARMv7 = 1, ARM64 = 2 }

    public enum AndroidSdkVersions { AndroidApiLevel23 = 23, AndroidApiLevel26 = 26 }

    public static class PlayerSettings
    {
        public static string companyName { get; set; }
        public static string productName { get; set; }
        public static UIOrientation defaultInterfaceOrientation { get; set; }
        public static bool allowedAutorotateToPortrait { get; set; }
        public static bool allowedAutorotateToPortraitUpsideDown { get; set; }
        public static bool allowedAutorotateToLandscapeLeft { get; set; }
        public static bool allowedAutorotateToLandscapeRight { get; set; }
        public static bool runInBackground { get; set; }
        public static void SetApplicationIdentifier(Build.NamedBuildTarget buildTarget, string identifier) { }
        public static string GetApplicationIdentifier(Build.NamedBuildTarget buildTarget) => "";
        public static void SetScriptingBackend(Build.NamedBuildTarget buildTarget, ScriptingImplementation backend) { }
        public static ScriptingImplementation GetScriptingBackend(Build.NamedBuildTarget buildTarget) => ScriptingImplementation.IL2CPP;

        public static class Android
        {
            public static AndroidArchitecture targetArchitectures { get; set; }
            public static AndroidSdkVersions minSdkVersion { get; set; }
        }

        public static class iOS
        {
            public static string targetOSVersionString { get; set; }
            public static bool appleEnableAutomaticSigning { get; set; }
            public static string appleDeveloperTeamID { get; set; }
        }
    }

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object => null;
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static void SaveAssets() { }
        public static bool IsValidFolder(string path) => false;
        public static UnityEngine.Object[] LoadAllAssetsAtPath(string assetPath) => new UnityEngine.Object[0];
    }

    public class SerializedProperty { public int intValue { get; set; } }

    public class SerializedObject
    {
        public SerializedObject(UnityEngine.Object obj) { }
        public SerializedProperty FindProperty(string propertyPath) => null;
        public bool ApplyModifiedProperties() => true;
    }

    public sealed class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { }
    }

    public static class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes { get; set; } }

    public static class EditorUserBuildSettings { public static BuildTarget activeBuildTarget => BuildTarget.Android; }

    public struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public BuildTarget target { get; set; }
        public BuildOptions options { get; set; }
    }

    public static class BuildPipeline { public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) => null; }
}

namespace UnityEditor.Build
{
    public struct NamedBuildTarget
    {
        public static readonly NamedBuildTarget Android = default;
        public static readonly NamedBuildTarget iOS = default;
    }

    public interface IOrderedCallback { int callbackOrder { get; } }

    public interface IPreprocessBuildWithReport : IOrderedCallback { void OnPreprocessBuild(Reporting.BuildReport report); }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }
    public struct BuildSummary
    {
        public BuildResult result => BuildResult.Unknown;
        public BuildTarget platform => BuildTarget.iOS;
    }
    public sealed class BuildReport : UnityEngine.Object { public BuildSummary summary => default; }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }

    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string dstScenePath) => true;
    }
}

namespace UnityEditor.Callbacks
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PostProcessBuildAttribute : Attribute { public PostProcessBuildAttribute(int callbackOrder) { } }
}

namespace UnityEditor.Android
{
    public interface IPostGenerateGradleAndroidProject
    {
        int callbackOrder { get; }
        void OnPostGenerateGradleAndroidProject(string path);
    }
}

namespace UnityEditor.iOS.Xcode
{
    public class PlistElementDict { public void SetString(string key, string val) { } }

    public class PlistDocument
    {
        public PlistElementDict root => null;
        public void ReadFromFile(string path) { }
        public void WriteToFile(string path) { }
    }

    public class PBXProject
    {
        public static string GetPBXProjectPath(string buildPath) => "";
        public void ReadFromFile(string path) { }
        public void WriteToFile(string path) { }
        public string GetUnityFrameworkTargetGuid() => "";
        public string GetUnityMainTargetGuid() => "";
        public static string GetUnityTestTargetName() => "Unity-iPhone Tests";
        public string TargetGuidByName(string name) => "";
        public void SetBuildProperty(string targetGuid, string name, string value) { }
        public void AddFrameworkToProject(string targetGuid, string framework, bool weak) { }
    }
}
