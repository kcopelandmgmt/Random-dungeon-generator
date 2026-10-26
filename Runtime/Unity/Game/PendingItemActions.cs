using System.Collections.Generic;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The local player's item choices made in the UI (take this item, drop that one, Need on a roll), waiting to ride the next
    /// intent. The UI assembly enqueues; <c>LocalPlayerController</c> (the only input reader) copies them into the intent it
    /// sends with <see cref="ApplyTo"/>, so clicks merge with movement and Space and the server validates them like any intent
    /// (<see cref="PlayerIntent.TakeItemId"/>, <see cref="PlayerIntent.DropItemId"/>, <see cref="PlayerIntent.RollItemId"/>).
    /// Tests with the controller switched off drain it themselves the same way.
    /// </summary>
    public static class PendingItemActions
    {
        public enum Kind : byte { Take, Drop, Roll }

        public readonly struct Action
        {
            public readonly Kind Kind; public readonly int ItemId; public readonly LootRollChoice Choice;
            public Action(Kind kind, int itemId, LootRollChoice choice = LootRollChoice.Pass) { Kind = kind; ItemId = itemId; Choice = choice; }
            public override string ToString() => Kind == Kind.Roll ? $"{Kind} {ItemId} {Choice}" : $"{Kind} {ItemId}";
        }

        private static readonly List<Action> _queue = new List<Action>();

        public static int Count => _queue.Count;
        public static IReadOnlyList<Action> Queued => _queue;

        public static void Take(int itemId) { if (itemId != 0) _queue.Add(new Action(Kind.Take, itemId)); }
        public static void Drop(int itemId) { if (itemId != 0) _queue.Add(new Action(Kind.Drop, itemId)); }
        public static void Roll(int itemId, LootRollChoice choice) { if (itemId != 0) _queue.Add(new Action(Kind.Roll, itemId, choice)); }
        public static void Clear() => _queue.Clear();

        /// <summary>
        /// Copy the oldest waiting choices into the intent: at most one of each kind per intent, in the order they were made
        /// (a second take waits for the next send). Returns true if anything was copied.
        /// </summary>
        public static bool ApplyTo(ref PlayerIntent intent)
        {
            bool any = false;
            while (_queue.Count > 0)
            {
                var a = _queue[0];
                if (a.Kind == Kind.Take) { if (intent.TakeItemId != 0) break; intent.TakeItemId = a.ItemId; }
                else if (a.Kind == Kind.Drop) { if (intent.DropItemId != 0) break; intent.DropItemId = a.ItemId; }
                else { if (intent.RollItemId != 0) break; intent.RollItemId = a.ItemId; intent.RollChoice = (byte)a.Choice; }
                _queue.RemoveAt(0);
                any = true;
            }
            return any;
        }
    }
}
