using System.Collections.Generic;
using DungeonGen.Core.Net;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The local player's shrine choices made in the UI (arm the ultimate, resurrect a teammate, buy a consumable), waiting to ride the next
    /// intent, as <see cref="PendingItemActions"/> does for items. The shrine panel enqueues; <c>LocalPlayerController</c> copies one choice per
    /// intent into <see cref="PlayerIntent.ShrineAction"/> / <see cref="PlayerIntent.ShrineArg"/> with <see cref="ApplyTo"/>; the server checks it
    /// against <see cref="MatchServer.ShrineOptionsFor"/> and raises the validated event. Tests with the controller switched off drain it themselves.
    /// </summary>
    public static class PendingShrineActions
    {
        public readonly struct Choice
        {
            public readonly ShrineAction Action; public readonly int Arg;
            public Choice(ShrineAction action, int arg) { Action = action; Arg = arg; }
            public override string ToString() => $"{Action} {Arg}";
        }

        private static readonly List<Choice> _queue = new List<Choice>();

        public static int Count => _queue.Count;
        public static IReadOnlyList<Choice> Queued => _queue;

        /// <summary>Arm your ultimate (two souls).</summary>
        public static void Arm() => _queue.Add(new Choice(ShrineAction.ArmUltimate, 0));
        /// <summary>Resurrect this dead teammate.</summary>
        public static void Resurrect(int playerId) => _queue.Add(new Choice(ShrineAction.Resurrect, playerId));
        public static void Clear() => _queue.Clear();

        /// <summary>Copy the oldest waiting choice into the intent (one per intent; the next waits for the next send). Returns true if one was copied.</summary>
        public static bool ApplyTo(ref PlayerIntent intent)
        {
            if (_queue.Count == 0 || intent.ShrineAction != 0) return false;
            var c = _queue[0];
            _queue.RemoveAt(0);
            intent.ShrineAction = (byte)c.Action; intent.ShrineArg = c.Arg;
            return true;
        }
    }
}
