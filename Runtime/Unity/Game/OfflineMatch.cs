using System;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The offline round host, in release builds: one machine plays against bot teams. It hosts the round in-process over the
    /// <see cref="LoopbackTransport"/>, seats the local player (with the character's <see cref="DungeonGen.Core.Profile.Loadout"/>
    /// when the menu launched the round, else a class at <see cref="GearTier"/>), and seats roaming bot teams (one
    /// <see cref="BotSquad"/> each). <see cref="GameFlow"/> drives it after the round scene loads (<see cref="HostInScene"/>);
    /// the dev-only <c>PracticeMode</c> wraps it and adds dev casting, the ally bot, hosting on Play and the audition.
    /// Bots never enter the real queue. Because the host is local and alone, the round may pause (<see cref="SetPaused"/>);
    /// a networked round never does.
    /// </summary>
    public sealed class OfflineMatch : MonoBehaviour
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
        [Range(0, 3)] public int BotTeams = 1;
        public int BotsPerTeam = 2;
        public int GearTier = 1;
        public string LocalSpec = "Sentinel";
        public string[] BotSpecs = { "Berserker", "Warder", "Sharpshooter" };
        [Tooltip("Roaming bots play the round: explore, loot, fight what they meet, extract. Off, they stand at their spawn and fight only what comes.")]
        public bool BotsRoam = true;
        [Tooltip("Dev casting: no cooldowns, no costs, ultimates always armed. Off in a real round; the practice wrapper turns it on.")]
        public bool DevCasting;
        [Tooltip("Bots seated on the local team, standing at the spawn.")]
        [Range(0, 2)] public int AllyBots;

        /// <summary>The hosting match in the loaded round scene, or null.</summary>
        public static OfflineMatch Current { get; private set; }

        public LoopbackTransport Transport { get; private set; }
        public bool Hosting { get; private set; }
        /// <summary>Offline: the only client is this process, so the round may pause. Always true for this host.</summary>
        public bool IsOffline => Hosting && Transport != null;
        public bool Paused { get; private set; }
        /// <summary>The launch the round was hosted from (null for a greybox, -autotest or test round).</summary>
        public PracticeLaunch Launch { get; private set; }

        /// <summary>After the launch's settings are applied, before anything is hosted: a wrapper may adjust the settings.</summary>
        public event Action<OfflineMatch> Preparing;
        /// <summary>After the round is hosted and every player and bot is seated.</summary>
        public event Action<OfflineMatch> Hosted;

        private void Awake() { if (Current == null) Current = this; }

        private void OnDestroy()
        {
            if (Paused) SetPaused(false);
            if (Current == this) Current = null;
        }

        /// <summary>
        /// The round scene's host: the one already there, or one added to the bootstrap's object and wired from the scene
        /// (a release build has no practice wrapper in the scene). Null when the scene has no round in it.
        /// </summary>
        public static OfflineMatch HostInSceneOrNull() => Current != null ? Current : FindFirstObjectByType<OfflineMatch>();

        public static OfflineMatch HostInScene()
        {
            var m = HostInSceneOrNull();
            if (m != null) { m.WireFromScene(); return m; }
            var bootstrap = FindFirstObjectByType<DungeonMatchBootstrap>();
            if (bootstrap == null) return null;
            m = bootstrap.gameObject.AddComponent<OfflineMatch>();
            m.WireFromScene();
            return m;
        }

        /// <summary>Fill any missing reference from the scene.</summary>
        public void WireFromScene()
        {
            if (Bootstrap == null) Bootstrap = FindFirstObjectByType<DungeonMatchBootstrap>();
            if (Builder == null) Builder = Bootstrap != null && Bootstrap.Builder != null ? Bootstrap.Builder : FindFirstObjectByType<DungeonBuilder>();
            if (Server == null) Server = FindFirstObjectByType<ServerSimulation>();
            if (World == null) World = FindFirstObjectByType<ClientWorldView>();
            if (Hud == null) Hud = FindFirstObjectByType<PlayerHud>();
            if (Camera == null) Camera = FindFirstObjectByType<TopDownCamera>();
            if (IntentSource == null)
                foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (mb is IIntentSource) { IntentSource = mb; break; }
        }

        [ContextMenu("Host offline round")]
        public void Host()
        {
            if (Hosting) return;
            WireFromScene();
            if (Bootstrap == null || Builder == null || Server == null || World == null)
            {
                Debug.LogError("OfflineMatch: the round scene is missing its bootstrap, builder, server simulation or world view.");
                return;
            }
            Current = this;
            var launch = Launch = PracticeLaunch.Current;   // the menu's choice; null keeps the inspector defaults
            if (launch != null)
            {
                Seed = launch.Seed; BotTeams = Mathf.Clamp(launch.BotTeams, 0, 3); BotsPerTeam = Mathf.Max(1, launch.BotsPerTeam);
                DevCasting = launch.DevCasting; AllyBots = Mathf.Clamp(launch.AllyBots, 0, 2);
                if (launch.Loadout != null) LocalSpec = launch.Loadout.Spec;
            }
            Preparing?.Invoke(this);

            Transport = new LoopbackTransport { IsServer = true, LocalTeam = 0, LocalPlayerId = GameFlow.LocalPlayerId };
            Bootstrap.Attach(Transport);
            Server.Bootstrap = Bootstrap; Server.Builder = Builder; Server.Attach(Transport);
            World.Bootstrap = Bootstrap; World.Builder = Builder; World.LocalPlayerId = GameFlow.LocalPlayerId; World.Attach(Transport);
            if (Hud != null) { Hud.World = World; Hud.Bootstrap = Bootstrap; Hud.Attach(Transport); }
            if (IntentSource != null) IntentSource.SendMessage("Attach", Transport, SendMessageOptions.DontRequireReceiver);
            if (Bootstrap.Config == null) Bootstrap.Config = Builder.Config;
            Bootstrap.TeamSize = 3;
            Bootstrap.HostMatch(Seed);
            Transport.Pump();
            Server.StartRound();
            Server.Server.Sim.DevCasting = DevCasting;

            int me = GameFlow.LocalPlayerId;
            var cls = launch?.Loadout != null ? launch.Loadout.Character.Class : FightLab.ClassOf(LocalSpec);
            if (launch?.Loadout != null)
            {
                // The character's own gear: the sheet it gives, and every worn item registered so a death loses it and an extraction keeps it.
                try { Server.Server.AddPlayerWithLoadout(me, 0, launch.Loadout, launch.AccountLevel); }
                catch (InvalidOperationException e)
                {
                    Debug.LogWarning("OfflineMatch: the loadout was refused (" + e.Message + "); seating the class at tier " + GearTier + " instead.");
                    Server.AddPlayer(me, 0, cls, LocalSpec, PlayerStats.For(cls, GearTier, Bootstrap.Descriptor.Settings.Combat));
                }
            }
            else Server.AddPlayer(me, 0, cls, LocalSpec, PlayerStats.For(cls, GearTier, Bootstrap.Descriptor.Settings.Combat));

            int id = BotRules.BotIdBase;
            for (int b = 0; b < AllyBots; b++) Server.AddBot(id++, 0, BotSpecs[b % BotSpecs.Length], GearTier, null);   // stands and fights only
            for (int t = 1; t <= Mathf.Min(BotTeams, Bootstrap.Descriptor.TeamSpawnRooms.Count - 1); t++)
            {
                var squad = BotsRoam ? new BotSquad { Team = t } : null;
                for (int b = 0; b < BotsPerTeam; b++) Server.AddBot(id++, t, BotSpecs[(t * BotsPerTeam + b) % BotSpecs.Length], GearTier, squad);
            }
            Transport.Join();
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if (mb is IRoundUi ui) ui.Attach(Transport);
            Hosting = true;
            Hosted?.Invoke(this);
        }

        /// <summary>
        /// Pause or resume the offline round: the server stops stepping and <c>Time.timeScale</c> goes to 0 so effects and
        /// animations freeze (the UI runs on unscaled time). Refused (returns false) when this host is not hosting.
        /// </summary>
        public bool SetPaused(bool paused)
        {
            if (paused && !IsOffline) return false;
            Paused = paused;
            if (Server != null) Server.Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            return true;
        }

        private void LateUpdate()
        {
            if (Hosting) Transport.Pump();
        }
    }
}
