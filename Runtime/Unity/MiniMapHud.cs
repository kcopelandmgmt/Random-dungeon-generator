using System;
using System.Collections.Generic;
using DungeonGen.Core;
using DungeonGen.Core.Hud;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Where the uGUI halves of the compass and the minimap come from. They live in the UI assembly (which depends on this one and
    /// on the UI package), so they register here at start-up and <see cref="CompassHud"/> / <see cref="MiniMapHud"/> ask for their view in
    /// Awake. Each factory returns the view's root object, parented under the hud.
    /// </summary>
    public static class MapHudViews
    {
        public static Func<CompassHud, GameObject> Compass;
        public static Func<MiniMapHud, GameObject> MiniMap;
    }

    /// <summary>A player on the map: where (continuous grid cell coordinates, a cell's centre is its integer), which way it faces.</summary>
    public struct MapActor
    {
        public int Id; public Vector2 Cell; public Vector2 Facing; public bool Downed;
        public GridPoint Rounded => new GridPoint(Mathf.RoundToInt(Cell.x), Mathf.RoundToInt(Cell.y));
    }

    /// <summary>What both map components read from the client view: the avatars the team snapshot placed, the mirrored state, live team pings.</summary>
    public static class MapHudData
    {
        /// <summary>Grid coordinates of a world point, continuous (a cell's centre is its integer, as <see cref="DungeonBuilder.GridToWorld"/> lays them).</summary>
        public static Vector2 CellOf(DungeonBuilder b, Vector3 world) => new Vector2(world.x / b.CellSize, (b.UseXZPlane ? world.z : world.y) / b.CellSize);

        /// <summary>The local player: the assigned transform, else the avatar the client view spawned for it.</summary>
        public static bool TryGetSelf(DungeonBuilder builder, Transform local, ClientWorldView world, int localId, out MapActor self)
        {
            self = default;
            if (builder == null) return false;
            var t = local != null ? local : world != null ? world.LocalAvatar != null ? world.LocalAvatar.transform : null : null;
            if (t == null) return false;
            self = new MapActor { Id = localId, Cell = CellOf(builder, t.position), Facing = FacingOf(world, localId, t) };
            return true;
        }

        private static Vector2 FacingOf(ClientWorldView world, int id, Transform t)
        {
            if (world != null && world.Latest != null)
                foreach (var a in world.Latest.Avatars)
                    if (a.Id == id) { var f = new Vector2(a.FacingX, a.FacingY); if (f.sqrMagnitude > 1e-4f) return f.normalized; }
            var fwd = new Vector2(t.forward.x, t.forward.z);
            return fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector2.up;
        }

        /// <summary>The local player's team mates that are still in the round, from their avatars (or the snapshot when the object is missing).</summary>
        public static void Teammates(MatchState st, DungeonBuilder builder, ClientWorldView world, Dictionary<int, Transform> players, int localId, List<MapActor> into)
        {
            into.Clear();
            if (st == null || builder == null || !st.PlayerTeam.TryGetValue(localId, out int team)) return;
            foreach (var kv in st.PlayerTeam)
            {
                int id = kv.Key;
                if (kv.Value != team || id == localId || st.DeadPlayers.Contains(id) || st.ExtractedPlayers.Contains(id) || st.LeftPlayers.Contains(id)) continue;
                var actor = new MapActor { Id = id, Downed = st.Downed.ContainsKey(id) };
                Transform t = null;
                if (players != null) players.TryGetValue(id, out t);
                if (t == null && world != null && world.Avatars.TryGetValue(id, out var go) && go != null) t = go.transform;
                if (t != null) actor.Cell = CellOf(builder, t.position);
                else if (world != null && world.Latest != null && TrySnapshotCell(world.Latest, id, out var c)) actor.Cell = c;
                else continue;
                into.Add(actor);
            }
        }

        private static bool TrySnapshotCell(TeamSnapshot snap, int id, out Vector2 cell)
        {
            foreach (var a in snap.Avatars) if (a.Id == id && !a.IsMob) { cell = new Vector2(a.X - 0.5f, a.Y - 0.5f); return true; }   // sim cell centres sit at .5
            cell = default; return false;
        }

        /// <summary>The clock pings are timed against: the newest snapshot's, else the mirrored state's.</summary>
        public static float Now(MatchState st, ClientWorldView world) => world != null && world.Latest != null ? world.Latest.Time : st != null ? st.Time : 0f;

        /// <summary>Add a ping that arrived on the team channel; ignores anything that is not one and drops the expired.</summary>
        public static void AddPing(List<TeamPing> pings, byte[] bytes, float now)
        {
            if (bytes == null || bytes.Length != 21) return;   // a TeamPing is int, int, byte, int, int, float
            try
            {
                var p = TeamPing.FromBytes(bytes);
                pings.RemoveAll(q => now - q.Time > TeamPing.LifetimeSeconds + 1f);
                pings.Add(p);
            }
            catch (Exception) { /* not a ping */ }
        }

        public static TopDownCamera FindCamera(ref TopDownCamera cached)
        {
            if (cached == null) cached = UnityEngine.Object.FindFirstObjectByType<TopDownCamera>();
            return cached;
        }
    }

    /// <summary>
    /// The minimap in the corner and the larger map overlay (<see cref="FullMapOpen"/>, toggled with M by the input layer). This class
    /// holds what the map knows and the view reads: the explored rooms and the corridors the team has walked or seen
    /// (<see cref="Model"/>, <see cref="Trail"/>), where the local player and the team mates are, the live pings. The uGUI that draws it lives in the UI
    /// assembly (<c>MapMiniMapView</c>), registered through <see cref="MapHudViews"/>. Nothing here touches the server: fog of war is whatever
    /// the mirrored <see cref="MatchState"/> says the team has seen, plus the corridor cells the local client has had in sight.
    /// </summary>
    public sealed class MiniMapHud : MonoBehaviour, IRoundUi
    {
        public DungeonMatchBootstrap Match;
        public DungeonBuilder Builder;
        [Tooltip("The local player's transform. Left empty, the local avatar from World is used once the server has spawned it.")]
        public Transform LocalPlayer;
        public ClientWorldView World;
        public TopDownCamera Camera;
        public int LocalPlayerId = 1;
        public Dictionary<int, Transform> Players = new Dictionary<int, Transform>();
        [Tooltip("Live team pings. Filled from the round's team channel once the transport is attached; expired ones are ignored.")]
        public List<TeamPing> Pings = new List<TeamPing>();
        /// <summary>True while the larger map overlay is open. The input layer (M) toggles it.</summary>
        public bool FullMapOpen { get; set; }
        /// <summary>The old name for <see cref="FullMapOpen"/>.</summary>
        public bool FullMapHeld { get => FullMapOpen; set => FullMapOpen = value; }
        public void ToggleFullMap() => FullMapOpen = !FullMapOpen;
        [Tooltip("How far, in cells, the local client sees corridors from where it stands.")]
        public int CorridorSightCells = 7;

        /// <summary>The root object of the uGUI view (null until the UI assembly's factory has made it).</summary>
        public GameObject View { get; private set; }
        /// <summary>The fog-of-war texture the view paints (1 texel block per cell); published by the view so tests and tools can read what is revealed.</summary>
        public Texture2D FogTexture { get; set; }

        public MatchState State => Match != null ? Match.State : null;
        public DungeonLayout Layout => State != null ? State.Layout : null;
        /// <summary>The local player's team, or -1 before the first view has arrived.</summary>
        public int Team { get; private set; } = -1;
        /// <summary>Explored rooms, minus any room the model must not show. Rooms the team has never seen are never listed.</summary>
        public MiniMapModel Model { get; private set; }
        public IReadOnlyCollection<int> ExploredRooms => _explored;
        public int RoomCount => Layout != null ? Layout.Rooms.Count : 0;
        /// <summary>Corridor and door cells the local client has had in sight, in the order it saw them.</summary>
        public IReadOnlyList<GridPoint> Trail => _trail;
        /// <summary>Bumps whenever the explored set or the trail grows, or the round changes; the view repaints on a change.</summary>
        public int Revision { get; private set; }
        public bool HasSelf { get; private set; }
        public MapActor Self { get; private set; }
        public List<MapActor> Teammates { get; } = new List<MapActor>();
        public List<TeamPing> LivePings { get; } = new List<TeamPing>();
        public float YawDegrees { get; private set; } = 45f;

        private static readonly IReadOnlyCollection<int> NoRooms = new int[0];
        private IReadOnlyCollection<int> _explored = NoRooms;
        private readonly List<GridPoint> _trail = new List<GridPoint>();
        private readonly HashSet<GridPoint> _trailSet = new HashSet<GridPoint>();
        private MatchState _stateSeen; private int _exploredCount = -1; private GridPoint _lastCell = new GridPoint(int.MinValue, 0);
        private float _modelAt = -10f; private bool _modelDirty = true; private int _dataFrame = -1;
        private IMatchTransport _transport;

        private void Awake() { TryMakeView(); }

        private void OnDestroy() { if (_transport != null) _transport.TeamMessageReceived -= OnTeamMessage; }

        private void LateUpdate() { TryMakeView(); Tick(); }

        private void TryMakeView()
        {
            if (View == null && MapHudViews.MiniMap != null) View = MapHudViews.MiniMap(this);
        }

        public void SetLocalPlayerId(int id) => LocalPlayerId = id;

        /// <summary>The round's transport: team pings arrive on it.</summary>
        public void Attach(IMatchTransport transport)
        {
            if (_transport != null) _transport.TeamMessageReceived -= OnTeamMessage;
            _transport = transport;
            if (transport != null) transport.TeamMessageReceived += OnTeamMessage;
        }

        private void OnTeamMessage(byte[] bytes) => MapHudData.AddPing(Pings, bytes, MapHudData.Now(State, World));

        /// <summary>Refresh everything the view reads, once per frame however many callers ask.</summary>
        public void Tick()
        {
            if (_dataFrame == Time.frameCount) return;
            _dataFrame = Time.frameCount;
            var st = State;
            if (st == null || Builder == null) { HasSelf = false; return; }
            if (!ReferenceEquals(st, _stateSeen))
            {
                _stateSeen = st; _trail.Clear(); _trailSet.Clear(); _lastCell = new GridPoint(int.MinValue, 0);
                _exploredCount = -1; _modelDirty = true; Model = null; Revision++;
            }
            var cam = Camera != null ? Camera : MapHudData.FindCamera(ref Camera);
            YawDegrees = cam != null ? cam.YawDegrees : 45f;
            Team = st.PlayerTeam.TryGetValue(LocalPlayerId, out int team) ? team : -1;
            _explored = Team >= 0 && st.Explored.TryGetValue(Team, out var seen) ? seen : NoRooms;
            if (_explored.Count != _exploredCount) { _exploredCount = _explored.Count; _modelDirty = true; Revision++; }

            HasSelf = MapHudData.TryGetSelf(Builder, LocalPlayer, World, LocalPlayerId, out var me);
            Self = me;
            MapHudData.Teammates(st, Builder, World, Players, LocalPlayerId, Teammates);
            LivePings.Clear();
            float now = MapHudData.Now(st, World);
            foreach (var p in Pings) if (p.Team == Team && now - p.Time <= TeamPing.LifetimeSeconds) LivePings.Add(p);

            if (HasSelf)
            {
                var cell = me.Rounded;
                if (cell != _lastCell) { _lastCell = cell; RevealAround(st, cell); }
            }
            if (Team >= 0 && (_modelDirty || Time.unscaledTime - _modelAt > 0.25f))
            {
                _modelAt = Time.unscaledTime; _modelDirty = false;
                Model = MiniMap.Build(st, LocalPlayerId, HasSelf ? me.Rounded : st.Layout.Rooms[st.Descriptor.TeamSpawnRooms[Team]].Center, null, null, now);   // players and pings come live, not from the model
            }
        }

        /// <summary>Corridors are not rooms, so the state never marks them seen: remember the corridor and door cells the local player has had a clear line to.</summary>
        private void RevealAround(MatchState st, GridPoint cell)
        {
            var layout = st.Layout;
            if (!layout.InBounds(cell)) return;
            foreach (var c in LineOfSight.VisibleCells(layout, cell, Mathf.Max(2, CorridorSightCells), st.ClosedDoors))
            {
                var t = layout.GetTile(c);
                if ((t == TileType.Corridor || t == TileType.Door || t == TileType.LockedDoor) && _trailSet.Add(c)) { _trail.Add(c); Revision++; }
            }
        }
    }
}
