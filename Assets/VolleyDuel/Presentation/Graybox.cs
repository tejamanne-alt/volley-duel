using UnityEngine;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// Graybox geometry and materials built from primitives at runtime. There are no prefabs or
    /// hand-made assets until art lands. URP Lit when available, Unlit for flat effects.
    /// </summary>
    public static class Graybox
    {
        static Shader lit;
        static Shader unlit;

        public static Material Lit(Color c)
        {
            if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            var m = new Material(lit) { color = c };
            return m;
        }

        public static Material Unlit(Color c)
        {
            if (unlit == null) unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Unlit/Color");
            var m = new Material(unlit) { color = c };
            return m;
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
