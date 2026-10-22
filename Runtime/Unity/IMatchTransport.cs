using System;
using DungeonGen.Core.Net;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The seam between this package and whatever netcode you pick (Netcode for GameObjects,
    /// Mirror, Fishnet, a custom relay). Two messages carry the whole dungeon layer: the match
    /// descriptor once, then match events as they happen. Implement this for your transport and
    /// hand it to <see cref="DungeonMatchBootstrap"/>.
    /// </summary>
    public interface IMatchTransport
    {
        bool IsServer { get; }
        /// <summary>Server → all clients, reliable. A few hundred bytes, sent once when the match is created and to every late joiner.</summary>
        void BroadcastDescriptor(byte[] descriptorBytes);
        /// <summary>Server → all clients, reliable, ordered. Clients apply them in order.</summary>
        void BroadcastEvent(byte[] eventBytes);
        /// <summary>Server → one client, reliable: the full log so far, for a late joiner or reconnect.</summary>
        void SendLog(int clientId, byte[] logBytes);
        /// <summary>Any → the sender's team only, unreliable is fine: pings. Never reaches another team and never enters the log.</summary>
        void SendToTeam(int team, byte[] pingBytes);

        event Action<byte[]> DescriptorReceived;
        event Action<byte[]> EventReceived;
        event Action<byte[]> LogReceived;
        event Action<int> ClientJoined;
        event Action<byte[]> TeamMessageReceived;
    }
}
