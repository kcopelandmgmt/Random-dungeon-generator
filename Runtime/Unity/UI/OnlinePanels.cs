using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The menu's online screens (2026-10-26 online play), built from code like the rest: <see cref="BuildOnline"/> (host a round from this
    /// machine on a port, or join one at an address, default 127.0.0.1:7770, the last address remembered in PlayerPrefs) and
    /// <see cref="BuildLobby"/> (the players and their teams, the session's status, the host's bot teams and Start, Leave). A dedicated
    /// server needs no screen: <c>-batchmode -nographics -server -port 7770</c>.
    /// </summary>
    public static class OnlinePanels
    {
        public const string OnlineName = "OnlinePanel", LobbyName = "LobbyPanel";

        private static Text Header(RectTransform panel, string text)
        {
            var t = UiStyle.Label(panel, "Header", text, 40, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(1000f, 56f));
            UiStyle.Rule(panel, "HeaderRule", 520f, -96f);
            return t;
        }

        private static Button Btn(Transform parent, string name, string label, Action onClick, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int font = 28)
        {
            var b = UiStyle.MakeButton(parent, name, label, onClick, font);
            ((RectTransform)b.transform).Place(anchor, pivot, pos, size);
            return b;
        }

        /// <summary>Host or join. <paramref name="onShow"/> rebuilds a screen by name (the lobby once a session started).</summary>
        public static RectTransform BuildOnline(RectTransform parent, GameFlow flow, Action back, Action<string> setMessage)
        {
            var panel = UiStyle.Panel(parent, OnlineName);
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1180f, 700f));
            Header(panel, "PLAY ONLINE");
            bool available = NetSessions.Available;
            var intro = UiStyle.Label(panel, "Intro", available
                ? "Host a round from this machine and let friends join, or join a host or a dedicated server. Your character brings what it wears and its potions; what you carry out is yours, what you die with is gone."
                : "Online play is not in this build.", 21, UiStyle.TextDim, TextAnchor.UpperLeft);
            intro.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -120f), new Vector2(1060f, 70f));

            // Host.
            var hl = UiStyle.Label(panel, "HostLabel", "HOST", 28, UiStyle.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            hl.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(60f, -230f), new Vector2(200f, 40f));
            var portLabel = UiStyle.Label(panel, "PortLabel", "Port", 24, UiStyle.TextDim);
            portLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(60f, -290f), new Vector2(100f, 40f));
            var port = UiStyle.MakeInput(panel, "PortInput", NetSessions.DefaultPort.ToString(), 26);
            ((RectTransform)port.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(170f, -290f), new Vector2(200f, 54f));
            port.characterLimit = 5; port.contentType = InputField.ContentType.IntegerNumber;
            port.SetTextWithoutNotify(PlayerPrefs.GetInt(NetSessions.LastPortKey, NetSessions.DefaultPort).ToString());
            var host = Btn(panel, "HostButton", "Host a round", () =>
            {
                ushort p = ushort.TryParse(port.text, out var v) && v > 0 ? v : NetSessions.DefaultPort;
                var why = flow.HostOnline(p);
                if (why != null) { setMessage(why); UiSound.Play(UiSound.Error); }
            }, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(400f, -290f), new Vector2(320f, 60f));
            host.interactable = available;
            var hostNote = UiStyle.Label(panel, "HostNote", "Others join at this machine's address. Across the internet the port must be forwarded (UDP).", 18, UiStyle.TextFaint, TextAnchor.MiddleLeft);
            hostNote.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(60f, -340f), new Vector2(1060f, 30f));

            // Join.
            var jl = UiStyle.Label(panel, "JoinLabel", "JOIN", 28, UiStyle.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            jl.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(60f, -410f), new Vector2(200f, 40f));
            var addrLabel = UiStyle.Label(panel, "AddressLabel", "Address", 24, UiStyle.TextDim);
            addrLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(60f, -470f), new Vector2(110f, 40f));
            var addr = UiStyle.MakeInput(panel, "AddressInput", NetSessions.DefaultAddress + ":" + NetSessions.DefaultPort, 26);
            ((RectTransform)addr.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(170f, -470f), new Vector2(520f, 54f));
            addr.characterLimit = 64;
            string last = PlayerPrefs.GetString(NetSessions.LastAddressKey, NetSessions.DefaultAddress);
            int lastPort = PlayerPrefs.GetInt(NetSessions.LastPortKey, NetSessions.DefaultPort);
            addr.SetTextWithoutNotify(lastPort == NetSessions.DefaultPort ? last : last + ":" + lastPort);
            var join = Btn(panel, "JoinButton", "Join", () =>
            {
                var (h, p) = NetSessions.ParseAddress(addr.text);
                var why = flow.JoinOnline(h, p);
                if (why != null) { setMessage(why); UiSound.Play(UiSound.Error); }
            }, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(720f, -470f), new Vector2(260f, 60f));
            join.interactable = available;
            var ded = UiStyle.Label(panel, "DedicatedNote", "A dedicated server is this game run with  -batchmode -nographics -server -port 7770.", 18, UiStyle.TextFaint, TextAnchor.MiddleLeft);
            ded.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(60f, -520f), new Vector2(1060f, 30f));

            Btn(panel, "BackButton", "Back", back, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 36f), new Vector2(260f, 64f));
            return panel;
        }

        /// <summary>The lobby: teams and players, the status line, the host's bot teams and Start, Leave.</summary>
        public static RectTransform BuildLobby(RectTransform parent, GameFlow flow)
        {
            var panel = UiStyle.Panel(parent, LobbyName);
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1180f, 740f));
            var session = flow.Session;
            bool server = session != null && session.IsServer;
            Header(panel, server ? "YOUR LOBBY" : "LOBBY");
            var status = UiStyle.Label(panel, "Status", session?.Status ?? "Not connected.", 22, UiStyle.TextDim, TextAnchor.MiddleCenter, FontStyle.Italic);
            status.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(1060f, 34f));

            var lobby = session?.Lobby ?? new LobbyState();
            int teams = Math.Max(1, lobby.Teams);
            float colW = 1060f / teams;
            for (int t = 0; t < teams; t++)
            {
                var head = UiStyle.Label(panel, "Team-" + (t + 1), $"TEAM {t + 1}", 26, UiStyle.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
                head.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f + t * colW, -170f), new Vector2(colW - 20f, 40f));
                float y = -220f;
                var seats = lobby.Seats.Where(s => s.Team == t).OrderBy(s => s.PlayerId).ToList();
                foreach (var s in seats)
                {
                    bool me = session != null && s.PlayerId == session.LocalPlayerId;
                    string tag = (s.Local ? "  host" : "") + (me ? "  you" : "") + (s.Connected ? "" : "  (away)");
                    var l = UiStyle.Label(panel, "Seat-" + s.PlayerId, $"<b>{s.Name}</b>  {UiStyle.Colour(s.ClassName + tag, UiStyle.TextDim)}", 22, me ? UiStyle.TextMain : UiStyle.TextDim, TextAnchor.MiddleLeft);
                    l.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f + t * colW, y), new Vector2(colW - 20f, 34f));
                    y -= 40f;
                }
                if (seats.Count == 0)
                {
                    var e = UiStyle.Label(panel, "Empty-" + (t + 1), "open" + (t > 0 && server ? "  ·  bots if unfilled" : ""), 20, UiStyle.TextFaint, TextAnchor.MiddleLeft, FontStyle.Italic);
                    e.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f + t * colW, y), new Vector2(colW - 20f, 34f));
                }
            }
            var rule = UiStyle.Label(panel, "Rule", $"Teams fill up to {lobby.TeamSize} players, then the next. {(server ? "You choose how many empty teams get bots." : "The host chooses the bots.")}", 19, UiStyle.TextFaint, TextAnchor.MiddleCenter);
            rule.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(1060f, 30f));

            if (server)
            {
                var bl = UiStyle.Label(panel, "BotsLabel", "Bot teams", 24, UiStyle.TextDim, TextAnchor.MiddleRight);
                bl.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(1f, 0.5f), new Vector2(-150f, 140f), new Vector2(300f, 50f));
                for (int n = 0; n <= 3; n++)
                {
                    int k = n;
                    var b = Btn(panel, "LobbyBots-" + n, n.ToString(), () => { lobby.BotTeams = k; flow.BotTeams = Math.Max(1, k); }, new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(-130f + n * 80f, 140f), new Vector2(64f, 56f), 26);
                    b.SetSelected(lobby.BotTeams == n);
                }
                var start = Btn(panel, "StartRoundButton", "Start the round", () =>
                {
                    var why = flow.StartOnlineRound();
                    if (why != null) { flow.Message = why; UiSound.Play(UiSound.Error); }
                }, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 36f), new Vector2(380f, 64f), 30);
                start.GetComponent<UiButtonSound>().ClickKey = UiSound.Confirm;
                start.interactable = lobby.Seats.Count > 0 && lobby.Phase == 0;
            }
            else
            {
                var wait = UiStyle.Label(panel, "Waiting", session != null && session.LocalPlayerId > 0 ? "Waiting for the host to start the round…" : "Joining…", 26, UiStyle.Gold, TextAnchor.MiddleRight, FontStyle.Italic);
                wait.rectTransform.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 46f), new Vector2(700f, 50f));
            }
            Btn(panel, "LeaveLobbyButton", "Leave", flow.LeaveLobby, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 36f), new Vector2(260f, 64f));
            return panel;
        }
    }
}
