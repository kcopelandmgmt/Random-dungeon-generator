using System;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Client-side prediction for the local avatar on a remote client (2026-10-26 online play beyond the LAN). The core's <see cref="MovePredictor"/>
    /// walks the player with every intent the moment <see cref="ClientTransport"/> sends it, at the server's own speed and collision rules, and each
    /// authoritative snapshot reconciles it (snap over 1.5 cells, otherwise a 100 ms smoothed correction). After the world view has placed every avatar
    /// from the snapshots (interpolated as before), this overrides the local avatar's ground position with the predicted one, before the camera follows it.
    /// Only on a remote client and only the local avatar: the host and the offline round have no latency and keep the plain interpolation.
    /// <c>-noprediction</c> turns it off for comparison.
    /// </summary>
    [DefaultExecutionOrder(-50)]   // LateUpdate before the camera rig's (order 0), after every Update
    public sealed class ClientPrediction : MonoBehaviour
    {
        public const string OffFlag = "-noprediction";

        public ClientWorldView World;
        public DungeonBuilder Builder;
        public DungeonMatchBootstrap Bootstrap;
        [Tooltip("The layout to walk; empty: the bootstrap's mirror state's (or the builder's).")]
        public DungeonLayout Layout;
        [Tooltip("Half of this is added to the round trip: on average an intent waits half a server tick to be applied.")]
        public float ServerTickSeconds = 0.05f;

        /// <summary>The measured or known round trip (seconds), network plus simulated lag.</summary>
        public Func<float> RoundTripSeconds = () => 0f;
        /// <summary>The local player's sheet (the loadout sent with the join).</summary>
        public PlayerStats Sheet;

        public MovePredictor Predictor { get; private set; }
        public bool Active => Predictor != null && Predictor.Started;
        /// <summary>Cells between the drawn avatar and the last authoritative position: how far prediction runs ahead.</summary>
        public float LeadCells { get; private set; }
        public float MaxLeadCells { get; private set; }
        /// <summary>The last authoritative position of the local avatar (cells).</summary>
        public Vec2 Authoritative { get; private set; }
        /// <summary>The position drawn this frame (cells).</summary>
        public Vec2 Shown { get; private set; }
        public int Snapshots { get; private set; }

        private ClientTransport _link;
        private Vec2 _move;
        private uint _lastTick;
        private bool _haveTick;

        public static bool Enabled => !LaunchFlags.Has(OffFlag);

        /// <summary>Start predicting: walk with the intents the link sends, reconcile with the snapshots the world view applies.</summary>
        public void Begin(ClientTransport link, ClientWorldView world, DungeonMatchBootstrap bootstrap, DungeonBuilder builder, PlayerStats sheet, Func<float> roundTrip)
        {
            _link = link; World = world; Bootstrap = bootstrap; Builder = builder; Sheet = sheet;
            if (roundTrip != null) RoundTripSeconds = roundTrip;
            link.IntentSent += OnIntent;
            world.SnapshotApplied += OnSnapshot;
        }

        private void OnDestroy()
        {
            if (_link != null) _link.IntentSent -= OnIntent;
            if (World != null) World.SnapshotApplied -= OnSnapshot;
        }

        private static float Now => Time.unscaledTime;

        /// <summary>The time the server's position is behind the prediction's clock: the round trip and half a server tick.</summary>
        public float LagSeconds => Mathf.Max(0f, RoundTripSeconds()) + ServerTickSeconds * 0.5f;

        private void OnIntent(PlayerIntent intent)
        {
            if (World != null && intent.PlayerId == World.LocalPlayerId) _move = intent.Move;
        }

        private void OnSnapshot(TeamSnapshot snap)
        {
            if (!enabled || World == null) return;
            if (_haveTick && snap.Tick <= _lastTick) return;   // unreliable snapshots may arrive out of order
            _haveTick = true; _lastTick = snap.Tick;
            int me = World.LocalPlayerId;
            var mine = snap.Avatars.FirstOrDefault(a => a.Id == me);
            if (mine.Id != me) return;
            var at = new Vec2(mine.X, mine.Y);
            Authoritative = at;
            Snapshots++;
            if (Predictor == null)
            {
                var layout = Layout ?? (Bootstrap != null && Bootstrap.State != null ? Bootstrap.State.Layout : Builder != null ? Builder.Layout : null);
                if (layout == null || Sheet == null) return;
                Predictor = new MovePredictor(layout, Sheet, me);
            }
            Predictor.Reconcile(Now, at, LagSeconds);
        }

        private MoveGate Gate()
        {
            var st = Bootstrap != null ? Bootstrap.State : null;
            var gate = new MoveGate
            {
                DoorsOpen = st == null || st.DoorsOpen || (Bootstrap.Descriptor != null && Bootstrap.Descriptor.Rules.IsTutorial),
                ClosedDoors = st?.ClosedDoors,
            };
            var latest = World.Latest;
            if (latest != null)
                foreach (var a in latest.Avatars)
                    if (a.Id == World.LocalPlayerId) { gate.DownOrDead = a.Downed || a.Dead; gate.Invisible = a.Invisible; break; }
            var self = World.Self;
            if (self.HasValue)
            {
                gate.Channel = self.Value.ChannelName;
                if (self.Value.Items != null) { gate.LoadKg = self.Value.Items.LoadKg; gate.CapacityKg = self.Value.Items.CapacityKg; }
            }
            return gate;
        }

        private void LateUpdate()
        {
            if (!Active || World == null || Builder == null) return;
            var go = World.LocalAvatar;
            if (go == null) return;
            float now = Now, dt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
            Predictor.Step(now, dt, _move, Gate());
            Shown = Predictor.Shown(now);
            var w = Builder.SimToWorld(Shown);
            var p = go.transform.position;
            go.transform.position = Builder.UseXZPlane ? new Vector3(w.x, p.y, w.z) : new Vector3(w.x, w.y, p.z);
            LeadCells = Vec2.Distance(Shown, Authoritative);
            MaxLeadCells = Mathf.Max(MaxLeadCells, LeadCells);
        }
    }
}
