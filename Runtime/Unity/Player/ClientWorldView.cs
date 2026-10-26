using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The client's picture of the fight: avatars from the team snapshot (interpolated between ticks),
    /// shut doors and known hazards from the team view. Enemies exist here only while the server says a
    /// teammate can see them; when the snapshot stops listing one it is removed, except a mob that was killed, which stays on the
    /// floor playing its death clip for <see cref="CorpseLingerSeconds"/> and then sinks away. Each avatar is the model its
    /// <see cref="Library"/> names (driven by an <see cref="AvatarVisual"/>: animator, team ring, facing), or, with no
    /// library or no prefab, a greybox capsule coloured by team and flattened when downed. Both carry a gold
    /// "UltimateAura" disc while an ultimate is armed.
    /// </summary>
    public sealed class ClientWorldView : MonoBehaviour
    {
        public DungeonMatchBootstrap Bootstrap;
        public DungeonBuilder Builder;
        public int LocalPlayerId = 1;
        public float InterpolationSeconds = 0.1f;
        [Tooltip("Class, mob and boss models. Empty: every avatar is a greybox capsule.")]
        public AvatarVisualLibrary Library;
        [Tooltip("Seconds a killed mob stays on the floor playing its death clip before it sinks away.")]
        public float CorpseLingerSeconds = 2.5f;
        [Tooltip("Seconds the sinking fade takes after the linger.")]
        public float CorpseFadeSeconds = 0.8f;
        public Color AllyColour = new Color(0.3f, 0.7f, 1f), EnemyColour = new Color(1f, 0.3f, 0.3f), MobColour = new Color(0.8f, 0.6f, 0.2f), SelfColour = Color.white, BossColour = new Color(0.6f, 0.2f, 0.9f);

        public TeamSnapshot Latest { get; private set; }
        /// <summary>Raised after every snapshot is applied, with the avatars already placed. The effects layer draws its casts, hits and zones from here.</summary>
        public event System.Action<TeamSnapshot> SnapshotApplied;
        public SelfSnapshot? Self { get; private set; }
        /// <summary>Avatar id → its object. The local player's is the camera target.</summary>
        public Dictionary<int, GameObject> Avatars { get; } = new Dictionary<int, GameObject>();
        public GameObject LocalAvatar => Avatars.TryGetValue(LocalPlayerId, out var go) ? go : null;

        /// <summary>Ground-plane interpolation per avatar. The capsule's lift off the floor is added once, in Update, never fed back in.</summary>
        private readonly Dictionary<int, (Vector3 from, Vector3 to, float at)> _lerp = new Dictionary<int, (Vector3, Vector3, float)>();
        private readonly Dictionary<int, Vector3> _ground = new Dictionary<int, Vector3>();
        private readonly HashSet<int> _seenThisSnapshot = new HashSet<int>();
        private readonly Dictionary<int, AvatarSnapshot> _last = new Dictionary<int, AvatarSnapshot>();
        private readonly Dictionary<int, float> _lastHitAt = new Dictionary<int, float>();
        /// <summary>Killed mobs the snapshot no longer lists, kept on the floor for their death clip: object, id, when they died, where they lie.</summary>
        private readonly List<(GameObject go, int id, float at, Vector3 pos, Vector3 scale)> _dying = new List<(GameObject, int, float, Vector3, Vector3)>();
        /// <summary>Killed mobs still on the floor (for tests).</summary>
        public IEnumerable<GameObject> DyingAvatars { get { foreach (var d in _dying) if (d.go != null) yield return d.go; } }
        private IMatchTransport _transport;
        private System.Action<byte[]> _onSnapshot, _onView;

        /// <summary>
        /// Listen to a transport. Idempotent: a second Attach to the same transport changes nothing, and attaching to another one
        /// lets go of the first. (The offline host's <c>SendMessage("Attach")</c> to the input controller reaches every component on
        /// the scene's Dungeon object, this one included, after the host already attached it; subscribing twice applied every
        /// snapshot twice and raised <see cref="SnapshotApplied"/> twice per tick.)
        /// </summary>
        public void Attach(IMatchTransport transport)
        {
            if (transport == null || ReferenceEquals(transport, _transport)) return;
            Detach();
            _transport = transport;
            _onSnapshot = bytes => Apply(TeamSnapshot.FromBytes(bytes));
            _onView = _ => SyncDoorsAndHazards();
            transport.SnapshotReceived += _onSnapshot;
            transport.ViewReceived += _onView;
        }

        /// <summary>Stop listening to the attached transport, if any.</summary>
        public void Detach()
        {
            if (_transport == null) return;
            if (_onSnapshot != null) _transport.SnapshotReceived -= _onSnapshot;
            if (_onView != null) _transport.ViewReceived -= _onView;
            _transport = null; _onSnapshot = null; _onView = null;
        }

        /// <summary>How many snapshots have been applied; for tests (one apply per snapshot sent).</summary>
        public int AppliedCount { get; private set; }

        public void Apply(TeamSnapshot snap)
        {
            AppliedCount++;
            Latest = snap;
            _seenThisSnapshot.Clear();
            foreach (var a in snap.Avatars)
            {
                _seenThisSnapshot.Add(a.Id);
                _last[a.Id] = a;
                if (!Avatars.TryGetValue(a.Id, out var go) || go == null) { DropDying(a.Id); go = Make(a); Avatars[a.Id] = go; }
                var target = Builder.SimToWorld(new Vec2(a.X, a.Y));
                _lerp[a.Id] = (_ground.TryGetValue(a.Id, out var shown) ? shown : target, target, Time.time);   // from where it is drawn now, on the ground
                var facing = new Vector3(a.FacingX, 0f, a.FacingY);
                var visual = go.GetComponent<AvatarVisual>();
                if (visual != null) visual.SetFacing(facing);
                else go.transform.rotation = Quaternion.LookRotation(facing.sqrMagnitude > 0.001f ? facing : Vector3.forward, Vector3.up);
                Style(go, a, visual);
            }
            foreach (var e in snap.Effects)
            {
                if (e.Kind == EffectKind.Cast && Avatars.TryGetValue(e.Source, out var caster) && caster != null) caster.GetComponent<AvatarVisual>()?.OnCast(e.Ability);
                else if (e.Kind == EffectKind.Hit && Avatars.TryGetValue(e.Target, out var victim) && victim != null) { victim.GetComponent<AvatarVisual>()?.OnHit(); _lastHitAt[e.Target] = Time.time; }
            }
            foreach (var id in Avatars.Keys.Where(id => !_seenThisSnapshot.Contains(id)).ToList())
            {
                var gone = Avatars[id];
                if (gone != null)
                {
                    if (Killed(id)) StartDying(id, gone);
                    else Destroy(gone);
                }
                Avatars.Remove(id); _lerp.Remove(id); _ground.Remove(id); _models.Remove(id); _last.Remove(id); _lastHitAt.Remove(id);
            }
            // Teammates' channels (looting, opening, praying) come in their self snapshots; anyone not listed is not seen channelling.
            foreach (var a in snap.Avatars)
            {
                if (!Avatars.TryGetValue(a.Id, out var go) || go == null) continue;
                var visual = go.GetComponent<AvatarVisual>();
                if (visual == null) continue;
                string channel = null;
                foreach (var s in snap.Selves) if (s.PlayerId == a.Id) { channel = a.Dead || a.Downed ? null : s.ChannelName; break; }
                visual.SetChannel(channel);
            }
            Self = snap.Selves.Count == 0 ? (SelfSnapshot?)null : snap.Selves.FirstOrDefault(s => s.PlayerId == LocalPlayerId);
            if (Self.HasValue && Self.Value.PlayerId != LocalPlayerId) Self = null;
            SnapshotApplied?.Invoke(snap);
        }

        /// <summary>
        /// Did this avatar leave the snapshot because it died? The server drops a mob the tick it dies (it leaves no corpse), so the
        /// client infers it: a mob last seen dead, nearly dead, or hit within the last 1.5 s. A mob that walked out of sight
        /// unhurt simply vanishes, as before.
        /// </summary>
        private bool Killed(int id)
        {
            if (!_last.TryGetValue(id, out var a) || !a.IsMob) return false;
            return a.Dead || a.HealthFraction <= 0.05f || (_lastHitAt.TryGetValue(id, out float t) && Time.time - t < 1.5f);
        }

        private void StartDying(int id, GameObject go)
        {
            go.name += " (dead)";
            var visual = go.GetComponent<AvatarVisual>();
            var anim = visual != null && visual.Animator != null ? visual.Animator : go.GetComponentInChildren<Animator>();
            if (anim != null && anim.runtimeAnimatorController != null) { anim.SetBool(AvatarVisual.DeadId, true); anim.SetFloat(AvatarVisual.SpeedId, 0f); }
            if (visual != null && visual.Ring != null) visual.Ring.enabled = false;
            var aura = go.transform.Find("UltimateAura"); if (aura != null) aura.gameObject.SetActive(false);
            if (visual == null) go.transform.localScale = new Vector3(go.transform.localScale.x, 0.3f, go.transform.localScale.z);   // the capsule lies down
            var ground = _ground.TryGetValue(id, out var g) ? g : go.transform.position;
            _dying.Add((go, id, Time.time, ground, go.transform.localScale));
        }

        private void DropDying(int id)
        {
            for (int i = _dying.Count - 1; i >= 0; i--)
                if (_dying[i].id == id) { if (_dying[i].go != null) Destroy(_dying[i].go); _dying.RemoveAt(i); }
        }

        /// <summary>Linger, then sink through the floor and shrink away.</summary>
        private void UpdateDying()
        {
            for (int i = _dying.Count - 1; i >= 0; i--)
            {
                var d = _dying[i];
                if (d.go == null) { _dying.RemoveAt(i); continue; }
                float age = Time.time - d.at;
                if (age < CorpseLingerSeconds) continue;
                float f = CorpseFadeSeconds <= 0f ? 1f : Mathf.Clamp01((age - CorpseLingerSeconds) / CorpseFadeSeconds);
                if (f >= 1f) { Destroy(d.go); _dying.RemoveAt(i); continue; }
                float k = f * f;
                d.go.transform.position = d.pos + Vector3.down * (k * 1.5f);
                d.go.transform.localScale = d.scale * (1f - 0.6f * k);
            }
        }

        private void OnDestroy()
        {
            Detach();
            foreach (var d in _dying) if (d.go != null) Destroy(d.go);
            _dying.Clear();
        }

        private void Update()
        {
            UpdateDying();
            foreach (var kv in _lerp)
            {
                if (!Avatars.TryGetValue(kv.Key, out var go) || go == null) continue;
                float t = InterpolationSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - kv.Value.at) / InterpolationSeconds);
                var ground = Vector3.Lerp(kv.Value.from, kv.Value.to, t);
                _ground[kv.Key] = ground;
                // A Unity capsule is two units tall at scale 1 and centred, so its half-height is its Y scale: lift by that and the feet sit on the floor at any size.
                // A model prefab is normalised with its feet at its origin.
                go.transform.position = _models.Contains(kv.Key) ? ground : ground + Vector3.up * go.transform.localScale.y;
            }
        }

        private readonly HashSet<int> _models = new HashSet<int>();

        /// <summary>A boss by its label: an archetype name from the pool, or the final boss.</summary>
        public static bool IsBossLabel(AvatarSnapshot a) => a.IsMob && a.Label != null && (BossArchetype.DefaultPool.Any(b => a.Label.StartsWith(b.Name)) || a.Label.StartsWith(BossArchetype.FinalBoss.Name));

        private GameObject Make(AvatarSnapshot a)
        {
            var entry = Library != null ? Library.Resolve(a) : null;
            if (entry != null)
            {
                GameObject model = null;
                try { model = MakeModel(a, entry); }
                catch (System.Exception ex) { Debug.LogWarning($"DungeonGen: avatar model for {a.Label} failed ({ex.Message}); using the capsule."); if (model != null) Destroy(model); model = null; }
                if (model != null) { _models.Add(a.Id); return model; }
            }
            _models.Remove(a.Id);
            return MakeCapsule(a);
        }

        private GameObject MakeModel(AvatarSnapshot a, AvatarVisualLibrary.Entry entry)
        {
            var go = Instantiate(entry.Prefab);
            go.name = $"Avatar_{a.Id}_{a.Label}";
            if (entry.Scale > 0f && !Mathf.Approximately(entry.Scale, 1f)) go.transform.localScale = go.transform.localScale * entry.Scale;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            var visual = go.GetComponent<AvatarVisual>();
            if (visual == null) visual = go.AddComponent<AvatarVisual>();
            visual.Init(Library, entry);
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "UltimateAura";
            var rc = ring.GetComponent<Collider>(); if (rc != null) Destroy(rc);
            ring.transform.SetParent(go.transform, false);
            float d = Mathf.Max(0.6f, entry.RingRadius) * 2.4f;
            ring.transform.localScale = new Vector3(d, 0.02f, d);
            ring.transform.localPosition = Vector3.up * 0.02f;
            var rr = ring.GetComponent<Renderer>();
            rr.sharedMaterial = new Material(AvatarShader());
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.SetActive(false);
            return go;
        }

        private GameObject MakeCapsule(AvatarSnapshot a)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"Avatar_{a.Id}_{a.Label}";
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.transform.localScale = IsBossLabel(a) ? new Vector3(2f, 2.5f, 2f) : Vector3.one;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "UltimateAura";
            var rc = ring.GetComponent<Collider>(); if (rc != null) Destroy(rc);
            ring.transform.SetParent(go.transform, false);
            ring.transform.localScale = new Vector3(1.6f, 0.05f, 1.6f);
            ring.transform.localPosition = Vector3.down * 0.95f;
            ring.SetActive(false);
            // CreatePrimitive's default material is stripped from player builds (it renders magenta): give both an explicit URP Lit material.
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true)) rend.sharedMaterial = new Material(AvatarShader());
            return go;
        }

        private static Shader _avatarShader;
        private static Shader AvatarShader()
        {
            if (_avatarShader == null) _avatarShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard");
            return _avatarShader;
        }

        /// <summary>The team colour this avatar is drawn with: self, ally, enemy, mob or boss.</summary>
        public Color ColourFor(AvatarSnapshot a)
        {
            int myTeam = Bootstrap != null && Bootstrap.State != null && Bootstrap.State.PlayerTeam.TryGetValue(LocalPlayerId, out int t) ? t : (Latest != null && Latest.Selves.Count > 0 ? a.Team : -1);
            return a.Id == LocalPlayerId ? SelfColour : a.IsMob ? (IsBossLabel(a) ? BossColour : MobColour) : a.Team == myTeam ? AllyColour : EnemyColour;
        }

        private void Style(GameObject go, AvatarSnapshot a, AvatarVisual visual)
        {
            if (visual != null)
            {
                visual.Apply(a, ColourFor(a));
                StyleAura(go, a);
                return;
            }
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                var colour = ColourFor(a);
                if (a.Downed) colour *= 0.5f;
                if (a.Invisible) colour.a = 0.3f;
                if (r.material.color != colour) r.material.color = colour;
            }
            go.transform.localScale = new Vector3(go.transform.localScale.x, a.Downed ? 0.3f : (go.transform.localScale.x > 1.5f ? 2.5f : 1f), go.transform.localScale.z);
            StyleAura(go, a);
        }

        private static void StyleAura(GameObject go, AvatarSnapshot a)
        {
            var aura = go.transform.Find("UltimateAura");
            if (aura != null)
            {
                aura.gameObject.SetActive(a.ArmedUltimate);
                var ar = aura.GetComponent<Renderer>(); if (ar != null && a.ArmedUltimate) ar.material.color = new Color(1f, 0.85f, 0.2f);
            }
        }

        /// <summary>Shut doors and known hazards come from the team view the bootstrap already loaded into its mirror state.</summary>
        public void SyncDoorsAndHazards()
        {
            var state = Bootstrap?.State; if (state == null || Builder == null || Builder.Layout == null) return;
            foreach (var kv in Builder.DoorObjects) Builder.SetDoorClosed(kv.Key, state.ClosedDoors.Contains(kv.Key));
            foreach (var kv in Builder.HazardObjects) if (kv.Value != null) kv.Value.SetActive(state.KnownHazards.Contains(kv.Key) || (state.Index.Hazards.TryGetValue(kv.Key, out var h) && !h.Hidden));
        }
    }
}
