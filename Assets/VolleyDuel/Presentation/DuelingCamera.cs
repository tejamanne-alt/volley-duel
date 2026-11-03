using UnityEngine;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// Side-view dueling camera (spec). It frames both fighters and the ball with the view width clamped to
    /// 7-15 m and damped toward its target, and keeps the net (x = 0) in frame. At about 2.1:1 a 1.9 m
    /// fighter fills about 60% of the height at 7 m and about 27% at 15 m. Impact recipes play short
    /// camera moves on top; Finishers are the only time it leaves the side view.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class DuelingCamera : MonoBehaviour
    {
        public float MinWidth = 7f;
        public float MaxWidth = 15f;
        public float Padding = 3f;
        public float Damping = 4f;
        public float VerticalFov = 30f;

        Camera cam;
        float width = 11f;
        float centerX;
        float targetWidth = 11f;
        float targetX;

        string shot = string.Empty;
        float shotAge;
        float shotLength;
        Vector3 shotPoint;
        float shake;
        bool orbiting;
        Vector3 orbitAround;

        public Camera Camera => cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.fieldOfView = VerticalFov;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
        }

        /// <summary>Set the framing target from the drawn positions this frame.</summary>
        public void Track(Vector3 fighter0, Vector3 fighter1, Vector3 ball, bool ballInPlay, bool wide)
        {
            float min = Mathf.Min(0f, Mathf.Min(fighter0.x, fighter1.x));
            float max = Mathf.Max(0f, Mathf.Max(fighter0.x, fighter1.x));
            if (ballInPlay)
            {
                min = Mathf.Min(min, ball.x);
                max = Mathf.Max(max, ball.x);
            }
            targetWidth = wide ? MaxWidth : Mathf.Clamp(max - min + Padding, MinWidth, MaxWidth);
            float mid = (min + max) * 0.5f;
            // The net never leaves the frame.
            float limit = targetWidth * 0.5f - 1f;
            targetX = Mathf.Clamp(mid, -limit, limit);
        }

        /// <summary>Impact recipe camera move by name (data: tuning.json impacts.*.camera).</summary>
        public void Play(string move, Vector3 point)
        {
            switch (move)
            {
                case "HardCut":
                    orbiting = false;
                    shot = string.Empty;
                    width = targetWidth;
                    centerX = targetX;
                    return;
                case "Nudge":
                    shake = Mathf.Max(shake, 0.08f);
                    return;
            }
            shot = move ?? string.Empty;
            shotAge = 0f;
            shotPoint = point;
            switch (shot)
            {
                case "PunchIn": shotLength = 0.35f; shake = Mathf.Max(shake, 0.06f); break;
                case "SnapZoomHands": shotLength = 0.5f; break;
                case "PushInLanding": shotLength = 0.6f; break;
                case "LowTilt": shotLength = 0.6f; break;
                case "WhipPan": shotLength = 0.5f; break;
                case "QuickZoomDefender": shotLength = 0.4f; break;
                case "FreezeSnap": shotLength = 0.25f; break;
                case "SetPointCut":
                case "MatchPointCut": shotLength = 1f; break;
                default: shotLength = 0.3f; break;
            }
        }

        /// <summary>Finisher cinematic: orbit the user until <see cref="EndOrbit"/> (the sim decides how long).</summary>
        public void BeginOrbit(Vector3 around)
        {
            orbiting = true;
            orbitAround = around;
            shotAge = 0f;
        }

        public void EndOrbit() => Play("HardCut", Vector3.zero);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-Damping * dt);
            width = Mathf.Lerp(width, targetWidth, k);
            centerX = Mathf.Lerp(centerX, targetX, k);
            float aspect = Mathf.Max(1f, cam.aspect);
            float zoom = 1f;
            float x = centerX;
            float yOffset = 0f;
            float pitch = 0f;

            if (shot.Length > 0)
            {
                shotAge += dt;
                float t = Mathf.Clamp01(shotAge / shotLength);
                float env = Mathf.Sin(t * Mathf.PI);
                switch (shot)
                {
                    case "PunchIn": zoom = 1f - 0.2f * env; x = Mathf.Lerp(x, shotPoint.x, 0.35f * env); break;
                    case "SnapZoomHands": zoom = 1f - 0.45f * env; x = Mathf.Lerp(x, shotPoint.x, 0.6f * env); break;
                    case "PushInLanding": zoom = 1f - 0.25f * env; x = Mathf.Lerp(x, shotPoint.x, 0.5f * env); break;
                    case "LowTilt": yOffset = -1.2f * env; pitch = -8f * env; break;
                    case "WhipPan": x = Mathf.Lerp(x, shotPoint.x, env); break;
                    case "QuickZoomDefender": zoom = 1f - 0.3f * env; x = Mathf.Lerp(x, shotPoint.x, 0.5f * env); break;
                    case "FreezeSnap": zoom = 1f - 0.4f * (1f - t); x = Mathf.Lerp(x, shotPoint.x, 0.6f * (1f - t)); break;
                    case "SetPointCut":
                    case "MatchPointCut": zoom = 1f - 0.2f * env; break;
                }
                if (t >= 1f) shot = string.Empty;
            }

            float w = width * zoom;
            float halfH = w / aspect * 0.5f;
            float hFov = 2f * Mathf.Atan(Mathf.Tan(VerticalFov * 0.5f * Mathf.Deg2Rad) * aspect);
            float distance = w * 0.5f / Mathf.Tan(hFov * 0.5f);
            float y = halfH - 0.25f + yOffset;
            Vector3 jitter = shake > 0f ? (Vector3)(Random.insideUnitCircle * shake) : Vector3.zero;
            shake = Mathf.MoveTowards(shake, 0f, dt * 0.5f);

            if (orbiting)
            {
                shotAge += dt;
                float a = shotAge * 0.9f;
                Vector3 offset = new Vector3(Mathf.Sin(a) * 6f, 1.2f, -Mathf.Cos(a) * 6f);
                transform.position = orbitAround + offset;
                transform.LookAt(orbitAround + Vector3.up * 1.2f);
                return;
            }
            transform.position = new Vector3(x, y, -distance) + jitter;
            transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>Screen position of a world point (HUD edge marker).</summary>
        public Vector3 ToScreen(Vector3 world) => cam.WorldToScreenPoint(world);
    }
}
