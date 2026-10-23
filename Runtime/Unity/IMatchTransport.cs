using System;
using DungeonGen.Core.Net;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The seam between this package and the netcode (Fishnet by decision). Three server → team
    /// messages carry the whole dungeon layer: the match descriptor once, a <see cref="TeamView"/>
    /// whenever the team's knowledge changes, and the events a team is allowed to hear about as
    /// notifications (sounds, a line of UI). Clients never receive the event log or another team's
    /// view; see <see cref="EventVisibility"/>. Pings go team to team and never touch the server state.
    /// </summary>
    public interface IMatchTransport
    {
        bool IsServer { get; }
        /// <summary>Server → all clients, reliable. A few hundred bytes, once when the match is created and to every joiner.</summary>
        void BroadcastDescriptor(byte[] descriptorBytes);
        /// <summary>Server → one team's clients, reliable, ordered: an event that team may know happened.</summary>
        void SendEventToTeam(int team, byte[] eventBytes);
        /// <summary>Server → one team's clients, reliable, ordered: the team's current view. Also the reconnect message.</summary>
        void SendViewToTeam(int team, byte[] viewBytes);
        /// <summary>Any → the sender's team only, unreliable is fine: pings. Never reaches another team and never enters the log.</summary>
        void SendToTeam(int team, byte[] pingBytes);
        event Action<byte[]> DescriptorReceived;
        event Action<byte[]> EventReceived;
        event Action<byte[]> ViewReceived;
        /// <summary>clientId, team: a client connected or reconnected and needs the descriptor and its team's view.</summary>
        event Action<int, int> ClientJoined;
        event Action<byte[]> TeamMessageReceived;
    }
}
