using System;
using System.Collections.Generic;
using System.IO;
using Duel.Core.AI;
using Duel.Core.Net;
using Duel.Core.Sim;
using Duel.Core.Transport.BLE;
using Duel.Core.Transport.LAN;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VolleyDuel.AI;
using VolleyDuel.Presentation;
using VolleyDuel.Sim;

namespace VolleyDuel.UI
{
    /// <summary>
    /// App root: menus, lobby, match screen and results, all built in code on one scene. Modes shipped in
    /// the graybox: Quick Match (any fighter, court and CPU tier, with Rival mode), Bluetooth Duel, Wi-Fi
    /// Duel, Training (rings and frame data shown), Replays, and a CPU-vs-CPU showcase. World Tour and
    /// Volley School arrive with content (P5).
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        enum Screen
        {
            Main,
            QuickMatch,
            Lobby,
            Match,
            Results,
            Replays,
            Settings,
        }

        SimTables tables;
        CpuTables cpuTables;
        Canvas canvas;
        RectTransform safe;
        RectTransform screenRoot;
        DuelingCamera duelCamera;

        // Choices (persist across screens).
        int myFighter;
        int theirFighter = 2;
        int court;
        int tier = 2;
        int setsToWin = 2;
        bool rival;
        bool simple;
        bool showDebug;
        bool training;

        // Match state.
        IMatchRunner runner;
        MatchView view;
        HudView hud;
        TouchControls touch;
        DebugOverlay debug;
        readonly InputCollector input = new InputCollector();
        NetLobby lobby;
        bool paused;
        string lastReplayPath = string.Empty;
        bool rematchRequested;
        bool resultsShown;
        TextMeshProUGUI lobbyStatus;
        RectTransform courtList;
        int listedCourts = -1;
        TMP_InputField addressField;

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;
            UnityEngine.Screen.orientation = ScreenOrientation.LandscapeLeft;
            UnityEngine.Screen.autorotateToPortrait = false;
            UnityEngine.Screen.autorotateToPortraitUpsideDown = false;
            UnityEngine.Screen.autorotateToLandscapeLeft = true;
            UnityEngine.Screen.autorotateToLandscapeRight = true;

            tables = ResourcesDataSource.LoadTables();
            cpuTables = new CpuTables(tables);
            theirFighter = Math.Min(theirFighter, tables.Fighters.Length - 1);
            simple = PlayerPrefs.GetInt("simple", 0) == 1;
            showDebug = PlayerPrefs.GetInt("debug", 0) == 1;
            Haptics.Enabled = PlayerPrefs.GetInt("haptics", 1) == 1;

