// Compile-only stand-ins for the slice of the Unity 6 API the game layer uses. Signatures follow the real
// API so the headless build catches typos and type errors in Unity-facing code; behavior is not modeled.
// Never shipped, never referenced by Unity (Unity does not see Headless/).
#pragma warning disable CS0067, CS1591, CA1822, CA2211, CA1805, IDE0060
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static T FindAnyObjectByType<T>() where T : Object => null;
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null);
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }

    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }
        public Transform transform => null;
        public string tag { get; set; }
        public bool activeSelf => true;
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component => null;
        public T GetComponent<T>() => default;
        public static GameObject CreatePrimitive(PrimitiveType type) => null;
    }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
        public T GetComponent<T>() => default;
        public T GetComponentInChildren<T>() => default;
    }

    public class Behaviour : Component { public bool enabled { get; set; } }
    public class MonoBehaviour : Behaviour { }

    public class Transform : Component, System.Collections.IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Transform parent => null;
        public int childCount => 0;
        public Transform GetChild(int index) => null;
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public void LookAt(Vector3 worldPosition) { }
        public System.Collections.IEnumerator GetEnumerator() => null;
    }

    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Rect rect => default;
    }

    public struct Rect : IEquatable<Rect>
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public Vector2 center => new Vector2(x + width / 2, y + height / 2);
        public bool Equals(Rect other) => x == other.x && y == other.y && width == other.width && height == other.height;
        public override bool Equals(object o) => o is Rect r && Equals(r);
        public override int GetHashCode() => 0;
        public static bool operator ==(Rect a, Rect b) => a.Equals(b);
        public static bool operator !=(Rect a, Rect b) => !a.Equals(b);
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public static Vector2 one => new Vector2(1, 1);
        public float magnitude => 0f;
        public static Vector2 ClampMagnitude(Vector2 v, float max) => v;
        public static Vector2 operator +(Vector2 a, Vector2 b) => a;
        public static Vector2 operator -(Vector2 a, Vector2 b) => a;
        public static Vector2 operator *(Vector2 a, float d) => a;
        public static Vector2 operator /(Vector2 a, float d) => a;
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => a;
        public static Vector3 operator -(Vector3 a, Vector3 b) => a;
        public static Vector3 operator *(Vector3 a, float d) => a;
        public static Vector3 operator *(float d, Vector3 a) => a;
    }

    public struct Quaternion
    {
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => default;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f);
        public static Color Lerp(Color a, Color b, float t) => a;
        public static Color operator *(Color a, float b) => a;
        public static bool operator ==(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        public static bool operator !=(Color a, Color b) => !(a == b);
        public override bool Equals(object o) => o is Color c && this == c;
        public override int GetHashCode() => 0;
    }

    public static class ColorUtility { public static bool TryParseHtmlString(string htmlString, out Color color) { color = default; return true; } }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public const float Deg2Rad = PI / 180f;
        public static float Sin(float f) => 0f;
        public static float Cos(float f) => 0f;
        public static float Tan(float f) => 0f;
        public static float Atan(float f) => 0f;
        public static float Exp(float f) => 0f;
        public static float Sqrt(float f) => 0f;
        public static float Abs(float f) => 0f;
        public static float Min(float a, float b) => a;
        public static float Max(float a, float b) => a;
        public static int Max(int a, int b) => a;
        public static int Min(int a, int b) => a;
        public static float Clamp(float v, float min, float max) => v;
        public static float Clamp01(float v) => v;
        public static float Lerp(float a, float b, float t) => a;
        public static float MoveTowards(float current, float target, float maxDelta) => current;
        public static int RoundToInt(float f) => 0;
        public static int Abs(int v) => v;
    }

    public static class Random { public static Vector2 insideUnitCircle => default; }

    public static class Time
    {
        public static float unscaledDeltaTime => 0f;
        public static float unscaledTime => 0f;
    }

    public enum ScreenOrientation { Portrait = 1, PortraitUpsideDown = 2, LandscapeLeft = 3, LandscapeRight = 4, AutoRotation = 5 }

    public static class SleepTimeout { public const int NeverSleep = -1; }

    public static class Screen
    {
        public static int width => 0;
        public static int height => 0;
        public static Rect safeArea => default;
        public static int sleepTimeout { get; set; }
        public static ScreenOrientation orientation { get; set; }
        public static bool autorotateToPortrait { get; set; }
        public static bool autorotateToPortraitUpsideDown { get; set; }
        public static bool autorotateToLandscapeLeft { get; set; }
        public static bool autorotateToLandscapeRight { get; set; }
    }

    public enum RuntimePlatform { OSXEditor = 0, OSXPlayer = 1, WindowsPlayer = 2, WindowsEditor = 7, IPhonePlayer = 8, Android = 11, LinuxEditor = 16 }

    public static class Application
    {
        public static RuntimePlatform platform => RuntimePlatform.OSXEditor;
        public static bool isBatchMode => false;
        public static int targetFrameRate { get; set; }
        public static string persistentDataPath => "";
        public static string dataPath => "";
    }

    public static class QualitySettings
    {
        public static int vSyncCount { get; set; }
        public static Rendering.RenderPipelineAsset renderPipeline { get; set; }
    }

    public static class PlayerPrefs
    {
        public static int GetInt(string key, int defaultValue) => defaultValue;
        public static void SetInt(string key, int value) { }
    }

    public static class Handheld { public static void Vibrate() { } }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogError(object message) { }
        public static void LogWarning(object message) { }
        public static void LogException(Exception exception) { }
    }

    public class TextAsset : Object { public string text => ""; }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
    }

    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }

    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }

    public enum HorizontalWrapMode { Wrap, Overflow }

    public enum VerticalWrapMode { Truncate, Overflow }

    public sealed class Font : Object
    {
        public static Font CreateDynamicFontFromOSFont(string[] fontnames, int size) => null;
    }

    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public enum KeyCode { Space = 32, A = 97, D = 100, I = 105, J = 106, K = 107, L = 108, O = 111, S = 115, U = 117, W = 119, UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276 }

    public static class Input { public static bool GetKey(KeyCode key) => false; }

    public class GUIStyleState { public Color textColor { get; set; } }

    public class GUIStyle
    {
        public GUIStyle(GUIStyle other) { }
        public int fontSize { get; set; }
        public bool wordWrap { get; set; }
        public GUIStyleState normal => null;
    }

    public sealed class GUISkin : Object { public GUIStyle label => null; }

    public static class GUI
    {
        public static GUISkin skin => null;
        public static void Label(Rect position, string text, GUIStyle style) { }
    }

    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject => null; }

    public class Shader : Object { public static Shader Find(string name) => null; }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Material(Material source) { }
        public Color color { get; set; }
        public Shader shader { get; set; }
    }

    public class Renderer : Component
    {
        public Material sharedMaterial { get; set; }
        public bool enabled { get; set; }
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
    }

    public sealed class LineRenderer : Renderer
    {
        public bool useWorldSpace { get; set; }
        public bool loop { get; set; }
        public int positionCount { get; set; }
        public float widthMultiplier { get; set; }
        public void SetPosition(int index, Vector3 position) { }
    }

    public sealed class TrailRenderer : Renderer
    {
        public float time { get; set; }
        public float widthMultiplier { get; set; }
        public float minVertexDistance { get; set; }
        public bool emitting { get; set; }
    }

    public class Collider : Component { }

    public enum CameraClearFlags { Skybox = 1, SolidColor = 2, Depth = 3, Nothing = 4 }

    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public float fieldOfView { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public float aspect => 1f;
        public Vector3 WorldToScreenPoint(Vector3 position) => position;
    }

    public enum LightType { Spot, Directional, Point }
    public enum LightShadows { None, Hard, Soft }

    public sealed class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public LightShadows shadows { get; set; }
    }

    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }

    public sealed class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; }
        public int sortingOrder { get; set; }
    }

    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint) { localPoint = default; return true; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute { public RequireComponent(Type requiredComponent) { } }

    public class AndroidJavaObject : IDisposable
    {
        public AndroidJavaObject(string className, params object[] args) { }
        public void Call(string methodName, params object[] args) { }
        public T Call<T>(string methodName, params object[] args) => default;
        public T GetStatic<T>(string fieldName) => default;
        public void Dispose() { }
    }

    public class AndroidJavaClass : AndroidJavaObject { public AndroidJavaClass(string className) : base(className) { } }

    public sealed class AndroidJavaException : Exception { }
}

