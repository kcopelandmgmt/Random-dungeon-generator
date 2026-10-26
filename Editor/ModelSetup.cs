using UnityEditor;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Greybox step: puts the generated model prefabs (from <see cref="GeneratedModelImporter.Build"/>) into the
    /// <see cref="DungeonBuilder"/> tile and marker slots and the avatar library into <see cref="ClientWorldView"/>. A slot
    /// whose prefab is missing stays empty, so the builder's greybox fallback draws it; the pit always uses the fallback.
    /// </summary>
    public static class ModelSetup
    {
        [GreyboxStep(10)]
        public static void Step(GreyboxContext ctx)
        {
            var b = ctx.Builder;
            if (b != null)
            {
                b.FloorPrefab = Load("dungeon_floor_tile");
                b.CorridorPrefab = Load("dungeon_floor_tile");
                b.DoorPrefab = Load("wooden_door");
                b.LockedDoorPrefab = Load("vault_door");
                b.WallPrefab = Load("dungeon_wall_block");
                b.PillarPrefab = Load("stone_pillar");
                b.LowWallPrefab = Load("low_wall");
                b.SpikeTrapPrefab = Load("spike_trap");
                b.GasVentPrefab = Load("gas_vent");
                b.LavaPrefab = Load("lava_pool");
                b.LootMarkerPrefab = Load("treasure_chest");
                b.VaultMarkerPrefab = Load("boss_chest");
                b.BossMarkerPrefab = Load("boss_chest");
                b.FinalBossMarkerPrefab = Load("boss_chest");
                b.ExtractMarkerPrefab = Load("extract_portal");
                b.BossGatedExitPrefab = Load("extract_portal");
                b.KeyMarkerPrefab = Load("dungeon_key");
                b.RoomLightPrefab = Load("brazier");
                EditorUtility.SetDirty(b);
            }
            if (ctx.World != null)
            {
                var lib = AssetDatabase.LoadAssetAtPath<AvatarVisualLibrary>(GeneratedModelImporter.LibraryPath);
                if (lib == null) Debug.LogWarning("Dungeon Gen models: no AvatarVisualLibrary at " + GeneratedModelImporter.LibraryPath + "; run GeneratedModelImporter.Build first. Avatars stay capsules.");
                ctx.World.Library = lib;
                EditorUtility.SetDirty(ctx.World);
            }
        }

        private static GameObject Load(string slug)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(GeneratedModelImporter.PrefabPath(slug));
            if (go == null) Debug.LogWarning($"Dungeon Gen models: no prefab for {slug}; the greybox fallback is used.");
            return go;
        }
    }
}
