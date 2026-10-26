using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// A model door on a door tile. <see cref="DungeonBuilder.SetDoorClosed"/> keeps its greybox contract (the tile object
    /// stands tall and raised when shut, flat on the floor when open), so this component undoes that scale and lift for its
    /// children every frame: the floor slab and the door leaf keep their authored size, and the leaf sinks into the floor
    /// when the door opens. The leaf's design pose is captured from the prefab (root at the cell, scale 1).
    /// </summary>
    public sealed class DoorVisual : MonoBehaviour
    {
        public Transform Leaf;
        public Transform Floor;
        [Tooltip("How far the leaf sinks when open, metres.")] public float SinkDepth = 2.8f;
        [Tooltip("Seconds to open or shut.")] public float Seconds = 0.35f;

        private Vector3 _leafPos, _leafScale, _floorPos, _floorScale;
        private float _baseY, _open, _target;
        private bool _captured;
        private Renderer[] _leafRenderers;

        private void Awake() => Capture();

        private void Capture()
        {
            if (_captured) return;
            _captured = true;
            _baseY = transform.position.y;
            if (Leaf != null) { _leafPos = Leaf.localPosition; _leafScale = Leaf.localScale; _leafRenderers = Leaf.GetComponentsInChildren<Renderer>(true); }
            if (Floor != null) { _floorPos = Floor.localPosition; _floorScale = Floor.localScale; }
        }

        /// <summary>Called after the builder has set the tile's scale and lift. <paramref name="baseY"/> is the cell's floor height.</summary>
        public void Apply(bool closed, float baseY, bool instant = false)
        {
            Capture();
            _baseY = baseY;
            _target = closed ? 0f : 1f;
            if (instant || !Application.isPlaying) _open = _target;
            Layout();
        }

        private void LateUpdate()
        {
            if (!Mathf.Approximately(_open, _target)) _open = Mathf.MoveTowards(_open, _target, Time.deltaTime / Mathf.Max(0.01f, Seconds));
            Layout();
        }

        private void Layout()
        {
            var s = transform.localScale;
            float lift = transform.position.y - _baseY;
            Place(Floor, _floorPos, _floorScale, 0f, s, lift);
            Place(Leaf, _leafPos, _leafScale, -SinkDepth * _open, s, lift);
            if (_leafRenderers != null) { bool show = _open < 0.98f; foreach (var r in _leafRenderers) if (r != null) r.enabled = show; }
        }

        private static void Place(Transform t, Vector3 pos, Vector3 scale, float dy, Vector3 s, float lift)
        {
            if (t == null) return;
            float sx = Mathf.Abs(s.x) < 1e-4f ? 1f : s.x, sy = Mathf.Abs(s.y) < 1e-4f ? 1f : s.y, sz = Mathf.Abs(s.z) < 1e-4f ? 1f : s.z;
            t.localPosition = new Vector3(pos.x / sx, (pos.y + dy - lift) / sy, pos.z / sz);
            t.localScale = new Vector3(scale.x / sx, scale.y / sy, scale.z / sz);
        }
    }
}
