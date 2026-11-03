using System;
using Duel.Core.Net;
using Duel.Core.Sim;
using UnityEngine;
using VolleyDuel.Sim;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// Everything the player sees of one match, driven only by the viewer's state (through
    /// <see cref="Visibility"/>) and the sim's events. The rollback-aware event ledger decides which effects
    /// spawn, which are late (spawned aged) and which no longer happened (faded out fast). Impact recipes
    /// from data drive the camera, callouts and haptics.
    /// </summary>
    public sealed class MatchView : MonoBehaviour
    {
        VolleySim sim;
        Tuning t;
        int viewer;
        bool simple;
        CourtView court;
        FighterView fighter0;
        FighterView fighter1;
        BallView ball;
        RingView rings;
        EffectsView effects;
        DuelingCamera duelCamera;
        readonly EventLedger ledger = new EventLedger();
        bool finisherOrbit;

        /// <summary>Callout text and the beneficiary, for the HUD.</summary>
        public event Action<string, int> Callout;

        /// <summary>Every newly spawned event (audio, haptics, stats hooks).</summary>
        public event Action<SimEvent> EventSpawned;

        public int Viewer => viewer;

        public DuelingCamera DuelCamera => duelCamera;

        public Vector3 BallDrawn => ball.DrawnPosition;

        public static MatchView Create(VolleySim sim, DuelingCamera camera, int viewer, bool simple)
        {
            var root = new GameObject("Match");
            MatchView v = root.AddComponent<MatchView>();
            v.sim = sim;
            v.t = sim.T;
            v.viewer = viewer;
            v.simple = simple;
            v.duelCamera = camera;
            v.court = Child<CourtView>(root, "Court");
            v.court.Build(v.t, sim.Court);
            v.fighter0 = Child<FighterView>(root, "Fighter0");
            v.fighter0.Build(sim.Def(0), 0, v.t.Meters.SurgeMax);
            v.fighter1 = Child<FighterView>(root, "Fighter1");
            v.fighter1.Build(sim.Def(1), 1, v.t.Meters.SurgeMax);
            v.ball = Child<BallView>(root, "Ball");
            v.ball.Build(v.t);
            v.rings = Child<RingView>(root, "Rings");
            v.rings.Build();
            v.effects = Child<EffectsView>(root, "Effects");
            return v;
        }

        static T Child<T>(GameObject root, string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go.AddComponent<T>();
        }

        /// <param name="view">The viewer's state: <c>Visibility.ForViewer(truth, viewer)</c>.</param>
        /// <param name="events">Events from this app tick, including resimulated frames.</param>
        /// <param name="resimulatedFrom">First resimulated frame, or -1.</param>
        public void Present(in GameState view, SimEventBuffer events, int resimulatedFrom)
        {
            ledger.Reconcile(events, resimulatedFrom, view.Tick);
            foreach (SimEvent e in ledger.Orphaned) effects.Orphan(e.Key);
            if (resimulatedFrom >= 0) ball.BlendCorrection();
            foreach (SimEvent e in ledger.Spawned)
            {
                float late = Mathf.Max(0, view.Tick - e.Frame) / 60f;
                React(view, e, late);
                EventSpawned?.Invoke(e);
            }

            fighter0.Render(view, view.F0);
            fighter1.Render(view, view.F1);
            bool hasLanding = sim.PredictLanding(view, out int _, out Fix64 landingX);
            ball.Render(view, simple, hasLanding, Units.F(landingX));
            rings.Render(view, view.FighterAt(viewer), simple);

            bool wide = view.Phase == MatchPhase.PreServe || (view.Ball.Has(BallFlags.Serve) && !view.Ball.CrossedNet);
            bool ballInPlay = view.Ball.Mode == BallMode.Flight || view.Ball.Mode == BallMode.Held;
            duelCamera.Track(Units.World(view.F0.Pos), Units.World(view.F1.Pos), ball.DrawnPosition, ballInPlay, wide);
            if (finisherOrbit && !view.Fin.Active)
            {
                finisherOrbit = false;
                duelCamera.EndOrbit();
            }
        }

        void React(in GameState view, in SimEvent e, float late)
        {
            Vector3 at = new Vector3(Units.F(e.X), Units.F(e.Y), -0.1f);
            switch ((VolleyEvent)e.Type)
            {
                case VolleyEvent.Contact:
                    {
                        var kind = (TouchKind)e.A;
                        var grade = (TimingGrade)(e.B & 0xF);
                        Color c = grade == TimingGrade.Perfect ? new Color(1f, 0.82f, 0.2f) : grade == TimingGrade.Good ? Color.white : new Color(0.6f, 0.6f, 0.65f);
                        bool hard = kind == TouchKind.Spike || kind == TouchKind.JumpServe || kind == TouchKind.Finisher;
                        effects.Spawn(e.Key, at, c, hard ? 0.7f : 0.4f, hard ? 0.22f : 0.15f, late);
                        if (hard && e.Actor == viewer) Haptics.Pulse(Haptics.Strength.Medium);
                        break;
                    }
                case VolleyEvent.BlockTouch:
                    effects.Spawn(e.Key, at, new Color(0.45f, 0.8f, 1f), 0.8f, 0.25f, late);
                    court.Shake(0.6f);
                    break;
                case VolleyEvent.NetHit:
                    court.Shake(Mathf.Clamp01(e.A / 1500f));
                    break;
                case VolleyEvent.NetCord:
                    court.Shake(0.4f);
                    break;
                case VolleyEvent.BallLanded:
                    effects.Spawn(e.Key, new Vector3(Units.F(e.X), 0.05f, 0f), e.A == 1 ? Color.white : new Color(1f, 0.3f, 0.25f), 0.9f, 0.4f, late);
                    break;
                case VolleyEvent.Callout:
                    {
                        var moment = (VolleyDuel.Sim.Callout)e.A;
                        ImpactRecipe recipe = t.Impacts.For(moment);
                        if (recipe == null) break;
                        if (late < 0.2f) duelCamera.Play(recipe.Camera, at);
                        Callout?.Invoke(recipe.CalloutText, e.Actor);
                        if (e.Actor == viewer || moment == VolleyDuel.Sim.Callout.Stuffed) Haptics.Pulse(recipe.HitStop >= 10 ? Haptics.Strength.Heavy : Haptics.Strength.Medium);
                        break;
                    }
                case VolleyEvent.FinisherStart:
                    finisherOrbit = true;
                    duelCamera.BeginOrbit(Units.World(view.FighterAt(e.Actor).Pos));
                    break;
                case VolleyEvent.Denied:
                case VolleyEvent.FinisherScored:
                    finisherOrbit = false;
                    duelCamera.EndOrbit();
                    break;
                case VolleyEvent.Trick:
                    effects.Spawn(e.Key, Units.World(view.FighterAt(e.Actor).Pos) + Vector3.up * 1.2f, e.B == 1 ? new Color(1f, 0.4f, 1f) : new Color(0.7f, 0.5f, 1f), 1f, 0.3f, late);
                    break;
            }
        }
    }
}
