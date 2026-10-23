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

        /// <summary>Build a specific seed. Call this on clients with the seed the server chose.</summary>
        public void Build(int seed)
        {
            Clear();
            Seed = seed;
            Layout = DungeonGenerator.Generate(Config.Settings, seed);

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
                    Instantiate(prefab, GridToWorld(new GridPoint(x, y)), Quaternion.identity, _root);
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
