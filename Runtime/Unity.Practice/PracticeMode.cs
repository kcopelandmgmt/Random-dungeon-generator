using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The dev wrapper around the release <see cref="OfflineMatch"/>: hosts the round the moment Play starts (greybox, -autotest,
    /// the PlayMode suite), turns dev casting on, seats an ally bot, offers an OnGUI host button, and hides the effects
    /// audition for a menu-launched round. Editor and development builds only (the assembly is excluded from release).
    /// The hosting itself lives in OfflineMatch, which this keeps in step with its inspector settings.
    /// </summary>
    public sealed class PracticeMode : MonoBehaviour
    {
        public DungeonMatchBootstrap Bootstrap;
        public DungeonBuilder Builder;
        public ServerSimulation Server;
        public ClientWorldView World;
        public PlayerHud Hud;
        public TopDownCamera Camera;
        [Tooltip("The component that produces the local player's intents (LocalPlayerController from the Input assembly, or anything implementing IIntentSource).")]
        public MonoBehaviour IntentSource;
        public int Seed = 7;
        [Range(2, 8)] public int Teams = 4;
        [Range(0, 3)] public int BotTeams = 1;
        public int BotsPerTeam = 2;
        public int GearTier = 1;
        public string LocalSpec = "Sentinel";
        public string[] BotSpecs = { "Berserker", "Warder", "Sharpshooter" };
        [Tooltip("Roaming bots play the round: explore, loot, fight what they meet, extract. Off, they stand at their spawn and fight only what comes.")]
        public bool BotsRoam = true;
        [Tooltip("Host the round the moment Play starts, so the greybox needs no click. Off, use the on-screen button or the component's context menu.")]
        public bool HostOnPlay = true;
        [Tooltip("Dev casting: no cooldowns, no costs, ultimates always armed, any spec's ability castable. For trying abilities and effects. Off for a real practice fight.")]
        public bool DevCasting = true;
        [Tooltip("Bots seated on your own team, standing at the spawn. One gives heals and ally buffs something to land on.")]
        [Range(0, 2)] public int AllyBots = 1;

        /// <summary>The release host this wraps (on the same object; added when the scene has none).</summary>
        public OfflineMatch Match { get; private set; }
        public LoopbackTransport Transport => Match != null ? Match.Transport : null;
        public bool Hosting => Match != null && Match.Hosting;

        private void Awake() => EnsureMatch();

        private OfflineMatch EnsureMatch()
        {
            if (Match != null) return Match;
            Match = GetComponent<OfflineMatch>();
            if (Match == null) Match = gameObject.AddComponent<OfflineMatch>();
            Match.Preparing += OnPreparing;
            CopyReferences(Match);
            return Match;
        }

        private void OnDestroy()
        {
            if (Match == null) return;
            Match.Preparing -= OnPreparing;
        }

        private void OnGUI()
        {
            if (Hosting) return;
            GUILayout.BeginArea(new Rect(20f, 20f, 360f, 300f), GUI.skin.box);
            GUILayout.Label("PRACTICE (dev only)");
            GUILayout.Label($"seed {Seed}   teams {Teams}   bot teams {BotTeams} x {BotsPerTeam}   tier {GearTier}   you: {LocalSpec}");
            if (GUILayout.Button("Host practice round")) Host();
            if (GUILayout.Button("New seed")) Seed = Random.Range(1, 100000);
            GUILayout.EndArea();
        }

        private void Start()
        {
            if (HostOnPlay && !Hosting) Host();
        }

        [ContextMenu("Host practice round")]
        public void Host()
        {
            if (Hosting) return;
            var m = EnsureMatch();
            CopyReferences(m);
            m.Seed = Seed; m.BotTeams = BotTeams; m.BotsPerTeam = BotsPerTeam; m.GearTier = GearTier; m.LocalSpec = LocalSpec;
            m.BotSpecs = BotSpecs; m.BotsRoam = BotsRoam; m.DevCasting = DevCasting; m.AllyBots = AllyBots;
            m.Host();
        }

        /// <summary>Whoever hosts (this, or GameFlow for a menu round), the launch's settings land here too, so the inspector shows the round.</summary>
        private void OnPreparing(OfflineMatch m)
        {
            Seed = m.Seed; BotTeams = m.BotTeams; BotsPerTeam = m.BotsPerTeam; LocalSpec = m.LocalSpec; DevCasting = m.DevCasting; AllyBots = m.AllyBots;
            if (m.Launch != null && m.Launch.HideDevPanels)
                foreach (var a in FindObjectsByType<VfxAudition>(FindObjectsSortMode.None)) a.Open = false;
        }

        private void CopyReferences(OfflineMatch m)
        {
            if (Bootstrap != null) m.Bootstrap = Bootstrap;
            if (Builder != null) m.Builder = Builder;
            if (Server != null) m.Server = Server;
            if (World != null) m.World = World;
            if (Hud != null) m.Hud = Hud;
            if (Camera != null) m.Camera = Camera;
            if (IntentSource != null) m.IntentSource = IntentSource;
        }
    }
}
