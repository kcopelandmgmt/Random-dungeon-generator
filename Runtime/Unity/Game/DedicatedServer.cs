using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The dedicated server, inert unless the player runs with <c>-server</c> (normally <c>-batchmode -nographics -server -port 7770</c>; a
    /// Windows dedicated-server build when the editor has that module). No profile, no local player, no menu: it listens
    /// (<see cref="GameFlow.StartDedicated"/>, every interface unless <c>-bind &lt;address&gt;</c>), and starts a round
    /// <c>-autostart &lt;seconds&gt;</c> (default 30) after the first player joined, or at once when <c>-minplayers &lt;n&gt;</c> are in. Bot teams fill
    /// empty teams (<c>-botteams &lt;n&gt;</c>, default 1). After a round it goes back to the lobby with everyone still connected and starts again.
    /// </summary>
    public sealed class DedicatedServer : MonoBehaviour
    {
        public const string Flag = "-server";
        public float AutoStartSeconds = 30f;
        public int MinPlayers = 99;

        private float _firstJoinAt = -1f;
        private float _nextLog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!LaunchFlags.Has(Flag) || FindFirstObjectByType<DedicatedServer>() != null) return;
            var go = new GameObject("DedicatedServer");
            DontDestroyOnLoad(go);
            go.AddComponent<DedicatedServer>();
        }

        public static ushort PortFlag => ushort.TryParse(LaunchFlags.Value("-port"), out var p) && p > 0 ? p : NetSessions.DefaultPort;

        private void Start()
        {
            Application.targetFrameRate = 60;   // a server need not spin the CPU
            if (float.TryParse(LaunchFlags.Value("-autostart"), out float s) && s >= 0f) AutoStartSeconds = s;
            if (int.TryParse(LaunchFlags.Value("-minplayers"), out int m) && m > 0) MinPlayers = m;
            int bots = int.TryParse(LaunchFlags.Value("-botteams"), out int b) ? b : 1;
            var why = GameFlow.Ensure().StartDedicated(PortFlag, LaunchFlags.Value("-bind"), bots);
            if (why != null) { Debug.LogError("DedicatedServer: " + why); Application.Quit(2); return; }
            Debug.Log($"DedicatedServer: up on port {PortFlag}; a round starts {AutoStartSeconds:0} s after the first player joins (or at {MinPlayers} players).");
        }

        private void Update()
        {
            var flow = GameFlow.Instance;
            var session = flow != null ? flow.Session : null;
            if (session == null) return;
            if (Time.unscaledTime >= _nextLog) { _nextLog = Time.unscaledTime + 15f; Debug.Log($"DedicatedServer: {flow.Phase}, {session.Lobby.Seats.Count} in the lobby. {session.Status}"); }
            if (flow.Phase != FlowPhase.Lobby) { _firstJoinAt = -1f; return; }
            int players = session.Lobby.Seats.Count;
            if (players == 0) { _firstJoinAt = -1f; return; }
            if (_firstJoinAt < 0f) _firstJoinAt = Time.unscaledTime;
            if (players >= MinPlayers || Time.unscaledTime - _firstJoinAt >= AutoStartSeconds)
            {
                var why = flow.StartOnlineRound();
                if (why != null) Debug.LogWarning("DedicatedServer: could not start the round: " + why);
                else Debug.Log($"DedicatedServer: round started with {players} player(s).");
                _firstJoinAt = -1f;
            }
        }
    }
}
