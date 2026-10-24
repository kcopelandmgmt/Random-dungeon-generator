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
    /// teammate can see them; when the snapshot stops listing one it fades out. Greybox visuals: capsules
    /// coloured by team, a red ring for an armed ultimate's gold aura, a flattened capsule when downed.
    /// </summary>
    public sealed class ClientWorldView : MonoBehaviour
    {
        public DungeonMatchBootstrap Bootstrap;
        public DungeonBuilder Builder;
        public int LocalPlayerId = 1;
        public float InterpolationSeconds = 0.1f;
        public Color AllyColour = new Color(0.3f, 0.7f, 1f), EnemyColour = new Color(1f, 0.3f, 0.3f), MobColour = new Color(0.8f, 0.6f, 0.2f), SelfColour = Color.white, BossColour = new Color(0.7f, 0.1f, 0.4f);

        public TeamSnapshot Latest { get; private set; }
        public SelfSnapshot? Self { get; private set; }
        /// <summary>Avatar id → its object. The local player's is the camera target.</summary>
        public Dictionary<int, GameObject> Avatars { get; } = new Dictionary<int, GameObject>();
        public GameObject LocalAvatar => Avatars.TryGetValue(LocalPlayerId, out var go) ? go : null;

        private readonly Dictionary<int, (Vector3 from, Vector3 to, float at)> _lerp = new Dictionary<int, (Vector3, Vector3, float)>();
        private readonly HashSet<int> _seenThisSnapshot = new HashSet<int>();
        private IMatchTransport _transport;

        public void Attach(IMatchTransport transport)
        {
            _transport = transport;
            transport.SnapshotReceived += bytes => Apply(TeamSnapshot.FromBytes(bytes));
            transport.ViewReceived += _ => SyncDoorsAndHazards();
        }

        public void Apply(TeamSnapshot snap)
        {
            Latest = snap;
            _seenThisSnapshot.Clear();
            foreach (var a in snap.Avatars)
            {
                _seenThisSnapshot.Add(a.Id);
                if (!Avatars.TryGetValue(a.Id, out var go) || go == null) { go = Make(a); Avatars[a.Id] = go; }
                var target = Builder.SimToWorld(new Vec2(a.X, a.Y));
                _lerp[a.Id] = (go.transform.position, target, Time.time);
                go.transform.rotation = Quaternion.LookRotation(new Vector3(a.FacingX, 0f, a.FacingY).sqrMagnitude > 0.001f ? new Vector3(a.FacingX, 0f, a.FacingY) : Vector3.forward, Vector3.up);
                Style(go, a);
            }
            foreach (var id in Avatars.Keys.Where(id => !_seenThisSnapshot.Contains(id)).ToList()) { if (Avatars[id] != null) Destroy(Avatars[id]); Avatars.Remove(id); _lerp.Remove(id); }
            Self = snap.Selves.Count == 0 ? (SelfSnapshot?)null : snap.Selves.FirstOrDefault(s => s.PlayerId == LocalPlayerId);
            if (Self.HasValue && Self.Value.PlayerId != LocalPlayerId) Self = null;
        }

        private void Update()
        {
            foreach (var kv in _lerp)
            {
                if (!Avatars.TryGetValue(kv.Key, out var go) || go == null) continue;
                float t = InterpolationSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - kv.Value.at) / InterpolationSeconds);
                go.transform.position = Vector3.Lerp(kv.Value.from, kv.Value.to, t) + Vector3.up * 1f;
            }
        }

        private GameObject Make(AvatarSnapshot a)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"Avatar_{a.Id}_{a.Label}";
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.transform.localScale = a.IsMob && a.Label != null && BossArchetype.DefaultPool.Any(b => a.Label.StartsWith(b.Name)) || (a.Label ?? "").StartsWith(BossArchetype.FinalBoss.Name) ? new Vector3(2f, 2.5f, 2f) : Vector3.one;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "UltimateAura";
            var rc = ring.GetComponent<Collider>(); if (rc != null) Destroy(rc);
            ring.transform.SetParent(go.transform, false);
            ring.transform.localScale = new Vector3(1.6f, 0.05f, 1.6f);
            ring.transform.localPosition = Vector3.down * 0.95f;
            ring.SetActive(false);
            return go;
        }

        private void Style(GameObject go, AvatarSnapshot a)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                int myTeam = Bootstrap != null && Bootstrap.State != null && Bootstrap.State.PlayerTeam.TryGetValue(LocalPlayerId, out int t) ? t : (Latest != null && Latest.Selves.Count > 0 ? a.Team : -1);
                bool boss = a.IsMob && go.transform.localScale.x > 1.5f;
                var colour = a.Id == LocalPlayerId ? SelfColour : a.IsMob ? (boss ? BossColour : MobColour) : a.Team == myTeam ? AllyColour : EnemyColour;
                if (a.Downed) colour *= 0.5f;
                if (a.Invisible) colour.a = 0.3f;
                if (r.material.color != colour) r.material.color = colour;
            }
            go.transform.localScale = new Vector3(go.transform.localScale.x, a.Downed ? 0.3f : (go.transform.localScale.x > 1.5f ? 2.5f : 1f), go.transform.localScale.z);
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
