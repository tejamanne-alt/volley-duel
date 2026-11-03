using UnityEngine;
using VolleyDuel.Sim;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// The local player's timing rings, drawn in world space at the planned contact point. An outer ring
    /// closes on the target ring and meets it on the center frame. The target is gold inside the Perfect
    /// window, white inside Good, and grey when the ball is out of reach. Only the viewer's own rings
    /// exist in its state (Visibility hides the opponent's).
    /// </summary>
    public sealed class RingView : MonoBehaviour
    {
        const float TargetRadius = 0.32f;
        const float ClosingPerFrame = 0.045f;

        LineRenderer target;
        LineRenderer closing;
        LineRenderer block;
        Material targetMaterial;
        Material closingMaterial;

        public void Build()
        {
            target = Graybox.Ring("Target", transform, Color.white, 0.035f);
            closing = Graybox.Ring("Closing", transform, Color.white, 0.025f);
            block = Graybox.Ring("Block", transform, new Color(0.4f, 0.8f, 1f), 0.035f);
            targetMaterial = target.sharedMaterial;
            closingMaterial = closing.sharedMaterial;
        }

        public void Render(in GameState s, in FighterState me, bool simple)
        {
            int toCenter = me.RingCenter - s.World;
            bool show = me.Ring != RingKind.None && toCenter > -me.RingGood && toCenter < 90;
            target.enabled = show;
            closing.enabled = show && toCenter > 0;
            if (show)
            {
                Vector3 at = Units.World(me.RingPoint, -0.2f);
                float scale = simple ? 1.35f : 1f;
                target.transform.position = at;
                target.transform.localScale = Vector3.one * TargetRadius * scale;
                closing.transform.position = at;
                closing.transform.localScale = Vector3.one * (TargetRadius * scale + Mathf.Max(0, toCenter) * ClosingPerFrame);
                int half = me.RingPerfect / 2;
                Color c;
                if (!me.RingReachable) c = new Color(0.45f, 0.45f, 0.45f);
                else if (Mathf.Abs(toCenter) <= half) c = new Color(1f, 0.8f, 0.2f);
                else if (Mathf.Abs(toCenter) <= me.RingGood / 2) c = Color.white;
                else c = new Color(0.75f, 0.85f, 1f);
                targetMaterial.color = c;
                closingMaterial.color = me.RingDive ? new Color(1f, 0.55f, 0.3f) : new Color(1f, 1f, 1f, 0.8f);
            }

            int toBlock = me.BlockRingCenter - s.World;
            bool showBlock = toBlock > -4 && toBlock < 60;
            block.enabled = showBlock;
            if (showBlock)
            {
                block.transform.position = new Vector3(Units.F(me.Pos.X), 2.9f, -0.2f);
                block.transform.localScale = Vector3.one * (0.3f + Mathf.Max(0, toBlock) * 0.04f);
            }
        }
    }
}
