using System.Collections.Generic;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// How hard the game answers a hit. Everything is driven by the snapshot's <see cref="EffectSnapshot"/> feed (through
    /// <see cref="ClientWorldView.SnapshotApplied"/>), so a remote client feels exactly what the host does and nothing here reads the server.
    /// <list type="bullet">
    /// <item><b>Camera shake</b>: a trauma value (0..1) that hits add to and time drains; the camera rig's <see cref="TopDownCamera.ShakeOffset"/> follows a smooth
    /// noise scaled by it. Small for a hit the local player lands or takes, bigger for a crit, an ultimate (cast or landed near the player) and a boss slam
    /// (a ground-slam or crush wind-up that runs out near the player).</item>
    /// <item><b>Hit-stop</b>: on a crit or ultimate that involves the local player, the animators of both avatars and every playing particle system pause for 40 to 70
    /// ms of real time. Visual only: the server tick, the interpolation and <c>Time.timeScale</c> are never touched, so the simulation cannot change.</item>
    /// </list>
    /// The floating numbers that go with it are drawn by <see cref="WorldBars"/>; <see cref="Classify"/> is shared by both.
    /// </summary>
    public sealed class HitFeel : MonoBehaviour
    {
        public ClientWorldView World;
        public TopDownCamera Rig;

        [Header("Camera shake")]
        public bool ShakeEnabled = true;
        [Tooltip("Metres of camera offset at full trauma. The camera sits about 20 m up, so 0.5 m is a strong 3% of the screen height.")]
        public float MaxShakeMetres = 0.5f;
        [Tooltip("Trauma lost per second.")]
        public float TraumaDecay = 2.4f;
        [Tooltip("Noise frequency of the shake.")]
        public float ShakeHertz = 26f;
        [Tooltip("Cells (sim units) a boss slam or another player's ultimate is felt from: full strength at a third of it, none beyond it.")]
        public float FeltCells = 20f;

        [Header("Hit-stop (visual only)")]
        public bool HitStopEnabled = true;
        public float CritStopSeconds = 0.045f, UltimateStopSeconds = 0.07f;
        [Tooltip("At least this long between two hit-stops, so a crit-heavy fight is not one long freeze.")]
        public float MinGapSeconds = 0.22f;

        /// <summary>The trauma now (0..1).</summary>
        public float Trauma { get; private set; }
        /// <summary>Shakes added so far, the largest camera offset drawn (metres), and the last trauma amount added. For tests.</summary>
        public int ShakeEvents { get; private set; }
        public float PeakOffset { get; private set; }
        public float LastShakeAmount { get; private set; }
        public string LastShakeReason { get; private set; }
        public int HitStopEvents { get; private set; }
        public bool Frozen => _freezeUntil > 0f && Time.unscaledTime < _freezeUntil;
        public float LastStopSeconds { get; private set; }
        public int FrozenAnimators => _animators.Count;

        private readonly Dictionary<Animator, float> _animators = new Dictionary<Animator, float>();
        private readonly Dictionary<ParticleSystem, float> _systems = new Dictionary<ParticleSystem, float>();
        private float _freezeUntil, _lastStopAt = -99f;
        private ClientWorldView _hookedWorld;
        private uint _lastTick; private bool _haveTick;
        private sealed class Wind { public float Until, X, Y, LastSeen; public string Name; }
        private readonly Dictionary<int, Wind> _winds = new Dictionary<int, Wind>();
        private readonly List<int> _scratch = new List<int>();
        private float _seed;

        /// <summary>What a hit label says about the blow.</summary>
        public struct HitKind { public bool Crit, Ultimate, Blocked; }

        /// <summary>Crit (the label ends "(crit)"), an ultimate (the label names an ultimate), blocked (no damage landed). Heals are none of those.</summary>
        public static HitKind Classify(EffectSnapshot e)
        {
            var k = new HitKind();
            if (e.Kind != EffectKind.Hit) return k;
            string a = e.Ability ?? "";
            k.Crit = a.EndsWith("(crit)", System.StringComparison.Ordinal);
            k.Blocked = e.Amount <= 0.01f;
            var def = GameAudio.ResolveAbility(a);
            k.Ultimate = def != null && def.Kind == AbilityKind.Ultimate;
            return k;
        }

        private void Awake()
        {
            if (World == null) World = GetComponent<ClientWorldView>() ?? FindFirstObjectByType<ClientWorldView>();
            if (Rig == null) Rig = FindFirstObjectByType<TopDownCamera>();
            _seed = Random.value * 100f;
        }

        private void OnDisable()
        {
            if (_hookedWorld != null) _hookedWorld.SnapshotApplied -= OnSnapshot;
            _hookedWorld = null; _haveTick = false;
            Release();
            Trauma = 0f;
            if (Rig != null) Rig.ShakeOffset = Vector3.zero;
        }

        // ---- feed ----

        private void Update()
        {
            if (World == null) World = FindFirstObjectByType<ClientWorldView>();
            if (World != _hookedWorld)
            {
                if (_hookedWorld != null) _hookedWorld.SnapshotApplied -= OnSnapshot;
                _hookedWorld = World; _haveTick = false;
                if (_hookedWorld != null) _hookedWorld.SnapshotApplied += OnSnapshot;
            }
            if (Rig == null) Rig = FindFirstObjectByType<TopDownCamera>();

            if (_freezeUntil > 0f && Time.unscaledTime >= _freezeUntil) Release();
            CheckSlams();

            float dt = Time.unscaledDeltaTime;
            Trauma = Mathf.Max(0f, Trauma - TraumaDecay * dt);
            if (Rig == null) return;
            if (!ShakeEnabled || Trauma <= 0.001f) { Rig.ShakeOffset = Vector3.zero; return; }
            float amp = Mathf.Pow(Trauma, 1.25f) * MaxShakeMetres;
            float t = Time.unscaledTime * ShakeHertz;
            var off = new Vector3(Noise(t, 0f), Noise(t, 7.3f), Noise(t, 15.1f) * 0.4f) * amp;
            Rig.ShakeOffset = off;
            if (off.magnitude > PeakOffset) PeakOffset = off.magnitude;
        }

        private float Noise(float t, float lane) => Mathf.PerlinNoise(_seed + lane, t * 0.35f) * 2f - 1f;

        private Vec2? LocalPosition()
        {
            var snap = World != null ? World.Latest : null;
            if (snap == null) return null;
            foreach (var a in snap.Avatars) if (a.Id == World.LocalPlayerId) return new Vec2(a.X, a.Y);
            return null;
        }

        private float LocalMaxHealth()
        {
            var self = World != null ? World.Self : null;
            return self.HasValue && self.Value.MaxHealth > 1f ? self.Value.MaxHealth : 100f;
        }

        // ---- reactions ----

        /// <summary>Each snapshot's effects once: the same tick is never felt twice, even if the view applies it twice.</summary>
        private void OnSnapshot(TeamSnapshot snap)
        {
            if (_haveTick && snap.Tick == _lastTick) return;
            _haveTick = true; _lastTick = snap.Tick;
            foreach (var e in snap.Effects) OnEffect(e);
        }

        private void OnEffect(EffectSnapshot e)
        {
            if (World == null) return;
            int me = World.LocalPlayerId;
            switch (e.Kind)
            {
                case EffectKind.Hit: OnHit(e, me); break;
                case EffectKind.Cast: OnCast(e, me); break;
                case EffectKind.Telegraph: OnTelegraph(e); break;
            }
        }

        private void OnHit(EffectSnapshot e, int me)
        {
            var kind = Classify(e);
            bool byMe = e.Source == me, toMe = e.Target == me;
            if (!byMe && !toMe)
            {
                // Someone else's ultimate or crit landing close to us still rumbles a little.
                if (kind.Ultimate) AddNearby(e.X, e.Y, 0.5f, "ultimate nearby");
                return;
            }
            if (kind.Blocked) return;
            float add;
            string why;
            if (kind.Ultimate) { add = 0.75f; why = byMe ? "my ultimate lands" : "ultimate on me"; }
            else if (kind.Crit) { add = toMe ? 0.5f : 0.42f; why = toMe ? "crit on me" : "my crit"; }
            else if (toMe) { add = 0.2f + 0.45f * Mathf.Clamp01(e.Amount / LocalMaxHealth()); why = "hit on me"; }
            else { add = 0.14f; why = "my hit"; }
            AddShake(add, why);
            if (kind.Ultimate) BeginHitStop(UltimateStopSeconds, e.Source, e.Target);
            else if (kind.Crit) BeginHitStop(CritStopSeconds, e.Source, e.Target);
        }

        private void OnCast(EffectSnapshot e, int me)
        {
            if (!AbilityBook.TryGet(e.Ability ?? "", out var def) || def.Kind != AbilityKind.Ultimate) return;
            if (e.Source == me) { AddShake(0.8f, "my ultimate"); BeginHitStop(UltimateStopSeconds, me); }
            else AddNearby(e.X, e.Y, 0.55f, "ultimate cast nearby");
        }

        private void OnTelegraph(EffectSnapshot e)
        {
            string name = e.Ability ?? "";
            if (name != "groundslam" && name != "crush" && name != "overdrive") return;
            if (!_winds.TryGetValue(e.Source, out var w)) _winds[e.Source] = w = new Wind();
            w.Until = e.Until; w.X = e.X2; w.Y = e.Y2; w.Name = name; w.LastSeen = Time.unscaledTime;
        }

        /// <summary>A wind-up that stopped arriving after its landing time was a slam that landed (one that stopped early was interrupted).</summary>
        private void CheckSlams()
        {
            if (_winds.Count == 0) return;
            _scratch.Clear();
            foreach (var kv in _winds) if (Time.unscaledTime - kv.Value.LastSeen > 0.3f) _scratch.Add(kv.Key);
            foreach (int id in _scratch)
            {
                var w = _winds[id]; _winds.Remove(id);
                float now = World != null && World.Latest != null ? World.Latest.Time : 0f;
                if (now >= w.Until - 0.25f) AddNearby(w.X, w.Y, w.Name == "groundslam" ? 0.95f : 0.7f, "boss " + w.Name);
            }
        }

        /// <summary>Shake scaled by how far (in cells) the source is from the local player: full inside a third of <see cref="FeltCells"/>, none beyond it.</summary>
        private void AddNearby(float x, float y, float amount, string why)
        {
            var me = LocalPosition();
            if (!me.HasValue) return;
            float d = Vec2.Distance(me.Value, new Vec2(x, y));
            float k = Mathf.Clamp01(1f - (d - FeltCells / 3f) / (FeltCells * 2f / 3f));
            if (k > 0.02f) AddShake(amount * k, why);
        }

        /// <summary>Add trauma (clamped to 1).</summary>
        public void AddShake(float amount, string why = null)
        {
            if (amount <= 0f) return;
            Trauma = Mathf.Min(1f, Trauma + amount);
            ShakeEvents++; LastShakeAmount = amount; LastShakeReason = why;
        }

        // ---- hit-stop ----

        /// <summary>Pause the animators of these avatars and every playing particle system for <paramref name="seconds"/> of real time. Visual only.</summary>
        public void BeginHitStop(float seconds, params int[] avatarIds)
        {
            if (!HitStopEnabled || seconds <= 0f || World == null) return;
            if (Time.unscaledTime - _lastStopAt < MinGapSeconds && !Frozen) return;
            if (Frozen) { _freezeUntil = Mathf.Max(_freezeUntil, Time.unscaledTime + seconds); return; }
            _lastStopAt = Time.unscaledTime;
            _freezeUntil = Time.unscaledTime + seconds;
            LastStopSeconds = seconds;
            HitStopEvents++;
            foreach (int id in avatarIds)
            {
                if (!World.Avatars.TryGetValue(id, out var go) || go == null) continue;
                foreach (var a in go.GetComponentsInChildren<Animator>(false))
                    if (a != null && !_animators.ContainsKey(a)) { _animators[a] = a.speed; a.speed = 0f; }
            }
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (ps == null || !ps.isPlaying || _systems.ContainsKey(ps)) continue;
                var main = ps.main;
                _systems[ps] = main.simulationSpeed;
                main.simulationSpeed = 0f;
            }
        }

        private void Release()
        {
            foreach (var kv in _animators) if (kv.Key != null) kv.Key.speed = kv.Value;
            _animators.Clear();
            foreach (var kv in _systems)
            {
                if (kv.Key == null) continue;
                var main = kv.Key.main;
                main.simulationSpeed = kv.Value;
            }
            _systems.Clear();
            _freezeUntil = 0f;
        }
    }
}
