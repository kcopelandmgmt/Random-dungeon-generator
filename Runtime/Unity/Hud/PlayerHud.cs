using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Greybox HUD for the local player: health and resource bars, the five ability slots with cooldowns,
    /// the channel bar, the downed timer, the round clock, and the last announcement. OnGUI, to be
    /// replaced by real UI; it reads only the snapshot the server sent this player.
    /// </summary>
    public sealed class PlayerHud : MonoBehaviour
    {
        public ClientWorldView World;
        public DungeonMatchBootstrap Bootstrap;
        public float AnnouncementSeconds = 6f;
        private string _announcement; private float _announcedAt = -100f;
        private static Texture2D _white;

        private IMatchTransport _transport;

        public void Attach(IMatchTransport transport)
        {
            if (transport == null || ReferenceEquals(transport, _transport)) return;   // attached twice (SendMessage reaches every component on the object): once is enough
            _transport = transport;
            transport.EventReceived += bytes =>
            {
                using var ms = new System.IO.MemoryStream(bytes); using var r = new System.IO.BinaryReader(ms);
                var e = MatchEvent.Read(r);
                if (Bootstrap?.State == null) return;
                var a = Announcer.For(e, Bootstrap.State.Layout);
                if (a.HasValue) { _announcement = a.Value.Text; _announcedAt = Time.time; }
            };
        }

        private void Bar(Rect r, float frac, Color c, string label)
        {
            GUI.color = new Color(0.15f, 0.13f, 0.125f, 1f); GUI.DrawTexture(r, _white);   // an inset track that still reads when empty
            float w = (r.width - 2f) * Mathf.Clamp01(frac);
            GUI.color = c; GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, w, r.height - 2f), _white);
            GUI.color = new Color(1f, 1f, 1f, 0.14f); GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, w, Mathf.Max(1f, (r.height - 2f) * 0.35f)), _white);   // a soft highlight along the top
            GUI.color = Color.white; GUI.Label(r, label, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
        }

        private void OnGUI()
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            if (World == null || !World.Self.HasValue) return;
            var self = World.Self.Value;
            float w = 320f, x = 16f, y = Screen.height - 110f;
            Bar(new Rect(x, y, w, 22f), self.MaxHealth > 0f ? self.Health / self.MaxHealth : 0f, new Color(0.8f, 0.15f, 0.15f), $"{self.Health:0} / {self.MaxHealth:0}");
            Bar(new Rect(x, y + 26f, w, 14f), self.MaxResource > 0f ? self.Resource / self.MaxResource : 0f, new Color(0.2f, 0.5f, 0.9f), $"{self.Resource:0}");
            // Ability slots: Basic, Bolt, Zone, Mobility, Ultimate in book order.
            string[] keys = { "LMB", "RMB", "Q", "E", "R" };
            for (int i = 0; i < Mathf.Min(5, self.CooldownRemaining?.Length ?? 0); i++)
            {
                var r = new Rect(x + i * 52f, y + 46f, 48f, 48f);
                float cd = self.CooldownRemaining[i];
                bool locked = i == 4 && !self.UltimateArmed;
                GUI.color = locked ? new Color(0.2f, 0.2f, 0.2f, 0.8f) : cd > 0f ? new Color(0.3f, 0.3f, 0.3f, 0.9f) : (i == 4 ? new Color(1f, 0.85f, 0.2f, 0.9f) : new Color(0.5f, 0.5f, 0.55f, 0.9f));
                GUI.DrawTexture(r, _white); GUI.color = Color.white;
                GUI.Label(r, locked ? "2 souls" : cd > 0f ? $"{cd:0.0}" : keys[i], new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
            }
            if (!string.IsNullOrEmpty(self.ChannelName)) Bar(new Rect(Screen.width / 2f - 120f, Screen.height / 2f + 60f, 240f, 16f), self.ChannelFraction, new Color(0.9f, 0.8f, 0.3f), self.ChannelName);
            if (self.DownedRemaining > 0f) GUI.Label(new Rect(Screen.width / 2f - 120f, Screen.height / 2f - 40f, 240f, 30f), $"DOWNED  {self.DownedRemaining:0}s", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
            if (Bootstrap?.State != null)
            {
                var st = Bootstrap.State;
                float remaining = Mathf.Max(0f, st.Descriptor.Rules.RoundSeconds - st.Time);
                string clock = $"{(int)remaining / 60:0}:{(int)remaining % 60:00}" + (st.ExtractsOpen ? "  extracts open" : $"  extracts in {Mathf.Max(0f, st.Descriptor.Rules.ExtractsOpenAfterSeconds - st.Time):0}s") + (st.BossExitOpen ? "  EXIT OPEN" : "");
                GUI.Label(new Rect(Screen.width - 340f, 44f, 330f, 24f), clock, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 18 });
                if (st.PlayerTeam.TryGetValue(World.LocalPlayerId, out _)) GUI.Label(new Rect(Screen.width - 340f, 68f, 330f, 24f), $"souls {st.SoulsOf(World.LocalPlayerId)}  silver {(st.PlayerSilver.TryGetValue(World.LocalPlayerId, out int sv) ? sv : 0)}", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 18 });
            }
            if (Time.time - _announcedAt < AnnouncementSeconds && !string.IsNullOrEmpty(_announcement))
                GUI.Label(new Rect(Screen.width / 2f - 300f, 60f, 600f, 30f), _announcement, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
        }
    }
}
