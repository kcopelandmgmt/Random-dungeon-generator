using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DungeonGen.Core.Net;

namespace DungeonGen.Unity
{
    /// <summary>How this process takes part in a round: alone (the offline loopback round), hosting (server and a local player), joined to a host or a dedicated server, or a dedicated server (no local player).</summary>
    public enum NetRole { Offline, Host, Client, Dedicated }

    /// <summary>
    /// Control messages that are not part of the match itself (the four transport messages and intents are). One byte of kind,
    /// then the payload. Appended only.
    /// </summary>
    public enum NetControl : byte
    {
        /// <summary>Client → server: a <see cref="JoinRequest"/> (also a reconnect: same account and character).</summary>
        Join = 1,
        /// <summary>Server → client: you are player N (payload: int player id, bool in a round already).</summary>
        Welcome = 2,
        /// <summary>Server → client: the lobby changed (<see cref="LobbyState"/>).</summary>
        Lobby = 3,
        /// <summary>Server → client: the round is starting; load the round scene (payload: int seed).</summary>
        RoundStarting = 4,
        /// <summary>Server → one client: your round is over (<see cref="RoundResult"/>).</summary>
        RoundResult = 5,
        /// <summary>Server → client: you may not join (payload: string reason). The server disconnects you after it.</summary>
        Refused = 6,
        /// <summary>Client → server: leave the round now; it counts as a death (the pause menu's Leave).</summary>
        Forfeit = 7,
    }

    /// <summary>One player in the lobby: who, which team, connected or not. The host's own player is <see cref="Local"/>.</summary>
    public struct LobbySeat
    {
        public int PlayerId, Team;
        public string Name, ClassName;
        public bool Connected, Local;
    }

    /// <summary>
    /// The lobby every connected player sees before the round: the seats (player, team, name, class) and the round's settings (bot teams, team size).
    /// Teams fill up to the team size, then the next team (<see cref="TeamFor"/>); bots fill empty teams per the host's choice.
    /// </summary>
    public sealed class LobbyState
    {
        public const int DefaultTeams = 3;
        /// <summary>0 waiting in the lobby, 1 the round is starting or running.</summary>
        public byte Phase;
        public int BotTeams = 1, TeamSize = 3, Teams = DefaultTeams;
        public List<LobbySeat> Seats = new List<LobbySeat>();

        /// <summary>The team the next player joins: the first team with a free place, or -1 when every team is full.</summary>
        public static int TeamFor(IEnumerable<LobbySeat> seats, int teamSize, int teams)
        {
            var counts = new int[Math.Max(1, teams)];
            foreach (var s in seats) if (s.Team >= 0 && s.Team < counts.Length) counts[s.Team]++;
            for (int t = 0; t < counts.Length; t++) if (counts[t] < Math.Max(1, teamSize)) return t;
            return -1;
        }

        /// <summary>Re-seat every player in lobby order over <paramref name="teams"/> teams (the round's real team count), keeping the fill rule.</summary>
        public void Reseat(int teams)
        {
            Teams = Math.Max(1, teams);
            var placed = new List<LobbySeat>();
            foreach (var s in Seats.OrderBy(s => s.PlayerId))
            {
                var seat = s; seat.Team = TeamFor(placed, TeamSize, Teams);
                if (seat.Team >= 0) placed.Add(seat);
            }
            Seats = placed;
        }

