using UnityEngine;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// Graybox geometry and materials built from primitives at runtime. There are no prefabs or hand-made
    /// meshes until art lands. Base materials come from Resources/Graybox: the editor creates them with
    /// the active pipeline's shaders, which is what puts those shaders in the build. Shader.Find alone
    /// returns null on device for shaders no build asset references. If they are missing (a project that
    /// never opened the editor scripts), the runtime picks the pipeline's shaders by name, and finally a
    /// shader every build includes, so a missing asset shows wrong colors instead of a crash.
    /// </summary>
    public static class Graybox
    {
        public const string LitMaterialPath = "Graybox/GrayboxLit";
        public const string UnlitMaterialPath = "Graybox/GrayboxUnlit";

        static Material litBase;
        static Material unlitBase;

        public static Material Lit(Color c) => Make(ref litBase, LitMaterialPath, true, c);

        public static Material Unlit(Color c) => Make(ref unlitBase, UnlitMaterialPath, false, c);

        static Material Make(ref Material baseMaterial, string resource, bool lit, Color c)
        {
            if (baseMaterial == null)
            {
                Material loaded = Resources.Load<Material>(resource);
                baseMaterial = loaded != null ? loaded : new Material(FindShader(lit));
            }
            return new Material(baseMaterial) { color = c };
        }

        /// <summary>The pipeline's shaders when it is URP, the built-in ones otherwise, then always-included fallbacks.</summary>
        public static Shader FindShader(bool lit)
        {
            bool srp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            string[] names = srp
                ? (lit ? new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit" } : new[] { "Universal Render Pipeline/Unlit" })
                : (lit ? new[] { "Standard", "Legacy Shaders/Diffuse" } : new[] { "Unlit/Color" });
            foreach (string n in names)
            {
                Shader s = Shader.Find(n);
                if (s != null) return s;
            }
            foreach (string n in new[] { "Sprites/Default", "UI/Default", "Hidden/InternalErrorShader" })
            {
                Shader s = Shader.Find(n);
                if (s != null) return s;
            }
            throw new System.InvalidOperationException("No usable shader found for graybox materials.");
        }

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static GameObject Box(string name, Transform parent, Material material, Vector3 center, Vector3 size)
        {
            GameObject go = Primitive(PrimitiveType.Cube, name, parent, material);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            return go;
        }

        /// <summary>A flat ring drawn with a LineRenderer in the XY plane (timing rings, landing marks).</summary>
        public static LineRenderer Ring(string name, Transform parent, Color color, float width, int segments = 48)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = segments;
            lr.widthMultiplier = width;
            lr.sharedMaterial = Unlit(color);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            }
            return lr;
        }
    }
}
