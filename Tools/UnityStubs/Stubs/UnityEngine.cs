// Minimal stand-ins for the Unity, Input System, Fishnet and UnityEditor APIs this package touches,
// so the Unity layer compiles under plain .NET for a syntax and type check. They do nothing.
// They mirror the real signatures as used; anything not used here is absent.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object { public string name; public HideFlags hideFlags; public static void Destroy(Object o) { } public static void DestroyImmediate(Object o) { } public static T Instantiate<T>(T o, Vector3 p, Quaternion q, Transform parent) where T : Object => o; public static T Instantiate<T>(T o) where T : Object => o; public static T FindFirstObjectByType<T>() where T : Object => null; public static implicit operator bool(Object o) => o != null; }
    public class Component : Object { public GameObject gameObject = new GameObject(); public Transform transform => gameObject.transform; public T GetComponent<T>() where T : class => null; public T GetComponentInChildren<T>() where T : class => null; public void SendMessage(string m, object v, SendMessageOptions o) { } }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject, new() => new T(); }
    public class GameObject : Object { public Transform transform = new Transform(); public GameObject() { } public GameObject(string n) { name = n; } public static GameObject CreatePrimitive(PrimitiveType t) => new GameObject(); public void SetActive(bool b) { } public T GetComponent<T>() where T : class => null; public T AddComponent<T>() where T : Component, new() => new T(); public Component AddComponent(Type t) => null; }
    public class Transform : Component { public Vector3 position, localPosition, localScale = Vector3.one; public Quaternion rotation; public Transform parent; public void SetParent(Transform t, bool w) { } public Transform Find(string n) => null; public void SetPositionAndRotation(Vector3 p, Quaternion q) { } public Vector3 forward => Vector3.forward; }
    public class Renderer : Component { public Material material = new Material(null); public Material sharedMaterial; }
    public class Collider : Component { }
    public class Camera : Component { public Ray ScreenPointToRay(Vector3 s) => new Ray(); }
    public class Light : Component { public LightType type; }
    public enum LightType { Directional }
    public class Material : Object { public Color color; public Material(Shader s) { } }
    public class Shader : Object { public static Shader Find(string n) => new Shader(); }
    public class Texture2D : Object { public Texture2D(int w, int h) { } public void SetPixel(int x, int y, Color c) { } public void Apply() { } }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public enum HideFlags { None, HideInHierarchy }
    public enum SendMessageOptions { RequireReceiver, DontRequireReceiver }
    public enum KeyCode { Tab, Space, F, Q, E, R }
    public enum TextAnchor { UpperLeft, MiddleCenter, MiddleRight, MiddleLeft }
    public enum FontStyle { Normal, Bold }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y); public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y); public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.x * s, a.y * s); public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f); }
    public struct Vector3
    {
        public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0); public static Vector3 one => new Vector3(1, 1, 1); public static Vector3 up => new Vector3(0, 1, 0); public static Vector3 down => new Vector3(0, -1, 0); public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s); public static Vector3 operator *(float s, Vector3 a) => a * s;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t; public static Vector3 ClampMagnitude(Vector3 v, float m) => v; public float sqrMagnitude => x * x + y * y + z * z; public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
    }
    public struct Quaternion { public static Quaternion identity => new Quaternion(); public static Quaternion Euler(float x, float y, float z) => new Quaternion(); public static Quaternion LookRotation(Vector3 f, Vector3 u) => new Quaternion(); public static Vector3 operator *(Quaternion q, Vector3 v) => v; }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; } public static Color white => new Color(1, 1, 1); public static Color black => new Color(0, 0, 0); public static Color red => new Color(1, 0, 0); public static Color green => new Color(0, 1, 0); public static Color yellow => new Color(1, 1, 0); public static Color cyan => new Color(0, 1, 1); public static Color magenta => new Color(1, 0, 1); public static Color gray => new Color(0.5f, 0.5f, 0.5f); public static Color grey => gray; public static Color blue => new Color(0, 0, 1); public static Color clear => new Color(0, 0, 0, 0); public static Color operator *(Color c, float s) => new Color(c.r * s, c.g * s, c.b * s, c.a); public static bool operator ==(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a; public static bool operator !=(Color a, Color b) => !(a == b); public override bool Equals(object o) => o is Color c && c == this; public override int GetHashCode() => 0; }
    public struct Rect { public float x, y, width, height; public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; } public float xMax => x + width; public float yMax => y + height; public Vector2 center => new Vector2(x + width / 2f, y + height / 2f); public Vector2 position => new Vector2(x, y); }
    public struct Ray { public Vector3 GetPoint(float t) => new Vector3(); }
    public struct Plane { public Plane(Vector3 n, Vector3 p) { } public bool Raycast(Ray r, out float t) { t = 0f; return false; } }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); public static int Max(int a, int b) => Math.Max(a, b); public static float Min(float a, float b) => Math.Min(a, b); public static int Min(int a, int b) => Math.Min(a, b); public static float Clamp(float v, float a, float b) => Math.Max(a, Math.Min(b, v)); public static int Clamp(int v, int a, int b) => Math.Max(a, Math.Min(b, v)); public static float Sqrt(float v) => (float)Math.Sqrt(v); public static float Floor(float v) => (float)Math.Floor(v); public static int FloorToInt(float v) => (int)Math.Floor(v); public static int CeilToInt(float v) => (int)Math.Ceiling(v); public static float Atan2(float y, float x) => (float)Math.Atan2(y, x); public const float PI = (float)Math.PI; public const float Rad2Deg = 57.29578f; public const float Deg2Rad = 0.017453292f; public static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v)); public static float Exp(float v) => (float)Math.Exp(v); public static float Abs(float v) => Math.Abs(v); public static int RoundToInt(float v) => (int)Math.Round(v); public static float Lerp(float a, float b, float t) => a + (b - a) * t; }
    public static class Debug { public static void Log(object m, Object c = null) { } public static void LogWarning(object m, Object c = null) { } public static void LogError(object m, Object c = null) { } }
    public static class Random { public static int Range(int a, int b) => a; public static float Range(float a, float b) => a; }
    public static class Time { public static float deltaTime => 0.016f; public static float time => 0f; }
    public static class Screen { public static int width => 1920; public static int height => 1080; }
    public static class Input { public static bool GetKey(KeyCode k) => false; public static Vector3 mousePosition => new Vector3(); }
    public class GUIStyle { public GUIStyle() { } public GUIStyle(GUIStyle o) { } public TextAnchor alignment; public FontStyle fontStyle; public int fontSize; }
    public class GUISkin { public GUIStyle label = new GUIStyle(), box = new GUIStyle(); }
    public class GUIContent { public static GUIContent none = new GUIContent(); }
    public static class GUI { public static Color color; public static GUISkin skin = new GUISkin(); public static void Box(Rect r, GUIContent c) { } public static void Label(Rect r, string s) { } public static void Label(Rect r, string s, GUIStyle st) { } public static void DrawTexture(Rect r, Texture2D t) { } public static void BeginGroup(Rect r) { } public static void EndGroup() { } }
    public static class GUILayout { public static void BeginArea(Rect r, GUIStyle s) { } public static void EndArea() { } public static void Label(string s) { } public static bool Button(string s) => false; }
    public static class Gizmos { public static Color color; public static void DrawWireCube(Vector3 c, Vector3 s) { } public static void DrawLine(Vector3 a, Vector3 b) { } public static void DrawCube(Vector3 c, Vector3 s) { } public static void DrawSphere(Vector3 c, float r) { } public static void DrawWireSphere(Vector3 c, float r) { } }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    public class TextAreaAttribute : Attribute { }
    public class ContextMenu : Attribute { public ContextMenu(string s) { } }
    public class CreateAssetMenuAttribute : Attribute { public string menuName, fileName; }
    public class SerializeField : Attribute { }
    public class RequireComponentAttribute : Attribute { public RequireComponentAttribute(params Type[] t) { } }
    public class ExecuteAlwaysAttribute : Attribute { }
    public class DisallowMultipleComponentAttribute : Attribute { }
    public class AddComponentMenuAttribute : Attribute { public AddComponentMenuAttribute(string s) { } }
    public static class Application { public static bool isPlaying => true; }
    namespace Events { public class UnityEvent<T> { public void Invoke(T v) { } } public class UnityEvent { public void Invoke() { } } }
}
namespace UnityEngine.InputSystem
{
    public enum InputActionType { Value, Button, PassThrough }
    public class InputAction
    {
        public InputAction(string name, InputActionType type = InputActionType.Value, string binding = null) { }
        public CompositeSyntax AddCompositeBinding(string c) => new CompositeSyntax();
        public InputAction AddBinding(string b) => this;
        public void Enable() { } public void Disable() { } public bool IsPressed() => false; public T ReadValue<T>() where T : struct => default;
        public event Action<CallbackContext> performed;
        public struct CallbackContext { }
        public class CompositeSyntax { public CompositeSyntax With(string part, string binding) => this; }
    }
    public class InputActionMap { public InputAction FindAction(string n, bool t) => new InputAction(n); public void Enable() { } }
    public class InputActionAsset : UnityEngine.ScriptableObject { public InputActionMap FindActionMap(string n, bool t) => new InputActionMap(); }
}
namespace FishNet.Connection { public class NetworkConnection { public int ClientId; } }
namespace FishNet.Transporting { public enum Channel { Reliable, Unreliable } }
namespace FishNet.Object
{
    public class NetworkBehaviour : UnityEngine.MonoBehaviour { public bool IsServerInitialized => true; public virtual void OnStopServer() { } }
    public class TargetRpcAttribute : Attribute { public FishNet.Transporting.Channel Channel; }
    public class ServerRpcAttribute : Attribute { public bool RequireOwnership; public FishNet.Transporting.Channel Channel; }
    public class ObserversRpcAttribute : Attribute { }
}
namespace UnityEditor
{
    public static class AssetDatabase { public static T LoadAssetAtPath<T>(string p) where T : UnityEngine.Object => null; public static void CreateAsset(UnityEngine.Object o, string p) { } public static void SaveAssets() { } }
    public static class Selection { public static UnityEngine.GameObject activeGameObject; }
    public class MenuItem : Attribute { public MenuItem(string s) { } }
    public static class EditorUtility { public static void SetDirty(UnityEngine.Object o) { } }
    public static class Handles { public static UnityEngine.Color color; public static void Label(UnityEngine.Vector3 p, string s) { } }
}
