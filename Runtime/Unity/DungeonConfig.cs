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
        }
    }
}
