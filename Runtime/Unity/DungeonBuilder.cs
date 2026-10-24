using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using UnityEngine;
using UnityEngine.Events;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Scene component that runs the generator and instantiates prefabs for every tile.
    /// Drop it on an empty GameObject, assign a <see cref="DungeonConfig"/> and some prefabs,
    /// then call <see cref="Build"/> (or tick Build On Start).
    ///
    /// Prefab-per-tile is the simplest renderer and works for both 2D and 3D. For large maps you
    /// will eventually want to swap this for a Tilemap (2D) or a chunked mesh/GPU-instanced
    /// renderer (3D); the <see cref="Layout"/> they would read from is identical.
    /// </summary>
    public sealed class DungeonBuilder : MonoBehaviour
    {
        [Header("Generation")]
        public DungeonConfig Config;
        public int Seed = 12345;
        [Tooltip("Pick a fresh random seed every time Build() runs. Turn off to iterate on one layout.")]
        public bool RandomizeSeed = true;
        public bool BuildOnStart = true;

        [Header("World mapping")]
        [Tooltip("World units per grid cell. Locked at 2 m: a 9-cell boss arena is 18 m across, a corridor is a 2 m hall.")]
        public float CellSize = 2f;
        [Tooltip("True: X/Z ground plane (the locked top-down 3D presentation). False: X/Y plane for a 2D prototype.")]
        public bool UseXZPlane = true;

        [Header("Tile prefabs")]
        public GameObject FloorPrefab;
        public GameObject CorridorPrefab;
        public GameObject DoorPrefab;
        [Tooltip("Vault doors. Needs a key component on the player side to open; falls back to DoorPrefab if empty.")]
        public GameObject LockedDoorPrefab;
        public GameObject WallPrefab;
        [Tooltip("Room interiors and hazards. Pillars block sight and movement; low walls block movement only; pits block movement only.")]
        public GameObject PillarPrefab, LowWallPrefab, PitPrefab;
        [Tooltip("Hazard markers by kind, instantiated at each hazard's centre. Hidden ones should render only for the local player's team per MatchState.HazardsVisibleTo.")]
        public GameObject SpikeTrapPrefab, GasVentPrefab, LavaPrefab;

        [Header("Role markers (optional)")]
        public GameObject SpawnMarkerPrefab;
        public GameObject ExtractMarkerPrefab;
        public GameObject LootMarkerPrefab;
        public GameObject VaultMarkerPrefab;
        public GameObject BossMarkerPrefab;
        public GameObject FinalBossMarkerPrefab;
        [Tooltip("Spawned at the final boss room center, disabled. Enable it when the final boss dies.")]
        public GameObject BossGatedExitPrefab;
        public GameObject KeyMarkerPrefab;

        [Header("Events")]
        public UnityEvent<DungeonLayout> OnBuilt;

        public DungeonLayout Layout { get; private set; }

        /// <summary>World positions where squads should be placed, one per Spawn room. Index = team number; in Ring mode teams go clockwise around the arena.</summary>
        public List<Vector3> SpawnPoints { get; } = new List<Vector3>();

        /// <summary>Number of teams this layout supports (one per spawn room).</summary>
        public int TeamCount => SpawnPoints.Count;
        public List<Vector3> ExtractPoints { get; } = new List<Vector3>();

        /// <summary>The disabled exit object in the final boss room. Call <see cref="OpenBossGatedExit"/> when the boss dies.</summary>
        public GameObject BossGatedExit { get; private set; }

        /// <summary>Key marker objects by key id; name them so loot code can map an item to its vault.</summary>
        public Dictionary<int, GameObject> KeyObjects { get; } = new Dictionary<int, GameObject>();

        private Transform _root;

        private void Start()
        {
            if (BuildOnStart) Build();
        }

        [ContextMenu("Build")]
        public void Build()
        {
            if (Config == null)
            {
                Debug.LogError("DungeonBuilder: no DungeonConfig assigned.", this);
                return;
            }

            if (RandomizeSeed) Seed = Random.Range(int.MinValue, int.MaxValue);
            Build(Seed);
        }

        /// <summary>Build a specific seed from this builder's config. Prefer <see cref="Build(DungeonLayout)"/> in a match: the descriptor's settings, not the local asset, decide the dungeon.</summary>
        public void Build(int seed) => Build(DungeonGenerator.Generate(Config.Settings, seed));

        /// <summary>Door objects by cell, so a shut door can be shown shut. Hazard objects by id, so hidden ones can be hidden.</summary>
        public Dictionary<GridPoint, GameObject> DoorObjects { get; } = new Dictionary<GridPoint, GameObject>();
        public Dictionary<int, GameObject> HazardObjects { get; } = new Dictionary<int, GameObject>();

        /// <summary>Build an already generated layout (the server's, or the client's verified rebuild).</summary>
        public void Build(DungeonLayout generated)
        {
            Clear();
            EnsureFallbackPrefabs();
            Seed = generated.Seed;
            Layout = generated;
            int seed = Seed;

            foreach (var w in Layout.Warnings) Debug.LogWarning("DungeonGen: " + w, this);
            var f = Layout.Fairness;
            Debug.Log($"DungeonGen: seed {seed}, {Layout.Rooms.Count} rooms, {Layout.SpawnRooms.Count()} teams, {Layout.BossRooms.Count()} bosses, {Layout.VaultRooms.Count()} vaults, fairness {f.Score:0.00} (extract {f.ExtractRatio:0.00}, final boss {f.FinalBossRatio:0.00}, boss {f.BossRatio:0.00}, spacing {f.SpawnSpacingRatio:0.00})", this);

            _root = new GameObject($"Dungeon_{seed}").transform;
            _root.SetParent(transform, false);

            for (int x = 0; x < Layout.Width; x++)
                for (int y = 0; y < Layout.Height; y++)
                {
                    var prefab = PrefabFor(Layout.Tiles[x, y]);
                    if (prefab == null) continue;
                    var tile = Instantiate(prefab, GridToWorld(new GridPoint(x, y)), Quaternion.identity, _root);
                    if (Layout.Tiles[x, y] == TileType.Door) DoorObjects[new GridPoint(x, y)] = tile;
                }
            foreach (var hz in Layout.Hazards)
            {
                var prefab = hz.Kind == HazardKind.SpikeTrap ? SpikeTrapPrefab : hz.Kind == HazardKind.GasVent ? GasVentPrefab : hz.Kind == HazardKind.Lava ? LavaPrefab : null;
                if (prefab == null) continue;
                var go = Instantiate(prefab, GridToWorld(hz.Bounds.Center), Quaternion.identity, _root);
                go.name = $"Hazard_{hz.Id}_{hz.Kind}";
                go.transform.localScale = new Vector3(hz.Bounds.Width * CellSize, go.transform.localScale.y, hz.Bounds.Height * CellSize);
                HazardObjects[hz.Id] = go;
                if (hz.Hidden) go.SetActive(false);   // the server tells each team what it may see
            }

            SpawnPoints.Clear();
            ExtractPoints.Clear();
            foreach (var room in Layout.Rooms)
            {
                var pos = GridToWorld(room.Center);
                GameObject marker = null;
                switch (room.Type)
                {
                    case RoomType.Spawn: SpawnPoints.Add(pos); marker = SpawnMarkerPrefab; break;
                    case RoomType.Extract: ExtractPoints.Add(pos); marker = ExtractMarkerPrefab; break;
                    case RoomType.Loot: marker = LootMarkerPrefab; break;
                    case RoomType.Vault: marker = VaultMarkerPrefab; break;
                    case RoomType.Boss: marker = BossMarkerPrefab; break;
                    case RoomType.FinalBoss: marker = FinalBossMarkerPrefab; break;
                }
                if (marker != null)
                {
                    var go = Instantiate(marker, pos, Quaternion.identity, _root);
                    go.name = room.Boss != null ? $"{room.Type}_{room.Id}_{room.Boss.DisplayName}" : $"{room.Type}_{room.Id}_tier{room.LootTier}";
                }
            }

            KeyObjects.Clear();
            if (KeyMarkerPrefab != null)
                foreach (var key in Layout.Keys)
                {
                    var go = Instantiate(KeyMarkerPrefab, GridToWorld(key.Cell), Quaternion.identity, _root);
                    go.name = $"Key_{key.KeyId}_opens_vault_{key.VaultRoomId}" + (key.DroppedByBoss ? "_bossdrop" : "");
                    KeyObjects[key.KeyId] = go;
                }

            BossGatedExit = null;
            if (BossGatedExitPrefab != null && Layout.BossExit != null)
            {
                BossGatedExit = Instantiate(BossGatedExitPrefab, GridToWorld(Layout.BossExit.Cell), Quaternion.identity, _root);
                BossGatedExit.name = "BossGatedExit";
                BossGatedExit.SetActive(false);
            }

            OnBuilt?.Invoke(Layout);
        }

        /// <summary>Reveal the guaranteed exit. Hook this to the final boss's death event.</summary>
        public void OpenBossGatedExit()
        {
            if (BossGatedExit != null) BossGatedExit.SetActive(true);
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            if (_root != null)
            {
                if (Application.isPlaying) Destroy(_root.gameObject);
                else DestroyImmediate(_root.gameObject);
                _root = null;
            }
            Layout = null;
        }

        /// <summary>A simulator position (cell units, cell centres at .5) to world. Vec2.Of(cell) lands exactly on GridToWorld(cell).</summary>
        public Vector3 SimToWorld(DungeonGen.Core.Combat.Vec2 p) => UseXZPlane ? new Vector3((p.X - 0.5f) * CellSize, 0f, (p.Y - 0.5f) * CellSize) : new Vector3((p.X - 0.5f) * CellSize, (p.Y - 0.5f) * CellSize, 0f);
        public DungeonGen.Core.Combat.Vec2 WorldToSim(Vector3 w) => new DungeonGen.Core.Combat.Vec2(w.x / CellSize + 0.5f, (UseXZPlane ? w.z : w.y) / CellSize + 0.5f);

        /// <summary>Show a door shut or open. The server state decides; this only draws it.</summary>
        public void SetDoorClosed(GridPoint cell, bool closed)
        {
            if (!DoorObjects.TryGetValue(cell, out var go) || go == null) return;
            var s = go.transform.localScale; go.transform.localScale = new Vector3(s.x, closed ? CellSize * 1.5f : Mathf.Max(0.1f, CellSize * 0.1f), s.z);
            go.transform.position = GridToWorld(cell) + Vector3.up * (closed ? CellSize * 0.75f : 0f);
        }

        private static Material _fallbackMat;
        /// <summary>Cubes and quads for anything with no prefab, so a fresh project builds a walkable greybox with nothing assigned.</summary>
        public void EnsureFallbackPrefabs()
        {
            GameObject Make(string name, PrimitiveType type, Vector3 scale, Color colour, bool collider = true)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.localScale = scale;
                if (!collider) { var c = go.GetComponent<Collider>(); if (c != null) Destroy(c); }
                var r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    var m = new Material(shader) { color = colour };
                    r.sharedMaterial = m;
                }
                go.SetActive(false);
                go.hideFlags = HideFlags.HideInHierarchy;
                return go;
            }
            float c = CellSize;
            if (FloorPrefab == null) FloorPrefab = Make("Floor", PrimitiveType.Cube, new Vector3(c, 0.1f, c), new Color(0.35f, 0.35f, 0.38f));
            if (CorridorPrefab == null) CorridorPrefab = Make("Corridor", PrimitiveType.Cube, new Vector3(c, 0.1f, c), new Color(0.28f, 0.28f, 0.3f));
            if (DoorPrefab == null) DoorPrefab = Make("Door", PrimitiveType.Cube, new Vector3(c, 0.1f, c), new Color(0.55f, 0.4f, 0.2f));
            if (LockedDoorPrefab == null) LockedDoorPrefab = Make("LockedDoor", PrimitiveType.Cube, new Vector3(c, c * 1.5f, c), new Color(0.8f, 0.6f, 0.1f));
            if (WallPrefab == null) WallPrefab = Make("Wall", PrimitiveType.Cube, new Vector3(c, c * 1.5f, c), new Color(0.18f, 0.18f, 0.2f));
            if (PillarPrefab == null) PillarPrefab = Make("Pillar", PrimitiveType.Cube, new Vector3(c * 0.9f, c * 1.5f, c * 0.9f), new Color(0.25f, 0.22f, 0.3f));
            if (LowWallPrefab == null) LowWallPrefab = Make("LowWall", PrimitiveType.Cube, new Vector3(c, c * 0.5f, c), new Color(0.3f, 0.3f, 0.34f));
            if (PitPrefab == null) PitPrefab = Make("Pit", PrimitiveType.Cube, new Vector3(c, 0.02f, c), new Color(0.02f, 0.02f, 0.04f), false);
            if (SpikeTrapPrefab == null) SpikeTrapPrefab = Make("SpikeTrap", PrimitiveType.Cube, new Vector3(1f, 0.15f, 1f), new Color(0.8f, 0.2f, 0.5f), false);
            if (GasVentPrefab == null) GasVentPrefab = Make("GasVent", PrimitiveType.Cube, new Vector3(1f, 0.15f, 1f), new Color(0.4f, 0.8f, 0.3f), false);
            if (LavaPrefab == null) LavaPrefab = Make("Lava", PrimitiveType.Cube, new Vector3(1f, 0.15f, 1f), new Color(1f, 0.45f, 0.1f), false);
            if (SpawnMarkerPrefab == null) SpawnMarkerPrefab = Make("Spawn", PrimitiveType.Cylinder, new Vector3(c * 0.6f, 0.2f, c * 0.6f), Color.cyan, false);
            if (ExtractMarkerPrefab == null) ExtractMarkerPrefab = Make("Extract", PrimitiveType.Cylinder, new Vector3(c * 0.8f, 0.3f, c * 0.8f), Color.green, false);
            if (LootMarkerPrefab == null) LootMarkerPrefab = Make("Chest", PrimitiveType.Cube, new Vector3(0.8f, 0.6f, 0.6f), new Color(0.9f, 0.75f, 0.2f));
            if (VaultMarkerPrefab == null) VaultMarkerPrefab = Make("Vault", PrimitiveType.Cube, new Vector3(1f, 0.8f, 0.8f), new Color(0.9f, 0.5f, 0.1f));
            if (BossMarkerPrefab == null) BossMarkerPrefab = Make("BossMarker", PrimitiveType.Sphere, new Vector3(1f, 1f, 1f), Color.red, false);
            if (FinalBossMarkerPrefab == null) FinalBossMarkerPrefab = Make("FinalBossMarker", PrimitiveType.Sphere, new Vector3(1.5f, 1.5f, 1.5f), new Color(0.6f, 0f, 0.3f), false);
            if (BossGatedExitPrefab == null) BossGatedExitPrefab = Make("BossExit", PrimitiveType.Cylinder, new Vector3(c, 0.4f, c), Color.white, false);
            if (KeyMarkerPrefab == null) KeyMarkerPrefab = Make("Key", PrimitiveType.Sphere, new Vector3(0.4f, 0.4f, 0.4f), Color.yellow, false);
        }

        public Vector3 GridToWorld(GridPoint p)
        {
            return UseXZPlane
                ? new Vector3(p.X * CellSize, 0f, p.Y * CellSize)
                : new Vector3(p.X * CellSize, p.Y * CellSize, 0f);
        }

        public GridPoint WorldToGrid(Vector3 world)
        {
            float gx = world.x / CellSize;
            float gy = (UseXZPlane ? world.z : world.y) / CellSize;
            return new GridPoint(Mathf.RoundToInt(gx), Mathf.RoundToInt(gy));
        }

        /// <summary>Which room the given world position is inside, or null when in a corridor / outside.</summary>
        public Room RoomAt(Vector3 world)
        {
            if (Layout == null) return null;
            var g = WorldToGrid(world);
            if (!Layout.InBounds(g)) return null;
            int id = Layout.RoomIdAt[g.X, g.Y];
            return id < 0 ? null : Layout.GetRoom(id);
        }

        private GameObject PrefabFor(TileType t)
        {
            switch (t)
            {
                case TileType.RoomFloor: return FloorPrefab;
                case TileType.Corridor: return CorridorPrefab != null ? CorridorPrefab : FloorPrefab;
                case TileType.Door: return DoorPrefab != null ? DoorPrefab : FloorPrefab;
                case TileType.LockedDoor: return LockedDoorPrefab != null ? LockedDoorPrefab : (DoorPrefab != null ? DoorPrefab : FloorPrefab);
                case TileType.Wall: return WallPrefab;
                case TileType.Pillar: return PillarPrefab != null ? PillarPrefab : WallPrefab;
                case TileType.LowWall: return LowWallPrefab != null ? LowWallPrefab : WallPrefab;
                case TileType.Pit: return PitPrefab != null ? PitPrefab : FloorPrefab;
                default: return null;
            }
        }
    }
}
