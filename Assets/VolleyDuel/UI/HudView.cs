using UnityEngine;
using UnityEngine.UI;
using VolleyDuel.Presentation;
using VolleyDuel.Sim;

namespace VolleyDuel.UI
{
    /// <summary>
    /// Original HUD (spec) built in code. It has score bars filling toward the set target at the top, set
    /// pips, Stamina under each bar, a three-segment Surge gauge in each lower corner, the server marker
    /// center-top, a large central counter, callouts, and an edge marker for a ball above the frame. The
    /// middle of the screen stays clear for the action.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        sealed class Side
        {
            public RectTransform ScoreFill;
            public RectTransform StaminaFill;
            public Image[] Pips;
            public Image[] Surge;
            public Text Name;
        }

        readonly Side[] sides = { new Side(), new Side() };
        Text counter;
        Text server;
        Text callout;
        Text banner;
        Text status;
        RectTransform edgeMarker;
        Text edgeHeight;
        float calloutAge = 99f;
        float bannerAge = 99f;
        Tuning t;
        int setsToWin;

        public static HudView Build(Transform safeArea, VolleySim sim)
        {
            RectTransform root = UiKit.Rect("Hud", safeArea, Vector2.zero, Vector2.one);
            HudView h = root.gameObject.AddComponent<HudView>();
            h.t = sim.T;
            h.setsToWin = sim.SetsToWin;
            for (int p = 0; p < 2; p++) h.BuildSide(root, p, sim.Def(p));
            h.counter = UiKit.Label(UiKit.At("Counter", root, new Vector2(0.5f, 1f), new Vector2(360f, 120f), new Vector2(0f, -20f)), "0 : 0", 96f, TextAnchor.MiddleCenter, UiKit.Paper);
            h.server = UiKit.Label(UiKit.At("Server", root, new Vector2(0.5f, 1f), new Vector2(360f, 50f), new Vector2(0f, -140f)), string.Empty, 34f, TextAnchor.MiddleCenter, UiKit.Accent);
            h.callout = UiKit.Label(UiKit.At("Callout", root, new Vector2(0.5f, 0.5f), new Vector2(1200f, 200f), new Vector2(0f, 160f)), string.Empty, 140f, TextAnchor.MiddleCenter, UiKit.Accent);
            h.callout.fontStyle = FontStyle.BoldAndItalic;
            h.banner = UiKit.Label(UiKit.At("Banner", root, new Vector2(0.5f, 0.5f), new Vector2(1400f, 160f), new Vector2(0f, -60f)), string.Empty, 90f, TextAnchor.MiddleCenter, UiKit.Paper);
            h.status = UiKit.Label(UiKit.At("Status", root, new Vector2(0.5f, 0.5f), new Vector2(1400f, 120f), new Vector2(0f, -220f)), string.Empty, 52f, TextAnchor.MiddleCenter, UiKit.Paper);
            h.edgeMarker = UiKit.At("EdgeMarker", root, new Vector2(0f, 1f), new Vector2(160f, 90f), Vector2.zero);
            h.edgeMarker.pivot = new Vector2(0.5f, 1f);
            h.edgeHeight = UiKit.Label(h.edgeMarker, "^", 40f, TextAnchor.MiddleCenter, UiKit.Accent);
            h.edgeMarker.gameObject.SetActive(false);
            return h;
        }

