using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Profile;
using Xunit;

namespace DungeonGen.Tests
{
    /// <summary>
    /// The crafting bench's profile rules (<see cref="CraftingActions"/>): what a craft costs the account, why it is refused without a
    /// single change, and the craft itself, which spends scrap, silver, shards and pinned essences and puts the new item in the stash.
    /// </summary>
    public class CraftingActionsTests
    {
        private static PlayerProfile Rich(long silver = 1000, int scrap = 1000, int shards = 0)
        {
            var p = PlayerProfile.NewAccount("a", "Bram", ClassArchetype.ByName("Warden"));
            p.Silver = silver; p.Materials.Scrap = scrap; p.Materials.PrimeShards = shards;
            return p;
        }

        [Fact]
        public void CraftSpendsScrapAndSilverAndAddsTheItemToTheStash()
        {
            var p = Rich();
            int stash = p.Stash.Count;
            var req = new CraftRequest(ItemBase.ByName("Longsword"), 2);
            Assert.Null(CraftingActions.Craft(p, req, new DungeonRandom(5), out var item));
            Assert.NotNull(item);
            Assert.Equal(1000 - CraftingRules.ScrapCost(2), p.Materials.Scrap);
            Assert.Equal(1000 - CraftingRules.SilverFee(2), p.Silver);
            Assert.Equal(stash + 1, p.Stash.Count);
            Assert.Contains(item, p.Stash);
            Assert.Equal("Longsword", item.Base.Name);
            Assert.Equal(2, item.ItemLevel);
            Assert.True(item.Rarity == Rarity.Magic || item.Rarity == Rarity.Rare);
        }

        [Fact]
        public void ARefusedCraftChangesNothingAndSaysWhy()
        {
            var p = Rich(silver: 5, scrap: 100);
            var req = new CraftRequest(ItemBase.ByName("Ring"), 3);   // 160 scrap, 80 silver
            var why = CraftingActions.Refusal(p, req);
            Assert.NotNull(why);
            Assert.Contains("scrap", why);
            Assert.Equal(why, CraftingActions.Craft(p, req, new DungeonRandom(1), out var item));
            Assert.Null(item);
            Assert.Equal(100, p.Materials.Scrap);
            Assert.Equal(5, p.Silver);
            Assert.DoesNotContain(p.Stash, i => i.Base.Name == "Ring");

            p.Materials.Scrap = 1000;   // scrap now fine, silver is not
            Assert.Contains("silver", CraftingActions.Refusal(p, req));
            Assert.Contains("silver", CraftingActions.Craft(p, req, new DungeonRandom(1), out _));
            Assert.Equal(1000, p.Materials.Scrap);
            Assert.Equal(5, p.Silver);
        }

        [Fact]
        public void PinnedEssencesAndShardsAreSpentAndTheFamiliesAlwaysRoll()
        {
            var p = Rich(shards: 4);
            p.Materials.AddEssence("Resist:Fire", 4);
            p.Materials.AddEssence("Attribute:Strength", 3);
            var req = new CraftRequest(ItemBase.ByName("Ring"), 4, new[] { "Resist:Fire", "Attribute:Strength" }, forceRare: true);
            Assert.Null(CraftingActions.Craft(p, req, new DungeonRandom(9), out var item));
            Assert.Equal(Rarity.Rare, item.Rarity);
            Assert.Contains(item.Affixes, a => a.Definition.FamilyKey == "Resist:Fire");
            Assert.Contains(item.Affixes, a => a.Definition.FamilyKey == "Attribute:Strength");
            Assert.Equal(1, p.Materials.Essence("Resist:Fire"));
            Assert.Equal(0, p.Materials.Essence("Attribute:Strength"));
            Assert.Equal(1, p.Materials.PrimeShards);
            Assert.Equal(1000 - CraftingRules.SilverFee(4), p.Silver);
        }

        [Fact]
        public void ShortOnAnEssenceOrAShardRefusesWithoutSpendingScrap()
        {
            var p = Rich(shards: 2);
            p.Materials.AddEssence("Resist:Fire", 2);
            var pinned = new CraftRequest(ItemBase.ByName("Ring"), 1, new[] { "Resist:Fire" });
            Assert.Contains("Resist:Fire", CraftingActions.Craft(p, pinned, new DungeonRandom(1), out _));
            var rare = new CraftRequest(ItemBase.ByName("Ring"), 1, forceRare: true);
            Assert.Contains("shards", CraftingActions.Craft(p, rare, new DungeonRandom(1), out _));
            Assert.Equal(1000, p.Materials.Scrap);
            Assert.Equal(1000, p.Silver);
            Assert.Empty(p.Stash);
        }

