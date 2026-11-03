using UnityEngine;
using VolleyDuel.Sim;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// Graybox fighter: torso, head and two arms posed from sim state each frame. The pose is a pure
    /// function of the state (frame-locked); nothing here feeds back into the sim. Clips from Blender
    /// replace the procedural poses later, sampled by sim frame through Playables.
    /// </summary>
    public sealed class FighterView : MonoBehaviour
    {
        Transform body;
        Transform head;
        Transform armL;
        Transform armR;
        Renderer bodyRenderer;
        Material bodyMaterial;
        Color baseColor;
        float height;
        int player;
        int surgeMax;

        public void Build(FighterDefinition def, int playerIndex, int fullSurge)
        {
            player = playerIndex;
            surgeMax = fullSurge;
            height = Units.F(def.Height);
            baseColor = Units.Hex(def.IdentityColor);
            bodyMaterial = Graybox.Lit(baseColor);
            Material skin = Graybox.Lit(new Color(0.85f, 0.7f, 0.58f));
            body = Graybox.Primitive(PrimitiveType.Capsule, "Body", transform, bodyMaterial).transform;
            bodyRenderer = body.GetComponent<Renderer>();
            head = Graybox.Primitive(PrimitiveType.Sphere, "Head", transform, skin).transform;
            armL = Arm("ArmL", skin);
            armR = Arm("ArmR", skin);
        }

        Transform Arm(string name, Material m)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(transform, false);
            GameObject limb = Graybox.Primitive(PrimitiveType.Capsule, "Limb", pivot, m);
            limb.transform.localScale = new Vector3(0.09f, height * 0.2f, 0.09f);
            limb.transform.localPosition = new Vector3(0f, -height * 0.2f, 0f);
            return pivot;
        }

        /// <summary>Pose from the viewer's state (hidden choices already masked by Visibility).</summary>
        public void Render(in GameState s, in FighterState f)
        {
            float dir = player == 0 ? 1f : -1f;
            transform.position = Units.World(f.Pos);
            float torso = height * 0.62f;
            float crouch = 1f;
            float lean = 0f;
            float armForward = 10f;
            float armUp = 0f;
            bool lying = false;

            switch (f.Action)
            {
                case FighterAction.DigStance: crouch = 0.72f; lean = 18f; armForward = 60f; break;
                case FighterAction.Committed:
                    if (f.CommitKind == TouchKind.Set || f.CommitKind == TouchKind.Dump) armUp = 170f;
                    else { crouch = 0.8f; armForward = 70f; }
                    break;
                case FighterAction.JumpStartup: crouch = 0.78f; armForward = -40f; break;
                case FighterAction.Airborne:
                    if (f.Jump == JumpKind.Block || f.Jump == JumpKind.CommitBlock || f.Jump == JumpKind.ShockwaveBlock || f.Jump == JumpKind.FakeHop) armUp = 175f;
                    else if (f.Charging || f.CommitKind == TouchKind.Spike) armUp = f.Charging ? 200f : 160f;
                    else armUp = 120f;
                    break;
                case FighterAction.Landing: crouch = 0.82f; break;
                case FighterAction.Diving:
                case FighterAction.Floor: lying = true; armForward = 90f; break;
                case FighterAction.Knockback: lean = -25f; break;
                case FighterAction.ServeHold: armForward = 45f; break;
                case FighterAction.ServeToss: armUp = 150f; break;
                case FighterAction.ServeApproach: armForward = -30f; break;
                case FighterAction.Recovery: crouch = 0.9f; break;
            }

            float bodyHeight = torso * crouch;
            if (lying)
            {
                body.localRotation = Quaternion.Euler(0f, 0f, -90f * dir);
                body.localPosition = new Vector3(0f, 0.25f, 0f);
                head.localPosition = new Vector3(dir * torso * 0.55f, 0.25f, 0f);
            }
            else
            {
                body.localRotation = Quaternion.Euler(0f, 0f, -lean * dir);
                body.localPosition = new Vector3(0f, height * 0.38f * crouch + bodyHeight * 0.25f, 0f);
                head.localPosition = new Vector3(Mathf.Sin(lean * Mathf.Deg2Rad) * dir * bodyHeight * 0.4f, height * 0.38f * crouch + bodyHeight * 0.62f + height * 0.06f, 0f);
            }
            body.localScale = new Vector3(0.42f, bodyHeight * 0.5f, 0.32f);
            head.localScale = Vector3.one * height * 0.13f;

            Vector3 shoulder = head.localPosition + new Vector3(0f, -height * 0.11f, 0f);
            float angle = armUp > 0f ? armUp : armForward;
            armL.localPosition = shoulder + new Vector3(0f, 0f, -0.2f);
            armR.localPosition = shoulder + new Vector3(0f, 0f, 0.2f);
            armL.localRotation = Quaternion.Euler(0f, 0f, angle * dir);
            armR.localRotation = Quaternion.Euler(0f, 0f, angle * dir);

            // Meters on the body: Gassed dulls the color, full Surge brightens it, a Trick flashes.
            Color c = baseColor;
            if (f.Gassed) c = Color.Lerp(c, Color.gray, 0.55f);
            if (f.ActiveTrick != TrickKind.None && s.World < f.TrickUntil) c = Color.Lerp(c, Color.white, 0.45f + 0.25f * Mathf.Sin(s.World * 0.6f));
            else if (f.Surge >= surgeMax) c = Color.Lerp(c, Color.white, 0.2f);
            if (bodyMaterial.color != c) bodyMaterial.color = c;
            bodyRenderer.enabled = true;
        }
    }
}