            EnsureSceneBasics();
            canvas = UiKit.Canvas("UI", 10);
            safe = UiKit.SafeArea(canvas.transform);
            Show(Screen.Main);
        }

        void EnsureSceneBasics()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.11f, 0.14f);
            duelCamera = cam.GetComponent<DuelingCamera>();
            if (duelCamera == null) duelCamera = cam.gameObject.AddComponent<DuelingCamera>();
            if (FindAnyObjectByType<Light>() == null)
            {
                var light = new GameObject("Key Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }
        }

        // ------------------------------------------------------------------ screens

        void Show(Screen screen)
        {
            if (screenRoot != null) Destroy(screenRoot.gameObject);
            screenRoot = UiKit.Rect(screen.ToString(), safe, Vector2.zero, Vector2.one);
            lobbyStatus = null;
            courtList = null;
            listedCourts = -1;
            addressField = null;
            switch (screen)
            {
                case Screen.Main: BuildMain(); break;
                case Screen.QuickMatch: BuildQuickMatch(); break;
                case Screen.Replays: BuildReplays(); break;
                case Screen.Settings: BuildSettings(); break;
                case Screen.Results: BuildResults(); break;
                case Screen.Match: BuildMatchScreen(); break;
            }
        }

        RectTransform Backdrop(string title)
        {
            UiKit.Panel(screenRoot, "Backdrop", new Color(0.04f, 0.05f, 0.07f, 0.92f), Vector2.zero, Vector2.one);
            UiKit.Label(UiKit.At("Title", screenRoot, new Vector2(0.5f, 1f), new Vector2(1600f, 160f), new Vector2(0f, -40f)), title, 96f, TextAlignmentOptions.Center, UiKit.Accent).fontStyle = FontStyles.Bold | FontStyles.Italic;
            return UiKit.Column(screenRoot, "Column", 900f, new Vector2(0.5f, 0.5f));
        }

        void BuildMain()
        {
            RectTransform col = Backdrop("VOLLEY DUEL");
            UiKit.Button(col, "Quick Match", () => { training = false; Show(Screen.QuickMatch); });
            UiKit.Button(col, "Bluetooth Duel", () => OpenLobby(true));
            UiKit.Button(col, "Wi-Fi Duel", () => OpenLobby(false));
            UiKit.Button(col, "Training", () => { training = true; Show(Screen.QuickMatch); });
            UiKit.Button(col, "Replays", () => Show(Screen.Replays));
            UiKit.Button(col, "Settings", () => Show(Screen.Settings));
            UiKit.Button(col, "Watch CPU vs CPU", StartShowcase);
            Button tour = UiKit.Button(col, "World Tour / Volley School (P5)", null, 80f);
            tour.interactable = false;
        }

        void BuildQuickMatch()
        {
            RectTransform col = Backdrop(training ? "TRAINING" : "QUICK MATCH");
            Cycle(col, () => "You: " + tables.Fighters[myFighter].ShortName, () => myFighter = (myFighter + 1) % tables.Fighters.Length);
            Cycle(col, () => "Opponent: " + tables.Fighters[theirFighter].ShortName, () => theirFighter = (theirFighter + 1) % tables.Fighters.Length);
            Cycle(col, () => "Court: " + tables.Courts[court].Name, () => court = (court + 1) % tables.Courts.Length);
            Cycle(col, () => "CPU: " + cpuTables.Tiers[tier].Name, () => tier = (tier + 1) % cpuTables.Tiers.Length);
            Cycle(col, () => "Sets: best of " + (setsToWin * 2 - 1), () => setsToWin = setsToWin == 2 ? 3 : 2);
            Cycle(col, () => "Rival mode: " + (rival ? "On" : "Off"), () => rival = !rival);
            UiKit.Button(col, "Start", StartQuickMatch);
            UiKit.Button(col, "Back", () => Show(Screen.Main), 80f);
        }

        void Cycle(RectTransform col, Func<string> label, Action next)
        {
            TextMeshProUGUI text = null;
            Button b = UiKit.Button(col, label(), () =>
            {
                next();
                text.text = label();
            }, 90f);
            text = b.GetComponentInChildren<TextMeshProUGUI>();
        }

        void BuildSettings()
        {
            RectTransform col = Backdrop("SETTINGS");
            Cycle(col, () => "Simple mode: " + (simple ? "On" : "Off"), () => { simple = !simple; PlayerPrefs.SetInt("simple", simple ? 1 : 0); });
            Cycle(col, () => "Haptics: " + (Haptics.Enabled ? "On" : "Off"), () => { Haptics.Enabled = !Haptics.Enabled; PlayerPrefs.SetInt("haptics", Haptics.Enabled ? 1 : 0); });
            Cycle(col, () => "Netplay overlay: " + (showDebug ? "On" : "Off"), () => { showDebug = !showDebug; PlayerPrefs.SetInt("debug", showDebug ? 1 : 0); });
            UiKit.Button(col, "Back", () => Show(Screen.Main), 80f);
        }

        void BuildReplays()
        {
            RectTransform col = Backdrop("REPLAYS");
            List<string> files = ReplayStore.List();
            int shown = 0;
            foreach (string f in files)
            {
                if (shown++ >= 6) break;
                string path = f;
                UiKit.Button(col, Path.GetFileNameWithoutExtension(path), () => PlayReplay(path), 80f);
            }
            if (files.Count == 0) UiKit.Label(UiKit.Rect("Empty", col, Vector2.zero, Vector2.one), "No replays yet: every match is saved here.", 40f, TextAlignmentOptions.Center, UiKit.Paper).gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;
            UiKit.Button(col, "Back", () => Show(Screen.Main), 80f);
        }

        void BuildResults()
        {
            GameState s = runner.Truth;
            int winner = s.Winner;
            string name = winner < 2 ? runner.Sim.Def(winner).ShortName.ToUpperInvariant() : "NO CONTEST";
            RectTransform col = Backdrop(winner < 2 ? name + " WINS " + s.SetsWon(winner) + "-" + s.SetsWon(1 - winner) : name);
            if (lastReplayPath.Length > 0) UiKit.Label(UiKit.Rect("Saved", col, Vector2.zero, Vector2.one), "Replay saved", 36f, TextAlignmentOptions.Center, UiKit.Dim).gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            if (runner is NetMatchRunner)
            {
                UiKit.Button(col, "Rematch", () =>
                {
                    rematchRequested = true;
                    lobby.RequestRematch();
                });
            }
            else if (!(runner is ReplayRunner))
            {
                UiKit.Button(col, "Rematch", StartQuickMatch);
            }
            UiKit.Button(col, "Menu", () =>
            {
                EndMatch();
                CloseLobby();
                Show(Screen.Main);
            }, 80f);
            lobbyStatus = UiKit.Label(UiKit.Rect("Status", col, Vector2.zero, Vector2.one), string.Empty, 36f, TextAlignmentOptions.Center, UiKit.Paper);
            lobbyStatus.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
        }

        // ------------------------------------------------------------------ lobby

        void OpenLobby(bool bluetooth)
        {
            CloseLobby();
            ITransport transport;
            if (bluetooth)
            {
                BleBridges.RequestPermissions();
                IBleBridge bridge = BleBridges.CreateForPlatform();
                if (bridge == null)
                {
                    ShowMessage("Bluetooth Duel needs a phone build. In the editor, use Wi-Fi Duel.");
                    return;
                }
                transport = new BleTransport(bridge);
            }
            else
            {
                transport = new LanTransport(new BroadcastDiscovery());
            }
            lobby = new NetLobby(transport, tables);
            training = false;
            Show(Screen.Lobby);
            RectTransform col = Backdrop(bluetooth ? "BLUETOOTH DUEL" : "WI-FI DUEL");
            Cycle(col, () => "You: " + tables.Fighters[myFighter].ShortName, () => myFighter = (myFighter + 1) % tables.Fighters.Length);
            Cycle(col, () => "Court (host): " + tables.Courts[court].Name, () => court = (court + 1) % tables.Courts.Length);
            Cycle(col, () => "Sets (host): best of " + (setsToWin * 2 - 1), () => setsToWin = setsToWin == 2 ? 3 : 2);
            Button host = UiKit.Button(col, "Host a court", () => lobby.Host(myFighter, court, setsToWin, simple, tables.Fighters[myFighter].ShortName));
            host.interactable = transport.CanHost;
            UiKit.Button(col, "Find courts", () => lobby.Scan());
            if (!bluetooth)
            {
                addressField = AddressField(col);
                UiKit.Button(col, "Join address", () =>
                {
                    lobby.Join(new PeerInfo { Id = addressField.text.Trim() }, myFighter, simple, tables.Fighters[myFighter].ShortName);
                }, 80f);
            }
            courtList = UiKit.Column(screenRoot, "Courts", 700f, new Vector2(0.88f, 0.5f));
            lobbyStatus = UiKit.Label(UiKit.At("Status", screenRoot, new Vector2(0.5f, 0f), new Vector2(1600f, 90f), new Vector2(0f, 30f)), string.Empty, 40f, TextAlignmentOptions.Center, UiKit.Paper);
            UiKit.Button(col, "Back", () =>
            {
                CloseLobby();
                Show(Screen.Main);
            }, 80f);
        }

        TMP_InputField AddressField(RectTransform col)
        {
            RectTransform r = UiKit.Rect("Address", col, Vector2.zero, Vector2.one);
            r.gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;
            Image bg = r.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.1f);
            RectTransform area = UiKit.Rect("Text Area", r, Vector2.zero, Vector2.one);
            area.offsetMin = new Vector2(20f, 6f);
            area.offsetMax = new Vector2(-20f, -6f);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI placeholder = UiKit.Label(area, "192.168.1.20:47611", 40f, TextAlignmentOptions.Left, UiKit.Dim);
            TextMeshProUGUI text = UiKit.Label(area, string.Empty, 40f, TextAlignmentOptions.Left, UiKit.Paper);
            TMP_InputField field = r.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.contentType = TMP_InputField.ContentType.Standard;
            return field;
        }

        void PollLobby()
        {
            if (lobby == null) return;
            lobby.Poll();
            if (lobbyStatus != null)
            {
                switch (lobby.State)
                {
                    case NetLobby.Phase.Hosting: lobbyStatus.text = "Court " + lobby.CourtCode + " is open. Waiting for a guest..."; break;
                    case NetLobby.Phase.Scanning: lobbyStatus.text = lobby.Courts.Count == 0 ? "Looking for nearby courts..." : "Tap a court to join."; break;
                    case NetLobby.Phase.Connecting: lobbyStatus.text = "Joining..."; break;
                    case NetLobby.Phase.Handshaking: lobbyStatus.text = rematchRequested ? "Rematch! Syncing..." : "Connected. Syncing..."; break;
                    case NetLobby.Phase.Failed: lobbyStatus.text = lobby.Error; break;
                    case NetLobby.Phase.Idle:
                        if (rematchRequested) lobbyStatus.text = lobby.PeerWantsRematch ? "Starting..." : "Waiting for the other player...";
                        break;
                }
            }
            if (courtList != null && lobby.Courts.Count != listedCourts)
            {
                listedCourts = lobby.Courts.Count;
                for (int i = courtList.childCount - 1; i >= 0; i--) Destroy(courtList.GetChild(i).gameObject);
                foreach (PeerInfo p in lobby.Courts)
                {
                    PeerInfo peer = p;
                    string signal = p.SignalDbm != 0 ? "  " + p.SignalDbm + " dBm" : string.Empty;
                    UiKit.Button(courtList, "Court " + p.CourtCode + signal, () => lobby.Join(peer, myFighter, simple, tables.Fighters[myFighter].ShortName), 90f);
                }
            }
            if (lobby.State == NetLobby.Phase.Ready)
            {
                EndMatch();
                rematchRequested = false;
                NetMatchRunner net = lobby.CreateRunner();
                SessionLog.Write("match start transport=" + lobby.Transport.Name + " host=" + (net.LocalPlayer == 0) + " delay=" + lobby.Agreement.InputDelay + " rtt=" + lobby.Agreement.RttMicros / 1000 + "ms");
                StartMatch(net, net.Sim.Config.SimpleOf(net.LocalPlayer), true);
            }
        }

        void CloseLobby()
        {
            if (lobby == null) return;
            lobby.Dispose();
            lobby = null;
        }

        void ShowMessage(string text)
        {
            Show(Screen.Main);
            UiKit.Label(UiKit.At("Message", screenRoot, new Vector2(0.5f, 0f), new Vector2(1600f, 90f), new Vector2(0f, 30f)), text, 40f, TextAlignmentOptions.Center, UiKit.Accent);
        }

        // ------------------------------------------------------------------ matches

        void StartQuickMatch()
        {
            CloseLobby();
            uint seed = (uint)Environment.TickCount;
            MatchConfig config = MatchConfig.Create(myFighter, theirFighter, court, seed, setsToWin);
            config.Simple0 = simple;
            var sim = new VolleySim(tables, config);
            int cpuTier = training ? 0 : tier;
            StartMatch(new LocalMatchRunner(sim, cpuTables, true, false, 0, cpuTier, rival && !training, seed), simple, true);
        }

        void StartShowcase()
        {
            CloseLobby();
            uint seed = (uint)Environment.TickCount;
            var sim = new VolleySim(tables, MatchConfig.Create(myFighter, theirFighter, court, seed, setsToWin));
            int legend = cpuTables.Tiers.Length - 1;
            StartMatch(new LocalMatchRunner(sim, cpuTables, false, false, legend, legend, false, seed), false, false);
        }

        void PlayReplay(string path)
        {
            Replay replay;
            try
            {
                replay = ReplayStore.Load(path);
            }
            catch (IOException e)
            {
                ShowMessage("Could not read the replay: " + e.Message);
                return;
            }
            if (replay.SimVersion != SimVersion.Compute(tables))
            {
                ShowMessage("This replay was recorded by a different build.");
                return;
            }
            MatchConfig config = MatchConfig.FromBytes(replay.Config);
            StartMatch(new ReplayRunner(new VolleySim(tables, config), replay), false, false);
        }

        void StartMatch(IMatchRunner r, bool simpleView, bool human)
        {
            EndMatch();
            runner = r;
            paused = false;
            resultsShown = false;
            lastReplayPath = string.Empty;
            view = MatchView.Create(r.Sim, duelCamera, r.LocalPlayer, simpleView);
            Show(Screen.Match);
            hud = HudView.Build(screenRoot, r.Sim);
            view.Callout += hud.ShowCallout;
            view.EventSpawned += OnEvent;
            touch = human ? TouchControls.Build(screenRoot) : null;
            input.Touch = touch;
            input.LocalPlayer = r.LocalPlayer;
            if (showDebug || training) debug = DebugOverlay.Build(screenRoot);
            if (r is LocalMatchRunner)
            {
                Button pause = UiKit.Button(UiKit.At("Pause", screenRoot, new Vector2(0.5f, 1f), new Vector2(150f, 70f), new Vector2(0f, -190f)), "II", TogglePause, 70f);
                pause.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
            }
        }

        void BuildMatchScreen()
        {
            // StartMatch fills the match screen (HUD, touch controls, overlay).
        }

        void TogglePause()
        {
            if (!(runner is LocalMatchRunner local)) return;
            paused = !paused;
            local.Paused = paused;
            hud.SetStatus(paused ? "Paused" : string.Empty);
        }

        void OnEvent(SimEvent e)
        {
            switch ((VolleyEvent)e.Type)
            {
                case VolleyEvent.SetStart: hud.ShowBanner("SET " + (e.A + 1)); break;
                case VolleyEvent.MatchWon: hud.ShowBanner(runner.Sim.Def(e.Actor).ShortName.ToUpperInvariant() + " WINS"); break;
                case VolleyEvent.OnARun: hud.ShowBanner("ON A RUN"); break;
                case VolleyEvent.Gassed: if (e.Actor == runner.LocalPlayer) hud.ShowBanner("GASSED"); break;
            }
        }

        void EndMatch()
        {
            if (runner != null)
            {
                runner.Dispose();
                runner = null;
            }
            if (view != null) Destroy(view.gameObject);
            view = null;
            hud = null;
            touch = null;
            debug = null;
        }

        void Update()
        {
            PollLobby();
            if (runner == null || view == null) return;
            // During a rematch handshake the finished session stops ticking, so no stale inputs follow it.
            bool rematchSync = lobby != null && rematchRequested && lobby.State == NetLobby.Phase.Handshaking;
            if (!rematchSync)
            {
                long elapsed = (long)(Time.unscaledDeltaTime * 1000000.0);
                runner.Advance(elapsed, input.Sample);
            }
            GameState truth = runner.Truth;
            GameState seen = Visibility.ForViewer(truth, runner.LocalPlayer);
            view.Present(seen, runner.FrameEvents, runner.ResimulatedFrom);
            if (resultsShown) return;
            hud.Render(seen, duelCamera, view.BallDrawn);
            hud.SetStatus(paused ? "Paused" : runner.Status);
            if (touch != null)
            {
                FighterState me = seen.FighterAt(runner.LocalPlayer);
                bool attacking = seen.Possession == runner.LocalPlayer;
                bool denying = seen.Fin.Active && seen.Fin.User != runner.LocalPlayer;
                touch.SetContext(attacking, me.Surge >= runner.Sim.T.Meters.SurgeMax, denying);
            }
            if (debug != null) debug.Show(runner, lobby != null ? lobby.Transport : null);

            if (!runner.Finished) return;
            resultsShown = true;
            if (runner is LocalMatchRunner local && local.Replay != null)
            {
                lastReplayPath = ReplayStore.Save(local.Replay, runner.Sim.Def(0).Id + "-vs-" + runner.Sim.Def(1).Id);
            }
            if (runner is NetMatchRunner net)
            {
                NetStats st = net.Session.Stats;
                SessionLog.Write("match end frames=" + st.Frames + " rollbacks=" + st.Rollbacks + " max=" + st.MaxRollbackDepth + " desyncs=" + st.Desyncs + " rtt=" + st.RttMicros / 1000 + "ms");
            }
            Show(Screen.Results);
        }
    }
}
