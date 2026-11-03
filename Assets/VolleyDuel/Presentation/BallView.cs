using UnityEngine;
using VolleyDuel.Sim;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// The ball, with the readability aids from the spec: an always-on floor shadow, a speed-scaled
    /// trail, and an optional landing ring (Simple mode). The off-screen edge marker lives in the HUD.
    /// After a rollback the drawn ball blends onto the corrected flight over 4 frames instead of popping.
    /// </summary>
    public sealed class BallView : MonoBehaviour
    {
        const int BlendFrames = 4;

        Transform ball;
        Transform shadow;
        TrailRenderer trail;
        LineRenderer landing;
        Vector3 drawn;
        Vector3 correction;
        int blendLeft;
        bool hasDrawn;

        public Vector3 DrawnPosition => drawn;

        public void Build(Tuning t)
        {
            float d = Units.F(t.Court.BallRadius) * 2f;
            ball = Graybox.Primitive(PrimitiveType.Sphere, "Ball", transform, Graybox.Lit(new Color(1f, 0.93f, 0.35f))).transform;
            ball.localScale = Vector3.one * d;
            shadow = Graybox.Primitive(PrimitiveType.Cylinder, "Shadow", transform, Graybox.Unlit(new Color(0.05f, 0.05f, 0.06f))).transform;
            trail = ball.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.12f;
            trail.widthMultiplier = d * 0.8f;
            trail.minVertexDistance = 0.05f;
            trail.sharedMaterial = Graybox.Unlit(new Color(1f, 1f, 1f));
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            landing = Graybox.Ring("LandingRing", transform, new Color(1f, 1f, 1f), 0.03f);
            landing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            landing.enabled = false;
        }

        /// <summary>Called when a resimulation moved the ball: blend from where it was drawn.</summary>
        public void BlendCorrection()
        {
            if (!hasDrawn) return;
            blendLeft = BlendFrames;
        }

        public void Render(in GameState s, bool showLanding, bool hasLanding, float landingX)
        {
            Vector3 target = Units.World(s.Ball.Pos);
            bool visible = s.Ball.Mode != BallMode.Dead || s.Phase == MatchPhase.DeadBall;
            if (blendLeft > 0 && hasDrawn)
            {
                correction = drawn - target;
                float k = (float)blendLeft / (BlendFrames + 1);
                drawn = target + correction * k;
                blendLeft--;
            }
            else
            {
                drawn = target;
            }
            hasDrawn = true;
            ball.position = drawn;
            ball.gameObject.SetActive(visible);

            float h = Mathf.Max(0f, drawn.y);
            float size = Mathf.Lerp(0.42f, 0.16f, Mathf.Clamp01(h / 8f));
            shadow.position = new Vector3(drawn.x, 0.015f, 0f);
            shadow.localScale = new Vector3(size, 0.002f, size);
            shadow.gameObject.SetActive(visible);

            float speed = Units.F(s.Ball.Vel.Length);
            trail.time = Mathf.Lerp(0.02f, 0.16f, Mathf.Clamp01(speed / 25f));
            trail.emitting = s.Ball.Mode == BallMode.Flight;

            landing.enabled = showLanding && hasLanding && s.Ball.Mode == BallMode.Flight;
            if (landing.enabled)
            {
                landing.transform.position = new Vector3(landingX, 0.02f, 0f);
                landing.transform.localScale = Vector3.one * 0.35f;
            }
        }
    }
}
