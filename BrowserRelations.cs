using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.Enums;
using Terraria.GameContent.ItemDropRules;
using Terraria.GameContent.UI;
using Terraria.ID;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

public sealed partial class BrowserIndex
{
    // The following four overrides and eight moon outcomes are hard-coded in vanilla Item.GetShimmered.
    // All normal item conversions, aliases and decrafts below read the final modded registry.
    private static readonly Dictionary<int, int> MoonLordTransforms = new() {
        [ItemID.RodofDiscord] = ItemID.RodOfHarmony, [ItemID.Clentaminator] = ItemID.Clentaminator2,
        [ItemID.BottomlessBucket] = ItemID.BottomlessShimmerBucket, [ItemID.BottomlessShimmerBucket] = ItemID.BottomlessBucket
    };
    private static readonly (MoonPhase Phase, int Item, string Name)[] MoonOutcomes = {
        (MoonPhase.Full, ItemID.HeavenforgeBrick, "FullMoon"), (MoonPhase.ThreeQuartersAtRight, ItemID.CosmicEmberBrick, "WaningGibbous"),
        (MoonPhase.HalfAtRight, ItemID.CryocoreBrick, "ThirdQuarter"), (MoonPhase.QuarterAtRight, ItemID.StarRoyaleBrick, "WaningCrescent"),
        (MoonPhase.Empty, ItemID.MercuryBrick, "NewMoon"), (MoonPhase.QuarterAtLeft, ItemID.DarkCelestialBrick, "WaxingCrescent"),
        (MoonPhase.HalfAtLeft, ItemID.AstraBrick, "FirstQuarter"), (MoonPhase.ThreeQuartersAtLeft, ItemID.LunarRustBrick, "WaxingGibbous")
    };
    private static readonly PropertyInfo ConsumeHooks = typeof(Recipe).GetProperty(
        "ConsumeIngredientHooks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private void AddShimmer(int id)
    {
        var item = ContentSamples.ItemsByType[id];
        int equiv = ItemID.Sets.ShimmerCountsAsItem[id];
        if (equiv < 0) equiv = id;
        if (!itemIds.Contains(equiv)) return;
        var candidates = (ItemID.Sets.CraftingRecipeIndices[equiv] ?? Array.Empty<int>())
            .Where(index => crafts.ContainsKey(index) && !Main.recipe[index].DecraftDisabled).ToArray();
        bool IsBossLocked(Recipe r) => (!NPC.downedBoss3 && r.ContainsIngredient(ItemID.Bone)) ||
            (!NPC.downedGolemBoss && r.ContainsIngredient(ItemID.LihzahrdBrick));
        int ActiveDecraft()
        {
            foreach (int i in candidates)
                if (Check(() => RecipeLoader.DecraftAvailable(Main.recipe[i]))) return i;
            return -1;
        }
        bool Unlocked()
        {
            int i = ActiveDecraft();
            return i < 0 || !IsBossLocked(Main.recipe[i]);
        }
        string progress = "";
        foreach (int i in candidates)
        {
            var candidate = Main.recipe[i];
            string gates = Join(candidate.ContainsIngredient(ItemID.Bone) ? JeiText.Get("AfterDefeatingSkeletron") : null,
                candidate.ContainsIngredient(ItemID.LihzahrdBrick) ? JeiText.Get("AfterDefeatingGolem") : null);
            if (gates.Length > 0)
            {
                progress = candidate.DecraftConditions.Count > 0 ? JeiText.Get("SubjectToThisDecraftingRecipeSBoss") : gates;
                break;
            }
            if (candidate.DecraftConditions.Count == 0) break;
        }

        string sourceKey = "shimmer|" + BrowserKeys.ItemKey(id);
        void Transform(string rule, int target, string note, Func<bool> enabled = null, int? spawned = null)
        {
            if (target != 0 && !itemIds.Contains(target)) return;
            Add(sourceKey + "|" + rule + "|" + (target == 0 ? "" : BrowserKeys.ItemKey(target)), new BrowserRow {
                Category = BrowserCategory.Shimmer, Inputs = new[] { BrowserSlot.Of(id) },
                Outputs = target == 0 ? Array.Empty<BrowserSlot>() : new[] { BrowserSlot.Of(target) },
                SpawnedNpc = spawned, Note = Join(note, progress),
                PreferredNow = () => Unlocked() && (enabled == null || enabled())
            });
        }
        if (ItemID.Sets.CoinLuckValue[equiv] > 0)
        {
            Transform("coin", 0, JeiText.Get("CoinLuckGained") + ItemID.Sets.CoinLuckValue[equiv]);
            return;
        }
        bool moonOverride = MoonLordTransforms.TryGetValue(equiv, out int moonTarget);
        if (moonOverride)
            Transform("moonlord", moonTarget, JeiText.Get("ShimmerTransformationAfterDefeatingMoonLord"), () => NPC.downedMoonlord);
        string beforeMoonLord = moonOverride ? JeiText.Get("BeforeDefeatingMoonLord") : null;
        if (equiv == ItemID.LunarBrick)
        {
            foreach (var outcome in MoonOutcomes)
                Transform("moon", outcome.Item, JeiText.Get("ShimmerTransformation2") + JeiText.Get(outcome.Name), () => Main.GetMoonPhase() == outcome.Phase);
            return;
        }
        if (item.createTile == TileID.MusicBoxes)
        {
            if (id != ItemID.MusicBox) Transform("music", ItemID.MusicBox, JeiText.Get("RevertsToAnEmptyMusicBox"));
            return;
        }
        int target = ItemID.Sets.ShimmerTransformToItem[equiv];
        if (target > 0)
        {
            if (target != id && (!moonOverride || target != moonTarget))
                Transform("item", target, Join(JeiText.Get("ShimmerTransformation"), beforeMoonLord), () => !moonOverride || !NPC.downedMoonlord);
            // Mods can register the same conversion as the vanilla post-Moon Lord override.
            if (target != id && moonOverride && target == moonTarget)
                Transform("item", target, JeiText.Get("ShimmerTransformationBeforeDefeatingMoonLord"), () => !NPC.downedMoonlord);
            return;
        }
        if (id == ItemID.GelBalloon)
        {
            Transform("slime", 0, JeiText.Get("ReleasesTheDivaSlimeIfStillLocked"), () => !NPC.unlockedSlimeRainbowSpawn, NPCID.TownSlimeRainbow);
            return;
        }
        if (item.makeNPC > 0 && item.makeNPC < NPCID.Sets.ShimmerTransformToNPC.Length)
        {
            int spawn = NPCID.Sets.ShimmerTransformToNPC[item.makeNPC];
            if (spawn < 0) spawn = item.makeNPC;
            if (npcIds.Contains(spawn))
                Transform("npc|" + BrowserKeys.NpcKey(spawn), 0, JeiText.Get("ReleasesCreature") + Lang.GetNPCNameValue(spawn), null, spawn);
            return;
        }
        bool fallback = false;
        foreach (int index in candidates)
        {
            var recipe = Main.recipe[index];
            var products = recipe.customShimmerResults ?? recipe.requiredItem;
            if (products.Count > 0 && products.All(ValidStack))
            {
                var conditions = recipe.DecraftConditions.Select(Describe).ToList();
                if (recipe.ContainsIngredient(ItemID.Bone)) conditions.Add(JeiText.Get("AfterDefeatingSkeletron"));
                if (recipe.ContainsIngredient(ItemID.LihzahrdBrick)) conditions.Add(JeiText.Get("AfterDefeatingGolem"));
                var handlers = (ConsumeHooks?.GetValue(recipe) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();
                bool alchemy = handlers.Contains(Recipe.IngredientQuantityRules.Alchemy);
                bool dynamicAmount = handlers.Any(h => !h.Equals(Recipe.IngredientQuantityRules.Alchemy));
                Add(sourceKey + "|decraft|" + crafts[index].Key, new BrowserRow {
                    Category = BrowserCategory.Shimmer, IsDecraft = true, Inputs = new[] { BrowserSlot.Of(id, recipe.createItem.stack) },
                    Outputs = products.Select(x => BrowserSlot.Of(x.type, x.stack)).ToArray(),
                    Conditions = conditions.ToArray(),
                    Note = Join(JeiText.Get("ShimmerDecrafting"), beforeMoonLord, fallback ? JeiText.Get("WhenHigherPriorityDecraftingConditionsAreUnmet") : null,
                        alchemy ? JeiText.Get("EachIngredientHasA13Chance") : null, dynamicAmount ? JeiText.Get("RegisteredQuantitiesShownModsMayAdjustThem") : null),
                    PreferredNow = () => (!moonOverride || !NPC.downedMoonlord) && ActiveDecraft() == index && !IsBossLocked(recipe)
                }, recipe.createItem.type);
            }
            if (recipe.DecraftConditions.Count == 0) break;
            fallback = true;
        }
    }

    private void BuildDrops()
    {
        var db = Main.ItemDropsDB;
        if (db == null) return;
        using var capture = BrowserDropReport.Capture();
        foreach (int id in Npcs) AddDropRules(db.GetRulesForNPCID(id, false), id, null);
        // Query a non-NPC key once to retrieve precisely the registered global rules.
        AddDropRules(db.GetRulesForNPCID(int.MaxValue), null, null);
        foreach (int id in Items)
            AddDropRules(db.GetRulesForItemID(id), null, id);
    }

    private void AddDropRules(IEnumerable<IItemDropRule> rules, int? npc, int? bag)
    {
        foreach (var rule in rules)
        {
            try
            {
                var rates = new List<DropRateInfo>();
                BrowserDropReport.Report(rule, rates);
                foreach (var drop in rates)
                {
                    if (!itemIds.Contains(drop.itemId) || drop.stackMax <= 0 || !float.IsFinite(drop.dropRate) ||
                        drop.dropRate <= 0 || drop.conditions?.Any(c => c is Conditions.NeverTrue) == true) continue;
                    var conditions = drop.conditions?.Where(c => c != null).ToArray() ?? Array.Empty<IItemDropRuleCondition>();
                    string count = drop.stackMin == drop.stackMax ? drop.stackMin.ToString() : $"{drop.stackMin}–{drop.stackMax}";
                    string percent = (drop.dropRate * 100d).ToString("0.#####", CultureInfo.InvariantCulture) + "%";
                    string origin = npc is int n ? BrowserKeys.NpcKey(n) : bag is int b ? BrowserKeys.ItemKey(b) : "global";
                    string key = string.Join("|", "drop", origin, BrowserKeys.ItemKey(drop.itemId), count,
                        drop.dropRate.ToString("R", CultureInfo.InvariantCulture), rule.GetType().FullName,
                        string.Join(",", conditions.Select(c => (c is BrowserDropReport.Negated ncond
                            ? ncond.Inner : c).GetType().FullName)));
                    Add(key, new BrowserRow {
                        Category = BrowserCategory.Drop, Npc = npc,
                        Inputs = bag.HasValue ? new[] { BrowserSlot.Of(bag.Value) } : Array.Empty<BrowserSlot>(),
                        Outputs = new[] { new BrowserSlot(drop.itemId, count, new[] { drop.itemId }) },
                        Note = Join(npc.HasValue ? null : bag is int container ? (ItemID.Sets.BossBag[container] ? JeiText.Get("TreasureBags") : ItemID.Sets.IsFishingCrate[container] ? JeiText.Get("FishingCrates") : JeiText.Get("OpenContainer")) : JeiText.Get("GlobalConditionalDrop"), JeiText.Get("BaseDropRate") + percent),
                        Difficulties = conditions.Aggregate(LootDifficulty.All, (mask, condition) => mask & DifficultyFor(condition)),
                        Conditions = conditions.Select(c => {
                            string label = DropConditionText(c);
                            if (npc.HasValue && label.Contains(JeiText.Get("AIState")))
                                return DropPredicateText.EncounterKillDescription(c,npc.Value) ?? label;
                            return npc.HasValue && label.Contains(JeiText.Get("Present")) ? DropPredicateText.KillDescription(c,npc.Value) ?? label : label;
                        }).Distinct().ToArray()
                    });
                }
            }
            catch (Exception e) { ReportIssue("掉落规则 " + rule?.GetType().Name, e); }
        }
    }

    private readonly Dictionary<IItemDropRuleCondition, string> dropConditionLabels = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<IItemDropRuleCondition, LootDifficulty> difficultyMasks = new(ReferenceEqualityComparer.Instance);
    private LootDifficulty DifficultyFor(IItemDropRuleCondition condition)
    {
        if (!difficultyMasks.TryGetValue(condition, out var mask))
            difficultyMasks[condition] = mask = DropPredicateText.Difficulties(condition);
        return mask;
    }

    private string DropConditionText(IItemDropRuleCondition condition)
    {
        if (!dropConditionLabels.TryGetValue(condition, out string label))
            dropConditionLabels[condition] = label = ReadDropCondition(condition);
        return label;
    }

    private string ReadDropCondition(IItemDropRuleCondition condition)
    {
        if (condition is BrowserDropReport.Negated negated)
            return DropPredicateText.Describe(negated.Inner, true) ?? JeiText.Get("UnmetCondition2") + DropConditionText(negated.Inner) + "）";
        if (PredicateVocabulary.Vanilla(condition) is string vanilla) return vanilla;
        if (condition is Conditions.IsExpert) return JeiText.Get("ExpertMode");
        try
        {
            string text = condition.GetConditionDescription();
            if (!string.IsNullOrWhiteSpace(text)) return PublishedCondition(text, () => DropPredicateText.Describe(condition));
        }
        catch (Exception e) { ReportIssue("掉落条件", e); }
        return condition.GetType().FullName switch {
            "Terraria.GameContent.ItemDropRules.Conditions+NotExpert" => JeiText.Get("ClassicMode"),
            "Terraria.GameContent.ItemDropRules.Conditions+NotMasterMode" => JeiText.Get("OutsideMasterMode"),
            "Terraria.GameContent.ItemDropRules.Conditions+NotFromStatue" => JeiText.Get("NotSpawnedByAStatue"),
            "Terraria.GameContent.ItemDropRules.Conditions+IsHardmode" => JeiText.Get("Hardmode"),
            "Terraria.GameContent.ItemDropRules.Conditions+DownedPlantera" => JeiText.Get("AfterDefeatingPlantera"),
            "Terraria.GameContent.ItemDropRules.Conditions+RemixSeed" => JeiText.Get("RemixWorld"),
            "Terraria.GameContent.ItemDropRules.Conditions+NotRemixSeed" => JeiText.Get("OutsideARemixWorld"),
            "Terraria.GameContent.ItemDropRules.Conditions+DontStarveIsUp" => JeiText.Get("TheConstantWorld"),
            "Terraria.GameContent.ItemDropRules.Conditions+DontStarveIsNotUp" => JeiText.Get("OutsideTheConstant"),
            "Terraria.GameContent.ItemDropRules.Conditions+RemixSeedHardmode" => JeiText.Get("RemixWorldHardmode"),
            "Terraria.GameContent.ItemDropRules.Conditions+RemixSeedEasymode" => JeiText.Get("RemixWorldPreHardmode"),
            "Terraria.GameContent.ItemDropRules.Conditions+LegacyHack_IsABoss" => JeiText.Get("TargetIsABoss"),
            "Terraria.GameContent.ItemDropRules.Conditions+LegacyHack_IsBossAndNotExpert" => JeiText.Get("ClassicModeTargetIsABoss"),
            "Terraria.GameContent.ItemDropRules.Conditions+MissingTwin" => JeiText.Get("TheOtherTwinIsNoLongerPresent"),
            _ => DropLabels.GetValueOrDefault(condition.GetType().Name) ?? DropPredicateText.Describe(condition)
                ?? JeiText.Get("CheckedOnDropBy", condition.GetType().Assembly.GetName().Name, condition.GetType().Name)
        };
    }

    private void BuildShops()
    {
        var unitField = typeof(CustomCurrencySystem).GetField("_valuePerUnit",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var currencies = new Dictionary<int, Dictionary<int, int>> {
            [-1] = new() { [ItemID.CopperCoin] = 1, [ItemID.SilverCoin] = 100, [ItemID.GoldCoin] = 10000, [ItemID.PlatinumCoin] = 1000000 }
        };
        foreach (var shop in NPCShopDatabase.AllShops)
        {
            try
            {
                foreach (var entry in shop.ActiveEntries)
                {
                    try { AddShopEntry(shop, entry); }
                    catch (Exception e) { ReportIssue("商店条目", e); }
                }
            }
            catch (Exception e) { ReportIssue("商店目录", e); }
        }

        void AddShopEntry(AbstractNPCShop shop, AbstractNPCShop.Entry entry)
        {
            var item = entry.Item;
            if (!ValidStack(item)) return;
            int price = item.GetStoreValue(), currency = item.shopSpecialCurrency;
            if (price < 0) return;
            if (!currencies.TryGetValue(currency, out var units))
            {
                units = new Dictionary<int, int>();
                if (CustomCurrencyManager.TryGetCurrencySystem(currency, out var system) &&
                    unitField?.GetValue(system) is Dictionary<int, int> values)
                    foreach (var unit in values.Where(v => v.Value > 0 && itemIds.Contains(v.Key))) units[unit.Key] = unit.Value;
                currencies[currency] = units;
            }
            int remaining = price;
            var payment = new List<BrowserSlot>();
            foreach (var unit in units.OrderByDescending(u => u.Value).ThenBy(u => u.Key))
            {
                int count = remaining / unit.Value;
                remaining %= unit.Value;
                if (count > 0) payment.Add(BrowserSlot.Of(unit.Key, count));
            }
            var conditions = (entry.Conditions ?? Enumerable.Empty<Condition>()).ToArray();
            bool traveller = shop is TravellingMerchantShop;
            string currencyKey = currency == -1 ? "coins" :
                string.Join(",", units.OrderBy(u => BrowserKeys.ItemKey(u.Key), StringComparer.Ordinal)
                    .Select(u => BrowserKeys.ItemKey(u.Key) + "*" + u.Value));
            string key = string.Join("|", "shop", shop.FullName, BrowserKeys.ItemKey(item.type), item.stack,
                price, currencyKey, string.Join(",", conditions.Select(c => c?.Description?.Key)));
            Add(key, new BrowserRow {
                Category = BrowserCategory.Shop, Npc = shop.NpcType, Inputs = payment.ToArray(),
                PaymentTypes = price > 0 ? units.Keys.ToArray() : Array.Empty<int>(),
                Outputs = new[] { BrowserSlot.Of(item.type, item.stack) },
                Conditions = conditions.Select(Describe).Distinct().ToArray(),
                Note = Join(price == 0 ? JeiText.Get("Free") : JeiText.Get("BasePrice"), traveller ? JeiText.Get("RandomTravelingMerchantStock") : null,
                    remaining > 0 ? JeiText.Get("AlsoRequiresUnitsOfCustomCurrency", remaining) : null),
                PreferredNow = () => conditions.All(c => c.IsMet()) && (!traveller || Main.travelShop.Contains(item.type))
            });
        }
    }
}
