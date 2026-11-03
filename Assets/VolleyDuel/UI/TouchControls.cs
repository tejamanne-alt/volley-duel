using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Duel.Core.Sim;

namespace VolleyDuel.UI
{
    /// <summary>
    /// Landscape touch layout (spec): a floating stick under the left thumb and context buttons under the
    /// right. HIT, JUMP, FEINT, TRICK (swipe up for Amped) and FINISHER, which becomes DENY during the
    /// opponent's Finisher. Labels follow the phase of play.
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        /// <summary>Seconds a TRICK press waits for a swipe before it counts as a plain Trick.</summary>
        public float SwipeWindow = 0.15f;
        public float SwipeDistance = 60f;

        TouchStick stick;
        TouchButton hit, jump, feint, trick, finisher;
        TextMeshProUGUI hitLabel, jumpLabel, feintLabel, finisherLabel;
        Image finisherImage;
        float trickDownAt = -1f;
        bool trickSent;
        bool trickAmped;

        public static TouchControls Build(Transform safeArea)
        {
            RectTransform root = UiKit.Rect("TouchControls", safeArea, Vector2.zero, Vector2.one);
            TouchControls c = root.gameObject.AddComponent<TouchControls>();
            Image stickArea = UiKit.Panel(root, "StickArea", new Color(0f, 0f, 0f, 0f), Vector2.zero, new Vector2(0.42f, 0.75f));
            c.stick = stickArea.gameObject.AddComponent<TouchStick>();
            c.stick.Build();

            c.hit = Round(root, "HIT", new Vector2(-150f, 170f), 210f, out c.hitLabel);
            c.jump = Round(root, "JUMP", new Vector2(-390f, 120f), 150f, out c.jumpLabel);
            c.feint = Round(root, "FEINT", new Vector2(-210f, 400f), 140f, out c.feintLabel);
            c.trick = Round(root, "TRICK", new Vector2(-420f, 330f), 140f, out _);
            c.finisher = Round(root, "FINISHER", new Vector2(-560f, 110f), 130f, out c.finisherLabel);
            c.finisherImage = c.finisher.GetComponent<Image>();
            return c;
        }

