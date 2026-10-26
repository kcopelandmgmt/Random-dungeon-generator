using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// With -autotest, a scripted 30-second scenario on the in-process practice round: cast the local spec's abilities and five other
    /// specs' (dev casting is on), walk four directions once the spawn doors open, hold Interact, take three screenshots, write
    /// smoke-result.json next to the player and quit with exit code 0 (no exceptions, the avatar moved, a cast succeeded) or 1.
    /// </summary>
    public sealed class AutoSmokeTest : MonoBehaviour
    {
        public const float Duration = 30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!LaunchFlags.AutoTest) return;
            if (FindFirstObjectByType<AutoSmokeTest>() != null) return;
            var go = new GameObject("AutoSmokeTest");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoSmokeTest>();
        }

        private PracticeMode _practice;
        private float _t0 = -1f;            // realtime the scenario began (after hosting)
        private float _waitStart;
        private int _frames0;
        private Vec2 _last; private bool _haveLast; private float _path;
        private int _castsOk, _castsRejected, _exceptions, _errors;
        private readonly List<string> _rejections = new List<string>();
        private readonly List<string> _notes = new List<string>();
        private readonly Queue<string> _castQueue = new Queue<string>();
        private bool _castsBuilt, _doorsSeen; private float _doorsAt = -1f;
        private int _shot;
        private string _shotsDir, _resultDir;
        private readonly List<string> _shotFiles = new List<string>();
        private string _interactAvailable;
        private bool _finished; private int _finishFrames;
        private int _exitCode;
        private float _minFps = float.MaxValue;

        private void Awake()
        {
            Application.logMessageReceived += OnLog;
            _waitStart = Time.realtimeSinceStartup;
            _resultDir = Directory.GetParent(Application.dataPath).FullName;
            _shotsDir = LaunchFlags.ShotsDirectory ?? Path.Combine(_resultDir, "shots");
            Directory.CreateDirectory(_shotsDir);
        }

        private void OnDestroy() { Application.logMessageReceived -= OnLog; }

        private void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception) { _exceptions++; if (_notes.Count < 20) _notes.Add("exception: " + condition); }
            else if (type == LogType.Error || type == LogType.Assert) { _errors++; if (_notes.Count < 20) _notes.Add("error: " + condition); }
        }

        private PracticeMode Practice => _practice != null ? _practice : (_practice = FindFirstObjectByType<PracticeMode>());

        private void Update()
        {
            if (_finished) { if (++_finishFrames > 5) Application.Quit(_exitCode); return; }
            var p = Practice;
            if (p == null || !p.Hosting)
            {
                if (Time.realtimeSinceStartup - _waitStart > 30f) { _notes.Add("practice round never hosted"); Finish(); }
                return;
            }
            var server = p.Server.Server;
            var me = server?.Sim?.Get(1);
            if (me == null) { if (Time.realtimeSinceStartup - _waitStart > 30f) { _notes.Add("local player never seated"); Finish(); } return; }

            if (_t0 < 0f) { _t0 = Time.realtimeSinceStartup; _frames0 = Time.frameCount; _last = me.Position; _haveLast = true; BuildCasts(p); }
            float t = Time.realtimeSinceStartup - _t0;
            if (Time.unscaledDeltaTime > 0f) _minFps = Mathf.Min(_minFps, 1f / Time.unscaledDeltaTime);

            // Distance walked so far, including dashes.
            var pos = me.Position;
            if (_haveLast) _path += Vec2.Distance(_last, pos);
            _last = pos;

            if (!_doorsSeen && server.State.DoorsOpen) { _doorsSeen = true; _doorsAt = t; }

            // Casts: one per frame from t = 1 s, the local spec first.
            if (t > 1f && _castQueue.Count > 0) DoCast(p, me);

            // Walk: north, east, south, west, 2.5 s each, from the moment the doors are open (or t = 14 s regardless); then hold Interact.
            float walkStart = _doorsSeen ? Mathf.Max(_doorsAt, 9f) : 14f;
            var intent = new PlayerIntent { PlayerId = 1, Tick = (uint)Time.frameCount };
            float interactStart = walkStart + 4 * 2.5f + 0.5f;
            if (t >= walkStart && t < interactStart - 0.5f)
            {
                int leg = Mathf.Clamp((int)((t - walkStart) / 2.5f), 0, 3);
                var dirs = new[] { new Vec2(0, 1), new Vec2(1, 0), new Vec2(0, -1), new Vec2(-1, 0) };
                intent.MoveX = dirs[leg].X; intent.MoveY = dirs[leg].Y;
                intent.AimX = pos.X + dirs[leg].X * 4f; intent.AimY = pos.Y + dirs[leg].Y * 4f;
            }
            else if (t >= interactStart && t < interactStart + 4f)
            {
                intent.Interact = true; intent.AimX = pos.X + 1f; intent.AimY = pos.Y;
                _interactAvailable ??= server.InteractionAvailable(1) ?? "nothing in reach";
            }
            server.SetIntent(1, intent);

            // Screenshots: while casting, while walking, while holding Interact.
            if (_shot == 0 && t >= 4f) Shot("1-casting");
            else if (_shot == 1 && t >= walkStart + 5f) Shot("2-walking");
            else if (_shot == 2 && t >= Duration - 4f) Shot("3-interact");

            if (t >= Duration) Finish();
        }

        private void BuildCasts(PracticeMode p)
        {
            if (_castsBuilt) return; _castsBuilt = true;
            foreach (var a in AbilityBook.For(p.LocalSpec)) _castQueue.Enqueue(a.Id);
            foreach (var spec in WeaponSpec.All.Select(w => w.Name).Distinct().Where(n => n != p.LocalSpec).Take(5))
                foreach (var a in AbilityBook.For(spec)) _castQueue.Enqueue(a.Id);
        }

        private void DoCast(PracticeMode p, Combatant me)
        {
            var id = _castQueue.Dequeue();
            var face = me.Facing.Length > 0.01f ? me.Facing.Normalized : new Vec2(1, 0);
            var r = p.Server.Server.Sim.Cast(1, id, me.Position + face * 4f);
            if (r.Ok) _castsOk++;
            else { _castsRejected++; if (_rejections.Count < 20) _rejections.Add(id + ": " + r.Rejection); }
        }

        private void Shot(string name)
        {
            var path = Path.Combine(_shotsDir, $"smoke-{name}.png");
            try { if (File.Exists(path)) File.Delete(path); ScreenCapture.CaptureScreenshot(path); _shotFiles.Add(path); }
            catch (Exception e) { _notes.Add("screenshot failed: " + e.Message); }
            _shot++;
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            float elapsed = _t0 < 0f ? 0f : Time.realtimeSinceStartup - _t0;
            int frames = _t0 < 0f ? 0 : Time.frameCount - _frames0;
            float avgFps = elapsed > 0f ? frames / elapsed : 0f;
            bool moved = _path > 1f;
            bool ok = _exceptions == 0 && moved && _castsOk >= 1;
            _exitCode = ok ? 0 : 1;
            var sb = new StringBuilder();
            sb.Append("{\n");
            void Num(string k, float v) => sb.Append($"  \"{k}\": {v.ToString("0.###", CultureInfo.InvariantCulture)},\n");
            void Int(string k, int v) => sb.Append($"  \"{k}\": {v},\n");
            void Arr(string k, IEnumerable<string> v) => sb.Append($"  \"{k}\": [{string.Join(", ", v.Select(s => "\"" + Esc(s) + "\""))}],\n");
            sb.Append($"  \"passed\": {(ok ? "true" : "false")},\n");
            Int("exitCode", _exitCode);
            Num("seconds", elapsed);
            Int("framesRendered", frames);
            Num("averageFps", avgFps);
            Num("minFps", _minFps == float.MaxValue ? 0f : _minFps);
            Num("avatarMovedCells", _path);
            Num("avatarMovedMetres", _path * 2f);
            Num("doorsOpenedAtSeconds", _doorsAt);
            Int("castsOk", _castsOk);
            Int("castsRejected", _castsRejected);
            Int("castsNotAttempted", _castQueue.Count);
            Int("exceptionsLogged", _exceptions);
            Int("errorsLogged", _errors);
            sb.Append($"  \"interactAvailable\": \"{Esc(_interactAvailable ?? "")}\",\n");
            Arr("rejections", _rejections);
            Arr("notes", _notes);
            Arr("screenshots", _shotFiles);
            sb.Append($"  \"unityVersion\": \"{Application.unityVersion}\"\n}}\n");
            try { File.WriteAllText(Path.Combine(_resultDir, "smoke-result.json"), sb.ToString()); }
            catch (Exception e) { Debug.LogWarning("AutoSmokeTest: could not write result: " + e.Message); }
            Debug.Log("AutoSmokeTest: " + (ok ? "PASS" : "FAIL") + $" frames={frames} fps={avgFps:0.0} moved={_path:0.0} cells castsOk={_castsOk}");
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
