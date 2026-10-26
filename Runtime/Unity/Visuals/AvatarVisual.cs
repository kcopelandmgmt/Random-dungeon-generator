using System.Collections.Generic;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The model side of one avatar: drives its Animator from what the snapshots say (Speed from the drawn ground motion,
    /// Attack/Cast triggers from casts it made, Hit from hits it took, Downed/Dead bools, Channel while it loots or prays), turns
    /// it smoothly toward its facing, draws a team-coloured ring under its feet and swaps to a ghost material while invisible.
    /// A <see cref="Humanoid"/> model runs an <c>AnimationSetup</c> role controller: the ground velocity in the model's own frame
    /// (MoveX, MoveZ, divided by the avatar's human scale) picks walk, run, strafe or back-pedal from a directional blend, LocoRate
    /// speeds the cycle up past the run clip's own speed, actions play full body while standing and on the upper-body layer
    /// (weighted by speed) while moving, so a swing on the move keeps the legs running. It never writes <c>Animator.speed</c>, which <c>HitFeel</c> owns.
    /// <see cref="ClientWorldView"/> adds it to every library prefab it instantiates; the greybox capsule has none.
    /// </summary>
    public sealed class AvatarVisual : MonoBehaviour
    {
        public static readonly int SpeedId = Animator.StringToHash("Speed"), AttackId = Animator.StringToHash("Attack"), CastId = Animator.StringToHash("Cast"),
            HitId = Animator.StringToHash("Hit"), DeadId = Animator.StringToHash("Dead"), DownedId = Animator.StringToHash("Downed");
        public static readonly int MoveXId = Animator.StringToHash("MoveX"), MoveZId = Animator.StringToHash("MoveZ"), LocoRateId = Animator.StringToHash("LocoRate"),
            VariantId = Animator.StringToHash("Variant"), ChannelId = Animator.StringToHash("Channel"), ChannelPrayId = Animator.StringToHash("ChannelPray"),
            AttackUId = Animator.StringToHash("AttackU"), CastUId = Animator.StringToHash("CastU"), HitUId = Animator.StringToHash("HitU"),
            MoveSpeedId = Animator.StringToHash("MoveSpeed");
        /// <summary>Channel kinds drawn kneeling in prayer; every other channel (door, chest, corpse, key, hazard) is drawn as reaching out to work it.</summary>
        public static readonly string[] PrayerChannels = { "shrine", "revive", "extract" };

        public Animator Animator;
        [Tooltip("Degrees per second the model turns toward its facing.")] public float TurnRate = 720f;
        [Tooltip("Seconds between two hit flinches.")] public float HitCooldown = 0.6f;
        public bool IsBoss;
        [Tooltip("The Animator runs an AnimationSetup role controller (Humanoid, directional locomotion, action layers).")] public bool Humanoid;
        [Tooltip("The forward run clip's own root speed in the avatar's human scale; past it the cycle plays faster.")] public float RunClipSpeed = 3.6f;

        /// <summary>The last trigger fired ("Attack", "Cast" or "Hit"), when (Time.time), and how many so far. For tests and debugging.</summary>
        public string LastTrigger { get; private set; }
        public float LastTriggerTime { get; private set; } = -1f;
        public int TriggerCount { get; private set; }
        public float Speed => _speed;
        /// <summary>The ground velocity fed to the blend: model-local X (right) and Z (forward), in the avatar's human scale per second.</summary>
        public Vector2 LocalMove => _move;
        /// <summary>The channel this avatar is drawn working (a channel kind such as "chest" or "shrine"), or null.</summary>
        public string Channel { get; private set; }
        public Renderer Ring { get; private set; }

        private Quaternion _targetRotation = Quaternion.identity;
        private bool _hasFacing;
        private Vector3 _lastPos;
        private bool _hasLast;
        private float _speed, _lastHit = -10f, _moving;
        private Vector3 _velocity;
        private Vector2 _move;
        private int _attacks, _casts;
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

        /// <summary>The channel this avatar is working (from its team's self snapshot), or null when it is not channelling.</summary>
        public void SetChannel(string kind)
        {
            if (string.IsNullOrEmpty(kind)) kind = null;
            if (kind == Channel) return;
            Channel = kind;
            if (!Humanoid || Animator == null || Animator.runtimeAnimatorController == null) return;
            Animator.SetBool(ChannelId, kind != null);
            Animator.SetBool(ChannelPrayId, kind != null && System.Array.IndexOf(PrayerChannels, kind) >= 0);
        }

        /// <summary>Is any layer playing (or blending into) a state with this tag: "Attack", "Cast", "Hit", "Death", "Channel", "Locomotion" or "Empty"?</summary>
        public bool IsPlaying(string tag)
        {
            if (Animator == null || !Animator.isActiveAndEnabled || Animator.runtimeAnimatorController == null) return false;
            for (int l = 0; l < Animator.layerCount; l++)
            {
                if (Animator.GetCurrentAnimatorStateInfo(l).IsTag(tag)) return true;
                if (Animator.IsInTransition(l) && Animator.GetNextAnimatorStateInfo(l).IsTag(tag)) return true;
            }
            return false;
        }

        /// <summary>This avatar was hit. Flinches at most once per <see cref="HitCooldown"/> and never over its own attack.</summary>
        public void OnHit()
        {
            if (Time.time - _lastHit < HitCooldown) return;
            if (Animator != null && Animator.isActiveAndEnabled)
            {
                if (Humanoid) { if (IsPlaying("Attack") || IsPlaying("Cast") || IsPlaying("Death")) return; }
                else
                {
                    var st = Animator.GetCurrentAnimatorStateInfo(0);
                    if (st.IsName("Attack") || st.IsName("Cast") || st.IsName("Death")) return;
                }
            }
            _lastHit = Time.time;
            Fire(HitId, "Hit");
        }

        private void Fire(int id, string name)
        {
            LastTrigger = name; LastTriggerTime = Time.time; TriggerCount++;
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            if (!Humanoid)
            {
                if (id != HitId) Animator.ResetTrigger(HitId);
                Animator.SetTrigger(id);
                return;
            }
            // Two variants per kind, alternated, so a string of basic attacks is not one swing on repeat. The upper-body layer always
            // takes the action (its weight follows speed); the base layer takes it full body only while standing, so a trigger it
            // cannot use is never left pending to fire when the body stops.
            if (id == AttackId) Animator.SetInteger(VariantId, _attacks++ & 1);
            else if (id == CastId) Animator.SetInteger(VariantId, _casts++ & 1);
            int upper = id == AttackId ? AttackUId : id == CastId ? CastUId : HitUId;
            if (id != HitId) { Animator.ResetTrigger(HitId); Animator.ResetTrigger(HitUId); }
            if (_move.magnitude <= MovingSpeed) Animator.SetTrigger(id);
            Animator.SetTrigger(upper);
        }

        /// <summary>Human-scale ground speed above which actions play on the upper body only (the base layer cuts back to Walk past it too).</summary>
        public const float MovingSpeed = 0.6f;

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            var p = transform.position; p.y = 0f;
            if (_hasLast && dt > 0f)
            {
                var raw = Vector3.ClampMagnitude((p - _lastPos) / dt, 10f);
                _velocity = Vector3.Lerp(_velocity, raw, 1f - Mathf.Exp(-dt * 10f));
                _speed = _velocity.magnitude;
                if (_speed < 0.02f) { _speed = 0f; _velocity = Vector3.zero; }
            }
            _lastPos = p; _hasLast = true;
            if (_hasFacing) transform.rotation = Quaternion.RotateTowards(transform.rotation, _targetRotation, TurnRate * dt);
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            Animator.SetFloat(SpeedId, _speed);
            if (Humanoid) DriveHumanoid(dt);
        }

        /// <summary>Directional locomotion in the model's frame, the cycle rate past the run clip, and the action layers' split between full and upper body.</summary>
        private void DriveHumanoid(float dt)
        {
            var local = Quaternion.Inverse(transform.rotation) * _velocity;
            float scale = Animator.humanScale * Mathf.Abs(Animator.transform.lossyScale.y);
            if (scale < 0.01f) scale = 1f;
            _move = new Vector2(local.x, local.z) / scale;
            Animator.SetFloat(MoveXId, _move.x);
            Animator.SetFloat(MoveZId, _move.y);
            Animator.SetFloat(MoveSpeedId, _move.magnitude);
            float run = Mathf.Max(0.5f, RunClipSpeed), m = _move.magnitude;
            Animator.SetFloat(LocoRateId, m > run ? Mathf.Min(1.8f, m / run) : 1f);
            // Standing still a swing uses the whole body (base layer); on the move only the upper body, over the running legs.
            _moving = Mathf.MoveTowards(_moving, Mathf.InverseLerp(0.3f, MovingSpeed, m), dt * 6f);
            if (Animator.layerCount >= 2) Animator.SetLayerWeight(1, _moving);
        }

        private void OnDestroy()
        {
            if (_ringMaterial != null) Destroy(_ringMaterial);
            foreach (var (_, _, ghost) in _materials) if (ghost != null) foreach (var g in ghost) if (g != null) Destroy(g);
        }
    }
}