        public int TeamOf(int playerId) { foreach (var s in Seats) if (s.PlayerId == playerId) return s.Team; return -1; }

        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Phase); w.Write(BotTeams); w.Write(TeamSize); w.Write(Teams);
                w.Write(Seats.Count);
                foreach (var s in Seats) { w.Write(s.PlayerId); w.Write(s.Team); w.Write(s.Name ?? ""); w.Write(s.ClassName ?? ""); w.Write(s.Connected); w.Write(s.Local); }
            }
            return ms.ToArray();
        }

        public static LobbyState FromBytes(byte[] b)
        {
            using var ms = new MemoryStream(b);
            using var r = new BinaryReader(ms);
            var l = new LobbyState { Phase = r.ReadByte(), BotTeams = r.ReadInt32(), TeamSize = r.ReadInt32(), Teams = r.ReadInt32() };
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) l.Seats.Add(new LobbySeat { PlayerId = r.ReadInt32(), Team = r.ReadInt32(), Name = r.ReadString(), ClassName = r.ReadString(), Connected = r.ReadBoolean(), Local = r.ReadBoolean() });
            return l;
        }
    }

    /// <summary>What to start a session with.</summary>
    public sealed class NetSessionOptions
    {
        public NetRole Role;
        /// <summary>Client: the server's address. Server: the address to listen on (empty: every interface; "127.0.0.1" keeps it to this machine).</summary>
        public string Address = "127.0.0.1";
        public ushort Port = NetSessions.DefaultPort;
        /// <summary>Client and host: this player's character and loadout. Null on a dedicated server.</summary>
        public JoinRequest Join;
        public int BotTeams = 1, TeamSize = 3, MaxPlayers = 12;
    }

    /// <summary>
    /// A networked session, from the menu through the lobby to the round and back: the netcode's half of online play. The Fishnet assembly
    /// implements it (<c>FishnetSession</c>) and registers a factory in <see cref="NetSessions"/>; this assembly never references Fishnet.
    /// A server session keeps the lobby (who joined, with which loadout, on which team), seats reconnects, and relays per-player messages; a client
    /// session joins, receives its player id and the lobby, and gets a fresh <see cref="IMatchTransport"/> for each round
    /// (<see cref="Transport"/>) that buffers the round's messages until the round scene is ready.
    /// </summary>
    public interface INetSession
    {
        NetRole Role { get; }
        bool IsServer { get; }
        /// <summary>Client: connected and welcomed. Server: listening.</summary>
        bool Connected { get; }
        /// <summary>A line for the lobby screen: listening on…, connecting to…, connection lost….</summary>
        string Status { get; }
        /// <summary>This process's player: the host's own (1), the id the server gave a client, -1 on a dedicated server or before the welcome.</summary>
        int LocalPlayerId { get; }
        LobbyState Lobby { get; }
        /// <summary>The current round's transport (made when the round starts), or null in the lobby.</summary>
        IMatchTransport Transport { get; }
        /// <summary>The seed the server announced for the round.</summary>
        int RoundSeed { get; }
        bool InRound { get; }

        event Action LobbyChanged;
        /// <summary>Everyone: the round is starting, load the round scene. Also raised on a client that reconnects into a running round.</summary>
        event Action RoundStarting;
        /// <summary>The session ended (stopped, refused, or the connection gave up), with a reason for the menu.</summary>
        event Action<string> Closed;
        /// <summary>Client: a message the server sent to this player only (<see cref="NetControl.RoundResult"/>).</summary>
        event Action<NetControl, byte[]> PlayerMessageReceived;

        // ---- server ----
        /// <summary>Server: the join request each seated player sent (the host's own player included).</summary>
        IReadOnlyDictionary<int, JoinRequest> Joins { get; }
        /// <summary>Server: a seated player's connection dropped (playerId).</summary>
        event Action<int> PlayerDropped;
        /// <summary>Server: a seated player came back during the round (playerId); the round seats their new connection with <see cref="SeatInRound"/>.</summary>
        event Action<int> PlayerRejoined;
        /// <summary>Server: a control message from a seated player (playerId, kind, payload), e.g. <see cref="NetControl.Forfeit"/>.</summary>
        event Action<int, NetControl, byte[]> ControlReceived;
        /// <summary>Server: freeze the lobby and tell everyone to load the round. Returns why not, or null.</summary>
        string StartRound(int seed);
        /// <summary>Server: the round seated this player on this team; their connection now receives the team's messages.</summary>
        void SeatInRound(int playerId, int team);
        /// <summary>Server: send one player a control message.</summary>
        void SendToPlayer(int playerId, NetControl kind, byte[] payload);
        /// <summary>Server: the round is over; back to the lobby for the next one (dedicated) with the same connections.</summary>
        void EndRound();
        bool IsPlayerConnected(int playerId);

        // ---- client ----
        void SendControl(NetControl kind, byte[] payload);
        /// <summary>Client: the round scene is ready; deliver what the round transport buffered.</summary>
        void RoundSceneReady();

        void Stop();
    }

    /// <summary>The registry of the netcode: the Fishnet assembly puts its factory here at load. Null factory: online play is not built in.</summary>
    public static class NetSessions
    {
        public const ushort DefaultPort = 7770;
        public const string DefaultAddress = "127.0.0.1";
        /// <summary>PlayerPrefs: the last address and port typed in the Join box.</summary>
        public const string LastAddressKey = "dg.net.lastaddress", LastPortKey = "dg.net.lastport";

        public static Func<NetSessionOptions, INetSession> Factory;
        public static bool Available => Factory != null;
        /// <summary>The running session, or null (offline).</summary>
        public static INetSession Current { get; private set; }

        public static INetSession Start(NetSessionOptions options)
        {
            if (Factory == null) throw new InvalidOperationException("Online play is not in this build (the Fishnet transport is missing).");
            Current?.Stop();
            Current = Factory(options);
            return Current;
        }

        /// <summary>A session for tests and tools that must not become <see cref="Current"/> (two in one process).</summary>
        public static INetSession Create(NetSessionOptions options)
        {
            if (Factory == null) throw new InvalidOperationException("Online play is not in this build (the Fishnet transport is missing).");
            return Factory(options);
        }

        public static void StopCurrent()
        {
            var s = Current; Current = null;
            s?.Stop();
        }

        /// <summary>Parse "host" or "host:port"; the port falls back to <paramref name="defaultPort"/>.</summary>
        public static (string host, ushort port) ParseAddress(string text, ushort defaultPort = DefaultPort)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return (DefaultAddress, defaultPort);
            int colon = text.LastIndexOf(':');
            if (colon > 0 && text.IndexOf(':') == colon && ushort.TryParse(text.Substring(colon + 1), out ushort p) && p > 0) return (text.Substring(0, colon), p);
            return (text, defaultPort);
        }
    }

    /// <summary>The menu's choice for the next networked round (the online counterpart of <see cref="PracticeLaunch"/>).</summary>
    public sealed class OnlineLaunch
    {
        public static OnlineLaunch Current;
        public NetRole Role;
        /// <summary>The local player's loadout as sent (null on a dedicated server): the client settles against these very objects.</summary>
        public DungeonGen.Core.Profile.Loadout Loadout;
        public int AccountLevel = 1;
    }
}