        static TouchButton Round(Transform parent, string label, Vector2 fromBottomRight, float size, out TextMeshProUGUI text)
        {
            RectTransform r = UiKit.At(label, parent, new Vector2(1f, 0f), new Vector2(size, size), fromBottomRight);
            r.pivot = new Vector2(0.5f, 0.5f);
            Image img = r.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.18f);
            TouchButton b = r.gameObject.AddComponent<TouchButton>();
            b.Background = img;
            text = UiKit.Label(r, label, size * 0.2f, TextAlignmentOptions.Center, UiKit.Paper);
            return b;
        }

        /// <summary>Context labels: attacking or defending, Finisher ready, Denial window.</summary>
        public void SetContext(bool attacking, bool finisherReady, bool denying)
        {
            hitLabel.text = attacking ? "HIT" : "DIG";
            jumpLabel.text = attacking ? "JUMP" : "BLOCK";
            feintLabel.text = attacking ? "TIP" : "FAKE";
            finisherLabel.text = denying ? "DENY" : "FINISHER";
            finisherImage.color = denying ? new Color(1f, 0.3f, 0.3f, 0.6f) : finisherReady ? new Color(1f, 0.78f, 0.2f, 0.55f) : new Color(1f, 1f, 1f, 0.1f);
        }

        /// <summary>Adds this frame's touch state to the collected input.</summary>
        public void Read(ref float x, ref float y, ref InputButtons buttons)
        {
            Vector2 s = stick.Value;
            if (Mathf.Abs(s.x) > Mathf.Abs(x)) x = s.x;
            if (Mathf.Abs(s.y) > Mathf.Abs(y)) y = s.y;
            if (hit.Held) buttons |= InputButtons.Hit;
            if (jump.Held) buttons |= InputButtons.Jump;
            if (feint.Held) buttons |= InputButtons.Feint;
            if (finisher.Held) buttons |= InputButtons.Finisher;

            // TRICK waits briefly for a swipe so Trick and Amp arrive in the same frame (the sim's Amp window).
            float now = Time.unscaledTime;
            if (trick.Held)
            {
                if (trickDownAt < 0f)
                {
                    trickDownAt = now;
                    trickSent = false;
                    trickAmped = false;
                }
                if (!trickSent && (trick.SwipeUp >= SwipeDistance || now - trickDownAt >= SwipeWindow))
                {
                    trickSent = true;
                    trickAmped = trick.SwipeUp >= SwipeDistance;
                }
            }
            else if (trickDownAt >= 0f)
            {
                // Released before the window closed: a quick tap still fires the Trick for one frame.
                if (!trickSent)
                {
                    trickSent = true;
                    trickAmped = false;
                }
                else
                {
                    trickDownAt = -1f;
                    trickSent = false;
                }
            }
            if (trickSent)
            {
                buttons |= InputButtons.Trick;
                if (trickAmped) buttons |= InputButtons.Amp;
            }
        }
    }

    /// <summary>A stick that appears wherever the left thumb lands.</summary>
    public sealed class TouchStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public float Radius = 130f;
        RectTransform rect;
        RectTransform ring;
        RectTransform knob;
        Vector2 origin;
        int pointer = int.MinValue;

        public Vector2 Value { get; private set; }

        public void Build()
        {
            rect = (RectTransform)transform;
            ring = UiKit.At("Ring", rect, new Vector2(0.5f, 0.5f), new Vector2(Radius * 2f, Radius * 2f), Vector2.zero);
            ring.pivot = new Vector2(0.5f, 0.5f);
            Image r = ring.gameObject.AddComponent<Image>();
            r.color = new Color(1f, 1f, 1f, 0.12f);
            r.raycastTarget = false;
            knob = UiKit.At("Knob", ring, new Vector2(0.5f, 0.5f), new Vector2(110f, 110f), Vector2.zero);
            knob.pivot = new Vector2(0.5f, 0.5f);
            Image k = knob.gameObject.AddComponent<Image>();
            k.color = new Color(1f, 1f, 1f, 0.35f);
            k.raycastTarget = false;
            ring.gameObject.SetActive(false);
        }

        bool Local(PointerEventData e, out Vector2 p) => RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out p);

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != int.MinValue || !Local(e, out Vector2 p)) return;
            pointer = e.pointerId;
            origin = p;
            ring.gameObject.SetActive(true);
            ring.anchorMin = ring.anchorMax = new Vector2(0.5f, 0.5f);
            ring.anchoredPosition = origin - rect.rect.center;
            knob.anchoredPosition = Vector2.zero;
            Value = Vector2.zero;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointer || !Local(e, out Vector2 p)) return;
            Vector2 d = Vector2.ClampMagnitude(p - origin, Radius);
            knob.anchoredPosition = d;
            Value = d / Radius;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            pointer = int.MinValue;
            Value = Vector2.zero;
            ring.gameObject.SetActive(false);
        }
    }

    /// <summary>A hold button that also measures an upward swipe (TRICK's Amped version).</summary>
    public sealed class TouchButton : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Image Background;
        Vector2 downAt;
        int pointer = int.MinValue;

        public bool Held { get; private set; }

        /// <summary>How far (screen pixels) the finger has moved up since the press.</summary>
        public float SwipeUp { get; private set; }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != int.MinValue) return;
            pointer = e.pointerId;
            downAt = e.position;
            SwipeUp = 0f;
            Held = true;
            if (Background != null) Background.color = new Color(1f, 1f, 1f, 0.4f);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId == pointer) SwipeUp = Mathf.Max(SwipeUp, e.position.y - downAt.y);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            pointer = int.MinValue;
            Held = false;
            SwipeUp = 0f;
            if (Background != null) Background.color = new Color(1f, 1f, 1f, 0.18f);
        }
    }
}
