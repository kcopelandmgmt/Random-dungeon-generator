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
    /// The release player's smoke test, inert unless <c>-releasesmoke</c> is given (use with <c>-profile &lt;temp file&gt;</c> so the real
    /// save is never touched). From the main menu: make a character if there is none, take the first spec, start a round
    /// with one bot team through <see cref="GameFlow.Play"/>, drive the local player for <see cref="DriveSeconds"/> through
    /// intents (a <see cref="RoundBot"/> brain walks room to room and holds Interact at chests; enemies in sight get Basic
    /// and the ability buttons), pause from the HUD and check the round froze, leave (the forfeit the pause menu's Leave
    /// does), open the death recap and the summary, return to the menu, check the profile file changed, take three
    /// screenshots (menu, round, summary), write <c>smoke-release-result.json</c> beside the player and quit 0 or 1.
    /// Nothing here reads input; it calls what the menu and the HUD call.
    /// </summary>
    public sealed class ReleaseSmoke : MonoBehaviour
    {
        public const string Flag = "-releasesmoke";
        public const string CharacterName = "Smoke Tester";
        public const float DriveSeconds = 40f;
        public const float TimeoutSeconds = 200f;
        public const int RoundSeed = 7;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!LaunchFlags.Has(Flag)) return;
            if (FindFirstObjectByType<ReleaseSmoke>() != null) return;
            var go = new GameObject("ReleaseSmoke");
            DontDestroyOnLoad(go);
            go.AddComponent<ReleaseSmoke>();
        }

        private string _shotsDir, _resultDir, _stage = "boot";
        private readonly List<string> _notes = new List<string>();
        private readonly List<string> _shotFiles = new List<string>();
        private int _exceptions, _errors;
        private float _t0;
        private bool _done;

        // What the run saw.
        private bool _createdCharacter, _hosted, _pausedFroze, _resumed, _summarySeen, _backAtMenu, _profileChanged;
        private string _spec, _outcome, _profilePath;
        private float _moved, _minFps = float.MaxValue;
        private int _hitsByMe, _buttonFrames, _interactFrames, _roomsEntered, _chestsOpened, _framesInRound;
        private float _roundSeconds;

        private void Awake()
        {
            Application.logMessageReceived += OnLog;
            _resultDir = Directory.GetParent(Application.dataPath).FullName;
            _shotsDir = LaunchFlags.ShotsDirectory ?? Path.Combine(_resultDir, "shots");
            Directory.CreateDirectory(_shotsDir);
            _t0 = Time.realtimeSinceStartup;
        }

        private void OnDestroy() { Application.logMessageReceived -= OnLog; }

        private void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception) { _exceptions++; Note("exception: " + condition); }
            else if (type == LogType.Error || type == LogType.Assert) { _errors++; Note("error: " + condition); }
        }

        private void Note(string s) { if (_notes.Count < 30) _notes.Add(s); }

        private void Start() => StartCoroutine(Run());

        private void Update()
        {
            if (!_done && Time.realtimeSinceStartup - _t0 > TimeoutSeconds) { Note($"timed out at stage '{_stage}'"); StopAllCoroutines(); StartCoroutine(Finish()); }
        }

        private static float Now => Time.realtimeSinceStartup;

        private static IEnumerator WaitUntilOr(Func<bool> ok, float seconds)
        {
            float t = Now;
            while (!ok() && Now - t < seconds) yield return null;
        }

        private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        private static IEnumerator Seconds(float s) { float t = Now; while (Now - t < s) yield return null; }

        private IEnumerator Run()
        {
            // ---- the menu ----
            _stage = "menu";
            yield return WaitUntilOr(() => GameFlow.Instance != null && SceneManager.GetActiveScene().name == "MainMenu", 30f);
            var flow = GameFlow.Instance;
            if (flow == null) { Note("no GameFlow in the menu"); yield return Finish(); yield break; }
            yield return Frames(10);
            int prefBots = PlayerPrefs.GetInt(GameFlow.BotTeamsKey, 2), prefSel = PlayerPrefs.GetInt(GameFlow.SelectedKey, 0);
            _profilePath = ProfileStore.FilePath;

            if (flow.Character == null)
            {
                var cls = flow.Classes.FirstOrDefault(c => c.unlocked).cls;
                var why = flow.CreateCharacter(CharacterName, cls);
                if (why != null) { Note("character refused: " + why); yield return Finish(); yield break; }
                _createdCharacter = true;
            }
            var spec = flow.Specs.FirstOrDefault();
            if (spec == null) { Note("the character has no specs"); yield return Finish(); yield break; }
            var specWhy = flow.ChooseSpec(spec.Name);
            if (specWhy != null) Note("spec refused: " + specWhy);
            if (flow.CharacterNaked) flow.ClaimStarterKit();
            _spec = flow.CurrentSpec;
            yield return Frames(20);
            yield return Shot("1-menu");
            var before = Fingerprint(_profilePath);

            // ---- the round ----
            _stage = "starting round";
            flow.BotTeams = 1;
            var playWhy = flow.Play(RoundSeed);
            PlayerPrefs.SetInt(GameFlow.BotTeamsKey, prefBots);   // the smoke run never changes the player's own choices
            if (playWhy != null) { Note("Play refused: " + playWhy); yield return Finish(); yield break; }
            yield return WaitUntilOr(() => flow.Phase == FlowPhase.InRound && OfflineMatch.Current != null && OfflineMatch.Current.Hosting
                                           && flow.Server?.Sim?.Get(GameFlow.LocalPlayerId) != null, 60f);
            var match = OfflineMatch.Current;
            var server = flow.Server;
            if (match == null || !match.Hosting || server == null) { Note("the offline round never hosted"); yield return Finish(); yield break; }
            _hosted = true;

            _stage = "driving";
            yield return Drive(match, server);

            // ---- pause, check the freeze, leave ----
            _stage = "pause";
            var hud = GameObject.Find("HUD Canvas");
            if (hud == null) Note("no HUD Canvas in the round");
            else if (flow.Phase == FlowPhase.InRound)
            {
                hud.SendMessage("TogglePause");
                uint tick0 = server.Tick; float simT0 = server.State.Time;
                yield return Seconds(1.5f);
                _pausedFroze = Time.timeScale == 0f && server.Tick == tick0 && Mathf.Approximately(server.State.Time, simT0) && match.Paused;
                if (!_pausedFroze) Note($"pause did not freeze: timeScale {Time.timeScale}, tick {tick0}->{server.Tick}");
                // Leave: what the pause menu's Leave does (a forfeit that counts as a death), keeping the HUD up for the summary.
                _stage = "leave";
                flow.ForfeitRound();
                hud.SendMessage("TogglePause");
                yield return Frames(2);
                _resumed = Time.timeScale == 1f && !match.Paused;
            }

            _stage = "summary";
            yield return WaitUntilOr(() => flow.Phase == FlowPhase.RoundOver, 10f);
            _outcome = flow.LastReport?.Outcome.ToString();
            yield return WaitUntilOr(() => GameObject.Find("DeathRecapPanel") != null || GameObject.Find("RoundSummaryPanel") != null, 10f);
            if (GameObject.Find("RoundSummaryPanel") == null && hud != null) hud.SendMessage("ContinueToSummary");
            yield return WaitUntilOr(() => GameObject.Find("RoundSummaryPanel") != null, 10f);
            _summarySeen = GameObject.Find("RoundSummaryPanel") != null;
            if (!_summarySeen) Note("the round summary never opened");
            yield return Frames(20);
            yield return Shot("3-summary");

            _stage = "back to menu";
            if (hud != null) hud.SendMessage("ReturnToMenu"); else flow.ReturnToMenu();
            yield return WaitUntilOr(() => flow.Phase == FlowPhase.Menu && SceneManager.GetActiveScene().name == "MainMenu", 30f);
            _backAtMenu = flow.Phase == FlowPhase.Menu && SceneManager.GetActiveScene().name == "MainMenu";
            yield return Frames(10);
            var after = Fingerprint(_profilePath);
            _profileChanged = after != null && after != before;
            PlayerPrefs.SetInt(GameFlow.BotTeamsKey, prefBots); PlayerPrefs.SetInt(GameFlow.SelectedKey, prefSel); PlayerPrefs.Save();
            yield return Finish();
        }

        /// <summary>The local player plays: a round bot's brain walks and loots, the fight is ours through the buttons.</summary>
        private IEnumerator Drive(OfflineMatch match, MatchServer server)
        {
            const int me = GameFlow.LocalPlayerId;
            var brain = new RoundBot(me, new BotSquad { Team = 0, ItemsWanted = 99, LeaveWithSecondsLeft = 0f });
            float start = Now, nextAbility = 0f, nextShot = start + 25f;
            int abilityTurn = 0, eventIndex = server.Sim.Events.Count, lastRoom = -1;
            int chestsAtStart = server.State.Log.Count(e => e.Kind == MatchEventKind.ContainerOpened && e.B == me);
            var c0 = server.Sim.Get(me);
            Vec2 last = c0.Position;
            bool shot = false;
            while (Now - start < DriveSeconds && GameFlow.Instance != null && GameFlow.Instance.Phase == FlowPhase.InRound)
            {
                var c = server.Sim.Get(me);
                if (c == null) break;
                _framesInRound++;
                if (Time.unscaledDeltaTime > 0f) _minFps = Mathf.Min(_minFps, 1f / Time.unscaledDeltaTime);
                _moved += Vec2.Distance(last, c.Position); last = c.Position;
                int room = server.RoomOf(me);
                if (room >= 0 && room != lastRoom) { _roomsEntered++; lastRoom = room; }
                var ev = server.Sim.Events;
                for (; eventIndex < ev.Count; eventIndex++) if (ev[eventIndex].Kind == FightEventKind.Hit && ev[eventIndex].Source == me) _hitsByMe++;

                float dt = Time.deltaTime * (match.Server != null ? match.Server.GameSpeed : 1f);
                PlayerIntent intent;
                var foe = NearestFoe(server, c);
                if (foe != null && !c.Downed && !c.Dead)
                {
                    intent = new PlayerIntent { PlayerId = me, AimX = foe.Position.X, AimY = foe.Position.Y, Basic = true };
                    var to = foe.Position - c.Position;
                    if (to.Length > 2.5f) { var d = to.Normalized; intent.MoveX = d.X; intent.MoveY = d.Y; }
                    if (Now >= nextAbility)
                    {
                        nextAbility = Now + 1.25f;
                        switch (abilityTurn++ % 4) { case 0: intent.Bolt = true; break; case 1: intent.Zone = true; break; case 2: intent.Ultimate = true; break; default: intent.Mobility = true; break; }
                    }
                    _buttonFrames++;
                }
                else intent = brain.Think(server, Mathf.Max(0.001f, dt)) ?? new PlayerIntent { PlayerId = me };
                intent.PlayerId = me; intent.Tick = (uint)Time.frameCount;
                if (intent.Interact) _interactFrames++;
                server.SetIntent(me, intent);

                if (!shot && Now >= nextShot) { shot = true; yield return Shot("2-round"); continue; }
                yield return null;
            }
            if (!shot) yield return Shot("2-round");
            server.SetIntent(me, new PlayerIntent { PlayerId = me });
            _roundSeconds = Now - start;
            _chestsOpened = server.State.Log.Count(e => e.Kind == MatchEventKind.ContainerOpened && e.B == me) - chestsAtStart;
        }

        private static Combatant NearestFoe(MatchServer server, Combatant c)
        {
            if (!server.State.DoorsOpen) return null;
            int range = server.Descriptor.Rules.SightRangeCells;
            Combatant best = null; float bestD = float.MaxValue;
            foreach (var o in server.Sim.Combatants.Values)
            {
                if (o.Team == c.Team || o.Dead || o.Downed) continue;
                float d = Vec2.Distance(o.Position, c.Position);
                if (d > range || d >= bestD || !server.Sim.CanSee(c.Position, o.Position)) continue;
                best = o; bestD = d;
            }
            return best;
        }

        private IEnumerator Shot(string name)
        {
            var path = Path.Combine(_shotsDir, $"release-{name}.png");
            try { if (File.Exists(path)) File.Delete(path); ScreenCapture.CaptureScreenshot(path); }
            catch (Exception e) { Note("screenshot failed: " + e.Message); yield break; }
            float t = Now;
            while (!File.Exists(path) && Now - t < 3f) yield return null;
            if (File.Exists(path)) _shotFiles.Add(path); else Note("screenshot not written: " + path);
        }

        private static string Fingerprint(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                ulong h = 14695981039346656037UL;   // FNV-1a over the bytes
                foreach (var b in bytes) { h ^= b; h *= 1099511628211UL; }
                return bytes.Length + ":" + h.ToString("x16") + ":" + File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch (Exception) { return null; }
        }

        private IEnumerator Finish()
        {
            if (_done) yield break;
            _done = true;
            bool ok = _exceptions == 0 && _hosted && _moved > 1f && _pausedFroze && _resumed && _summarySeen && _backAtMenu && _profileChanged && _shotFiles.Count == 3;
            int code = ok ? 0 : 1;
            var sb = new StringBuilder();
            sb.Append("{\n");
            void Str(string k, string v) => sb.Append($"  \"{k}\": \"{Esc(v ?? "")}\",\n");
            void Num(string k, float v) => sb.Append($"  \"{k}\": {v.ToString("0.###", CultureInfo.InvariantCulture)},\n");
            void Int(string k, int v) => sb.Append($"  \"{k}\": {v},\n");
            void Bool(string k, bool v) => sb.Append($"  \"{k}\": {(v ? "true" : "false")},\n");
            void Arr(string k, IEnumerable<string> v) => sb.Append($"  \"{k}\": [{string.Join(", ", v.Select(s => "\"" + Esc(s) + "\""))}],\n");
            Bool("passed", ok);
            Int("exitCode", code);
            Bool("developmentBuild", Debug.isDebugBuild);
            Str("stageReached", _stage);
            Str("profilePath", _profilePath);
            Bool("createdCharacter", _createdCharacter);
            Str("spec", _spec);
            Bool("hosted", _hosted);
            Num("drivenSeconds", _roundSeconds);
            Int("framesInRound", _framesInRound);
            Num("minFps", _minFps == float.MaxValue ? 0f : _minFps);
            Num("avatarMovedCells", _moved);
            Int("roomsEntered", _roomsEntered);
            Int("hitsByLocalPlayer", _hitsByMe);
            Int("framesWithAttackButtons", _buttonFrames);
            Int("framesHoldingInteract", _interactFrames);
            Int("chestsOpened", _chestsOpened);
            Bool("pauseFroze", _pausedFroze);
            Bool("resumed", _resumed);
            Str("outcome", _outcome);
            Bool("summarySeen", _summarySeen);
            Bool("backAtMenu", _backAtMenu);
            Bool("profileChanged", _profileChanged);
            Int("exceptionsLogged", _exceptions);
            Int("errorsLogged", _errors);
            Arr("notes", _notes);
            Arr("screenshots", _shotFiles);
            sb.Append($"  \"unityVersion\": \"{Application.unityVersion}\"\n}}\n");
            try { File.WriteAllText(Path.Combine(_resultDir, "smoke-release-result.json"), sb.ToString()); }
            catch (Exception e) { Debug.LogWarning("ReleaseSmoke: could not write the result: " + e.Message); }
            Debug.Log("ReleaseSmoke: " + (ok ? "PASS" : "FAIL") + $" stage={_stage} moved={_moved:0.0} hits={_hitsByMe} pause={_pausedFroze} summary={_summarySeen} profileChanged={_profileChanged}");
            Time.timeScale = 1f;
            for (int i = 0; i < 5; i++) yield return null;
            Application.Quit(code);
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
