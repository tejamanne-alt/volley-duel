using UnityEngine;
using VolleyDuel.Sim;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// The play plane: floor, lines, net and posts, sized from the court rules in data. Court looks
    /// (12 venues) arrive with art. The graybox only tints the floor by its surface.
    /// </summary>
    public sealed class CourtView : MonoBehaviour
    {
        public const float Depth = 7f;

        Transform netBody;
        float netShake;

        public void Build(Tuning t, CourtDefinition court)
        {
            float half = Units.F(t.Court.HalfLength);
            float outer = Units.F(t.Court.OuterLimit);
            float netTop = Units.F(t.Court.NetHeight);
            Color floor = FloorColor(court != null ? court.Floor : "hardwood");

            Graybox.Box("Surround", transform, Graybox.Lit(floor * 0.55f), new Vector3(0f, -0.06f, 0f), new Vector3(outer * 2f + 6f, 0.1f, Depth + 6f));
            Graybox.Box("Court", transform, Graybox.Lit(floor), new Vector3(0f, -0.04f, 0f), new Vector3(half * 2f, 0.1f, Depth));
            Material line = Graybox.Unlit(new Color(0.95f, 0.95f, 0.92f));
            const float w = 0.05f;
            Graybox.Box("EndLine0", transform, line, new Vector3(-half, 0.012f, 0f), new Vector3(w, 0.01f, Depth));
            Graybox.Box("EndLine1", transform, line, new Vector3(half, 0.012f, 0f), new Vector3(w, 0.01f, Depth));
            Graybox.Box("CenterLine", transform, line, new Vector3(0f, 0.012f, 0f), new Vector3(w, 0.01f, Depth));
            Graybox.Box("SideLineNear", transform, line, new Vector3(0f, 0.012f, -Depth * 0.5f), new Vector3(half * 2f, 0.01f, w));
            Graybox.Box("SideLineFar", transform, line, new Vector3(0f, 0.012f, Depth * 0.5f), new Vector3(half * 2f, 0.01f, w));

            Material post = Graybox.Lit(new Color(0.2f, 0.2f, 0.22f));
            float postZ = Depth * 0.5f + 0.6f;
            Graybox.Box("PostNear", transform, post, new Vector3(0f, netTop * 0.5f, -postZ), new Vector3(0.12f, netTop, 0.12f));
            Graybox.Box("PostFar", transform, post, new Vector3(0f, netTop * 0.5f, postZ), new Vector3(0.12f, netTop, 0.12f));
            var body = new GameObject("Net");
            body.transform.SetParent(transform, false);
            netBody = body.transform;
            Graybox.Box("Mesh", netBody, Graybox.Lit(new Color(0.08f, 0.08f, 0.1f)), new Vector3(0f, netTop - 0.5f, 0f), new Vector3(0.02f, 1f, postZ * 2f));
            Graybox.Box("Cord", netBody, Graybox.Unlit(Color.white), new Vector3(0f, netTop - 0.02f, 0f), new Vector3(0.04f, 0.05f, postZ * 2f));
        }

        static Color FloorColor(string floor)
        {
            switch (floor)
            {
                case "sand": return new Color(0.86f, 0.76f, 0.55f);
                case "snow": return new Color(0.88f, 0.92f, 0.96f);
                case "concrete": return new Color(0.5f, 0.5f, 0.52f);
                case "cobble": return new Color(0.45f, 0.42f, 0.4f);
                case "mud": return new Color(0.42f, 0.32f, 0.22f);
                case "sportfloor": return new Color(0.2f, 0.45f, 0.55f);
                default: return new Color(0.78f, 0.56f, 0.33f);
            }
        }

        /// <summary>Net cord shudder on hard hits (event driven, decays in real time).</summary>
        public void Shake(float amount) => netShake = Mathf.Max(netShake, amount);

        void Update()
        {
            if (netBody == null) return;
            netShake = Mathf.MoveTowards(netShake, 0f, Time.unscaledDeltaTime * 2f);
            float wobble = Mathf.Sin(Time.unscaledTime * 55f) * netShake * 0.06f;
            netBody.localPosition = new Vector3(wobble, 0f, 0f);
        }
    }
}