        void BuildSide(RectTransform root, int p, FighterDefinition def)
        {
            Side s = sides[p];
            bool right = p == 1;
            Vector2 anchor = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            float sign = right ? -1f : 1f;
            RectTransform bar = UiKit.At("Score" + p, root, anchor, new Vector2(620f, 56f), new Vector2(sign * 40f, -40f));
            UiKit.Bar(bar, "ScoreBar", UiKit.Ink, Units.Hex(def.IdentityColor), right, out s.ScoreFill);
            RectTransform stam = UiKit.At("Stamina" + p, root, anchor, new Vector2(520f, 16f), new Vector2(sign * 40f, -104f));
            UiKit.Bar(stam, "StaminaBar", UiKit.Ink, new Color(0.45f, 0.95f, 0.55f), right, out s.StaminaFill);
            s.Name = UiKit.Label(UiKit.At("Name" + p, root, anchor, new Vector2(520f, 50f), new Vector2(sign * 40f, -126f)), def.ShortName, 40f, right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, UiKit.Paper);
            s.Pips = new Image[setsToWin];
            for (int i = 0; i < setsToWin; i++)
            {
                RectTransform pip = UiKit.At("Pip" + i, root, anchor, new Vector2(30f, 30f), new Vector2(sign * (680f + i * 42f), -52f));
                s.Pips[i] = pip.gameObject.AddComponent<Image>();
            }
            int segments = Mathf.Max(1, t.Meters.SurgeMax / Mathf.Max(1, t.Meters.SurgeBar));
            s.Surge = new Image[segments];
            Vector2 low = right ? new Vector2(1f, 0f) : new Vector2(0f, 0f);
            for (int i = 0; i < segments; i++)
            {
                RectTransform seg = UiKit.At("Surge" + i, root, low, new Vector2(110f, 34f), new Vector2(sign * (40f + i * 122f), 40f));
                s.Surge[i] = seg.gameObject.AddComponent<Image>();
            }
        }

        public void ShowCallout(string text, int beneficiary)
        {
            callout.text = text;
            calloutAge = 0f;
        }

        public void ShowBanner(string text)
        {
            banner.text = text;
            bannerAge = 0f;
        }

        /// <summary>Connection status line (netplay pauses, no contest); empty hides it.</summary>
        public void SetStatus(string text) => status.text = text ?? string.Empty;

        public void Render(in GameState s, DuelingCamera cam, Vector3 ballDrawn)
        {
            for (int p = 0; p < 2; p++)
            {
                Side side = sides[p];
                FighterState f = s.FighterAt(p);
                bool right = p == 1;
                UiKit.SetFill(side.ScoreFill, (float)s.Score(p) / t.Scoring.SetTarget, right);
                UiKit.SetFill(side.StaminaFill, Units.F(f.Stamina) / Mathf.Max(0.01f, Units.F(t.Meters.StaminaMax)), right);
                for (int i = 0; i < side.Pips.Length; i++) side.Pips[i].color = i < s.SetsWon(p) ? UiKit.Accent : UiKit.Dim;
                for (int i = 0; i < side.Surge.Length; i++)
                {
                    float fill = Mathf.Clamp01((f.Surge - i * t.Meters.SurgeBar) / (float)t.Meters.SurgeBar);
                    side.Surge[i].color = fill >= 1f ? UiKit.Accent : Color.Lerp(UiKit.Ink, UiKit.Accent, fill * 0.6f);
                }
            }
            counter.text = s.Score0 + " : " + s.Score1;
            server.text = s.Phase == MatchPhase.PreServe || s.Phase == MatchPhase.Rally ? (s.Server == 0 ? "< SERVE" : "SERVE >") : string.Empty;

            calloutAge += Time.unscaledDeltaTime;
            UiKit.SetAlpha(callout, calloutAge < 0.9f ? 1f : Mathf.Clamp01(1f - (calloutAge - 0.9f) * 3f));
            callout.rectTransform.localScale = Vector3.one * (calloutAge < 0.08f ? 1.25f : 1f);
            bannerAge += Time.unscaledDeltaTime;
            UiKit.SetAlpha(banner, bannerAge < 1.6f ? 1f : Mathf.Clamp01(1f - (bannerAge - 1.6f) * 2f));

            // A ball above the frame shows an edge marker with its height.
            Vector3 sp = cam.ToScreen(ballDrawn);
            bool above = sp.z > 0f && sp.y > Screen.height;
            edgeMarker.gameObject.SetActive(above && s.Ball.Mode == BallMode.Flight);
            if (edgeMarker.gameObject.activeSelf)
            {
                var parent = (RectTransform)edgeMarker.parent;
                float x = Mathf.Clamp01(sp.x / Mathf.Max(1, Screen.width)) * parent.rect.width;
                edgeMarker.anchoredPosition = new Vector2(x, 0f);
                edgeHeight.text = "^\n" + ballDrawn.y.ToString("0.0") + " m";
            }
        }
    }
}