        [Fact]
        public void AFamilyThatCannotRollOnTheBaseIsRefused()
        {
            var p = Rich();
            p.Materials.AddEssence("IncreasedDamage", 3);
            var req = new CraftRequest(ItemBase.ByName("Plate Helm"), 2, new[] { "IncreasedDamage" });
            Assert.Contains("cannot roll", CraftingActions.Refusal(p, req));
            Assert.DoesNotContain("IncreasedDamage", CraftingActions.LegalFamilies(ItemBase.ByName("Plate Helm")).Select(d => d.FamilyKey));
            Assert.DoesNotContain("IncreasedDamage", CraftingActions.PinnableFamilies(p, ItemBase.ByName("Plate Helm")));
            Assert.Contains("IncreasedDamage", CraftingActions.PinnableFamilies(p, ItemBase.ByName("Longsword")));
        }

        [Fact]
        public void PinnableFamiliesNeedThreeEssencesAndLeadWithTheLargestStock()
        {
            var p = Rich();
            p.Materials.AddEssence("Resist:Fire", 2);
            p.Materials.AddEssence("Resist:Frost", 3);
            p.Materials.AddEssence("Resist:Lightning", 7);
            var ring = ItemBase.ByName("Ring");
            Assert.Equal(new[] { "Resist:Lightning", "Resist:Frost" }, CraftingActions.PinnableFamilies(p, ring).ToArray());
        }

        [Fact]
        public void CostsListEveryPriceWithWhatTheAccountHolds()
        {
            var p = Rich(silver: 30, scrap: 500, shards: 1);
            p.Materials.AddEssence("Health", 3);
            var req = new CraftRequest(ItemBase.ByName("Plate Cuirass"), 3, new[] { "Health" }, forceRare: true);
            var costs = CraftingActions.Costs(p, req);
            Assert.Equal(new[] { "scrap", "silver", "prime shards", "Health essence" }, costs.Select(c => c.Name).ToArray());
            Assert.Equal(160, costs[0].Need); Assert.True(costs[0].Ok);
            Assert.Equal(80, costs[1].Need); Assert.False(costs[1].Ok);
            Assert.Equal(3, costs[2].Need); Assert.False(costs[2].Ok);
            Assert.Equal(3, costs[3].Need); Assert.True(costs[3].Ok);
            Assert.NotNull(CraftingActions.Refusal(p, req));
        }

        [Fact]
        public void RecipesCoverEveryLevelAndMarkWhatIsAffordable()
        {
            var p = Rich(silver: 45, scrap: 100);
            var recipes = CraftingActions.Recipes(p, ItemBase.ByName("Longsword"));
            Assert.Equal(5, recipes.Count);
            Assert.Equal(new[] { 10, 40, 90, 160, 250 }, recipes.Select(r => r.Scrap).ToArray());
            Assert.Equal(new long[] { 10, 20, 40, 80, 160 }, recipes.Select(r => r.SilverFee).ToArray());
            Assert.Equal(new[] { true, true, true, false, false }, recipes.Select(r => r.Affordable).ToArray());
        }

        [Fact]
        public void EverySlotHasBasesAndTheBenchCoversAllOfThem()
        {
            Assert.Equal(8, CraftingActions.Slots.Count);
            foreach (var slot in CraftingActions.Slots) Assert.NotEmpty(CraftingActions.Bases(slot));
            Assert.Equal(ItemBase.All.Count(), CraftingActions.Slots.Sum(s => CraftingActions.Bases(s).Count));
        }

        [Fact]
        public void RarityOddsMatchTheLootTableAndForcedRareIsCertain()
        {
            var odds0 = CraftingActions.RarityOdds(new CraftRequest(ItemBase.ByName("Longsword"), 0));
            var odds4 = CraftingActions.RarityOdds(new CraftRequest(ItemBase.ByName("Longsword"), 4));
            Assert.InRange(odds0.rare, 0.01f, 0.08f);       // 2 / 30
            Assert.InRange(odds4.rare, 0.50f, 0.60f);       // 50 / 90
            Assert.Equal(1f, odds0.magic + odds0.rare, 3);
            Assert.Equal((0f, 1f), CraftingActions.RarityOdds(new CraftRequest(ItemBase.ByName("Longsword"), 0, forceRare: true)));
            // Two prefixes need a Rare: a Magic item has one of each kind.
            Assert.Equal((0f, 1f), CraftingActions.RarityOdds(new CraftRequest(ItemBase.ByName("Longsword"), 1, new[] { "IncreasedDamage", "AddedDamage:Fire" })));
        }

        [Fact]
        public void AffixRangeReplacesTheNumberWithThePossibleRange()
        {
            Assert.Equal("+8–20% armour", CraftingActions.AffixRange("IncreasedArmour", 2));      // tier 2, or tier 1 three times in ten
            Assert.Equal("+8–12% armour", CraftingActions.AffixRange("IncreasedArmour", 0));      // level 0 is tier 1 only
            Assert.Equal("+8–12% armour", CraftingActions.AffixRange("IncreasedArmour", 1));
            Assert.Equal("+1–3 Strength", CraftingActions.AffixRange("Attribute:Strength", 3));
            Assert.Equal("+2–4 Strength", CraftingActions.AffixRange("Attribute:Strength", 4));
            Assert.Equal("Nonsense", CraftingActions.AffixRange("Nonsense", 2));
        }
    }
}
