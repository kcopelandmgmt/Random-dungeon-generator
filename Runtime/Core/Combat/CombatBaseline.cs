using System;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// The handful of numbers every encounter is tuned against. "One player" means a single
    /// character in baseline (tier-1) gear of the roster's average class; see <see cref="ClassArchetype"/>.
    /// All values are abstract units; scale them to whatever your damage formulas use.
    /// </summary>
    [Serializable]
    public class CombatBaseline
    {
        /// <summary>Sustained damage per second of one baseline player.</summary>
        public float PlayerDps = 100f;
        /// <summary>Effective hit points of one baseline player (health after armour).</summary>
        public float PlayerHealth = 1000f;
        /// <summary>How much extra damage a coordinated party absorbs through healing, blocks and kiting. 1 = none, 1.5 = half again.</summary>
        public float PartySustain = 1.5f;
        /// <summary>Multiplier on DPS and health that "good gear" (tier-3/4 drops) gives over baseline.</summary>
        public float GoodGearMultiplier = 1.4f;
        /// <summary>Players in a full party. Final-boss tuning targets exactly this many.</summary>
        public int PartySize = 3;
        /// <summary>Safety margin for the target group: 1.15 means the group it is tuned for wins with 15% health to spare.</summary>
        public float TuningMargin = 1.15f;

        /// <summary>Room bosses are balanced against this many baseline players. 2.5 = a duo barely loses, a trio wins.</summary>
        public float RoomBossPlayerEquivalents = 2.5f;
        public float RoomBossFightSeconds = 60f;
        public float FinalBossFightSeconds = 90f;
        /// <summary>Fights longer than this multiple of the target time trigger enrage (boss damage ramps). Stops a single tank cheesing it.</summary>
        public float EnrageAfterMultiple = 2f;

        public void Validate()
        {
            if (PlayerDps <= 0 || PlayerHealth <= 0) throw new ArgumentException("PlayerDps and PlayerHealth must be positive.");
            if (PartySize < 1) throw new ArgumentException("PartySize must be >= 1.");
            if (RoomBossPlayerEquivalents <= 0) throw new ArgumentException("RoomBossPlayerEquivalents must be positive.");
            if (TuningMargin < 1f) throw new ArgumentException("TuningMargin must be >= 1.");
        }
    }
}
