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
        (MoonPhase.Full, ItemID.HeavenforgeBrick, "满月"), (MoonPhase.ThreeQuartersAtRight, ItemID.CosmicEmberBrick, "右侧四分之三亮"),
        (MoonPhase.HalfAtRight, ItemID.CryocoreBrick, "右半月"), (MoonPhase.QuarterAtRight, ItemID.StarRoyaleBrick, "右侧四分之一亮"),
        (MoonPhase.Empty, ItemID.MercuryBrick, "新月"), (MoonPhase.QuarterAtLeft, ItemID.DarkCelestialBrick, "左侧四分之一亮"),
        (MoonPhase.HalfAtLeft, ItemID.AstraBrick, "左半月"), (MoonPhase.ThreeQuartersAtLeft, ItemID.LunarRustBrick, "左侧四分之三亮")
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
            string gates = Join(candidate.ContainsIngredient(ItemID.Bone) ? "击败骷髅王后" : null,
                candidate.ContainsIngredient(ItemID.LihzahrdBrick) ? "击败石巨人后" : null);
            if (gates.Length > 0)
            {
                progress = candidate.DecraftConditions.Count > 0 ? "受当前拆解配方的首领进度限制" : gates;
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
            Transform("coin", 0, "增加投币幸运值 " + ItemID.Sets.CoinLuckValue[equiv]);
            return;
        }
        bool moonOverride = MoonLordTransforms.TryGetValue(equiv, out int moonTarget);
        if (moonOverride)
            Transform("moonlord", moonTarget, "微光转换 · 击败月亮领主后", () => NPC.downedMoonlord);
        string beforeMoonLord = moonOverride ? "击败月亮领主前" : null;
        if (equiv == ItemID.LunarBrick)
        {
            foreach (var outcome in MoonOutcomes)
                Transform("moon", outcome.Item, "微光转换 · " + outcome.Name, () => Main.GetMoonPhase() == outcome.Phase);
            return;
        }
        if (item.createTile == TileID.MusicBoxes)
        {
            if (id != ItemID.MusicBox) Transform("music", ItemID.MusicBox, "恢复为空八音盒");
            return;
        }
        int target = ItemID.Sets.ShimmerTransformToItem[equiv];
        if (target > 0)
        {
            if (target != id && (!moonOverride || target != moonTarget))
                Transform("item", target, Join("微光转换", beforeMoonLord), () => !moonOverride || !NPC.downedMoonlord);
            // Mods can register the same conversion as the vanilla post-Moon Lord override.
            if (target != id && moonOverride && target == moonTarget)
                Transform("item", target, "微光转换 · 击败月亮领主前", () => !NPC.downedMoonlord);
            return;
        }
        if (id == ItemID.GelBalloon)
        {
            Transform("slime", 0, "尚未解锁时释放闪耀史莱姆", () => !NPC.unlockedSlimeRainbowSpawn, NPCID.TownSlimeRainbow);
            return;
        }
        if (item.makeNPC > 0 && item.makeNPC < NPCID.Sets.ShimmerTransformToNPC.Length)
        {
            int spawn = NPCID.Sets.ShimmerTransformToNPC[item.makeNPC];
            if (spawn < 0) spawn = item.makeNPC;
            if (npcIds.Contains(spawn))
                Transform("npc|" + BrowserKeys.NpcKey(spawn), 0, "释放生物 · " + Lang.GetNPCNameValue(spawn), null, spawn);
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
                if (recipe.ContainsIngredient(ItemID.Bone)) conditions.Add("击败骷髅王后");
                if (recipe.ContainsIngredient(ItemID.LihzahrdBrick)) conditions.Add("击败石巨人后");
                var handlers = (ConsumeHooks?.GetValue(recipe) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();
                bool alchemy = handlers.Contains(Recipe.IngredientQuantityRules.Alchemy);
                bool dynamicAmount = handlers.Any(h => !h.Equals(Recipe.IngredientQuantityRules.Alchemy));
                Add(sourceKey + "|decraft|" + crafts[index].Key, new BrowserRow {
                    Category = BrowserCategory.Shimmer, IsDecraft = true, Inputs = new[] { BrowserSlot.Of(id, recipe.createItem.stack) },
                    Outputs = products.Select(x => BrowserSlot.Of(x.type, x.stack)).ToArray(),
                    Conditions = conditions.ToArray(),
                    Note = Join("微光拆解", beforeMoonLord, fallback ? "较高优先级拆解条件未满足时" : null,
                        alchemy ? "每份材料有 1/3 概率不返还" : null, dynamicAmount ? "数量为登记值，模组可动态调整" : null),
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
                        Note = Join(npc.HasValue ? null : bag is int container ? (ItemID.Sets.BossBag[container] ? "开启宝藏袋" : ItemID.Sets.IsFishingCrate[container] ? "开启匣子" : "开启容器") : "全局条件掉落", "基础掉率 " + percent),
                        Difficulties = conditions.Aggregate(LootDifficulty.All, (mask, condition) => mask & DifficultyFor(condition)),
                        Conditions = conditions.Select(c => {
                            string label = DropConditionText(c);
                            if (npc.HasValue && label.Contains("AI 状态"))
                                return DropPredicateText.EncounterKillDescription(c,npc.Value) ?? label;
                            return npc.HasValue && label.Contains("在场") ? DropPredicateText.KillDescription(c,npc.Value) ?? label : label;
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
            return DropPredicateText.Describe(negated.Inner, true) ?? "未满足（" + DropConditionText(negated.Inner) + "）";
        if (PredicateVocabulary.Vanilla(condition) is string vanilla) return vanilla;
        if (condition is Conditions.IsExpert) return "专家模式";
        try
        {
            string text = condition.GetConditionDescription();
            if (!string.IsNullOrWhiteSpace(text)) return PublishedCondition(text, () => DropPredicateText.Describe(condition));
        }
        catch (Exception e) { ReportIssue("掉落条件", e); }
        return condition.GetType().FullName switch {
            "Terraria.GameContent.ItemDropRules.Conditions+NotExpert" => "经典模式",
            "Terraria.GameContent.ItemDropRules.Conditions+NotMasterMode" => "非大师模式",
            "Terraria.GameContent.ItemDropRules.Conditions+NotFromStatue" => "非雕像生成",
            "Terraria.GameContent.ItemDropRules.Conditions+IsHardmode" => "困难模式",
            "Terraria.GameContent.ItemDropRules.Conditions+DownedPlantera" => "击败世纪之花后",
            "Terraria.GameContent.ItemDropRules.Conditions+RemixSeed" => "颠倒世界",
            "Terraria.GameContent.ItemDropRules.Conditions+NotRemixSeed" => "非颠倒世界",
            "Terraria.GameContent.ItemDropRules.Conditions+DontStarveIsUp" => "饥荒联动世界",
            "Terraria.GameContent.ItemDropRules.Conditions+DontStarveIsNotUp" => "非饥荒联动世界",
            "Terraria.GameContent.ItemDropRules.Conditions+RemixSeedHardmode" => "颠倒世界 · 困难模式",
            "Terraria.GameContent.ItemDropRules.Conditions+RemixSeedEasymode" => "颠倒世界 · 困难模式前",
            "Terraria.GameContent.ItemDropRules.Conditions+LegacyHack_IsABoss" => "目标为首领",
            "Terraria.GameContent.ItemDropRules.Conditions+LegacyHack_IsBossAndNotExpert" => "经典模式 · 目标为首领",
            "Terraria.GameContent.ItemDropRules.Conditions+MissingTwin" => "双子魔眼中的另一只已不存在",
            _ => DropLabels.GetValueOrDefault(condition.GetType().Name) ?? DropPredicateText.Describe(condition)
                ?? $"由 {condition.GetType().Assembly.GetName().Name} 的 {condition.GetType().Name} 在掉落时判定"
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
                Note = Join(price == 0 ? "免费" : "基础价", traveller ? "旅商随机进货" : null,
                    remaining > 0 ? $"另需 {remaining} 自定义货币单位" : null),
                PreferredNow = () => conditions.All(c => c.IsMet()) && (!traveller || Main.travelShop.Contains(item.type))
            });
        }
    }
}
