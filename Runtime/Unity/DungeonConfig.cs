using DungeonGen.Core;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Designer-facing wrapper around <see cref="DungeonSettings"/>. Make one per dungeon
    /// "biome" or difficulty (Create > Dungeon Gen > Dungeon Config) and tweak in the Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon Gen/Dungeon Config", fileName = "DungeonConfig")]
    public sealed class DungeonConfig : ScriptableObject
    {
        public DungeonSettings Settings = new DungeonSettings();

        [Header("Preset")]
        [Tooltip("Number of teams to size the dungeon for. Right-click the component header > Apply Team Preset to overwrite Settings with DungeonSettings.ForTeams(PresetTeams).")]
        [Range(1, DungeonSettings.MaxTeams)]
        public int PresetTeams = 8;

        [ContextMenu("Apply Team Preset")]
        public void ApplyTeamPreset()
        {
            Settings = DungeonSettings.ForTeams(PresetTeams);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private void OnValidate()
        {
            Settings.Width = Mathf.Max(24, Settings.Width);
            Settings.Height = Mathf.Max(24, Settings.Height);
            Settings.MinRoomSize = Mathf.Max(3, Settings.MinRoomSize);
            Settings.MaxRoomSize = Mathf.Max(Settings.MinRoomSize, Settings.MaxRoomSize);
            Settings.RoomSpacing = Mathf.Max(3, Settings.RoomSpacing);
            Settings.BorderPadding = Mathf.Max(1, Settings.BorderPadding);
            Settings.CorridorWidth = Mathf.Clamp(Settings.CorridorWidth, 1, 2);
            Settings.LoopChance = Mathf.Clamp01(Settings.LoopChance);
            Settings.LootRoomFraction = Mathf.Clamp01(Settings.LootRoomFraction);
            Settings.LootTiers = Mathf.Max(2, Settings.LootTiers);
            Settings.SpawnCount = Mathf.Clamp(Settings.SpawnCount, 1, DungeonSettings.MaxTeams);
            Settings.CandidateCount = Mathf.Max(1, Settings.CandidateCount);
            Settings.RingJitter = Mathf.Clamp01(Settings.RingJitter);
            Settings.ExtractRingRadius = Mathf.Clamp(Settings.ExtractRingRadius, 0.2f, 0.9f);
        }
    }
}
