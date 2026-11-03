using System.Collections.Generic;
using UnityEngine;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// Pooled flash effects for contacts, blocks and landings. Each one is keyed by its sim event, so a
    /// rollback that un-happens an event fades its flash out fast, and an event that newly happened
    /// spawns already aged by how late it is (the event ledger's time offset).
    /// </summary>
    public sealed class EffectsView : MonoBehaviour
    {
        sealed class Flash
        {
            public Transform T;
            public Material M;
            public float Age;
            public float Life;
            public float Size;
            public ulong Key;
            public bool Active;
            public bool Fading;
        }

        readonly List<Flash> pool = new List<Flash>();

        public void Spawn(ulong key, Vector3 at, Color color, float size, float life, float lateSeconds)
        {
            Flash f = null;
            foreach (Flash p in pool)
            {
                if (!p.Active)
                {
                    f = p;
                    break;
                }
            }
            if (f == null)
            {
                f = new Flash();
                f.M = Graybox.Unlit(color);
                f.T = Graybox.Primitive(PrimitiveType.Sphere, "Flash", transform, f.M).transform;
                pool.Add(f);
            }
            f.T.gameObject.SetActive(true);
            f.T.position = at;
            f.M.color = color;
            f.Age = Mathf.Max(0f, lateSeconds);
            f.Life = life;
            f.Size = size;
            f.Key = key;
            f.Active = true;
            f.Fading = false;
        }

        /// <summary>The event no longer happened after a resimulation: fade its effect out fast.</summary>
        public void Orphan(ulong key)
        {
            foreach (Flash p in pool)
            {
                if (p.Active && p.Key == key)
                {
                    p.Fading = true;
                    p.Life = Mathf.Min(p.Life, p.Age + 0.06f);
                }
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (Flash f in pool)
            {
                if (!f.Active) continue;
                f.Age += dt;
                if (f.Age >= f.Life)
                {
                    f.Active = false;
                    f.T.gameObject.SetActive(false);
                    continue;
                }
                float k = f.Age / f.Life;
                float s = f.Fading ? f.Size * (1f - k) : f.Size * (0.4f + 0.6f * Mathf.Sqrt(k)) * (1f - k * k);
                f.T.localScale = Vector3.one * Mathf.Max(0.001f, s);
            }
        }
    }
}
