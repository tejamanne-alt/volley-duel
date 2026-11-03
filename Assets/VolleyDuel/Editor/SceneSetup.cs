using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using VolleyDuel.Presentation;
using VolleyDuel.UI;

namespace VolleyDuel.EditorTools
{
    /// <summary>
    /// The one app scene, Assets/VolleyDuel/Scenes/Main.unity. The editor makes and keeps it, and it is
    /// never hand-edited:
    /// - created when missing,
    /// - first in the shared build scene list (Build Profiles use it),
    /// - opened in place of Unity's untouched "Untitled" scene.
    /// So a build never stops to ask about saving "Untitled", and the scene list never needs changing by
    /// hand. A scene with real work in it is never closed or replaced.
    /// </summary>
    public static class SceneSetup
    {
        public const string ScenePath = "Assets/VolleyDuel/Scenes/Main.unity";

        /// <summary>The objects Unity puts in a new untitled scene (the URP template adds a Global Volume).</summary>
        static readonly string[] DefaultRoots = { "Main Camera", "Directional Light", "Global Volume" };

        /// <summary>Called on every editor load; does nothing once the scene exists and is open or in use.</summary>
        public static void Ensure()
        {
            if (Application.isBatchMode) return;
            Scene active = SceneManager.GetActiveScene();
            bool onDefault = IsUntouchedUntitled(active);
            if (!File.Exists(ScenePath))
            {
                if (onDefault)
                {
                    Create(NewSceneMode.Single);
                }
                else if (!string.IsNullOrEmpty(active.path) && !active.isDirty)
                {
                    Create(NewSceneMode.Additive);
                }
                else
                {
                    Debug.Log("Volley Duel: save the open scene, then use Volley Duel/Build Scenes to create " + ScenePath + ". Until then the game still starts in any scene.");
                    return;
                }
            }
            else if (onDefault)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            RegisterInBuild();
        }

        /// <summary>Menu: rebuilds the scene from code (asks first if open scenes have unsaved changes).</summary>
        public static void Rebuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create(NewSceneMode.Single);
            RegisterInBuild();
        }

        static void Create(NewSceneMode mode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            SceneManager.SetActiveScene(scene);
            Populate();
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (mode == NewSceneMode.Additive)
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log("Volley Duel: created " + ScenePath + " and put it first in the build scene list.");
        }

        static void Populate()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera cam = cameraObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.11f, 0.14f);
            cameraObject.AddComponent<DuelingCamera>();
            cameraObject.transform.position = new Vector3(0f, 2f, -20f);
            // URP adds these the first time it renders a camera or light. Saving them now means opening the
            // scene does not mark it modified, which would make every build ask to save.
            cameraObject.AddComponent<UniversalAdditionalCameraData>();

            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.gameObject.AddComponent<UniversalAdditionalLightData>();

            new GameObject("Volley Duel").AddComponent<GameApp>();
        }

        /// <summary>Main.unity first and enabled in the shared scene list; other scenes keep their places after it.</summary>
        static void RegisterInBuild()
        {
            if (!File.Exists(ScenePath)) return;
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            if (current.Length > 0 && current[0].path == ScenePath && current[0].enabled) return;
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (EditorBuildSettingsScene s in current)
            {
                if (s.path != ScenePath) scenes.Add(s);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>An unsaved scene holding only what Unity puts in a new scene: safe to replace.</summary>
        static bool IsUntouchedUntitled(Scene scene)
        {
            if (!scene.IsValid() || !string.IsNullOrEmpty(scene.path)) return false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (Array.IndexOf(DefaultRoots, root.name) < 0) return false;
            }
            return true;
        }
    }
}