namespace UnityEngine.Android
{
    public struct Permission
    {
        public static bool HasUserAuthorizedPermission(string permission) => true;
        public static void RequestUserPermissions(string[] permissions) { }
    }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    public abstract class RenderPipelineAsset : ScriptableObject { }
    public static class GraphicsSettings
    {
        public static RenderPipelineAsset defaultRenderPipeline { get; set; }
        public static RenderPipelineAsset currentRenderPipeline => null;
    }
}

namespace UnityEngine.Rendering.Universal
{
    public abstract class ScriptableRendererData : ScriptableObject { }
    public sealed class UniversalRendererData : ScriptableRendererData { }
    public sealed class UniversalAdditionalCameraData : MonoBehaviour { }
    public sealed class UniversalAdditionalLightData : MonoBehaviour { }

    public sealed class UniversalRenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset
    {
        public static UniversalRenderPipelineAsset Create(ScriptableRendererData rendererData = null) => null;
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string path => "";
        public bool isDirty => false;
        public bool IsValid() => true;
        public GameObject[] GetRootGameObjects() => new GameObject[0];
    }

    public static class SceneManager
    {
        public static Scene GetActiveScene() => default;
        public static bool SetActiveScene(Scene scene) => true;
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public class UnityEvent { public void AddListener(UnityAction call) { } }
}

namespace AOT
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MonoPInvokeCallbackAttribute : Attribute { public MonoPInvokeCallbackAttribute(Type type) { } }
}
