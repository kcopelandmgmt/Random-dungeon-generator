using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Every sound in a round. It listens to what the local team may know: the effects feed (casts, hits, heals, zones,
    /// telegraphs), the snapshot (who stands where, who is a boss, who died), and the match events the team is allowed to
    /// hear. One-shots play from a small pool of 3D sources at world positions, interface and announcer lines are 2D, and a
    /// music layer crossfades exploration, combat and boss. Nothing here decides anything.
    /// Honours <c>-mute</c>: requests are still recorded in <see cref="RecentKeys"/> (so tests can assert them) but nothing plays.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        public AudioLibrary Library;
        public ClientWorldView World;
        public DungeonBuilder Builder;
        public DungeonMatchBootstrap Bootstrap;
        public AbilityEffects Effects;

        [Header("Mixing")]
        [Tooltip("3D one-shots are full volume within this many metres. The camera is high above the floor, so this is large.")]
        public float MinDistance = 30f;
        public float MaxDistance = 140f;
        [Min(4)] public int Voices = 24;
        [Tooltip("A hit or telegraph involving the local team keeps the combat music on for this long.")]
        public float CombatSeconds = 6f;
        public float CrossfadeSeconds = 1.5f;
        [Tooltip("Metres the local avatar walks between footsteps.")]
        public float StrideMetres = 1.8f;
        public int RecentCapacity = 256;
        [Header("World loops and others' steps")]
        public int MaxHazardLoops = 6;
        public int MaxTorchLoops = 4;
        public float HazardLoopVolume = 0.35f, TorchLoopVolume = 0.18f;
        [Tooltip("Spatial range of the hazard and torch loops (metres from the loop source to the listener).")]
        public float LoopMinDistance = 10f, LoopMaxDistance = 40f;
        [Tooltip("Other avatars' footsteps are heard within this many metres of the listener.")]
        public float OtherStepRange = 15f;
        public float OtherStepVolume = 0.25f;
        public int MaxOtherStepsPerFrame = 2;

        /// <summary>Every sound key requested, newest last (capped). For tests and debugging; recorded even when muted.</summary>
        public IReadOnlyList<string> RecentKeys => _recent;
        public int RequestCount { get; private set; }
        public event Action<string> KeyRequested;
        public bool Muted => LaunchFlags.Has("-mute");
        public string CurrentMusic { get; private set; } = "music_exploration";
        public bool InCombat => Time.time - _lastCombatAt < CombatSeconds;
        public bool BossActive => _bossChannelUntil > Time.time || _inBossRoom;

        private readonly List<string> _recent = new List<string>();
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly Dictionary<AudioSource, float> _startedAt = new Dictionary<AudioSource, float>();
        private AudioSource _ui, _voice, _extractLoop, _ambience, _bossAmbience, _mExplore, _mCombat, _mBoss;
        private readonly Queue<string> _voiceQueue = new Queue<string>();
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        private readonly HashSet<string> _seenZones = new HashSet<string>();
        private readonly HashSet<string> _seenTelegraphs = new HashSet<string>();
        private readonly HashSet<int> _bossesMet = new HashSet<int>();
        private readonly HashSet<int> _deadMobs = new HashSet<int>();
        private readonly HashSet<int> _clearedBossRooms = new HashSet<int>();
        private readonly HashSet<int> _team = new HashSet<int>();
        private TeamSnapshot _teamFor;
        private int _localTeam;
        private float _lastCombatAt = -999f, _bossChannelUntil = -999f;
        private bool _inBossRoom;
        private float _mixExplore = 1f, _mixCombat, _mixBoss, _mixBossAmb;
        private Vector3 _lastStep; private bool _haveStep; private float _stride;
        private DungeonMatchBootstrap _hookedBootstrap; private MatchServer _hookedServer;
        private ClientWorldView _hookedWorld; private AbilityEffects _hookedEffects;
        private bool _loopsStarted, _awake;
        private readonly Dictionary<string, AudioSource> _worldLoops = new Dictionary<string, AudioSource>();
        private readonly List<AudioSource> _loopSpare = new List<AudioSource>();
        private readonly List<(string id, string key, Vector3 pos, float dist, float vol)> _loopWant = new List<(string, string, Vector3, float, float)>();
        private readonly List<GameObject> _torches = new List<GameObject>();
        private int _torchCountFor = -1;
        private float _nextLoopScan;
        private readonly Dictionary<int, Vector3> _otherLast = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, float> _otherStride = new Dictionary<int, float>();
        private readonly List<int> _otherGone = new List<int>();

        // ---------------------------------------------------------------- setup

        private void Awake()
        {
            for (int i = 0; i < Voices; i++) _pool.Add(MakeSource("Voice" + i, true, false));
            _ui = MakeSource("UI", false, false);
            _voice = MakeSource("Announcer", false, false);
            _extractLoop = MakeSource("ExtractLoop", false, true);
            _ambience = MakeSource("Ambience", false, true);
            _bossAmbience = MakeSource("BossAmbience", false, true);
            _mExplore = MakeSource("MusicExploration", false, true);
            _mCombat = MakeSource("MusicCombat", false, true);
            _mBoss = MakeSource("MusicBoss", false, true);
            _awake = true;
        }

        private AudioSource MakeSource(string name, bool spatial, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = loop;
            s.spatialBlend = spatial ? 1f : 0f;
            if (spatial) { s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = MinDistance; s.maxDistance = MaxDistance; s.dopplerLevel = 0f; }
            return s;
        }

        private void OnEnable()
        {
            Hook();
            GameAudioSettings.Changed += ApplyVolumes;
        }

        private void OnDisable()
        {
            Unhook();
            GameAudioSettings.Changed -= ApplyVolumes;
        }

        private void Hook()
        {
            if (World != null && _hookedWorld != World) { UnhookWorld(); _hookedWorld = World; World.SnapshotApplied += OnSnapshot; }
            if (Effects != null && _hookedEffects != Effects) { UnhookEffects(); _hookedEffects = Effects; Effects.EffectArrived += OnEffect; }
            if (Bootstrap != null && _hookedBootstrap != Bootstrap)
            {
                _hookedBootstrap = Bootstrap;
                if (Bootstrap.OnMatchReady == null) Bootstrap.OnMatchReady = new UnityEngine.Events.UnityEvent<MatchState>();
                if (Bootstrap.OnEventNotified == null) Bootstrap.OnEventNotified = new UnityEngine.Events.UnityEvent<MatchEvent>();
                Bootstrap.OnMatchReady.AddListener(OnMatchReady);
                Bootstrap.OnEventNotified.AddListener(OnMatchEvent);
            }
            HookServer();
        }

        private void HookServer()
        {
            if (Bootstrap == null || !Bootstrap.IsServer || Bootstrap.Server == null || _hookedServer == Bootstrap.Server) return;
            if (_hookedServer != null) _hookedServer.EventRaised -= OnServerEvent;
            _hookedServer = Bootstrap.Server;
            _hookedServer.EventRaised += OnServerEvent;
        }

        private void UnhookWorld() { if (_hookedWorld != null) _hookedWorld.SnapshotApplied -= OnSnapshot; _hookedWorld = null; }
        private void UnhookEffects() { if (_hookedEffects != null) _hookedEffects.EffectArrived -= OnEffect; _hookedEffects = null; }

        private void Unhook()
        {
            UnhookWorld(); UnhookEffects();
            if (_hookedServer != null) { _hookedServer.EventRaised -= OnServerEvent; _hookedServer = null; }
            if (_hookedBootstrap != null)
            {
                _hookedBootstrap.OnMatchReady?.RemoveListener(OnMatchReady);
                _hookedBootstrap.OnEventNotified?.RemoveListener(OnMatchEvent);
                _hookedBootstrap = null;
            }
        }

        private void OnMatchReady(MatchState _) => HookServer();

        /// <summary>The host is also the local client: it hears only what <see cref="EventVisibility"/> lets the local team hear.</summary>
        private void OnServerEvent(MatchEvent e)
        {
            var st = Bootstrap != null ? Bootstrap.State : null;
            if (st == null) return;
            var audience = EventVisibility.Audience(e, st);
            if (audience == null || audience.Contains(LocalTeam)) OnMatchEvent(e);
        }

        // ---------------------------------------------------------------- who is local

        private int LocalId => World != null ? World.LocalPlayerId : 1;

        private int LocalTeam
        {
            get
            {
                var snap = World != null ? World.Latest : null;
                if (snap != null) foreach (var a in snap.Avatars) if (a.Id == LocalId && !a.IsMob) return a.Team;
                int t = Bootstrap != null ? Bootstrap.TeamOf(LocalId) : -1;
                return t >= 0 ? t : _localTeam;
            }
        }

        private bool OnLocalTeam(int id)
        {
            var snap = World != null ? World.Latest : null;
            if (snap == null) return id == LocalId;
            if (!ReferenceEquals(_teamFor, snap))
            {
                _teamFor = snap; _team.Clear(); _team.Add(LocalId);
                _localTeam = LocalTeam;
                foreach (var a in snap.Avatars) if (!a.IsMob && a.Team == _localTeam) _team.Add(a.Id);
                foreach (var s in snap.Selves) _team.Add(s.PlayerId);
            }
            return _team.Contains(id);
        }

        private bool TryAvatar(int id, out AvatarSnapshot avatar)
        {
            var snap = World != null ? World.Latest : null;
            if (snap != null) foreach (var a in snap.Avatars) if (a.Id == id) { avatar = a; return true; }
            avatar = default; return false;
        }

        private static string[] _bossNames;
        private static string[] BossNames => _bossNames ??= BossArchetype.DefaultPool.Select(b => AudioLibrary.Normalise(b.Name)).Append(AudioLibrary.Normalise(BossArchetype.FinalBoss.Name)).ToArray();

        private static bool IsBoss(AvatarSnapshot a)
        {
            if (!a.IsMob || string.IsNullOrEmpty(a.Label)) return false;
            string n = AudioLibrary.Normalise(a.Label);
            foreach (var b in BossNames) if (n.Contains(b)) return true;
            return false;
        }

        // ---------------------------------------------------------------- requests

        private void Request(string key)
        {
            RequestCount++;
            _recent.Add(key);
            if (_recent.Count > RecentCapacity) _recent.RemoveRange(0, _recent.Count - RecentCapacity);
            KeyRequested?.Invoke(key);
        }

        private bool Throttled(string id, float seconds)
        {
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(id, out float last) && now - last < seconds) return true;
            _lastPlayed[id] = now;
            if (_lastPlayed.Count > 512)
            {
                var old = _lastPlayed.Where(kv => now - kv.Value > 10f).Select(kv => kv.Key).ToList();
                foreach (var k in old) _lastPlayed.Remove(k);
            }
            return false;
        }

        private static float SfxScale => GameAudioSettings.Master * GameAudioSettings.Sfx;

        /// <summary>A 3D one-shot at a world position.</summary>
        public void PlayAt(string key, Vector3 position, float scale = 1f)
        {
            Request(key);
            if (Muted || Library == null || !_awake) return;
            var clip = Library.Pick(key, out float vol);
            if (clip == null) return;
            var src = FreeVoice();
            src.transform.position = position;
            src.clip = clip; src.volume = Mathf.Clamp01(vol * scale * SfxScale); src.pitch = 1f;
            src.Play(); _startedAt[src] = Time.unscaledTime;
        }

        /// <summary>A 3D one-shot at a simulator position.</summary>
        public void PlayAtSim(string key, float x, float y, float scale = 1f)
        {
            if (Builder == null) { Play2D(key, scale); return; }
            PlayAt(key, Builder.SimToWorld(new Vec2(x, y)), scale);
        }

        /// <summary>An interface or non-positional sound.</summary>
        public void Play2D(string key, float scale = 1f)
        {
            Request(key);
            if (Muted || Library == null || !_awake) return;
            var clip = Library.Pick(key, out float vol);
            if (clip == null) return;
            _ui.PlayOneShot(clip, Mathf.Clamp01(vol * scale * SfxScale));
        }

        /// <summary>Queue a voiced line (announcer or boss); lines never overlap.</summary>
        public void Say(string key)
        {
            Request(key);
            if (Muted || Library == null || !Library.Has(key)) return;
            _voiceQueue.Enqueue(key);
            if (_voiceQueue.Count > 4) _voiceQueue.Dequeue();
        }

        /// <summary>Say the voiced line matching this text, if there is one. Returns whether there was.</summary>
        public bool SayText(string text)
        {
            string key = Library != null ? Library.KeyForLine(text) : null;
            if (key == null) return false;
            Say(key); return true;
        }

        private AudioSource FreeVoice()
        {
            AudioSource oldest = null; float oldestAt = float.MaxValue;
            foreach (var s in _pool)
            {
                if (!s.isPlaying) return s;
                float at = _startedAt.TryGetValue(s, out float t) ? t : 0f;
                if (at < oldestAt) { oldestAt = at; oldest = s; }
            }
            return oldest;
        }

        // ---------------------------------------------------------------- effects feed

        private void OnEffect(EffectSnapshot e)
        {
            switch (e.Kind)
            {
                case EffectKind.Cast: OnCast(e); break;
                case EffectKind.Hit: OnHit(e); break;
                case EffectKind.Heal:
                    if (!Throttled("heal" + e.Target, 0.4f)) PlayAtSim("impact_holy", e.X, e.Y, 0.8f);
                    break;
                case EffectKind.Zone:
                    {
                        string zk = $"{e.Source}:{e.Ability}:{e.X:0.#}:{e.Y:0.#}";
                        if (_seenZones.Add(zk)) PlayAtSim("zone_spawn", e.X, e.Y);
                        if (_seenZones.Count > 256) _seenZones.Clear();
                        break;
                    }
                case EffectKind.Telegraph: OnTelegraph(e); break;
            }
        }

        /// <summary>The sound key a cast of this ability makes, or null (mobs: their hits speak for them).</summary>
        public static string CastKey(string abilityId)
        {
            if (!AbilityBook.TryGet(abilityId, out var def)) return null;
            if (def.Spec == "Mob") return null;
            if (def.Spec == "Boss") return def.Id == "Boss.slam" ? "impact_physical" : "swing_hammer";
            if (def.Kind == AbilityKind.Ultimate) return "ultimate_release";
            if (def.Element.HasValue) return "cast_" + def.Element.Value.ToString().ToLowerInvariant();
            if (def.Shape == AbilityShape.Dash) return "dash_whoosh";
            if (def.Shape == AbilityShape.Self && def.Buff.HasValue && def.Damage <= 0f && def.Heal <= 0f) return "buff_on";
            if (def.Id == "Sentinel.bolt") return "shield_bash";
            return WeaponKey(def.Spec) ?? (def.Heal > 0f || def.Spec == "Warder" ? "cast_holy" : null);
        }

        private static string WeaponKey(string spec)
        {
            switch (spec)
            {
                case "Sentinel": case "Duelist": return "swing_sword";
                case "Breaker": return "swing_hammer";
                case "Berserker": return "swing_axe";
                case "Assassin": return "swing_dagger";
                case "Sharpshooter": case "Volleyer": case "Trapper": return "bow_release";
                default: return null;
            }
        }

        private void OnCast(EffectSnapshot e)
        {
            string key = CastKey(e.Ability);
            if (key != null) PlayAtSim(key, e.X, e.Y);
        }

        private void OnHit(EffectSnapshot e)
        {
            if (e.Amount <= 0f) return;
            if (OnLocalTeam(e.Target) || OnLocalTeam(e.Source)) _lastCombatAt = Time.time;
            bool armoured = false;
            if (TryAvatar(e.Target, out var a))
            {
                if (IsBoss(a)) armoured = true;
                else if (!a.IsMob && a.ClassIndex < ClassArchetype.DefaultRoster.Count) armoured = ClassArchetype.DefaultRoster[a.ClassIndex].Armour == ArmourWeight.Heavy;
            }
            if (!Throttled("hit" + e.Target, 0.08f)) PlayAtSim(armoured ? "hit_armour" : "hit_flesh", e.X, e.Y);
            if (!Throttled("imp" + e.Target, 0.12f)) PlayAtSim(ImpactKey(e.Ability), e.X, e.Y, 0.8f);
            if (e.Target == LocalId && !Throttled("hurt", 0.45f)) PlayAtSim("player_hurt", e.X, e.Y);
        }

        /// <summary>Resolve a hit label (usually an ability's display name, maybe with a suffix, or an id) to its definition, or null.</summary>
        public static AbilityDefinition ResolveAbility(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            if (AbilityBook.TryGet(label, out var byId)) return byId;
            AbilityDefinition prefix = null;
            foreach (var d in AbilityBook.All)
            {
                if (d.Name == label) return d;
                if (prefix == null && label.StartsWith(d.Name + " ", StringComparison.Ordinal)) prefix = d;
            }
            return prefix;
        }

        /// <summary>The per-element impact sound for a hit label: physical when it has no element, an arrow strike for bow abilities.</summary>
        public static string ImpactKey(string label)
        {
            var def = ResolveAbility(label);
            if (def == null) return "impact_physical";
            if (def.Element.HasValue && def.Element.Value != DamageType.Physical) return "impact_" + def.Element.Value.ToString().ToLowerInvariant();
            if (WeaponKey(def.Spec) == "bow_release") return "arrow_impact";
            return "impact_physical";
        }

        private void OnTelegraph(EffectSnapshot e)
        {
            string tk = $"{e.Source}:{e.Ability}:{e.Until:0.##}";
            if (_seenTelegraphs.Add(tk)) PlayAtSim("boss_roar", e.X, e.Y);
            if (_seenTelegraphs.Count > 256) _seenTelegraphs.Clear();
            if (TryAvatar(e.Source, out var src) && IsBoss(src)) _bossChannelUntil = Time.time + 1.2f;   // sent every tick while the wind-up lasts
            bool involvesUs = OnLocalTeam(e.Target);
            if (!involvesUs && World != null && World.Latest != null && e.Amount > 0f)
                foreach (var a in World.Latest.Avatars)
                    if (!a.IsMob && OnLocalTeam(a.Id) && Vec2.Distance(new Vec2(a.X, a.Y), new Vec2(e.X2, e.Y2)) <= e.Amount + 1f) { involvesUs = true; break; }
            if (involvesUs) _lastCombatAt = Time.time;
        }

        // ---------------------------------------------------------------- snapshot

        private void OnSnapshot(TeamSnapshot snap)
        {
            foreach (var a in snap.Avatars)
            {
                if (!a.IsMob) continue;
                if (IsBoss(a)) { if (!a.Dead && _bossesMet.Add(a.Id)) Taunt(a.Label); }
                else if (a.Dead && _deadMobs.Add(a.Id)) PlayAtSim("mob_death", a.X, a.Y);
            }
            if (_deadMobs.Count > 1024) _deadMobs.Clear();
            _inBossRoom = false;
            var st = Bootstrap != null ? Bootstrap.State : null;
            if (st != null && TryAvatar(LocalId, out var me) && !me.Dead)
            {
                int room = st.Index.RoomAt(new Vec2(me.X, me.Y).Cell);
                if (room >= 0 && room < st.Layout.Rooms.Count)
                {
                    var type = st.Layout.Rooms[room].Type;
                    _inBossRoom = (type == RoomType.Boss || type == RoomType.FinalBoss) && !_clearedBossRooms.Contains(room);
                }
            }
        }

        private void Taunt(string label)
        {
            if (Library == null) return;
            string n = AudioLibrary.Normalise(label).Replace(' ', '_');
            foreach (var entry in Library.Entries)
                if (entry.Category == "voice/boss" && n.Contains(entry.Key)) { Say(entry.Key); return; }
        }

        // ---------------------------------------------------------------- match events

        /// <summary>Something the local team may hear happened. Public so a transport hook elsewhere can feed it directly.</summary>
        public void OnMatchEvent(MatchEvent e)
        {
            var st = Bootstrap != null ? Bootstrap.State : null;
            bool mine = e.A == LocalId;
            switch (e.Kind)
            {
                case MatchEventKind.RoundStarted: Play2D("round_start_horn"); break;
                case MatchEventKind.DoorsOpened: Play2D("door_open"); Say("the_doors_are_open"); break;
                case MatchEventKind.ExtractsOpened:
                    Say("the_extracts_are_open");
                    if (st != null && !st.BossExitOpen) Say("the_way_out_lies_beneath_the_sovereign");
                    break;
                case MatchEventKind.RoundEnded: StopExtract(); Say("round_over"); break;
                case MatchEventKind.DoorOpened: PlayAtDoor("door_open", e.A); break;
                case MatchEventKind.DoorClosed: PlayAtDoor("door_close", e.A); break;
                case MatchEventKind.ContainerOpened: PlayAtRoom("chest_open", e.A); break;
                case MatchEventKind.VaultOpened: PlayAtRoom("vault_unlock", e.A); break;
                case MatchEventKind.KeyPickedUp: Play2D("key_pickup"); break;
                case MatchEventKind.ItemTaken: Play2D("loot_pickup"); break;
                case MatchEventKind.SilverPicked: case MatchEventKind.SilverLooted: Play2D("silver_pickup"); break;
                case MatchEventKind.ShrinePurchase: PlayAtRoom("shrine_activate", e.C); break;
                case MatchEventKind.UltimateArmed: Play2D("ultimate_charge"); break;
                case MatchEventKind.UltimateUsed: Play2D("ultimate_release", 0.8f); break;
                case MatchEventKind.HazardTriggered: Play2D("spike_trap", 0.8f); break;
                case MatchEventKind.ExtractStarted: if (mine) StartExtract(); break;
                case MatchEventKind.ExtractInterrupted: if (mine) StopExtract(); break;
                case MatchEventKind.PlayerExtracted:
                    if (mine) { StopExtract(); Say("you_have_extracted"); }
                    Play2D("extract_success");
                    break;
                case MatchEventKind.PlayerDowned: Play2D("player_downed", mine ? 1f : 0.7f); break;
                case MatchEventKind.PlayerDied:
                    if (mine) { StopExtract(); Say("you_have_fallen"); }
                    Play2D("player_death", mine ? 1f : 0.7f);
                    break;
                case MatchEventKind.PlayerRevived: case MatchEventKind.LastBreath: Play2D("revive"); break;
                case MatchEventKind.PlayerResurrected: Play2D("revive"); PlayAtRoom("shrine_activate", e.C); break;
                case MatchEventKind.BossKilled:
                    _clearedBossRooms.Add(e.A);
                    Play2D("boss_death");
                    break;
            }
            // Whatever the global announcer says, in voice if we have the line.
            if (st != null)
            {
                var line = Announcer.For(e, st.Layout);
                if (line.HasValue) SayText(line.Value.Text);
            }
        }

        private void PlayAtRoom(string key, int roomId)
        {
            var st = Bootstrap != null ? Bootstrap.State : null;
            if (st != null && Builder != null && roomId >= 0 && roomId < st.Layout.Rooms.Count) PlayAt(key, Builder.GridToWorld(st.Layout.Rooms[roomId].Center));
            else Play2D(key);
        }

        private void PlayAtDoor(string key, int doorId)
        {
            var st = Bootstrap != null ? Bootstrap.State : null;
            if (st != null && Builder != null && st.Index.Doors.TryGetValue(doorId, out var d)) PlayAt(key, Builder.GridToWorld(d.cell));
            else Play2D(key);
        }

        private void StartExtract()
        {
            Request("extract_channel_loop");
            if (Muted || Library == null || !_awake) return;
            var clip = Library.Pick("extract_channel_loop", out float vol);
            if (clip == null) return;
            _extractLoop.clip = clip; _extractLoop.volume = Mathf.Clamp01(vol * SfxScale); _extractLoop.Play();
        }

        private void StopExtract() { if (_awake && _extractLoop.isPlaying) _extractLoop.Stop(); }

        // ---------------------------------------------------------------- per frame

        private void Update()
        {
            Hook();
            Track();                                    // music choice is tracked (and recorded) even when muted
            WorldLoops();
            if (Muted || World == null || World.Latest == null) return;
            StartLoops();
            Mix(Time.unscaledDeltaTime);
            Voice();
            Footsteps();
            OtherFootsteps();
        }

        // ---------------------------------------------------------------- hazard and torch loops, others' footsteps

        private Vector3 ListenerPosition()
        {
            var av = World != null ? World.LocalAvatar : null;
            if (av != null) return av.transform.position;
            var cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        private void WorldLoops()
        {
            if (!_awake || World == null || World.Latest == null || Library == null) return;
            if (Time.unscaledTime < _nextLoopScan) return;
            _nextLoopScan = Time.unscaledTime + 0.25f;
            var me = ListenerPosition();
            _loopWant.Clear();
            var st = Bootstrap != null ? Bootstrap.State : null;
            if (st != null && Builder != null && st.Index != null)
            {
                foreach (var hz in st.HazardsVisibleTo(LocalTeam))
                {
                    if (hz.Kind != HazardKind.GasVent && hz.Kind != HazardKind.Lava) continue;
                    if (st.DisarmedHazards.Contains(hz.Id)) continue;
                    Vector3 pos;
                    if (Builder.HazardObjects.TryGetValue(hz.Id, out var go) && go != null) pos = go.transform.position;
                    else pos = Builder.GridToWorld(hz.Bounds.Center);
                    _loopWant.Add(("hz" + hz.Id, hz.Kind == HazardKind.GasVent ? "gas_vent_loop" : "lava_loop", pos, Vector3.Distance(me, pos), HazardLoopVolume));
                }
                KeepNearest("hz", MaxHazardLoops);
            }
            if (Builder != null)
            {
                if (_torchCountFor != Builder.RoomLightCount || (_torches.Count == 0 && Builder.RoomLightCount > 0)) RescanTorches();
                foreach (var t in _torches)
                {
                    if (t == null || !t.activeInHierarchy) continue;
                    var pos = t.transform.position;
                    _loopWant.Add(("torch" + t.GetInstanceID(), "torch_loop", pos, Vector3.Distance(me, pos), TorchLoopVolume));
                }
                KeepNearest("torch", MaxTorchLoops);
            }
            ApplyWorldLoops();
        }

        private void RescanTorches()
        {
            _torches.Clear();
            _torchCountFor = Builder.RoomLightCount;
            foreach (var list in Builder.CellObjects.Values)
                foreach (var go in list)
                    if (go != null && go.name.StartsWith("RoomLight_", StringComparison.Ordinal)) _torches.Add(go);
        }

        /// <summary>Of the wanted loops whose id starts with the prefix, keep only the nearest <paramref name="max"/>.</summary>
        private void KeepNearest(string prefix, int max)
        {
            var mine = _loopWant.Where(w => w.id.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(w => w.dist).ToList();
            _loopWant.RemoveAll(w => w.id.StartsWith(prefix, StringComparison.Ordinal));
            for (int i = 0; i < mine.Count && i < max; i++) _loopWant.Add(mine[i]);
        }

        private void ApplyWorldLoops()
        {
            var keep = new HashSet<string>();
            foreach (var w in _loopWant)
            {
                keep.Add(w.id);
                if (!_worldLoops.TryGetValue(w.id, out var src))
                {
                    Request(w.key);
                    var clip = Muted ? null : Library.Pick(w.key, out _);
                    if (clip == null) { _worldLoops[w.id] = null; continue; }
                    if (_loopSpare.Count > 0) { src = _loopSpare[_loopSpare.Count - 1]; _loopSpare.RemoveAt(_loopSpare.Count - 1); }
                    else src = MakeSource("WorldLoop", true, true);
                    src.minDistance = LoopMinDistance; src.maxDistance = LoopMaxDistance;
                    src.clip = clip; src.pitch = 1f; src.time = UnityEngine.Random.value * clip.length * 0.99f;
                    _worldLoops[w.id] = src;
                    src.Play();
                }
                if (src != null)
                {
                    Library.Pick(w.key, out float v);
                    src.transform.position = w.pos; src.volume = Mathf.Clamp01(v * w.vol * SfxScale);
                }
            }
            List<string> drop = null;
            foreach (var kv in _worldLoops) if (!keep.Contains(kv.Key)) (drop ??= new List<string>()).Add(kv.Key);
            if (drop == null) return;
            foreach (var id in drop)
            {
                var src = _worldLoops[id]; _worldLoops.Remove(id);
                if (src != null) { src.Stop(); _loopSpare.Add(src); }
            }
        }

        /// <summary>Footsteps for every other visible avatar that is moving near the listener, quieter than the local player's, capped per frame.</summary>
        private void OtherFootsteps()
        {
            if (World == null || Library == null) return;
            var me = ListenerPosition(); me.y = 0f;
            int played = 0;
            foreach (var kv in World.Avatars)
            {
                int id = kv.Key; var go = kv.Value;
                if (id == LocalId || go == null || !go.activeInHierarchy) continue;
                var p = go.transform.position; p.y = 0f;
                if (!_otherLast.TryGetValue(id, out var last)) { _otherLast[id] = p; continue; }
                _otherLast[id] = p;
                float d = Vector3.Distance(p, last);
                if (d <= 0.001f || d > 3f) continue;                       // standing, or teleported
                if (TryAvatar(id, out var a) && (a.Dead || a.Downed || a.Invisible)) continue;
                _otherStride.TryGetValue(id, out float acc);
                acc += d;
                if (acc < StrideMetres * 1.2f) { _otherStride[id] = acc; continue; }
                _otherStride[id] = 0f;
                float dist = Vector3.Distance(p, me);
                if (dist > OtherStepRange || played >= MaxOtherStepsPerFrame || Throttled("ofs" + id, 0.25f) || Throttled("ofsAll", 0.04f)) continue;
                played++;
                PlayAt("footstep_stone", go.transform.position, OtherStepVolume * (1f - 0.6f * dist / OtherStepRange));
            }
            if (_otherLast.Count > World.Avatars.Count + 8)
            {
                _otherGone.Clear();
                foreach (var id in _otherLast.Keys) if (!World.Avatars.ContainsKey(id)) _otherGone.Add(id);
                foreach (var id in _otherGone) { _otherLast.Remove(id); _otherStride.Remove(id); }
            }
        }

        private void Track()
        {
            string want = BossActive ? "music_boss" : InCombat ? "music_combat" : "music_exploration";
            if (want != CurrentMusic) { CurrentMusic = want; Request(want); }
        }

        private void StartLoops()
        {
            if (_loopsStarted || Library == null) return;
            _loopsStarted = true;
            Request("music_exploration"); Request("dungeon_ambience_loop");
            Begin(_mExplore, "music_exploration"); Begin(_mCombat, "music_combat"); Begin(_mBoss, "music_boss");
            Begin(_ambience, "dungeon_ambience_loop"); Begin(_bossAmbience, "boss_room_loop");
        }

        private void Begin(AudioSource s, string key)
        {
            var clip = Library.Pick(key, out _);
            if (clip == null) return;
            s.clip = clip; s.volume = 0f; s.Play();
        }

        private float EntryVolume(string key) { Library.Pick(key, out float v); return v; }

        private void ApplyVolumes() { if (_loopsStarted && !Muted) Mix(0f); }

        private void Mix(float dt)
        {
            bool boss = BossActive, combat = InCombat;
            float step = CrossfadeSeconds <= 0f ? 1f : dt / CrossfadeSeconds;
            _mixBoss = Mathf.MoveTowards(_mixBoss, boss ? 1f : 0f, step);
            _mixCombat = Mathf.MoveTowards(_mixCombat, !boss && combat ? 1f : 0f, step);
            _mixExplore = Mathf.MoveTowards(_mixExplore, !boss && !combat ? 1f : 0f, step);
            _mixBossAmb = Mathf.MoveTowards(_mixBossAmb, _inBossRoom ? 1f : 0f, step);
            float music = GameAudioSettings.Master * GameAudioSettings.Music;
            _mExplore.volume = _mixExplore * music * EntryVolume("music_exploration");
            _mCombat.volume = _mixCombat * music * EntryVolume("music_combat");
            _mBoss.volume = _mixBoss * music * EntryVolume("music_boss");
            _ambience.volume = SfxScale * EntryVolume("dungeon_ambience_loop");
            _bossAmbience.volume = _mixBossAmb * SfxScale * EntryVolume("boss_room_loop");
            if (_extractLoop.isPlaying) _extractLoop.volume = Mathf.Clamp01(SfxScale * EntryVolume("extract_channel_loop"));
        }

        private void Voice()
        {
            if (_voice.isPlaying || _voiceQueue.Count == 0 || Library == null) return;
            var clip = Library.Pick(_voiceQueue.Dequeue(), out float vol);
            if (clip == null) return;
            _voice.clip = clip; _voice.volume = Mathf.Clamp01(vol * SfxScale); _voice.Play();
        }

        private void Footsteps()
        {
            var av = World.LocalAvatar;
            if (av == null) { _haveStep = false; return; }
            var p = av.transform.position; p.y = 0f;
            if (_haveStep && World.Self.HasValue && World.Self.Value.DownedRemaining <= 0f)
            {
                _stride += Vector3.Distance(p, _lastStep);
                if (_stride >= StrideMetres) { _stride = 0f; PlayAt("footstep_stone", av.transform.position, 0.5f); }
            }
            _lastStep = p; _haveStep = true;
        }
    }
}
