using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// What a remote client knows that the host reads off its server (2026-10-26 online play), for the HUD, the item panels and the summary:
    /// the local player's spec, pockets and consumable cooldowns and item screens (from the <see cref="SelfSnapshot"/> the world view applied),
    /// every item detail its team's views carried (cached by id, so an item stays resolvable after it leaves the view), and, at the end, the
    /// server's <see cref="RoundResult"/> with the death recap. <see cref="Current"/> is set only on a client in a round; the host and the offline
    /// round keep reading their own server.
    /// </summary>
    public sealed class ClientMirror
    {
        public static ClientMirror Current { get; set; }

        public int PlayerId { get; }
        public ClientWorldView World { get; }
        /// <summary>The server's word on this player's finished round, once it arrived.</summary>
        public RoundResult Result { get; private set; }
        public event System.Action<RoundResult> ResultArrived;

        private readonly Dictionary<int, Item> _items = new Dictionary<int, Item>();
        private readonly Dictionary<int, byte[]> _itemBytes = new Dictionary<int, byte[]>();
        private PlayerItems _lastItems; private SelfItems _lastSelfItems;

        public ClientMirror(int playerId, ClientWorldView world, IMatchTransport transport)
        {
            PlayerId = playerId; World = world;
            transport.ViewReceived += OnView;
        }

        private void OnView(byte[] bytes)
        {
            TeamView v;
            try { v = TeamView.FromBytes(bytes); }
            catch (System.Exception e) { Debug.LogWarning("ClientMirror: unreadable view: " + e.Message); return; }
            foreach (var (id, item) in v.ItemDetails)
            {
                if (_itemBytes.TryGetValue(id, out var old) && Same(old, item)) continue;
                try { _items[id] = TeamView.ReadItem(item); _itemBytes[id] = item; }
                catch (System.Exception e) { Debug.LogWarning($"ClientMirror: item {id} unreadable: {e.Message}"); }
            }
        }

        private static bool Same(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }

        public void SetResult(RoundResult result)
        {
            if (result == null || result.PlayerId != PlayerId) return;
            Result = result;
            ResultArrived?.Invoke(result);
        }

        public SelfSnapshot? Self => World != null ? World.Self : null;

        public string Spec { get { var s = Self; return s.HasValue && !string.IsNullOrEmpty(s.Value.Spec) ? s.Value.Spec : null; } }

        /// <summary>The item behind an id, from the details the team's views carried, or null.</summary>
        public Item ItemById(int id) => _items.TryGetValue(id, out var it) ? it : null;

        public Dictionary<string, int> Pockets
        {
            get
            {
                var d = new Dictionary<string, int>();
                var s = Self;
                if (s.HasValue && s.Value.Pockets != null) foreach (var (name, count) in s.Value.Pockets) if (count > 0) d[name] = count;
                return d;
            }
        }

        public float ConsumableCooldown(Consumable c) { var s = Self; return c != null && s.HasValue ? s.Value.ConsumableCooldown(c.Name) : 0f; }

        /// <summary>The item screens' model, rebuilt from the last snapshot (null before the first one, or once the player is out).</summary>
        public PlayerItems Items
        {
            get
            {
                var s = Self;
                if (!s.HasValue || s.Value.Items == null) return null;
                if (!ReferenceEquals(s.Value.Items, _lastSelfItems)) { _lastSelfItems = s.Value.Items; _lastItems = s.Value.Items.ToPlayerItems(PlayerId, ItemById); }
                return _lastItems;
            }
        }

        public bool CanSalvage(int itemId) { var s = Self; return s.HasValue && s.Value.Items != null && s.Value.Items.CanSalvage(itemId); }

        /// <summary>What the player wears now (the carried pieces brought in worn): what a tooltip compares against.</summary>
        public Equipment Worn
        {
            get
            {
                var v = Items;
                return EquipPreview.FromWorn(v == null ? Enumerable.Empty<Item>() : v.Carried.Where(c => c.Worn).Select(c => c.Item));
            }
        }
    }
}
