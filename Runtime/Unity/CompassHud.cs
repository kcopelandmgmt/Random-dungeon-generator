using System.Collections.Generic;
using DungeonGen.Core;
using DungeonGen.Core.Hud;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The compass strip at the top of the screen: what <see cref="CompassBar"/> says is known (the nearest extract, boss rooms, shrines, vaults the team
    /// has seen, team mates and live pings), each with a bearing from the local player. This class holds the markers and the camera's yaw (the strip's
    /// centre); the uGUI that draws them is the UI assembly's <c>MapCompassView</c>, registered through <see cref="MapHudViews"/>. Positions come from the
    /// client view (avatars and the mirrored state), never the server.
    /// </summary>
    public sealed class CompassHud : MonoBehaviour, IRoundUi
    {
        public DungeonMatchBootstrap Match;
        public DungeonBuilder Builder;
        public TopDownCamera Camera;
        [Tooltip("The local player's transform. Left empty, the local avatar from World is used once the server has spawned it.")]
        public Transform LocalPlayer;
        public ClientWorldView World;
        public int LocalPlayerId = 1;
        [Tooltip("Other players' transforms by id, for teammate markers. Left empty, their avatars from World are used.")]
        public Dictionary<int, Transform> Players = new Dictionary<int, Transform>();
        [Tooltip("Live team pings. Filled from the round's team channel once the transport is attached; expired ones are ignored.")]
        public List<TeamPing> Pings = new List<TeamPing>();

        /// <summary>The root object of the uGUI view (null until the UI assembly's factory has made it).</summary>
        public GameObject View { get; private set; }
        /// <summary>The markers to draw, farthest first so the near ones sit on top. Empty until the round has a local player.</summary>
        public List<CompassMarker> Markers { get; } = new List<CompassMarker>();
        /// <summary>The camera's yaw: the bearing straight up the screen.</summary>
        public float YawDegrees { get; private set; } = 45f;
        public bool ExtractsOpen { get; private set; }
        public bool HasData { get; private set; }

        private readonly Dictionary<int, GridPoint> _cells = new Dictionary<int, GridPoint>();
        private readonly List<MapActor> _mates = new List<MapActor>();
        private readonly List<TeamPing> _live = new List<TeamPing>();
        private IMatchTransport _transport;

        private void Awake() { TryMakeView(); }

        private void OnDestroy() { if (_transport != null) _transport.TeamMessageReceived -= OnTeamMessage; }

        private void LateUpdate() { TryMakeView(); Evaluate(); }

        private void TryMakeView()
        {
            if (View == null && MapHudViews.Compass != null) View = MapHudViews.Compass(this);
        }

        public void SetLocalPlayerId(int id) => LocalPlayerId = id;

        /// <summary>The round's transport: team pings arrive on it.</summary>
        public void Attach(IMatchTransport transport)
        {
            if (_transport != null) _transport.TeamMessageReceived -= OnTeamMessage;
            _transport = transport;
            if (transport != null) transport.TeamMessageReceived += OnTeamMessage;
        }

        private void OnTeamMessage(byte[] bytes) => MapHudData.AddPing(Pings, bytes, MapHudData.Now(Match != null ? Match.State : null, World));

        private void Evaluate()
        {
            Markers.Clear(); HasData = false;
            var st = Match != null ? Match.State : null;
            if (st == null || Builder == null) return;
            var cam = Camera != null ? Camera : MapHudData.FindCamera(ref Camera);
            YawDegrees = cam != null ? cam.YawDegrees : 45f;
            if (!MapHudData.TryGetSelf(Builder, LocalPlayer, World, LocalPlayerId, out var me)) return;
            var cell = me.Rounded;
            MapHudData.Teammates(st, Builder, World, Players, LocalPlayerId, _mates);
            _cells.Clear();
            foreach (var m in _mates) _cells[m.Id] = m.Rounded;
            float now = MapHudData.Now(st, World);
            _live.Clear();
            foreach (var p in Pings) if (now - p.Time <= TeamPing.LifetimeSeconds) _live.Add(p);
            ExtractsOpen = st.ExtractsOpen;

            // One extract is enough on a strip this narrow: the nearest the team knows.
            CompassMarker? nearestExtract = null;
            foreach (var m in CompassBar.Markers(st, LocalPlayerId, cell, _cells, _live, now))
            {
                if (m.Kind == CompassMarkerKind.Extract) { if (!nearestExtract.HasValue || m.DistanceCells < nearestExtract.Value.DistanceCells) nearestExtract = m; continue; }
                Markers.Add(m);
            }
            if (nearestExtract.HasValue) Markers.Add(nearestExtract.Value);
            Markers.Sort((a, b) => b.DistanceCells.CompareTo(a.DistanceCells));
            HasData = true;
        }
    }
}
