using System;

namespace DungeonGen.Core.Combat
{
    public enum EncounterKind { RoomBoss, FinalBoss }

    /// <summary>The numbers a boss needs to be tuned for a given group. Attached to Boss and FinalBoss rooms.</summary>
    public sealed class EncounterBudget
    {
        public EncounterKind Kind { get; }
        /// <summary>How many baseline players this fight is balanced against.</summary>
        public float PlayerEquivalents { get; }
        /// <summary>Gear multiplier assumed on those players (1 = baseline, 1.4 = good gear).</summary>
        public float GearMultiplier { get; }
        public float Health { get; }
        public float Dps { get; }
        public float TargetSeconds { get; }
        public float EnrageSeconds { get; }

        public EncounterBudget(EncounterKind kind, float playerEquivalents, float gearMultiplier, float health, float dps, float targetSeconds, float enrageSeconds)
        {
            Kind = kind;
            PlayerEquivalents = playerEquivalents;
            GearMultiplier = gearMultiplier;
            Health = health;
            Dps = dps;
            TargetSeconds = targetSeconds;
            EnrageSeconds = enrageSeconds;
        }

        public override string ToString() => $"{Kind}: {Health:0} HP, {Dps:0.0} DPS, tuned for {PlayerEquivalents:0.0} players x{GearMultiplier:0.00} gear, {TargetSeconds:0}s target";
    }

    /// <summary>Predicted result of a group fighting a boss: who runs out first.</summary>
    public readonly struct FightOutcome
    {
        public readonly int Players;
        public readonly float GearMultiplier;
        public readonly float FightSeconds;
        public readonly float DamageTaken;
        public readonly float DamageCapacity;
        /// <summary>Capacity / damage taken. Above 1 the group wins; 1.0 is a coin flip; below 1 they wipe.</summary>
        public readonly float Margin;
        public bool Wins => Margin >= 1f;

        public FightOutcome(int players, float gear, float seconds, float taken, float capacity)
        {
            Players = players;
            GearMultiplier = gear;
            FightSeconds = seconds;
            DamageTaken = taken;
            DamageCapacity = capacity;
            Margin = taken <= 0 ? float.PositiveInfinity : capacity / taken;
        }
    }

    /// <summary>
    /// Turns "tuned for 2.5 players" into hit points and damage.
    ///
    /// Model: a group of k players with per-player DPS d and effective health h, plus party
    /// sustain μ, fights a boss with health H and damage D. The fight lasts T = H / (k·d). The
    /// group survives if D·T &lt; k·h·μ. Pick H so the *target* group takes exactly the target
    /// time, then pick D so the target group wins by <see cref="CombatBaseline.TuningMargin"/>:
    ///
    ///     H = k*·d·T*           D = k*·h·μ / (T*·margin)
    ///
    /// Because fight time scales as 1/k while damage capacity scales as k, a group one player
    /// short takes roughly (k*/k)² as much relative damage, which is what makes 2.5 a sharp
    /// line between "duo wipes" and "trio wins".
    /// </summary>
    public static class EncounterTuner
    {
        public static EncounterBudget RoomBoss(CombatBaseline b) =>
            Tune(EncounterKind.RoomBoss, b, b.RoomBossPlayerEquivalents, 1f, b.RoomBossFightSeconds);

        public static EncounterBudget FinalBoss(CombatBaseline b) =>
            Tune(EncounterKind.FinalBoss, b, b.PartySize, b.GoodGearMultiplier, b.FinalBossFightSeconds);

        public static EncounterBudget Tune(EncounterKind kind, CombatBaseline b, float playerEquivalents, float gear, float targetSeconds)
        {
            float d = b.PlayerDps * gear;
            float h = b.PlayerHealth * gear;
            float health = playerEquivalents * d * targetSeconds;
            float dps = playerEquivalents * h * b.PartySustain / (targetSeconds * b.TuningMargin);
            return new EncounterBudget(kind, playerEquivalents, gear, health, dps, targetSeconds, targetSeconds * b.EnrageAfterMultiple);
        }

        /// <summary>Predict how a group of <paramref name="players"/> with the given gear fares. Enrage doubles boss DPS past the enrage time.</summary>
        public static FightOutcome Simulate(EncounterBudget e, CombatBaseline b, int players, float gear = 1f)
        {
            float d = b.PlayerDps * gear;
            float h = b.PlayerHealth * gear;
            float seconds = e.Health / Math.Max(1e-3f, players * d);
            float normal = Math.Min(seconds, e.EnrageSeconds);
            float enraged = Math.Max(0f, seconds - e.EnrageSeconds);
            float taken = e.Dps * normal + e.Dps * 2f * enraged;
            float capacity = players * h * b.PartySustain;
            return new FightOutcome(players, gear, seconds, taken, capacity);
        }
    }
}
