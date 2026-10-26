using System.Collections.Generic;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Makes loot worth looking at. A rarity-coloured beam of light stands on every item lying on the floor (white, blue, yellow, orange, as the item
    /// tooltips colour them; taller for the better ones), and chests and corpses that still hold loot breathe a soft glow with a short shaft until
    /// they are emptied. A chest that has been opened shows the best rarity left in it as a beam. Everything is cheap: two crossed unlit alpha quads
    /// and a flat glow quad per spot, on the shared <see cref="GlowKit"/> material, tinted through a property block.
    /// <para>
    /// It reads only what the local team may know: the mirrored <see cref="MatchState"/> (on a client, what the team's views carried), the explored
    /// rooms, and the snapshot (a dead player's body). The fog of war decides what is drawn: a beam or corpse glow only while its cell is in sight,
    /// a chest glow also dimly from memory once the room is explored; a hidden cell shows nothing. Items carry no position on the wire (only the room
    /// they lie in), so a floor item is drawn at a spot spread around the room's centre that is the same on every machine.
    /// </para>
    /// </summary>
    public sealed class LootBeams : MonoBehaviour
    {
        public DungeonBuilder Builder;
        public ClientWorldView World;
        public DungeonMatchBootstrap Bootstrap;
        public FogOfWar Fog;
        [Tooltip("Seconds between two looks at the match state.")]
        public float RefreshSeconds = 0.2f;
        [Tooltip("Beam height in metres for Normal, Magic, Rare, Unique.")]
        public float[] Heights = { 1.7f, 3.3f, 4.4f, 6.2f };
        [Tooltip("Beam width in metres for Normal, Magic, Rare, Unique.")]
        public float[] Widths = { 0.5f, 0.65f, 0.8f, 1.05f };
        public Color ChestGold = new Color(0.878f, 0.69f, 0.341f), CorpseGlow = new Color(0.72f, 0.62f, 1f);
        public bool Enabled = true;

        public enum SpotKind { Item, Chest, Corpse }

        /// <summary>One lit spot as it is drawn now; read by tests.</summary>
        public struct SpotInfo
        {
            public SpotKind Kind; public int Id, Room; public Rarity Rarity; public bool HasBeam, Opened; public float Strength, Height; public Color Colour; public Vector3 Position;
        }

        private sealed class Spot
        {
            public string Key; public SpotInfo Info; public GameObject Go; public MeshRenderer Beam, Glow; public MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public float Shown, Phase, Want;
        }

        private readonly Dictionary<string, Spot> _spots = new Dictionary<string, Spot>();
        private readonly Dictionary<string, SpotInfo> _want = new Dictionary<string, SpotInfo>();
        private readonly Dictionary<int, Vector3> _bodies = new Dictionary<int, Vector3>();
        private readonly List<string> _scratch = new List<string>();
        private readonly List<int> _ids = new List<int>();
        private float _next;
        private Transform _root;

        /// <summary>Every lit spot with some strength, as drawn.</summary>
        public IEnumerable<SpotInfo> Spots { get { foreach (var s in _spots.Values) if (s.Shown > 0.01f) { var i = s.Info; i.Strength = s.Shown; yield return i; } } }
        public int SpotCount { get { int n = 0; foreach (var s in _spots.Values) if (s.Shown > 0.01f) n++; return n; } }
        public int BeamCount { get { int n = 0; foreach (var s in _spots.Values) if (s.Shown > 0.01f && s.Info.HasBeam) n++; return n; } }
        public bool Has(SpotKind kind, int id) { foreach (var s in _spots.Values) if (s.Shown > 0.01f && s.Info.Kind == kind && s.Info.Id == id) return true; return false; }

        /// <summary>Colours as the item tooltips and stash use them (<c>MainMenuUi.RarityColour</c>).</summary>
        public static Color RarityColour(Rarity r)
        {
            switch (r)
            {
                case Rarity.Unique: return new Color(0.95f, 0.55f, 0.20f);
                case Rarity.Rare: return new Color(0.95f, 0.85f, 0.35f);
                case Rarity.Magic: return new Color(0.45f, 0.62f, 1f);
                default: return new Color(0.93f, 0.885f, 0.79f);
            }
        }

        private void Awake() { Resolve(); }

        private void Resolve()
        {
            if (Builder == null) Builder = GetComponent<DungeonBuilder>() ?? FindFirstObjectByType<DungeonBuilder>();
            if (World == null) World = GetComponent<ClientWorldView>() ?? FindFirstObjectByType<ClientWorldView>();
            if (Bootstrap == null) Bootstrap = GetComponent<DungeonMatchBootstrap>() ?? FindFirstObjectByType<DungeonMatchBootstrap>();
            if (Fog == null) Fog = GetComponent<FogOfWar>() ?? FindFirstObjectByType<FogOfWar>();
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
        }

        private void OnDisable()
        {
            foreach (var s in _spots.Values) if (s.Go != null) Destroy(s.Go);
            _spots.Clear();
        }

        private void Update()
        {
            if (Builder == null || World == null || Bootstrap == null) Resolve();
            if (Time.time >= _next)
            {
                _next = Time.time + Mathf.Max(0.05f, RefreshSeconds);
                Refresh();
            }
            Animate();
        }

        /// <summary>Look at the match state now instead of at the next tick.</summary>
        public void Refresh()
        {
            _want.Clear();
            var st = Bootstrap != null ? Bootstrap.State : null;
            if (Enabled && st != null && Builder != null && Builder.Layout != null) Gather(st);
            Reconcile();
        }

        // ---------------------------------------------------------------- what is lit

        private int LocalTeam(MatchState st) => st.PlayerTeam.TryGetValue(World != null ? World.LocalPlayerId : 1, out int t) ? t : -1;

        private FogState FogAt(Vector3 world)
        {
            if (Fog == null) return FogState.Visible;
            return Fog.StateOf(Builder.WorldToGrid(world));
        }

        private void Gather(MatchState st)
        {
            var layout = Builder.Layout;
            int team = LocalTeam(st);
            if (team < 0 || !st.Explored.TryGetValue(team, out var seen)) return;
            RememberBodies();

            // Floor items, by room: spread around the room's centre in id order.
            var byRoom = new Dictionary<int, List<int>>();
            foreach (var kv in st.FloorItems)
            {
                if (!seen.Contains(kv.Value) || st.DestroyedItems.Contains(kv.Key) || st.ItemOwner.ContainsKey(kv.Key)) continue;
                if (kv.Value < 0 || kv.Value >= layout.Rooms.Count) continue;
                if (!byRoom.TryGetValue(kv.Value, out var list)) byRoom[kv.Value] = list = new List<int>();
                list.Add(kv.Key);
            }
            foreach (var kv in byRoom)
            {
                kv.Value.Sort();
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    int id = kv.Value[i];
                    var pos = FloorSpot(layout, layout.Rooms[kv.Key], i);
                    if (FogAt(pos) != FogState.Visible) continue;                  // out of sight: nothing, however well the room is known
                    var rarity = RarityOf(st, id);
                    AddBeam($"i{id}", SpotKind.Item, id, kv.Key, rarity, pos, 1f, false);
                }
            }

            // Chests: Loot rooms, opened vaults, boss rooms once the boss is down. Lit while closed (it may hold something), and once opened for as long as items remain.
            foreach (var room in layout.Rooms)
            {
                if (!seen.Contains(room.Id)) continue;
                bool candidate = room.Type == RoomType.Loot
                    || (room.Type == RoomType.Vault && st.OpenVaults.Contains(room.Id))
                    || (room.IsBossRoom && st.DeadBosses.Contains(room.Id));
                if (!candidate) continue;
                var pos = Builder.GridToWorld(room.Center);
                var fog = FogAt(pos);
                if (fog == FogState.Hidden) continue;
                float strength = fog == FogState.Visible ? 1f : 0.3f;
                if (!st.OpenedContainers.Contains(room.Id))
                {
                    AddGlow($"c{room.Id}", SpotKind.Chest, room.Id, room.Id, ChestGold, pos, strength, false);
                    continue;
                }
                bool any = false; Rarity best = Rarity.Normal;
                foreach (int id in st.VisibleItemsIn(room.Id))
                {
                    if (st.FloorItems.ContainsKey(id) || st.ItemOwner.ContainsKey(id) || st.DestroyedItems.Contains(id)) continue;   // on the floor: its own beam; taken: gone
                    any = true;
                    var r = RarityOf(st, id);
                    if (r > best) best = r;
                }
                if (any) AddBeam($"c{room.Id}", SpotKind.Chest, room.Id, room.Id, best, pos, strength, true);
            }

            // Corpses: lit while unopened, and while an opened one still holds items, silver or a soul. Only while in sight.
            foreach (var kv in st.Corpses)
            {
                int owner = MatchState.CorpseOwner(kv.Key);
                int room = st.PlayerRoom.TryGetValue(owner, out int pr) ? pr : -1;
                if (room < 0 || !seen.Contains(room)) continue;
                var pos = BodyPosition(layout, owner, room);
                if (FogAt(pos) != FogState.Visible) continue;
                bool opened = st.OpenedCorpses.Contains(kv.Key);
                bool holds = !opened || kv.Value.Count > 0 || st.CorpseSilver.ContainsKey(kv.Key) || st.CorpseSouls.Contains(kv.Key);
                if (!holds) continue;
                if (opened && kv.Value.Count > 0)
                {
                    Rarity best = Rarity.Normal;
                    foreach (int id in kv.Value) { var r = RarityOf(st, id); if (r > best) best = r; }
                    AddBeam($"k{kv.Key}", SpotKind.Corpse, kv.Key, room, best, pos, 1f, true);
                }
                else AddGlow($"k{kv.Key}", SpotKind.Corpse, kv.Key, room, CorpseGlow, pos, 1f, opened);
            }
        }

        private void AddBeam(string key, SpotKind kind, int id, int room, Rarity rarity, Vector3 pos, float strength, bool opened)
        {
            int i = Mathf.Clamp((int)rarity, 0, 3);
            _want[key] = new SpotInfo { Kind = kind, Id = id, Room = room, Rarity = rarity, HasBeam = true, Opened = opened, Strength = strength, Height = Heights[Mathf.Min(i, Heights.Length - 1)], Colour = RarityColour(rarity), Position = pos };
        }

        private void AddGlow(string key, SpotKind kind, int id, int room, Color colour, Vector3 pos, float strength, bool opened)
        {
            _want[key] = new SpotInfo { Kind = kind, Id = id, Room = room, Rarity = Rarity.Normal, HasBeam = false, Opened = opened, Strength = strength, Height = kind == SpotKind.Chest ? 1.9f : 1.3f, Colour = colour, Position = pos };
        }

        /// <summary>A body's drawn spot: where the dead player's avatar lay (remembered, so a resurrected player's remains keep their place), else the room's centre.</summary>
        private Vector3 BodyPosition(DungeonLayout layout, int owner, int room)
        {
            if (_bodies.TryGetValue(owner, out var p)) return p;
            return Builder.GridToWorld(layout.Rooms[room].Center);
        }

        private void RememberBodies()
        {
            var snap = World != null ? World.Latest : null;
            if (snap == null) return;
            foreach (var a in snap.Avatars)
                if (!a.IsMob && a.Dead) _bodies[a.Id] = Builder.SimToWorld(new Vec2(a.X, a.Y));
        }

        private Rarity RarityOf(MatchState st, int id)
        {
            if (st.KnownItems.TryGetValue(id, out var info)) return (Rarity)info.Rarity;
            if (st.Index.Items.TryGetValue(id, out var it)) return it.Rarity;
            if (id == LayoutIndex.EventLootId && st.Layout.EventLoot != null) return st.Layout.EventLoot.Rarity;
            var mirrored = ClientMirror.Current != null ? ClientMirror.Current.ItemById(id) : null;
            return mirrored != null ? mirrored.Rarity : Rarity.Normal;
        }

        /// <summary>A floor spot for the nth item lying in a room: a ring around the centre (where a chest stands), on room floor, the same everywhere.</summary>
        private Vector3 FloorSpot(DungeonLayout layout, Room room, int n)
        {
            var centre = Builder.GridToWorld(room.Center);
            float half = Mathf.Max(1f, Mathf.Min(room.Bounds.Width, room.Bounds.Height) * Builder.CellSize * 0.5f - 0.9f);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int k = n + attempt;
                float angle = (k * 2.399963f) + room.Id * 0.7f;
                float radius = Mathf.Min(half, 1.7f + 0.55f * (k % 3));
                var p = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                var cell = Builder.WorldToGrid(p);
                if (layout.InBounds(cell) && layout.Tiles[cell.X, cell.Y] == TileType.RoomFloor) return p;
            }
            return centre;
        }

        // ---------------------------------------------------------------- the objects

        private void Reconcile()
        {
            foreach (var kv in _want)
            {
                if (!_spots.TryGetValue(kv.Key, out var s)) { s = Make(kv.Key); _spots[kv.Key] = s; s.Shown = 0f; }
                bool restyle = s.Info.HasBeam != kv.Value.HasBeam || s.Info.Rarity != kv.Value.Rarity || s.Info.Kind != kv.Value.Kind || s.Go == null;
                s.Info = kv.Value;
                s.Want = kv.Value.Strength;
                if (s.Go != null)
                {
                    s.Go.transform.position = kv.Value.Position;
                    if (restyle) Style(s);
                }
            }
            _scratch.Clear();
            foreach (var kv in _spots) if (!_want.ContainsKey(kv.Key)) _scratch.Add(kv.Key);
            foreach (var key in _scratch) _spots[key].Want = 0f;
        }

        private Spot Make(string key)
        {
            if (_root == null) _root = new GameObject("LootBeams").transform;
            var s = new Spot { Key = key, Phase = Mathf.Abs(key.GetHashCode() % 628) / 100f };
            s.Go = new GameObject("Spot_" + key);
            s.Go.transform.SetParent(_root, false);
            s.Beam = GlowKit.Visual(s.Go.transform, "Beam", GlowKit.Crossed, GlowKit.Beam);
            s.Glow = GlowKit.Visual(s.Go.transform, "Glow", GlowKit.Flat, GlowKit.Disc);
            s.Glow.transform.localPosition = Vector3.up * 0.06f;
            s.Go.SetActive(false);
            return s;
        }

        private void Style(Spot s)
        {
            var info = s.Info;
            s.Beam.gameObject.SetActive(true);
            float width = info.HasBeam ? Widths[Mathf.Clamp((int)info.Rarity, 0, Widths.Length - 1)] : info.Kind == SpotKind.Chest ? 0.55f : 0.45f;
            s.Beam.transform.localScale = new Vector3(width, info.Height, width);
            float glow = info.HasBeam ? 0.9f + 0.35f * (int)info.Rarity : info.Kind == SpotKind.Chest ? 1.9f : 1.5f;
            s.Glow.transform.localScale = new Vector3(glow, 1f, glow);
        }

        private void Animate()
        {
            _scratch.Clear();
            float dt = Time.deltaTime;
            foreach (var kv in _spots)
            {
                var s = kv.Value;
                if (s.Go == null) { _scratch.Add(kv.Key); continue; }
                s.Shown = Mathf.MoveTowards(s.Shown, s.Want, dt * (s.Want > s.Shown ? 4f : 3f));
                if (s.Shown <= 0.005f && s.Want <= 0f) { _scratch.Add(kv.Key); continue; }
                if (!s.Go.activeSelf) { s.Go.SetActive(true); Style(s); }
                var info = s.Info;
                float t = Time.time + s.Phase;
                // Items shimmer quickly, chests and corpses breathe slowly; the better the item, the brighter.
                float pulse = info.HasBeam ? 0.86f + 0.14f * Mathf.Sin(t * 3.1f) : 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * 2.2f));
                float bright = info.HasBeam ? Mathf.Lerp(0.5f, 1f, ((int)info.Rarity) / 3f) : 0.8f;
                var c = info.Colour;
                float beamA = (info.HasBeam ? bright : 0.5f) * pulse * s.Shown;
                GlowKit.Tint(s.Beam, s.Block, new Color(c.r, c.g, c.b, Mathf.Clamp01(beamA)));
                float glowA = (info.HasBeam ? 0.55f : 0.85f) * (info.HasBeam ? 1f : pulse) * s.Shown;
                GlowKit.Tint(s.Glow, s.Block, new Color(c.r, c.g, c.b, Mathf.Clamp01(glowA)));
                if (info.HasBeam)
                {
                    // a slow sway in the pillar's width keeps it alive without moving it
                    float w = Widths[Mathf.Clamp((int)info.Rarity, 0, Widths.Length - 1)] * (1f + 0.08f * Mathf.Sin(t * 2.3f));
                    s.Beam.transform.localScale = new Vector3(w, info.Height, w);
                    s.Beam.transform.localRotation = Quaternion.Euler(0f, t * 25f, 0f);
                }
                else
                {
                    s.Beam.transform.localScale = new Vector3(0.5f, info.Height * (0.9f + 0.1f * pulse), 0.5f);
                    s.Beam.transform.localRotation = Quaternion.Euler(0f, t * 25f, 0f);
                }
            }
            foreach (var key in _scratch)
            {
                if (_spots.TryGetValue(key, out var s) && s.Go != null) Destroy(s.Go);
                _spots.Remove(key);
            }
        }
    }
}
