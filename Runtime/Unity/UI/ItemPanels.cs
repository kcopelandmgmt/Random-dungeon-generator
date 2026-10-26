using System.Collections.Generic;
using System.Linq;
using System.Text;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using UnityEngine;
using UnityEngine.UI;
#if DUNGEONGEN_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The in-round item screens, on the HUD canvas: the loot panel (opens by itself at an opened chest, an opened corpse
    /// or items on the floor: every item with rarity colour, base, item level, key affixes, weight and a Take button), the
    /// inventory on I (worn and carried, the load against capacity, Drop), the group-roll prompt (Need / Greed / Pass with a
    /// countdown) and a hover tooltip with every affix and the gear-score change if worn. Choices go into
    /// <see cref="PendingItemActions"/>, which the input controller copies into the next intent; the server decides.
    /// Reads the host's <see cref="MatchServer.ItemsFor"/>; on a remote client (no host) the same model rebuilt from the player's own snapshot
    /// (<see cref="SelfItems"/>) and the item details its team's views carried (<see cref="ClientMirror"/>).
    /// Tests drive it with SendMessage: ToggleInventory, RefreshNow, ShowTooltipFor(itemId), HideTooltip.
    /// </summary>
    public sealed class ItemPanels : MonoBehaviour
    {
        public ClientWorldView World;
        public DungeonMatchBootstrap Bootstrap;
        public ServerSimulation Server;
        public int LocalPlayerId = GameFlow.LocalPlayerId;

        /// <summary>Online: this process's player id (the server gave it).</summary>
        public void SetLocalPlayerId(int id) => LocalPlayerId = id;
        [Tooltip("Seconds between refreshes of the item view.")]
        public float RefreshSeconds = 0.1f;

        public const string RootName = "ItemPanels", LootName = "LootPanel", InventoryName = "InventoryPanel", RollsName = "RollPrompts", TooltipName = "ItemTooltip";
        private const int MaxLootRows = 7, MaxInventoryRows = 11;

        public bool InventoryOpen { get; private set; }
        public bool LootVisible => _loot != null && _loot.gameObject.activeSelf;
        public bool RollVisible => _rolls != null && _rolls.childCount > 0 && _rolls.gameObject.activeSelf;
        /// <summary>The view the panels last drew (host only).</summary>
        public PlayerItems Items { get; private set; }

        private RectTransform _root, _loot, _inv, _rolls, _tip;
        private Text _tipText;
        private GameHud _hud;
        private float _nextRefresh, _refreshedAt;
        private string _lootSig, _invSig, _rollSig;
        private readonly Dictionary<int, (UiBar bar, Text secs, float left)> _rollBars = new Dictionary<int, (UiBar, Text, float)>();
        private int _tipItem;

        /// <summary>Put the panels on the HUD canvas (the greybox step calls this through <see cref="UiInstaller.InstallHud"/>).</summary>
        public static ItemPanels Install(GameObject hudCanvas, ClientWorldView world, DungeonMatchBootstrap bootstrap, ServerSimulation server)
        {
            var p = hudCanvas.GetComponent<ItemPanels>();
            if (p == null) p = hudCanvas.AddComponent<ItemPanels>();
            p.World = world; p.Bootstrap = bootstrap; p.Server = server;
            return p;
        }

        private MatchServer Host => Server != null ? Server.Server : null;
        /// <summary>A remote client's mirror (null offline and when hosting).</summary>
        private ClientMirror Mirror => Host == null ? ClientMirror.Current : null;

        private Dictionary<string, int> PocketsNow(MatchServer host) => host != null ? host.PocketsOf(LocalPlayerId) : Mirror?.Pockets ?? new Dictionary<string, int>();
        private Equipment WornNow(MatchServer host) => host != null ? host.WornBy(LocalPlayerId) : Mirror?.Worn ?? new Equipment();
        private bool CanSalvage(MatchServer host, int itemId) => host != null ? host.SalvageRefusal(LocalPlayerId, itemId) == null : Mirror != null && Mirror.CanSalvage(itemId);
        private Item ItemNow(MatchServer host, int itemId) => host != null ? host.ItemById(itemId) : Mirror?.ItemById(itemId);

        private void Awake()
        {
            if (World == null) World = FindFirstObjectByType<ClientWorldView>();
            if (Bootstrap == null) Bootstrap = FindFirstObjectByType<DungeonMatchBootstrap>();
            if (Server == null) Server = FindFirstObjectByType<ServerSimulation>();
            PendingItemActions.Clear();
            Build();
        }

        private void OnDestroy() => PendingItemActions.Clear();

        private void Build()
        {
            _root = UiStyle.Node(transform, RootName); _root.Stretch();
            _loot = UiStyle.Panel(_root, LootName);
            _loot.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-36f, 30f), new Vector2(600f, 300f));
            _inv = UiStyle.Panel(_root, InventoryName);
            _inv.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(36f, 30f), new Vector2(620f, 400f));
            _rolls = UiStyle.Node(_root, RollsName);
            _rolls.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -136f), new Vector2(680f, 10f));
            _tip = UiStyle.Panel(_root, TooltipName, new Color(0.035f, 0.03f, 0.035f, 0.97f), true, false);
            _tip.Place(new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(470f, 200f));
            _tipText = UiStyle.Label(_tip, "Text", "", 19, UiStyle.TextMain, TextAnchor.UpperLeft);
            _tipText.rectTransform.Stretch(22f, 18f, 16f, 16f);
            _loot.gameObject.SetActive(false); _inv.gameObject.SetActive(false); _tip.gameObject.SetActive(false);
        }

        // ---------------- per frame ----------------

        private void Update()
        {
            if (IKeyPressed()) ToggleInventory();
            if (_hud == null) _hud = GetComponent<GameHud>();
            var host = Host;
            var mirror = Mirror;
            if (mirror != null) LocalPlayerId = mirror.PlayerId;
            bool blocked = _hud != null && (_hud.Paused || _hud.SummaryVisible || _hud.RecapVisible);
            bool source = host != null ? host.Sim != null && host.State.PlayerTeam.ContainsKey(LocalPlayerId) : mirror != null;
            if (!source || blocked)
            {
                _root.gameObject.SetActive(false);
                return;
            }
            _root.gameObject.SetActive(true);
            if (Time.unscaledTime >= _nextRefresh) RefreshNow();
            // Countdowns run between refreshes.
            float since = Time.unscaledTime - _refreshedAt;
            foreach (var kv in _rollBars)
            {
                float left = Mathf.Max(0f, kv.Value.left - since * (Server == null ? 1f : Server.Paused ? 0f : Mathf.Max(0f, Server.GameSpeed)));
                kv.Value.bar.Set(left / LootRollRules.RollSeconds, null, left < 5f ? UiStyle.Bad : UiStyle.Gold);
                kv.Value.secs.text = $"{Mathf.CeilToInt(left)} s";
            }
        }

        /// <summary>Read the host's view now and redraw what changed.</summary>
        public void RefreshNow()
        {
            var host = Host;
            var mirror = Mirror;
            if ((host == null || host.Sim == null) && mirror == null) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds; _refreshedAt = Time.unscaledTime;
            bool alive;
            if (host != null)
            {
                Items = host.ItemsFor(LocalPlayerId);
                var me = host.Sim.Get(LocalPlayerId);
                alive = me != null && !me.Dead;
            }
            else
            {
                Items = mirror.Items;                  // from the snapshot: null once the player is out
                alive = Items != null && mirror.Self.HasValue;
            }
            DrawLoot(alive ? Items : null, host);
            DrawInventory(alive ? Items : null, host);
            DrawRolls(alive ? Items : null);
            if (_tipItem != 0 && ItemNow(host, _tipItem) == null) HideTooltip();
        }

        public void ToggleInventory() { InventoryOpen = !InventoryOpen; _invSig = null; if (!InventoryOpen) HideTooltip(); RefreshNow(); }

        // ---------------- loot ----------------

        private void DrawLoot(PlayerItems v, MatchServer host)
        {
            bool show = v != null && v.LootInReach;
            if (!show) { if (_loot.gameObject.activeSelf) { _loot.gameObject.SetActive(false); _lootSig = null; if (_tipItem != 0 && !InventoryOpen) HideTooltip(); } return; }
            string sig = string.Join("|", v.Here.Select(h => $"{h.ItemId}:{h.Refusal}:{h.InRoll}:{h.Winner}"));
            _loot.gameObject.SetActive(true);
            if (sig == _lootSig) return;
            _lootSig = sig;
            Clear(_loot);
            string title = v.Here.All(h => h.Source == LootSourceKind.Corpse) ? "CORPSE" : v.Here.All(h => h.Source == LootSourceKind.Floor) ? "ON THE FLOOR" : v.Here.Any(h => h.Source == LootSourceKind.Corpse) ? "CORPSE AND CHEST" : "CHEST";
            var t = UiStyle.Label(_loot, "Title", title, 30, UiStyle.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -14f), new Vector2(300f, 40f));
            var hint = UiStyle.Label(_loot, "Hint", "Space takes the first  ·  Take to choose", 17, UiStyle.TextDim, TextAnchor.MiddleRight);
            hint.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -18f), new Vector2(320f, 32f));
            UiStyle.Rule(_loot, "Rule", 548f, -62f);
            float y = -74f;
            int shown = 0;
            foreach (var h in v.Here)
            {
                if (shown == MaxLootRows) break;
                var c = h;
                string label = c.CanTake ? "Take" : c.InRoll ? "Rolling" : c.Winner >= 0 && c.Winner != LocalPlayerId ? "Won" : "—";
                string note = c.CanTake ? null : c.Refusal;
                ItemRow(_loot, "Loot-" + c.ItemId, c.Item, y, 548f, "Take-" + c.ItemId, label, c.CanTake, () => { PendingItemActions.Take(c.ItemId); UiSound.Play(UiSound.Click); }, note,
                    c.Source == LootSourceKind.Corpse ? "corpse" : c.Source == LootSourceKind.Floor ? "floor" : null, host);
                y -= 90f; shown++;
            }
            if (v.Here.Count > shown)
            {
                var more = UiStyle.Label(_loot, "More", $"+{v.Here.Count - shown} more: take some to see the rest", 18, UiStyle.TextDim, TextAnchor.MiddleCenter);
                more.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - 4f), new Vector2(540f, 28f));
                y -= 32f;
            }
            _loot.sizeDelta = new Vector2(600f, -y + 16f);
        }

        // ---------------- inventory ----------------

        private void DrawInventory(PlayerItems v, MatchServer host)
        {
            bool show = InventoryOpen && v != null;
            if (!show) { if (_inv.gameObject.activeSelf) { _inv.gameObject.SetActive(false); _invSig = null; } return; }
            _inv.gameObject.SetActive(true);
            var pockets = PocketsNow(host);
            string sig = string.Join("|", v.Carried.Select(c => $"{c.ItemId}:{c.Worn}")) + $"#{v.LoadKg:0.0}/{v.CapacityKg:0.0}#" + string.Join(",", pockets.Select(kv => kv.Key + kv.Value));
            if (sig == _invSig) return;
            _invSig = sig;
            Clear(_inv);
            var t = UiStyle.Label(_inv, "Title", "INVENTORY", 30, UiStyle.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -14f), new Vector2(300f, 40f));
            var key = UiStyle.Label(_inv, "Key", $"{InventoryKeyName()} to close", 18, UiStyle.TextDim, TextAnchor.MiddleRight);
            key.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -18f), new Vector2(200f, 32f));

            // Load against capacity.
            float frac = v.CapacityKg > 0f ? v.LoadKg / v.CapacityKg : 1f;
            var bar = new UiBar(_inv, "WeightBar", new Vector2(568f, 22f), frac > 1f ? UiStyle.Bad : frac > 0.85f ? UiStyle.Channel : UiStyle.Good);
            bar.Root.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(568f, 22f));
            bar.Set(Mathf.Min(1f, frac), null);
            var wl = UiStyle.Label(_inv, "WeightLabel", $"Load {v.LoadKg:0.0} / {v.CapacityKg:0} kg" + (v.Overloaded ? UiStyle.Colour($"   overloaded: speed ×{v.SpeedFactor:0.00}", UiStyle.Bad) : UiStyle.Colour("   full speed", UiStyle.TextDim)), 19, UiStyle.TextMain, TextAnchor.MiddleLeft);
            wl.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -90f), new Vector2(568f, 28f));
            float y = -124f;
            int shown = 0;
            var worn = WornNow(host);
            foreach (var c in v.Carried)
            {
                if (shown == MaxInventoryRows) break;
                var cc = c;
                bool salvage = CanSalvage(host, cc.ItemId);
                ItemRow(_inv, "Inv-" + cc.ItemId, cc.Item, y, 568f, "Drop-" + cc.ItemId, "Drop", true, () => { PendingItemActions.Drop(cc.ItemId); UiSound.Play(UiSound.Click); }, null, cc.Worn ? "worn" : null, host, worn, compact: true, reserve: salvage ? 262f : 140f);
                if (salvage)
                {
                    // A Salvage Kit in the pockets: break the item down here; the materials come home only if you do.
                    var row = _inv.Find("Inv-" + cc.ItemId);
                    if (row != null)
                    {
                        var sb = UiStyle.MakeButton(row, "Salvage-" + cc.ItemId, "Salvage", () => { PendingItemActions.Salvage(cc.ItemId); UiSound.Play(UiSound.Click); }, 18);
                        ((RectTransform)sb.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-136f, 0f), new Vector2(110f, 40f));
                    }
                }
                y -= 64f; shown++;
            }
            if (v.Carried.Count == 0)
            {
                var none = UiStyle.Label(_inv, "Empty", "You carry nothing.", 20, UiStyle.TextDim, TextAnchor.MiddleCenter);
                none.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - 6f), new Vector2(560f, 30f));
                y -= 40f;
            }
            if (v.Carried.Count > shown) { var more = UiStyle.Label(_inv, "More", $"+{v.Carried.Count - shown} more", 18, UiStyle.TextDim, TextAnchor.MiddleCenter); more.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - 4f), new Vector2(560f, 28f)); y -= 32f; }
            string pk = pockets.Count == 0 ? "Pockets empty" : "Pockets  " + string.Join("   ", pockets.Select(kv => $"{kv.Key} ×{kv.Value}"));
            var p = UiStyle.Label(_inv, "Pockets", pk, 18, UiStyle.TextDim, TextAnchor.MiddleLeft);
            p.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, y - 8f), new Vector2(568f, 28f));
            y -= 44f;
            _inv.sizeDelta = new Vector2(620f, -y + 8f);
        }

        // ---------------- rolls ----------------

        private void DrawRolls(PlayerItems v)
        {
            var list = v?.Rolls ?? new List<RollPrompt>();
            string sig = string.Join("|", list.Select(r => $"{r.ItemId}:{r.Answered}:{r.Answers}"));
            if (sig == _rollSig)
            {
                foreach (var r in list) if (_rollBars.TryGetValue(r.ItemId, out var b)) _rollBars[r.ItemId] = (b.bar, b.secs, r.SecondsLeft);
                return;
            }
            _rollSig = sig;
            Clear(_rolls); _rollBars.Clear();
            float y = 0f;
            foreach (var r in list.Take(3))
            {
                var rr = r;
                bool answered = rr.Answered.HasValue;
                var card = UiStyle.Panel(_rolls, "RollPrompt-" + rr.ItemId);
                float h = answered ? 146f : 172f;
                card.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(680f, h));
                Hover(card, rr.ItemId);
                var icon = ItemIconTile.Icon(card, "ItemIcon", rr.Item, 64f);
                icon.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(64f, 64f));
                var tag = UiStyle.Label(card, "Tag", "GROUP ROLL", 18, UiStyle.GoldDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                tag.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(98f, -8f), new Vector2(200f, 22f));
                var head = UiStyle.Label(card, "Head", $"<b>{ItemText.Name(rr.Item)}</b>", 24, UiStyle.TextMain, TextAnchor.MiddleLeft);
                head.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(98f, -28f), new Vector2(440f, 30f));
                OneLine(head, 18);
                var secs = UiStyle.Label(card, "Seconds", "", 22, UiStyle.TextMain, TextAnchor.MiddleRight, FontStyle.Bold);
                secs.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -10f), new Vector2(110f, 34f));
                var sub = UiStyle.Label(card, "Sub", ItemText.Line(rr.Item) + "   " + UiStyle.Colour(rr.OnClass ? $"fits your class: Need +{LootRollRules.OnClassNeedBonus}" : "off-class for you", rr.OnClass ? UiStyle.Good : UiStyle.TextFaint), 18, UiStyle.TextDim, TextAnchor.MiddleLeft);
                sub.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(98f, -58f), new Vector2(560f, 24f));
                OneLine(sub, 18);
                var bar = new UiBar(card, "Countdown", new Vector2(632f, 8f), UiStyle.Gold);
                bar.Root.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(632f, 8f));
                _rollBars[rr.ItemId] = (bar, secs, rr.SecondsLeft);
                if (answered)
                {
                    var st = UiStyle.Label(card, "Answered", $"You chose {UiStyle.Colour(rr.Answered.ToString(), UiStyle.Gold)}  ·  {rr.Answers} of {rr.Eligible} answered", 19, UiStyle.TextMain, TextAnchor.MiddleLeft);
                    st.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(98f, 30f), new Vector2(560f, 28f));
                }
                else
                {
                    string[] labels = { "Need", "Greed", "Pass" };
                    for (int i = 0; i < 3; i++)
                    {
                        var choice = (LootRollChoice)i;
                        var b = UiStyle.MakeButton(card, labels[i] + "-" + rr.ItemId, labels[i], () => { PendingItemActions.Roll(rr.ItemId, choice); UiSound.Play(UiSound.Click); }, 24);
                        ((RectTransform)b.transform).Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f + i * 214f, 30f), new Vector2(200f, 52f));
                        if (i == 0) b.SetSelected(rr.OnClass);
                    }
                }
                y -= h + 10f;
            }
        }

        // ---------------- rows and the tooltip ----------------

        private void ItemRow(RectTransform parent, string name, Item item, float y, float width, string buttonName, string buttonLabel, bool canClick, System.Action onClick, string note, string tag, MatchServer host, Equipment worn = null, bool compact = false, float reserve = 140f)
        {
            float h = compact ? 58f : 84f;
            var row = UiStyle.Img(parent, name, new Color(1f, 1f, 1f, 0.035f), UiStyle.Rounded);
            row.raycastTarget = true;
            row.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(width, h));
            // The painted icon in a rarity frame (ItemIconTile), then the text beside it.
            float iconSize = h - 12f;
            var icon = ItemIconTile.Icon(row.transform, "ItemIcon", item, iconSize, !compact);
            icon.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(iconSize, iconSize));
            float tx = 6f + iconSize + 12f, tw = width - tx - reserve;
            // The name keeps to one line clear of the buttons (shrinking to 18, then cut at a word); the tag leads the second line.
            var n = UiStyle.Label(row.transform, "Name", $"<b>{ItemText.Name(item)}</b>", compact ? 20 : 21, UiStyle.TextMain, TextAnchor.UpperLeft);
            n.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(tx, compact ? -4f : -5f), new Vector2(tw, 26f));
            OneLine(n, 18);
            string second = (tag != null ? UiStyle.Colour(tag, tag == "worn" ? UiStyle.Gold : UiStyle.TextFaint) + UiStyle.Colour("  ·  ", UiStyle.TextFaint) : "") + UiStyle.Colour(ItemText.Line(item), UiStyle.TextDim);
            var l = UiStyle.Label(row.transform, "Line", second, 18, UiStyle.TextMain, TextAnchor.UpperLeft);
            l.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(tx, compact ? -29f : -30f), new Vector2(tw, 24f));
            OneLine(l, 18);
            if (!compact)
            {
                string aff = note != null ? UiStyle.Colour(note, UiStyle.Bad) : UiStyle.Colour(ItemText.KeyAffixes(item), new Color(0.62f, 0.75f, 1f));
                var a = UiStyle.Label(row.transform, "Affixes", aff, 18, UiStyle.TextMain, TextAnchor.UpperLeft);
                a.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(tx, -52f), new Vector2(tw, 24f));
                OneLine(a, 18);
            }
            var b = UiStyle.MakeButton(row.transform, buttonName, buttonLabel, onClick, 21);
            ((RectTransform)b.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(118f, compact ? 40f : 46f));
            b.interactable = canClick;
            int id = 0;
            if (name.Contains("-")) int.TryParse(name.Substring(name.IndexOf('-') + 1), out id);
            Hover(row.rectTransform, id);
        }

        private void Hover(RectTransform target, int itemId)
        {
            if (itemId == 0) return;
            var h = target.gameObject.AddComponent<ItemHover>();
            h.Enter = () => ShowTooltipNear(itemId, target);
            h.Exit = () => { if (_tipItem == itemId) HideTooltip(); };
        }

        public void ShowTooltipFor(int itemId) => ShowTooltipNear(itemId, null);

        /// <summary>Show the full card for an item beside the row it belongs to (or beside whichever panel holds it).</summary>
        private void ShowTooltipNear(int itemId, RectTransform near)
        {
            var host = Host;
            var item = ItemNow(host, itemId);
            if (item == null) { HideTooltip(); return; }
            _tipItem = itemId;
            var worn = WornNow(host);
            _tipText.text = ItemText.Tooltip(item, worn);
            _tip.gameObject.SetActive(true);
            _tip.SetAsLastSibling();
            float height = LayoutUtility.GetPreferredHeight(_tipText.rectTransform) + 40f;
            _tip.sizeDelta = new Vector2(470f, Mathf.Max(120f, height));
            if (near == null) near = FindRow(itemId);
            // Beside the row: left of the loot panel, right of the inventory, under a roll card. Anchored like the panel it sits by,
            // so it stays put when the canvas changes width (window resize, a screenshot at another aspect).
            var rootRect = _root.rect;
            Vector2 local = near != null ? (Vector2)_root.InverseTransformPoint(near.TransformPoint(near.rect.center)) : Vector2.zero;
            float halfH = _tip.sizeDelta.y / 2f, limit = rootRect.height / 2f - 20f - halfH;
            float midY = Mathf.Clamp(local.y, -limit, limit);
            if (near != null && near.IsChildOf(_rolls))
            {
                _tip.anchorMin = _tip.anchorMax = new Vector2(0.5f, 1f); _tip.pivot = new Vector2(0.5f, 1f);
                _tip.anchoredPosition = new Vector2(local.x, local.y - rootRect.yMax - near.rect.height / 2f - 8f);
            }
            else if (near != null && near.IsChildOf(_inv))
            {
                _tip.anchorMin = _tip.anchorMax = new Vector2(0f, 0.5f); _tip.pivot = new Vector2(0f, 0.5f);
                _tip.anchoredPosition = new Vector2(_inv.anchoredPosition.x + _inv.sizeDelta.x + 14f, midY);
            }
            else
            {
                _tip.anchorMin = _tip.anchorMax = new Vector2(1f, 0.5f); _tip.pivot = new Vector2(1f, 0.5f);
                _tip.anchoredPosition = new Vector2(_loot.anchoredPosition.x - _loot.sizeDelta.x - 14f, midY);
            }
        }

        public void HideTooltip() { _tipItem = 0; if (_tip != null) _tip.gameObject.SetActive(false); }

        private RectTransform FindRow(int itemId)
        {
            foreach (var n in new[] { "Loot-" + itemId, "Inv-" + itemId, "RollPrompt-" + itemId })
            {
                var t = FindDeep(_root, n);
                if (t != null && t.gameObject.activeInHierarchy) return (RectTransform)t;
            }
            return null;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }

        private static void Clear(RectTransform panel)
        {
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                var c = panel.GetChild(i);
                if (c.name == "Frame") continue;
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }
        }

        /// <summary>Keep a label to its box: one line, shrinking down to <paramref name="min"/>, then cut at a word.</summary>
        private static void OneLine(Text t, int min)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = t.fontSize > min; t.resizeTextMinSize = min; t.resizeTextMaxSize = t.fontSize;
        }

        private static string InventoryKeyName()
        {
#if DUNGEONGEN_INPUTSYSTEM
            return GameBindings.DisplayName(BindingAction.Inventory);
#else
            return "I";
#endif
        }

        /// <summary>The inventory key, through the rebindable table (Settings, Controls).</summary>
        private static bool IKeyPressed()
        {
#if DUNGEONGEN_INPUTSYSTEM
            return GameBindings.WasPressedThisFrame(BindingAction.Inventory);
#else
            return Input.GetKeyDown(KeyCode.I);
#endif
        }
    }
}
