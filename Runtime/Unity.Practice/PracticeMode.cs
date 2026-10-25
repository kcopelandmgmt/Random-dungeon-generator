using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// One developer, one machine: host a round in-process over the loopback transport, seat the local
    /// player, and seat one or two bot teams that fight with the lab's brain. Editor and development
    /// builds only (the assembly is excluded from release). Bots never enter the real queue.
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

        public LoopbackTransport Transport { get; private set; }
        public bool Hosting { get; private set; }

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
            Transport = new LoopbackTransport { IsServer = true, LocalTeam = 0, LocalPlayerId = 1 };
            Bootstrap.Attach(Transport);
            Server.Bootstrap = Bootstrap; Server.Builder = Builder; Server.Attach(Transport);
            World.Bootstrap = Bootstrap; World.Builder = Builder; World.LocalPlayerId = 1; World.Attach(Transport);
            if (Hud != null) { Hud.World = World; Hud.Bootstrap = Bootstrap; Hud.Attach(Transport); }
            var source = IntentSource as IIntentSource;
            if (IntentSource != null) IntentSource.SendMessage("Attach", Transport, SendMessageOptions.DontRequireReceiver);
            if (Bootstrap.Config == null) Bootstrap.Config = Builder.Config;
            Bootstrap.TeamSize = 3;
            Bootstrap.HostMatch(Seed);
            Transport.Pump();
            Server.StartRound();
            Server.Server.Sim.DevCasting = DevCasting;
            var cls = FightLab.ClassOf(LocalSpec);
            Server.AddPlayer(1, 0, cls, LocalSpec, PlayerStats.For(cls, GearTier, Bootstrap.Descriptor.Settings.Combat));
            int id = BotRules.BotIdBase;
            for (int b = 0; b < AllyBots; b++) Server.AddBot(id++, 0, BotSpecs[b % BotSpecs.Length], GearTier, null);   // stands and fights only
            for (int t = 1; t <= Mathf.Min(BotTeams, Bootstrap.Descriptor.TeamSpawnRooms.Count - 1); t++)
            {
                var squad = BotsRoam ? new BotSquad { Team = t } : null;
                for (int b = 0; b < BotsPerTeam; b++) Server.AddBot(id++, t, BotSpecs[(t * BotsPerTeam + b) % BotSpecs.Length], GearTier, squad);
            }
            Transport.Join();
            Hosting = true;
        }

        private void LateUpdate()
        {
            if (Hosting) Transport.Pump();
        }
    }
}
