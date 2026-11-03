using UnityEngine;

namespace VolleyDuel.UI
{
    /// <summary>
    /// Starts the game in whatever scene the build opens with: <c>Main.unity</c> from "Volley Duel/Build
    /// Scenes", a template's SampleScene, or the empty scene Unity builds when the build list is empty.
    /// The app needs nothing from the scene, so a missing scene setup can never leave the phone on a blank
    /// sky again.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartGame()
        {
            if (Object.FindAnyObjectByType<GameApp>() != null) return;
            var root = new GameObject("Volley Duel");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<GameApp>();
        }
    }
}
