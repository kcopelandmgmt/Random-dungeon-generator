using System.Collections.Generic;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// What one player's item screens may show, built on the host by <see cref="MatchServer.ItemsFor"/>: the items in reach
    /// where they stand (an opened chest of the room, an opened corpse within reach, the room's floor), what they carry and
    /// its weight against their capacity, and the group rolls they may answer with the time left. Only that player's own
    /// knowledge: nothing about another team's pockets. The <see cref="Item"/> references are host objects (via
    /// <see cref="MatchServer.ItemById"/>); a network client gets less: <see cref="TeamView.ItemInfo"/> carries id, room,
    /// item level, value, rarity and name only (no base, affixes or weight), corpses list bare ids, and a team's carried
    /// items are not in the view at all. A remote client's panels need that view extended before they can show this much.
    /// </summary>
    public sealed class PlayerItems
    {
        public int PlayerId;
        /// <summary>What Space would do here (<see cref="MatchServer.InteractionAvailable"/>): "item", "floor-item", "corpse-item", "roll", "chest", null…</summary>
        public string Interaction;
        /// <summary>Room the player stands in (by cell; -1 in a corridor).</summary>
        public int Room = -1;
        /// <summary>The opened corpse in reach (the dead player's id), or -1.</summary>
        public int Corpse = -1;
        public readonly List<LootChoice> Here = new List<LootChoice>();
        public readonly List<CarriedItem> Carried = new List<CarriedItem>();
        public readonly List<RollPrompt> Rolls = new List<RollPrompt>();
        /// <summary>Kilograms worn and carried, consumables included, against the sheet's capacity (<see cref="CarryRules"/>).</summary>
        public float LoadKg, CapacityKg;
        public float SpeedFactor => CarryRules.MoveSpeedFactor(LoadKg, CapacityKg);
        public bool Overloaded => CarryRules.IsOverloaded(LoadKg, CapacityKg);
        /// <summary>True when an opened chest or corpse (or the floor) has something here: the loot panel opens.</summary>
        public bool LootInReach => Here.Count > 0;
    }

    public enum LootSourceKind : byte { Chest, Corpse, Floor }

    /// <summary>One item in reach. <see cref="Refusal"/> is the server's reason it cannot be taken right now (a roll, a teammate's win, a channel), or null.</summary>
    public struct LootChoice
    {
        public int ItemId; public Item Item; public LootSourceKind Source;
        /// <summary>The item is in an open group roll (whoever is eligible must roll first; an enemy may still grab it).</summary>
        public bool InRoll;
        /// <summary>The teammate who won it, or -1.</summary>
        public int Winner;
        public string Refusal;
        public bool CanTake => Refusal == null;
    }

    /// <summary>One item the player holds. Worn: brought in worn from the profile and not handed off since.</summary>
    public struct CarriedItem
    {
        public int ItemId; public Item Item; public bool Worn;
    }

    /// <summary>A group roll this player is in: answer it with <see cref="PlayerIntent.RollItemId"/> and <see cref="PlayerIntent.RollChoice"/>.</summary>
    public struct RollPrompt
    {
        public int ItemId; public Item Item; public float SecondsLeft;
        /// <summary>Null until this player answered.</summary>
        public LootRollChoice? Answered;
        public int Eligible, Answers;
        /// <summary>The piece fits this player's current class: a Need adds <see cref="LootRollRules.OnClassNeedBonus"/>.</summary>
        public bool OnClass;
    }
}
