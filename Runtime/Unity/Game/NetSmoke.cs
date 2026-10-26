using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The two-process network smoke test (<c>unity-batch.sh smoke-net</c>), inert unless <c>-autohost</c> or <c>-autojoin &lt;address&gt;</c> is given
    /// (with <c>-profile &lt;temp file&gt;</c> so no real save is touched, and <c>-port</c>). Both start at the main menu like a player would.
    /// Host: makes a character, hosts on 127.0.0.1 only (no firewall prompt), starts the round when a client joined (or after 30 s), plays its own
    /// player for <see cref="DriveSeconds"/> and checks that it sees the client's avatar and that the client's intents move, cast and work a door on
    /// its server, waits for the client to leave and get its result, then leaves. Client: makes a character, joins, follows the round from snapshots
    /// only, walks to a door of its spawn room and holds Space (the door shuts, then opens), casts, checks it sees the host's avatar, leaves (a
    /// forfeit), settles its profile from the server's result. Each writes <c>net-host-result.json</c> or <c>net-client-result.json</c> beside the
    /// player, takes screenshots (lobby, round), and quits 0 or 1.
    /// </summary>
    public sealed class NetSmoke : MonoBehaviour
    {
        public const float DriveSeconds = 40f, TimeoutSeconds = 200f;
        public const int RoundSeed = 7;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!LaunchFlags.Has("-autohost") && LaunchFlags.Value("-autojoin") == null) return;
            if (FindFirstObjectByType<NetSmoke>() != null) return;
            var go = new GameObject("NetSmoke");
            DontDestroyOnLoad(go);
            go.AddComponent<NetSmoke>();
        }

        private bool _host;
        private string _shotsDir, _resultDir, _stage = "boot", _address;
        private ushort _port;
        private readonly List<string> _notes = new List<string>();
        private readonly List<string> _shots = new List<string>();
        private int _exceptions, _errors;
        private float _t0;
        private bool _done;
        private readonly Dictionary<string, object> _facts = new Dictionary<string, object>();

        private void Awake()
        {
            _host = LaunchFlags.Has("-autohost");
            _address = LaunchFlags.Value("-autojoin") ?? NetSessions.DefaultAddress;
            _port = ushort.TryParse(LaunchFlags.Value("-port"), out var p) && p > 0 ? p : NetSessions.DefaultPort;
            Application.logMessageReceived += OnLog;
            _resultDir = Directory.GetParent(Application.dataPath).FullName;
            _shotsDir = LaunchFlags.ShotsDirectory ?? Path.Combine(_resultDir, "shots");
            Directory.CreateDirectory(_shotsDir);
            _t0 = Time.realtimeSinceStartup;
            Application.runInBackground = true;   // two minimised players must both keep running
        }

        private void OnDestroy() => Application.logMessageReceived -= OnLog;

        private void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception) { _exceptions++; Note("exception: " + condition + " @ " + (stack ?? "").Split('\n').FirstOrDefault()); }
            else if (type == LogType.Error || type == LogType.Assert) { _errors++; Note("error: " + condition); }
        }

        private void Note(string s) { if (_notes.Count < 40) _notes.Add(s); }
        private void Start() => StartCoroutine(_host ? RunHost() : RunClient());

        private void Update()
        {
            if (!_done && Time.realtimeSinceStartup - _t0 > TimeoutSeconds) { Note($"timed out at stage '{_stage}'"); StopAllCoroutines(); StartCoroutine(Finish(false)); }
        }

        private static float Now => Time.realtimeSinceStartup;
        private static IEnumerator WaitUntilOr(Func<bool> ok, float seconds) { float t = Now; while (!ok() && Now - t < seconds) yield return null; }
        private static IEnumerator Seconds(float s) { float t = Now; while (Now - t < s) yield return null; }

        private IEnumerator Menu(string characterName)
        {
            _stage = "menu";
            yield return WaitUntilOr(() => GameFlow.Instance != null && SceneManager.GetActiveScene().name == "MainMenu", 30f);
            var flow = GameFlow.Instance;
            if (flow == null) { Note("no GameFlow in the menu"); yield break; }
            for (int i = 0; i < 10; i++) yield return null;
            if (flow.Character == null)
            {
                var cls = flow.Classes.FirstOrDefault(c => c.unlocked).cls;
                var why = flow.CreateCharacter(characterName, cls);
                if (why != null) Note("character refused: " + why);
            }
            var spec = flow.Specs.FirstOrDefault();
            if (spec != null) { var w = flow.ChooseSpec(spec.Name); if (w != null) Note("spec refused: " + w); }
            if (flow.CharacterNaked) flow.ClaimStarterKit();
            _facts["character"] = flow.Character?.Name; _facts["class"] = flow.Character?.Class.Name; _facts["spec"] = flow.CurrentSpec;
        }

        private static void SilenceInput()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if (mb is IIntentSource && mb.enabled) mb.enabled = false;
        }

        // ---------------- host ----------------

        private IEnumerator RunHost()
        {
            yield return Menu("Net Host");
            var flow = GameFlow.Instance;
            if (flow == null || flow.Character == null) { yield return Finish(false); yield break; }
            _stage = "hosting";
            flow.BotTeams = 1;
            var why = flow.HostOnline(_port, "127.0.0.1");
            if (why != null) { Note("host refused: " + why); yield return Finish(false); yield break; }
            var session = flow.Session;
            _facts["status"] = session.Status;
            _stage = "lobby";
            float lobbyAt = Now;
            yield return WaitUntilOr(() => session.Lobby.Seats.Count >= 2, 30f);
            int clientId = session.Lobby.Seats.Where(s => !s.Local).Select(s => s.PlayerId).DefaultIfEmpty(-1).First();
            _facts["clientJoined"] = clientId > 0; _facts["clientPlayerId"] = clientId; _facts["secondsToJoin"] = Now - lobbyAt;
            _facts["lobbySeats"] = string.Join("; ", session.Lobby.Seats.Select(s => $"{s.PlayerId}:{s.Name}/{s.ClassName}@team{s.Team}"));
            yield return Seconds(1.5f);
            yield return Shot("host-1-lobby");
            _stage = "starting round";
            why = flow.StartOnlineRound(RoundSeed);
            if (why != null) { Note("start refused: " + why); yield return Finish(false); yield break; }
            yield return WaitUntilOr(() => flow.Phase == FlowPhase.InRound && OnlineMatch.Current != null && OnlineMatch.Current.Hosting, 60f);
            var match = OnlineMatch.Current;
            if (match == null || !match.Hosting) { Note("the online round never hosted"); yield return Finish(false); yield break; }
            var server = match.MatchServer;
            _facts["hosted"] = true; _facts["remoteSeated"] = match.RemoteSeats.Count; _facts["teams"] = server.Descriptor.TeamSpawnRooms.Count;
            _facts["refused"] = string.Join("; ", match.Refused.Select(kv => kv.Key + ": " + kv.Value));
            SilenceInput();

            _stage = "driving";
            const int me = GameFlow.LocalPlayerId;
            var brain = new RoundBot(me, new BotSquad { Team = server.TeamOf(me), ItemsWanted = 99, LeaveWithSecondsLeft = 0f });
            float start = Now, nextAbility = 0f;
            bool shot = false, sawClient = false;
            Vec2? clientStart = null; float clientMoved = 0f; Vec2 clientLast = default;
            int castCursor = server.Sim.Casts.Count, clientCasts = 0, hostCasts = 0, turn = 0;
            while (Now - start < DriveSeconds && flow.Phase == FlowPhase.InRound)
            {
                var c = server.Sim.Get(me);
                if (c == null) break;
                if (match.World.Avatars.ContainsKey(clientId)) sawClient = true;
                var cc = clientId > 0 ? server.Sim.Get(clientId) : null;
                if (cc != null) { if (clientStart == null) { clientStart = cc.Position; clientLast = cc.Position; } clientMoved += Vec2.Distance(clientLast, cc.Position); clientLast = cc.Position; }
                for (; castCursor < server.Sim.Casts.Count; castCursor++) { var k = server.Sim.Casts[castCursor]; if (k.Caster == clientId) clientCasts++; if (k.Caster == me) hostCasts++; }
                PlayerIntent intent = brain.Think(server, Mathf.Max(0.001f, Time.deltaTime)) ?? new PlayerIntent { PlayerId = me };
                if (server.State.DoorsOpen && Now >= nextAbility && !intent.Interact)
                {
                    nextAbility = Now + 1.5f;
                    // Aim away from the client (a teammate): a friendly hit would break its door channel.
                    var away = cc != null && Vec2.Distance(cc.Position, c.Position) > 0.1f ? (c.Position - cc.Position).Normalized : new Vec2(1f, 0f);
                    intent.AimX = c.Position.X + away.X * 3f; intent.AimY = c.Position.Y + away.Y * 3f;
                    switch (turn++ % 3) { case 0: intent.Basic = true; break; case 1: intent.Bolt = true; break; default: intent.Zone = true; break; }
                }
                intent.PlayerId = me; intent.Tick = (uint)Time.frameCount;
                server.SetIntent(me, intent);
                if (!shot && Now - start > 22f) { shot = true; yield return Shot("host-2-round"); continue; }
                yield return null;
            }
            server.SetIntent(me, new PlayerIntent { PlayerId = me });
            if (!shot) yield return Shot("host-2-round");
            int doorsByClient = server.State.Log.Count(e => (e.Kind == MatchEventKind.DoorClosed || e.Kind == MatchEventKind.DoorOpened) && e.B == clientId);
            _facts["drivenSeconds"] = Now - start; _facts["sawClientAvatar"] = sawClient; _facts["clientMovedCells"] = clientMoved;
            _facts["clientCasts"] = clientCasts; _facts["hostCasts"] = hostCasts; _facts["doorEventsByClient"] = doorsByClient;
            _facts["hostMovedFromSpawn"] = Vec2.Distance(server.Sim.Get(me)?.Position ?? default, Vec2.Of(server.Layout.Rooms[server.Descriptor.TeamSpawnRooms[server.TeamOf(me)]].Center));

            _stage = "waiting for the client to leave";
            yield return WaitUntilOr(() => clientId < 0 || RoundSettlement.OutcomeOf(server.State, clientId) != null, 25f);
            var outcome = clientId > 0 ? RoundSettlement.OutcomeOf(server.State, clientId) : null;
            _facts["clientOutcome"] = outcome?.ToString();
            yield return Seconds(4f);   // the result goes out next frame; give the client time to settle and disconnect
            _stage = "leaving";
            flow.LeaveRound();
            yield return WaitUntilOr(() => flow.Phase == FlowPhase.Menu && SceneManager.GetActiveScene().name == "MainMenu", 30f);
            _facts["backAtMenu"] = flow.Phase == FlowPhase.Menu;
            _facts["hostSettled"] = flow.LastReport != null; _facts["hostOutcome"] = flow.LastReport?.Outcome.ToString();
            bool ok = clientId > 0 && sawClient && clientMoved > 1f && clientCasts > 0 && doorsByClient >= 1 && outcome != null && flow.LastReport != null;
            yield return Finish(ok);
        }

        // ---------------- client ----------------

        private IEnumerator RunClient()
        {
            yield return Menu("Net Client");
            var flow = GameFlow.Instance;
            if (flow == null || flow.Character == null) { yield return Finish(false); yield break; }
            string before = Fingerprint(ProfileStore.FilePath);
            _stage = "joining";
            string why = null;
            float joinStart = Now;
            // The host may still be starting: retry the join until it answers (the session reports a refused or lost connection).
            while (Now - joinStart < 40f)
            {
                why = flow.JoinOnline(_address, _port);
                if (why != null) { Note("join refused: " + why); yield return Finish(false); yield break; }
                yield return WaitUntilOr(() => flow.Session == null || flow.Session.LocalPlayerId > 0, 8f);
                if (flow.Session != null && flow.Session.LocalPlayerId > 0) break;
                if (flow.Phase == FlowPhase.Lobby) flow.LeaveLobby();
                yield return Seconds(1f);
            }
            var session = flow.Session;
            if (session == null || session.LocalPlayerId <= 0) { Note("never welcomed by the host: " + flow.Message); yield return Finish(false); yield break; }
            int me = session.LocalPlayerId;
            _facts["playerId"] = me; _facts["status"] = session.Status;
            yield return Seconds(1f);
            yield return Shot("client-1-lobby");
            _stage = "lobby";
            yield return WaitUntilOr(() => flow.Phase == FlowPhase.InRound && OnlineMatch.Current != null && OnlineMatch.Current.Joined, 60f);
            var match = OnlineMatch.Current;
            if (match == null || !match.Joined) { Note("never joined the round"); yield return Finish(false); yield break; }
            var world = match.World;
            yield return WaitUntilOr(() => world.LocalAvatar != null && match.Bootstrap.State != null, 20f);
            _facts["rebuiltDungeon"] = match.Bootstrap.State != null; _facts["sawOwnAvatar"] = world.LocalAvatar != null;
            if (world.LocalAvatar == null) { Note("no own avatar from the snapshots"); yield return Finish(false); yield break; }
            SilenceInput();
            var transport = session.Transport;
            var st = match.Bootstrap.State;
            var layout = st.Layout;

            _stage = "waiting for the doors";
            yield return WaitUntilOr(() => st.DoorsOpen, 20f);
            _stage = "driving";
            int team = st.PlayerTeam.TryGetValue(me, out int tt) ? tt : 0;
            int spawn = match.Bootstrap.Descriptor.TeamSpawnRooms[team];
            var doors = st.Index.Doors.Where(kv => kv.Value.room == spawn).ToList();
            float start = Now, nextCast = Now + 1f, moved = 0f;
            bool sawHost = false, shot = false;
            int myCasts = 0, hits = 0, doorClosedSeen = 0, doorOpenedSeen = 0, doorTries = 0;
            Vec2? last = null;
            uint tick = 0;
            int views = 0;
            transport.ViewReceived += _ => views++;
            world.SnapshotApplied += snap => { foreach (var e in snap.Effects) { if (e.Kind == EffectKind.Cast && e.Source == me) myCasts++; if (e.Kind == EffectKind.Hit && e.Source == me) hits++; } };

            // The plan: walk to a door of the spawn room, hold Space until it shuts and opens again, then wander and cast.
            int doorIdx = 0; List<GridPoint> path = null; int pathAt = 0; float holdUntil = -1f, stuckSince = Now; Vec2 stuckPos = default;
            bool doorDone = false; bool sawClosed = false;
            while (Now - start < DriveSeconds && flow.Phase == FlowPhase.InRound)
            {
                var self = world.Latest?.Avatars.FirstOrDefault(a => a.Id == me);
                if (world.Avatars.ContainsKey(FishnetHostId)) sawHost = true;
                if (self == null || self.Value.Id != me) { yield return null; continue; }
                var pos = new Vec2(self.Value.X, self.Value.Y);
                if (last.HasValue) moved += Vec2.Distance(last.Value, pos);
                last = pos;
                var intent = new PlayerIntent { PlayerId = me, Tick = ++tick, AimX = pos.X + 2f, AimY = pos.Y };
                if (!doorDone && doorIdx < doors.Count)
                {
                    var door = doors[doorIdx].Value.cell;
                    if (holdUntil > 0f)
                    {
                        intent.Interact = true;
                        if (st.ClosedDoors.Contains(door) && !sawClosed) { sawClosed = true; Debug.Log($"NetSmoke: the door at {door} shut (views {views})."); }
                        if (sawClosed && !st.ClosedDoors.Contains(door)) { doorDone = true; holdUntil = -1f; Debug.Log($"NetSmoke: the door at {door} opened again (views {views})."); }
                        else if (Now > holdUntil) { Debug.Log($"NetSmoke: gave up on the door at {door}: closed doors in view {st.ClosedDoors.Count}, saw it shut {sawClosed}, views {views}."); holdUntil = -1f; doorIdx++; path = null; }
                    }
                    else
                    {
                        if (path == null)
                        {
                            var inner = Inner(layout, door, spawn);
                            path = inner.HasValue ? GridPath.Find(layout, pos.Cell, inner.Value, closedDoors: st.ClosedDoors) : null;
                            pathAt = 0; doorTries++;
                            if (path == null) { doorIdx++; yield return null; continue; }
                        }
                        while (pathAt < path.Count - 1 && Vec2.Distance(Vec2.Of(path[pathAt]), pos) < 0.45f) pathAt++;
                        var target = Vec2.Of(path[Math.Min(pathAt, path.Count - 1)]);
                        if (Vec2.Distance(Vec2.Of(door), pos) <= 1.2f && pathAt >= path.Count - 1) { holdUntil = Now + 15f; sawClosed = false; Debug.Log($"NetSmoke: holding Space at the door {door} from {pos.Cell}; it is {(st.ClosedDoors.Contains(door) ? "shut" : "open")} (views {views})."); }
                        else { var d = (target - pos); if (d.Length > 0.05f) { d = d.Normalized; intent.MoveX = d.X; intent.MoveY = d.Y; } }
                        if (Vec2.Distance(stuckPos, pos) > 0.3f) { stuckPos = pos; stuckSince = Now; }
                        else if (Now - stuckSince > 4f) { doorIdx++; path = null; stuckSince = Now; }
                    }
                }
                else
                {
                    // Wander toward the spawn room's far side and cast now and then.
                    float a = (Now - start) * 0.6f;
                    intent.MoveX = Mathf.Cos(a); intent.MoveY = Mathf.Sin(a);
                    if (Now >= nextCast) { nextCast = Now + 1.2f; if ((tick & 1) == 0) intent.Basic = true; else intent.Bolt = true; }
                }
                if (holdUntil < 0f && Now >= nextCast && !intent.Interact) { nextCast = Now + 1.2f; intent.Basic = true; }
                transport.SendIntent(intent.ToBytes());
                if (!shot && Now - start > 20f) { shot = true; yield return Shot("client-2-round"); continue; }
                yield return Seconds(0.05f);
            }
            transport.SendIntent(new PlayerIntent { PlayerId = me, Tick = ++tick }.ToBytes());
            if (!shot) yield return Shot("client-2-round");
            _facts["drivenSeconds"] = Now - start; _facts["movedCells"] = moved; _facts["sawHostAvatar"] = sawHost; _facts["castsSeen"] = myCasts; _facts["hitsSeen"] = hits;
            _facts["doorShutThenOpened"] = doorDone; _facts["doorsTried"] = doorTries; _facts["spawnDoors"] = doors.Count;
            var mirror = ClientMirror.Current;
            bool itemScreens = mirror?.Items != null;
            _facts["itemScreensFromWire"] = itemScreens; _facts["carriedFromWire"] = mirror?.Items?.Carried.Count ?? 0;
            _facts["itemDetailsResolved"] = mirror?.Items?.Carried.Count(c => c.Item != null) ?? 0;
            _facts["pocketsFromWire"] = string.Join(", ", mirror?.Pockets.Select(kv => kv.Key + " x" + kv.Value) ?? Array.Empty<string>());
            _facts["specFromWire"] = mirror?.Spec;

            _stage = "leaving";
            flow.LeaveRound();
            yield return WaitUntilOr(() => flow.Phase == FlowPhase.Menu && SceneManager.GetActiveScene().name == "MainMenu", 30f);
            _facts["backAtMenu"] = flow.Phase == FlowPhase.Menu;
            _facts["settledFromResult"] = flow.LastReport != null; _facts["outcome"] = flow.LastReport?.Outcome.ToString();
            _facts["recapHeadline"] = flow.LastRecap?.Headline;
            _facts["message"] = flow.Message;
            for (int i = 0; i < 10; i++) yield return null;
            string after = Fingerprint(ProfileStore.FilePath);
            _facts["profileChanged"] = after != null && after != before;
            bool ok = sawHost && moved > 1f && myCasts > 0 && doorDone && flow.LastReport != null && after != before && itemScreens;
            yield return Finish(ok);
        }

        private const int FishnetHostId = GameFlow.LocalPlayerId;

        /// <summary>A floor cell next to the door inside the room.</summary>
        private static GridPoint? Inner(DungeonLayout layout, GridPoint door, int room)
        {
            foreach (var d in new[] { new GridPoint(1, 0), new GridPoint(-1, 0), new GridPoint(0, 1), new GridPoint(0, -1) })
            {
                var n = new GridPoint(door.X + d.X, door.Y + d.Y);
                if (layout.InBounds(n) && layout.RoomIdAt[n.X, n.Y] == room && layout.IsWalkable(n)) return n;
            }
            return null;
        }

        private IEnumerator Shot(string name)
        {
            var path = Path.Combine(_shotsDir, $"net-{name}.png");
            try { if (File.Exists(path)) File.Delete(path); ScreenCapture.CaptureScreenshot(path); }
            catch (Exception e) { Note("screenshot failed: " + e.Message); yield break; }
            float t = Now;
            while (!File.Exists(path) && Now - t < 3f) yield return null;
            if (File.Exists(path)) _shots.Add(path); else Note("screenshot not written: " + path);
        }

        private static string Fingerprint(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                ulong h = 14695981039346656037UL;
                foreach (var b in bytes) { h ^= b; h *= 1099511628211UL; }
                return bytes.Length + ":" + h.ToString("x16");
            }
            catch (Exception) { return null; }
        }

        private IEnumerator Finish(bool scenario)
        {
            if (_done) yield break;
            _done = true;
            bool ok = scenario && _exceptions == 0;
            int code = ok ? 0 : 1;
            var sb = new StringBuilder("{\n");
            sb.Append($"  \"role\": \"{(_host ? "host" : "client")}\",\n  \"passed\": {(ok ? "true" : "false")},\n  \"exitCode\": {code},\n  \"stageReached\": \"{Esc(_stage)}\",\n");
            foreach (var kv in _facts)
            {
                string v = kv.Value switch
                {
                    null => "null",
                    bool b => b ? "true" : "false",
                    int i => i.ToString(CultureInfo.InvariantCulture),
                    float f => f.ToString("0.###", CultureInfo.InvariantCulture),
                    _ => "\"" + Esc(kv.Value.ToString()) + "\"",
                };
                sb.Append($"  \"{kv.Key}\": {v},\n");
            }
            sb.Append($"  \"exceptionsLogged\": {_exceptions},\n  \"errorsLogged\": {_errors},\n");
            sb.Append($"  \"notes\": [{string.Join(", ", _notes.Select(s => "\"" + Esc(s) + "\""))}],\n");
            sb.Append($"  \"screenshots\": [{string.Join(", ", _shots.Select(s => "\"" + Esc(s) + "\""))}],\n");
            sb.Append($"  \"unityVersion\": \"{Application.unityVersion}\"\n}}\n");
            try { File.WriteAllText(Path.Combine(_resultDir, _host ? "net-host-result.json" : "net-client-result.json"), sb.ToString()); }
            catch (Exception e) { Debug.LogWarning("NetSmoke: could not write the result: " + e.Message); }
            Debug.Log($"NetSmoke ({(_host ? "host" : "client")}): {(ok ? "PASS" : "FAIL")} at stage {_stage}");
            NetSessions.StopCurrent();
            for (int i = 0; i < 10; i++) yield return null;
            Application.Quit(code);
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
