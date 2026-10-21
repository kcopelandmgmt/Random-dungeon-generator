using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Combat
{
    /// <summary>A concrete boss in a concrete room: archetype, rolled affixes and the resulting budget.</summary>
    public sealed class BossInstance
    {
        public BossArchetype Archetype { get; }
        public List<BossAffix> Affixes { get; } = new List<BossAffix>();
        public EncounterBudget Budget { get; set; }

        public BossInstance(BossArchetype archetype) { Archetype = archetype; }

        public int BonusLootRolls => Affixes.Sum(a => a.BonusLootRolls);
        public bool HasAffix(BossAffix a) => Affixes.Contains(a);

        /// <summary>"Ashen Colossus (Armoured, Keybearer)" style name for logs, markers and the HUD.</summary>
        public string DisplayName => Affixes.Count == 0
            ? Archetype.Name
            : $"{Archetype.Name} ({string.Join(", ", Affixes.Select(a => a.Name))})";

        public override string ToString() => $"{DisplayName}: {Budget}";
    }
}
