using System.Collections.Generic;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The way out, made hard to miss.
    /// <list type="bullet">
    /// <item>A tall beacon pillar stands in every extract room, and in the centre once the boss exit opens. Closed, it is a short, dim, cold column; open, it
    /// is a bright, tall one with a bright core, a turning ring on the floor and a point light, and it can be seen over the walls from across the map.
    /// A closed beacon follows the fog of war (nothing where the cell is hidden); an open one is a landmark the whole team was told about, so it always shows.</item>
    /// <item>While someone is extracting, three rings rise around them and a turning swirl column grows with the channel. The local player's comes from their own
    /// <see cref="SelfSnapshot"/> channel; a teammate's from the mirrored state's channels where it has them.</item>
    /// <item>When an exit opens within <see cref="NearCells"/> of the local player, a cue sounds (<c>exit_open</c>, through <see cref="GameAudio"/>) and a line
    /// "An exit opened nearby" shows near the top of the view (through <see cref="WorldBars"/>).</item>
    /// </list>
    /// Everything is read from the mirrored match state and the snapshot, so a remote client sees what the host does.
    /// </summary>
    public sealed class ExtractBeacon : MonoBehaviour
    {
        public DungeonBuilder Builder;
        public ClientWorldView World;
        public DungeonMatchBootstrap Bootstrap;
        public FogOfWar Fog;
        public GameAudio Audio;
        public WorldBars Bars;
        [Tooltip("An exit that opens this close to the local player (in cells) gets the cue and the line.")]
        public float NearCells = 25f;
        public string CueKey = "exit_open", NearText = "An exit opened nearby";
        public float OpenHeight = 12f, ClosedHeight = 3.2f;
        public Color OpenColour = new Color(0.4f, 1f, 0.78f), ClosedColour = new Color(0.4f, 0.52f, 0.62f), BossExitColour = new Color(1f, 0.74f, 0.3f), ChannelColour = new Color(0.62f, 1f, 0.92f);

        /// <summary>One beacon as drawn; read by tests.</summary>
        public struct BeaconInfo { public int Room; public bool BossExit, Open; public float Level, Height, Alpha; public Vector3 Position; }

        private sealed class Beacon
        {
            public int Room; public bool BossExit; public Vector3 Pos; public GridPoint Cell;
            public GameObject Go; public MeshRenderer Pillar, Core, Disc, RingR; public Light Lamp; public MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public float Level, Shown, Phase;
        }

        private sealed class Channel
        {
            public int Id; public GameObject Go; public Transform Follow; public MeshRenderer Column, Base; public MeshRenderer[] Rings = new MeshRenderer[3];
            public MaterialPropertyBlock Block = new MaterialPropertyBlock(); public float Fraction, Shown, Want, Phase;
        }

        private readonly List<Beacon> _beacons = new List<Beacon>();
        private readonly Dictionary<int, Channel> _channels = new Dictionary<int, Channel>();
        private readonly HashSet<int> _extracting = new HashSet<int>();
        private readonly List<int> _scratch = new List<int>();
        private readonly Dictionary<int, float> _fractions = new Dictionary<int, float>();
        private DungeonLayout _builtFor;
        private Transform _root;
        private bool _seenFlags, _prevExtractsOpen, _prevBossOpen;

        public IReadOnlyList<BeaconInfo> Beacons
        {
            get
            {
                var l = new List<BeaconInfo>();
                foreach (var b in _beacons)
                    l.Add(new BeaconInfo { Room = b.Room, BossExit = b.BossExit, Open = b.Level > 0.5f, Level = b.Level, Height = b.Pillar != null ? b.Pillar.transform.localScale.y : 0f, Alpha = b.Shown, Position = b.Pos });
                return l;
            }
        }
        public int BeaconCount => _beacons.Count;
        /// <summary>How many extraction effects are showing, and how many exit-opened cues have fired (for tests).</summary>
        public int ChannelEffects { get { int n = 0; foreach (var c in _channels.Values) if (c.Shown > 0.02f) n++; return n; } }
        public int Cues { get; private set; }
        public float LastCueDistance { get; private set; } = -1f;

        private void Awake() { Resolve(); }

        private void Resolve()
        {
            if (Builder == null) Builder = GetComponent<DungeonBuilder>() ?? FindFirstObjectByType<DungeonBuilder>();
            if (World == null) World = GetComponent<ClientWorldView>() ?? FindFirstObjectByType<ClientWorldView>();
            if (Bootstrap == null) Bootstrap = GetComponent<DungeonMatchBootstrap>() ?? FindFirstObjectByType<DungeonMatchBootstrap>();
            if (Fog == null) Fog = GetComponent<FogOfWar>() ?? FindFirstObjectByType<FogOfWar>();
            if (Audio == null) Audio = GetComponent<GameAudio>() ?? FindFirstObjectByType<GameAudio>();
            if (Bars == null) Bars = GetComponent<WorldBars>() ?? FindFirstObjectByType<WorldBars>();
        }

        private void OnDestroy() { if (_root != null) Destroy(_root.gameObject); }

        private void OnDisable()
        {
            foreach (var b in _beacons) if (b.Go != null) Destroy(b.Go);
            _beacons.Clear(); _builtFor = null;
            foreach (var c in _channels.Values) if (c.Go != null) Destroy(c.Go);
            _channels.Clear();
        }

        private void Update()
        {
            if (Builder == null || World == null || Bootstrap == null || Audio == null || Bars == null) Resolve();
            var st = Bootstrap != null ? Bootstrap.State : null;
            if (Builder == null || Builder.Layout == null || st == null) return;
            if (!ReferenceEquals(Builder.Layout, _builtFor)) Build(Builder.Layout);
            WatchOpenings(st);
            AnimateBeacons(st);
            AnimateChannels(st);
        }

        // ---------------------------------------------------------------- the pillars

        private void Build(DungeonLayout layout)
        {
            foreach (var b in _beacons) if (b.Go != null) Destroy(b.Go);
            _beacons.Clear();
            _builtFor = layout;
            if (_root == null) _root = new GameObject("ExtractBeacons").transform;
            foreach (var room in layout.Rooms)
                if (room.Type == RoomType.Extract) _beacons.Add(MakeBeacon(room.Id, room.Center, false));
            if (layout.BossExit != null) _beacons.Add(MakeBeacon(layout.BossExit.RoomId, layout.Rooms[layout.BossExit.RoomId].Center, true));
        }

        private Beacon MakeBeacon(int room, GridPoint cell, bool boss)
        {
            var b = new Beacon { Room = room, BossExit = boss, Cell = cell, Pos = Builder.GridToWorld(cell), Phase = room * 1.37f };
            b.Go = new GameObject((boss ? "BossExitBeacon_" : "ExtractBeacon_") + room);
            b.Go.transform.SetParent(_root, false);
            b.Go.transform.position = b.Pos + Vector3.up * 0.05f;
            b.Pillar = GlowKit.Visual(b.Go.transform, "Pillar", GlowKit.Crossed, GlowKit.Beam);
            b.Core = GlowKit.Visual(b.Go.transform, "Core", GlowKit.Crossed, GlowKit.Beam);
            b.Disc = GlowKit.Visual(b.Go.transform, "Pool", GlowKit.Flat, GlowKit.Disc);
            b.RingR = GlowKit.Visual(b.Go.transform, "Ring", GlowKit.Flat, GlowKit.Ring);
            b.Disc.transform.localPosition = Vector3.up * 0.04f; b.RingR.transform.localPosition = Vector3.up * 0.08f;
            var lampGo = new GameObject("Lamp");
            lampGo.transform.SetParent(b.Go.transform, false);
            lampGo.transform.localPosition = Vector3.up * 2.4f;
            b.Lamp = lampGo.AddComponent<Light>();
            b.Lamp.type = LightType.Point; b.Lamp.shadows = LightShadows.None; b.Lamp.range = 15f; b.Lamp.intensity = 0f; b.Lamp.enabled = false;
            b.Go.SetActive(false);
            return b;
        }

        private bool IsOpen(MatchState st, Beacon b) => b.BossExit ? st.BossExitOpen : st.ExtractsOpen;

        private void AnimateBeacons(MatchState st)
        {
            float dt = Time.deltaTime;
            foreach (var b in _beacons)
            {
                bool open = IsOpen(st, b);
                var fog = Fog != null ? Fog.StateOf(b.Cell) : FogState.Visible;
                // A closed extract is only drawn where the team's fog allows; a boss exit that is shut is not drawn at all; an open one is a landmark.
                float want = open ? 1f : b.BossExit ? 0f : fog == FogState.Hidden ? 0f : (fog == FogState.Visible ? 1f : 0.55f);
                b.Level = Mathf.MoveTowards(b.Level, open ? 1f : 0f, dt * 0.8f);
                b.Shown = Mathf.MoveTowards(b.Shown, want, dt * 2.5f);
                if (b.Shown <= 0.003f && want <= 0f) { if (b.Go.activeSelf) b.Go.SetActive(false); continue; }
                if (!b.Go.activeSelf) b.Go.SetActive(true);

                float t = Time.time + b.Phase;
                var colour = Color.Lerp(ClosedColour, b.BossExit ? BossExitColour : OpenColour, b.Level);
                float pulse = Mathf.Lerp(0.8f + 0.2f * Mathf.Sin(t * 1.3f), 0.85f + 0.15f * Mathf.Sin(t * 4.2f), b.Level);
                float height = Mathf.Lerp(ClosedHeight, OpenHeight, b.Level);
                float width = Mathf.Lerp(0.9f, 1.7f, b.Level) * (1f + 0.05f * Mathf.Sin(t * 2.1f));
                float alpha = Mathf.Lerp(0.3f, 0.85f, b.Level) * pulse * b.Shown;

                b.Pillar.transform.localScale = new Vector3(width, height, width);
                b.Pillar.transform.localRotation = Quaternion.Euler(0f, t * 18f, 0f);
                GlowKit.Tint(b.Pillar, b.Block, new Color(colour.r, colour.g, colour.b, alpha));
                // the bright core only when open
                b.Core.gameObject.SetActive(b.Level > 0.02f);
                b.Core.transform.localScale = new Vector3(width * 0.32f, height * 1.04f, width * 0.32f);
                b.Core.transform.localRotation = Quaternion.Euler(0f, -t * 40f, 0f);
                GlowKit.Tint(b.Core, b.Block, new Color(1f, 1f, 1f, Mathf.Clamp01(b.Level * 0.9f * pulse) * b.Shown));

                float pool = Mathf.Lerp(3.6f, 6.2f, b.Level);
                b.Disc.transform.localScale = new Vector3(pool, 1f, pool);
                GlowKit.Tint(b.Disc, b.Block, new Color(colour.r, colour.g, colour.b, Mathf.Lerp(0.25f, 0.55f, b.Level) * pulse * b.Shown));
                float ring = Mathf.Lerp(3.2f, 5.2f, b.Level) * (1f + 0.04f * Mathf.Sin(t * 3f));
                b.RingR.transform.localScale = new Vector3(ring, 1f, ring);
                b.RingR.transform.localRotation = Quaternion.Euler(0f, t * 30f, 0f);
                GlowKit.Tint(b.RingR, b.Block, new Color(colour.r, colour.g, colour.b, Mathf.Lerp(0.35f, 0.95f, b.Level) * b.Shown));

                b.Lamp.enabled = b.Level > 0.05f;
                b.Lamp.color = colour; b.Lamp.intensity = 9f * b.Level * b.Shown;
            }
        }

        // ---------------------------------------------------------------- the cue

        private void WatchOpenings(MatchState st)
        {
            if (!_seenFlags) { _seenFlags = true; _prevExtractsOpen = st.ExtractsOpen; _prevBossOpen = st.BossExitOpen; return; }
            bool extracts = st.ExtractsOpen && !_prevExtractsOpen, boss = st.BossExitOpen && !_prevBossOpen;
            _prevExtractsOpen = st.ExtractsOpen; _prevBossOpen = st.BossExitOpen;
            if (!extracts && !boss) return;
            var me = LocalPosition();
            if (!me.HasValue) return;
            float nearest = float.MaxValue;
            foreach (var b in _beacons)
            {
                if (b.BossExit ? !boss : !extracts) continue;
                float d = Vec2.Distance(me.Value, Vec2.Of(b.Cell));
                if (d < nearest) nearest = d;
            }
            if (nearest > NearCells) return;
            Cues++; LastCueDistance = nearest;
            if (Audio != null) Audio.Play2D(CueKey);
            if (Bars != null) Bars.ShowNote(NearText, 4.5f, boss ? new Color(1f, 0.86f, 0.5f) : new Color(0.7f, 1f, 0.88f));
        }

        private Vec2? LocalPosition()
        {
            var snap = World != null ? World.Latest : null;
            if (snap == null) return null;
            foreach (var a in snap.Avatars) if (a.Id == World.LocalPlayerId) return new Vec2(a.X, a.Y);
            return null;
        }

        // ---------------------------------------------------------------- the channel

        private void AnimateChannels(MatchState st)
        {
            _extracting.Clear(); _fractions.Clear();
            int me = World != null ? World.LocalPlayerId : 1;
            var self = World != null ? World.Self : null;
            if (self.HasValue && self.Value.ChannelName == "extract") { _extracting.Add(me); _fractions[me] = self.Value.ChannelFraction; }
            // Teammates: the mirrored state's channels, where it carries them (the host's does; a client's views do not).
            int team = st.PlayerTeam.TryGetValue(me, out int t) ? t : -1;
            float seconds = st.Descriptor != null ? Mathf.Max(1f, st.Descriptor.Rules.ExtractChannelSeconds) : 8f;
            if (team >= 0)
                foreach (var kv in st.Channels)
                {
                    if (kv.Key == me || !st.PlayerTeam.TryGetValue(kv.Key, out int pt) || pt != team) continue;
                    if (kv.Value.room < 0 || kv.Value.room >= st.Layout.Rooms.Count) continue;
                    var type = st.Layout.Rooms[kv.Value.room].Type;
                    if (type != RoomType.Extract && type != RoomType.FinalBoss) continue;
                    _extracting.Add(kv.Key); _fractions[kv.Key] = Mathf.Clamp01((st.Time - kv.Value.started) / seconds);
                }

            foreach (int id in _extracting)
            {
                if (!World.Avatars.TryGetValue(id, out var av) || av == null) continue;
                if (!_channels.TryGetValue(id, out var c) || c.Go == null) { c = MakeChannel(id); _channels[id] = c; }
                c.Follow = av.transform; c.Want = 1f; c.Fraction = _fractions[id];
            }
            _scratch.Clear();
            float dt = Time.deltaTime;
            foreach (var kv in _channels)
            {
                var c = kv.Value;
                if (c.Go == null) { _scratch.Add(kv.Key); continue; }
                if (!_extracting.Contains(kv.Key)) c.Want = 0f;
                c.Shown = Mathf.MoveTowards(c.Shown, c.Want, dt * (c.Want > c.Shown ? 3f : 4f));
                if (c.Shown <= 0.005f && c.Want <= 0f) { _scratch.Add(kv.Key); continue; }
                c.Go.SetActive(true);
                if (c.Follow != null) c.Go.transform.position = new Vector3(c.Follow.position.x, 0.06f, c.Follow.position.z);
                DrawChannel(c);
            }
            foreach (int id in _scratch)
            {
                if (_channels.TryGetValue(id, out var c) && c.Go != null) Destroy(c.Go);
                _channels.Remove(id);
            }
        }

        private Channel MakeChannel(int id)
        {
            if (_root == null) _root = new GameObject("ExtractBeacons").transform;
            var c = new Channel { Id = id, Phase = id * 0.37f };
            c.Go = new GameObject("ExtractChannel_" + id);
            c.Go.transform.SetParent(_root, false);
            c.Column = GlowKit.Visual(c.Go.transform, "Swirl", GlowKit.Crossed, GlowKit.Beam);
            c.Base = GlowKit.Visual(c.Go.transform, "Base", GlowKit.Flat, GlowKit.Disc);
            for (int i = 0; i < c.Rings.Length; i++) c.Rings[i] = GlowKit.Visual(c.Go.transform, "Rise" + i, GlowKit.Flat, GlowKit.Ring);
            return c;
        }

        private void DrawChannel(Channel c)
        {
            float t = Time.time + c.Phase, f = c.Fraction, k = c.Shown;
            var col = ChannelColour;
            // the swirl column: grows with the channel and turns faster as it completes
            float h = Mathf.Lerp(1.8f, 5.2f, f), w = Mathf.Lerp(0.9f, 1.4f, f);
            c.Column.transform.localScale = new Vector3(w, h, w);
            c.Column.transform.localRotation = Quaternion.Euler(0f, t * Mathf.Lerp(120f, 300f, f), 0f);
            GlowKit.Tint(c.Column, c.Block, new Color(col.r, col.g, col.b, (0.3f + 0.45f * f) * k));
            c.Base.transform.localScale = new Vector3(Mathf.Lerp(2.2f, 3.2f, f), 1f, Mathf.Lerp(2.2f, 3.2f, f));
            GlowKit.Tint(c.Base, c.Block, new Color(col.r, col.g, col.b, (0.35f + 0.3f * Mathf.Sin(t * 5f) * 0.5f + 0.15f) * k));
            // three rings rise from the feet, narrowing as they climb and fading at both ends
            for (int i = 0; i < c.Rings.Length; i++)
            {
                float ph = Mathf.Repeat(t * Mathf.Lerp(0.6f, 1.1f, f) + i / (float)c.Rings.Length, 1f);
                float r = Mathf.Lerp(1.9f, 0.7f, ph);
                c.Rings[i].transform.localPosition = new Vector3(0f, ph * Mathf.Lerp(2.2f, 3.4f, f), 0f);
                c.Rings[i].transform.localScale = new Vector3(r * 2f, 1f, r * 2f);
                c.Rings[i].transform.localRotation = Quaternion.Euler(0f, t * 90f * (i % 2 == 0 ? 1f : -1f), 0f);
                float a = Mathf.Sin(ph * Mathf.PI) * (0.7f + 0.3f * f) * k;
                GlowKit.Tint(c.Rings[i], c.Block, new Color(col.r, col.g, col.b, Mathf.Clamp01(a)));
            }
        }
    }
}
