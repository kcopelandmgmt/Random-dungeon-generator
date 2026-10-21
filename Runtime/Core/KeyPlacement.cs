namespace DungeonGen.Core
{
    /// <summary>Where a vault key is found and which vault it opens. Keys are items: lootable from corpses in full-loot PvP.</summary>
    public sealed class KeyPlacement
    {
        public int KeyId { get; }
        /// <summary>The Vault room whose locked doors this key opens.</summary>
        public int VaultRoomId { get; }
        /// <summary>Room where the key spawns: a chest on the floor, or the boss's drop if <see cref="DroppedByBoss"/>.</summary>
        public int HolderRoomId { get; }
        public bool DroppedByBoss { get; }
        /// <summary>Cell to spawn the key (or the boss) at.</summary>
        public GridPoint Cell { get; }
        /// <summary>Walking distance from the holder room to the vault it opens, in cells.</summary>
        public float WalkToVault { get; }

        public KeyPlacement(int keyId, int vaultRoomId, int holderRoomId, bool droppedByBoss, GridPoint cell, float walkToVault)
        {
            KeyId = keyId;
            VaultRoomId = vaultRoomId;
            HolderRoomId = holderRoomId;
            DroppedByBoss = droppedByBoss;
            Cell = cell;
            WalkToVault = walkToVault;
        }
    }

    /// <summary>The exit that appears when the final boss dies.</summary>
    public sealed class BossGatedExit
    {
        public int RoomId { get; }
        public GridPoint Cell { get; }
        public BossGatedExit(int roomId, GridPoint cell) { RoomId = roomId; Cell = cell; }
    }
}
