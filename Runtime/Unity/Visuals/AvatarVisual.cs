using System.Collections.Generic;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The model side of one avatar: drives its Animator from what the snapshots say (Speed from the drawn ground motion,
    /// Attack/Cast triggers from casts it made, Hit from hits it took, Downed/Dead bools), turns it smoothly toward its
    /// facing, draws a team-coloured ring under its feet and swaps to a ghost material while invisible.
    /// <see cref="ClientWorldView"/> adds it to every library prefab it instantiates; the greybox capsule has none.
    /// </summary>
    public sealed class AvatarVisual : MonoBehaviour
    {
        public static readonly int SpeedId = Animator.StringToHash("Speed"), AttackId = Animator.StringToHash("Attack"), CastId = Animator.StringToHash("Cast"),
            HitId = Animator.StringToHash("Hit"), DeadId = Animator.StringToHash("Dead"), DownedId = Animator.StringToHash("Downed");

        public Animator Animator;
        [Tooltip("Degrees per second the model turns toward its facing.")] public float TurnRate = 720f;
        [Tooltip("Seconds between two hit flinches.")] public float HitCooldown = 0.6f;
        public bool IsBoss;

        /// <summary>The last trigger fired ("Attack", "Cast" or "Hit"), when (Time.time), and how many so far. For tests and debugging.</summary>
        public string LastTrigger { get; private set; }
        public float LastTriggerTime { get; private set; } = -1f;
        public int TriggerCount { get; private set; }
        public float Speed => _speed;
        public Renderer Ring { get; private set; }

        private Quaternion _targetRotation = Quaternion.identity;
        private bool _hasFacing;
        private Vector3 _lastPos;
        private bool _hasLast;
        private float _speed, _lastHit = -10f;
        private bool _invisible;
        private Material _ringMaterial;
        private readonly List<(Renderer r, Material[] normal, Material[] ghost)> _materials = new List<(Renderer, Material[], Material[])>();
        private Material _ghostTemplate;

        public void Init(AvatarVisualLibrary library, AvatarVisualLibrary.Entry entry)
        {
            if (Animator == null) Animator = GetComponentInChildren<Animator>(true);
            if (Animator != null) { Animator.applyRootMotion = false; Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
            IsBoss = entry != null && entry.IsBoss;
            _ghostTemplate = library != null ? library.GhostMaterial : null;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r.gameObject.name != "UltimateAura") _materials.Add((r, r.sharedMaterials, null));
            float radius = entry != null ? entry.RingRadius : 0.6f;
            if (library != null && library.RingMaterial != null) MakeRing(library.RingMaterial, radius);
            _targetRotation = transform.rotation;
        }

        private void MakeRing(Material template, float radius)
        {
            var go = new GameObject("TeamRing");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.03f;
            go.AddComponent<MeshFilter>().sharedMesh = RingMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _ringMaterial = new Material(template);
            mr.sharedMaterial = _ringMaterial;
            go.transform.localScale = Vector3.one * Mathf.Max(0.3f, radius);
            Ring = mr;
        }

        private static Mesh _ringMesh;
        /// <summary>A flat annulus of outer radius 1 (inner 0.78) in the XZ plane, facing up.</summary>
        private static Mesh RingMesh()
        {
            if (_ringMesh != null) return _ringMesh;
            const int n = 48; const float inner = 0.78f;
            var v = new Vector3[n * 2]; var uv = new Vector2[n * 2]; var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = d; v[i * 2 + 1] = d * inner; uv[i * 2] = new Vector2(i / (float)n, 1f); uv[i * 2 + 1] = new Vector2(i / (float)n, 0f);
                int j = (i + 1) % n, t = i * 6;
                tris[t] = i * 2; tris[t + 1] = i * 2 + 1; tris[t + 2] = j * 2;
                tris[t + 3] = j * 2; tris[t + 4] = i * 2 + 1; tris[t + 5] = j * 2 + 1;
            }
            _ringMesh = new Mesh { name = "TeamRing", vertices = v, uv = uv, triangles = tris };
            // Winding above is clockwise seen from +Y (Unity's front face) because sin grows toward +Z; normals point up.
            _ringMesh.RecalculateNormals();
            _ringMesh.RecalculateBounds();
            return _ringMesh;
        }

        /// <summary>The facing the server reported; the model turns toward it in <see cref="LateUpdate"/>.</summary>
        public void SetFacing(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            _targetRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (!_hasFacing) { transform.rotation = _targetRotation; _hasFacing = true; }
        }

        /// <summary>Per snapshot: ring colour, downed/dead, invisible.</summary>
        public void Apply(AvatarSnapshot a, Color ringColour)
        {
            if (_ringMaterial != null)
            {
                var c = a.Downed || a.Dead ? ringColour * 0.5f : ringColour; c.a = 1f;
                if (_ringMaterial.HasProperty("_BaseColor")) _ringMaterial.SetColor("_BaseColor", c); else _ringMaterial.color = c;
            }
            if (Ring != null) Ring.enabled = !a.Dead;
            if (Animator != null && Animator.runtimeAnimatorController != null)
            {
                Animator.SetBool(DeadId, a.Dead);
                Animator.SetBool(DownedId, a.Downed);
            }
            SetInvisible(a.Invisible);
        }

        private void SetInvisible(bool on)
        {
            if (on == _invisible) return;
            _invisible = on;
            for (int i = 0; i < _materials.Count; i++)
            {
                var (r, normal, ghost) = _materials[i];
                if (r == null) continue;
                if (on && _ghostTemplate == null) { r.enabled = false; continue; }
                if (!on) { r.enabled = true; r.sharedMaterials = normal; continue; }
                if (ghost == null)
                {
                    ghost = new Material[normal.Length];
                    for (int k = 0; k < normal.Length; k++)
                    {
                        ghost[k] = new Material(_ghostTemplate);
                        if (normal[k] != null && normal[k].HasProperty("_BaseMap") && ghost[k].HasProperty("_BaseMap")) ghost[k].SetTexture("_BaseMap", normal[k].GetTexture("_BaseMap"));
                    }
                    _materials[i] = (r, normal, ghost);
                }
                r.sharedMaterials = ghost;
            }
        }

        /// <summary>This avatar cast an ability: a basic attack fires Attack, anything else Cast. Unknown ids (mob and boss moves) fire Attack.</summary>
        public void OnCast(string abilityId)
        {
            bool basic = !AbilityBook.TryGet(abilityId ?? "", out var def) || def.Kind == AbilityKind.Basic;
            Fire(basic ? AttackId : CastId, basic ? "Attack" : "Cast");
        }

        /// <summary>This avatar was hit. Flinches at most once per <see cref="HitCooldown"/> and never over its own attack.</summary>
        public void OnHit()
        {
            if (Time.time - _lastHit < HitCooldown) return;
            if (Animator != null && Animator.isActiveAndEnabled)
            {
                var st = Animator.GetCurrentAnimatorStateInfo(0);
                if (st.IsName("Attack") || st.IsName("Cast") || st.IsName("Death")) return;
            }
            _lastHit = Time.time;
            Fire(HitId, "Hit");
        }

        private void Fire(int id, string name)
        {
            LastTrigger = name; LastTriggerTime = Time.time; TriggerCount++;
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            if (id != HitId) Animator.ResetTrigger(HitId);
            Animator.SetTrigger(id);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            var p = transform.position; p.y = 0f;
            if (_hasLast && dt > 0f)
            {
                float raw = Mathf.Min(10f, (p - _lastPos).magnitude / dt);
                _speed = Mathf.Lerp(_speed, raw, 1f - Mathf.Exp(-dt * 10f));
                if (_speed < 0.02f) _speed = 0f;
            }
            _lastPos = p; _hasLast = true;
            if (Animator != null && Animator.runtimeAnimatorController != null) Animator.SetFloat(SpeedId, _speed);
            if (_hasFacing) transform.rotation = Quaternion.RotateTowards(transform.rotation, _targetRotation, TurnRate * dt);
        }

        private void OnDestroy()
        {
            if (_ringMaterial != null) Destroy(_ringMaterial);
            foreach (var (_, _, ghost) in _materials) if (ghost != null) foreach (var g in ghost) if (g != null) Destroy(g);
        }
    }
}
