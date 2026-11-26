using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.GameContent.Bestiary;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

public enum BrowserCategory { Craft, Shimmer, Drop, Shop, Harvest, Spawn, Bag, Crate, Fishing }

public sealed record BrowserSlot(int ItemId, string Count, int[] Alternatives)
{
    internal static BrowserSlot Of(int id, int count = 1) =>
        new(id, count.ToString(CultureInfo.InvariantCulture), new[] { id });
}

public sealed record BrowserStation(int TileId, string Name, int[] Items)
{
    internal bool CombinedOnly { get; init; }
}

[Flags]
internal enum LootDifficulty { Classic = 1, Expert = 2, Master = 4, All = Classic | Expert | Master }

public sealed class BrowserRow
{
    public string Key { get; internal set; }
    public BrowserCategory Category { get; init; }
    public BrowserHarvest Harvest { get; init; }
    internal bool LavaCrate { get; init; }
    public bool IsDecraft { get; init; }
    public BrowserSlot[] Inputs { get; init; } = Array.Empty<BrowserSlot>();
    public BrowserSlot[] Outputs { get; init; } = Array.Empty<BrowserSlot>();
    public BrowserStation[] Stations { get; init; } = Array.Empty<BrowserStation>();
    public int? Npc { get; init; }
    public int? SpawnedNpc { get; init; }
    public string Note { get; init; } = "";
    public string[] Conditions { get; init; } = Array.Empty<string>();
    public int[] EnvironmentIcons { get; init; } = Array.Empty<int>();
    public string ConditionTooltip { get; init; } = "";
    public string ConditionOwner { get; init; }
    public int RecipeIndex { get; init; } = -1;
    public IReadOnlyList<int> RecipeIndices => OriginalIndices;
    internal readonly List<int> OriginalIndices = new();
    internal string[] BagDetails { get; init; }
    internal LootDifficulty Difficulties { get; init; } = LootDifficulty.All;
    internal Func<bool> PreferredNow { get; init; }
    internal int[] PaymentTypes { get; init; } = Array.Empty<int>();
}

public sealed partial class BrowserIndex
{
    private readonly Dictionary<(int Id, bool Npc, bool Use, BrowserCategory Category), List<BrowserRow>> lookup = new();
    private readonly Dictionary<string, BrowserRow> rowsByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> nextSuffix = new(StringComparer.Ordinal);
    private readonly HashSet<string> errors = new();
    private readonly HashSet<int> itemIds;
    private readonly HashSet<int> npcIds;
    private readonly Dictionary<int, BrowserRow> crafts = new();
    private readonly HashSet<int> bannerProductsWithNpcDrops = new();

    public int[] Items { get; }
    internal CatalogFilters CatalogFilters { get; private set; }
    public int[] Npcs { get; }

    private BrowserIndex()
    {
        Items = ContentSamples.ItemsByType.Where(p => p.Key > 0 && p.Value != null &&
            p.Key == p.Value.type && !p.Value.IsAir).Select(p => p.Key).Order().ToArray();
        Npcs = ContentSamples.NpcsByNetId.Where(p => p.Key != 0 && p.Value != null)
            .Select(p => p.Key).Order().ToArray();
        itemIds = Items.ToHashSet();
        npcIds = Npcs.ToHashSet();
    }

    public static BrowserIndex Build()
    {
        var index = new BrowserIndex();
        index.BuildCrafts();
        foreach (int id in index.Items)
        {
            try { index.AddShimmer(id); }
            catch (Exception e) { index.ReportIssue("微光", e); }
        }
        var fishing=JsonSerializer.Deserialize<Dictionary<string,string[]>>(ModContent.GetInstance<JustEnoughItemsTerraria>().GetFileBytes("Data/FishingCrates.json"));
        foreach(int id in index.Items.Where(id=>ItemID.Sets.IsFishingCrate[id])){
            string name=id<ItemID.Count?ItemID.Search.GetName(id):ItemLoader.GetItem(id).FullName;
            var conditions=fishing.GetValueOrDefault(name)?.Select(JeiText.Get).ToArray()??new[]{JeiText.Get("RegisteredFishingCrateCatchConditionsAreUnknown")};
            index.Add("fishing|"+BrowserKeys.ItemKey(id),new BrowserRow{Category=BrowserCategory.Fishing,
                LavaCrate = name is "LavaCrate" or "LavaCrateHard" or "CalamityMod/SlagCrate" or "CalamityMod/BrimstoneCrate",
                Outputs=[BrowserSlot.Of(id)],Note=JeiText.Get("Fishing2"),Conditions=conditions});
        }
        index.BuildDrops();
        foreach (var (key, rows) in index.lookup)
            if (!key.Npc && !key.Use && key.Category == BrowserCategory.Drop && rows.Any(IsCrateDrop))
            {
                var ordered = rows.OrderByDescending(IsCrateDrop).ToArray();
                rows.Clear();
                rows.AddRange(ordered);
            }
        index.bannerProductsWithNpcDrops.RemoveWhere(id =>
            !index.lookup.TryGetValue((id, false, false, BrowserCategory.Drop), out var drops) ||
            !drops.Any(row => row.Npc.HasValue));
        index.BuildShops();
        index.BuildHarvests();
        index.BuildSpawnEntries();
        index.CatalogFilters = new CatalogFilters(index.Items);
        return index;
    }

    private void BuildSpawnEntries()
    {
        if (Main.BestiaryDB == null) return;
        var canonical = new Dictionary<int,int>();
        var itemOnly = new HashSet<int>();
        var summons = BossSummons(canonical,itemOnly);
        var partsByLeader = canonical.ToLookup(pair => pair.Value, pair => pair.Key);
        foreach (int id in Npcs)
        {
            if (canonical.TryGetValue(id,out int leader) && leader != id) continue;
            var info = partsByLeader[id].Append(id).Distinct().SelectMany(part =>
                Main.BestiaryDB.FindEntryByNPCID(part)?.Info ?? []);
            var tags = info?.Where(e => e is SpawnConditionBestiaryInfoElement or SpawnConditionBestiaryOverlayInfoElement or ModBiomeBestiaryInfoElement)
                .OfType<IFilterInfoProvider>().Select(e => e.GetDisplayNameKey()).Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => Language.GetTextValue(k)).Distinct().ToArray() ?? [];
            var inputs = summons.TryGetValue(id, out var choices) ? choices.Order().Select(item => BrowserSlot.Of(item)).ToArray() : [];
            if (tags.Length == 0 && itemOnly.Contains(id) && inputs.Length == 1 &&
                DropPredicateText.OnlyEncounterGate(ContentSamples.ItemsByType[inputs[0].ItemId].ModItem)) tags = [JeiText.Get("None")];
            var row = new BrowserRow {
                Category = BrowserCategory.Spawn, SpawnedNpc = id, Conditions = tags,
                Inputs = inputs
            };
            Add("spawn|" + BrowserKeys.NpcKey(id),row);
        }
        foreach (var (part,leader) in canonical)
        {
            if (part == leader || !TryRow("spawn|" + BrowserKeys.NpcKey(leader),out var row)) continue;
            rowsByKey["spawn|" + BrowserKeys.NpcKey(part)] = row;
            lookup[(part,true,false,BrowserCategory.Spawn)] = [row];
            lookup[(part,true,true,BrowserCategory.Spawn)] = [row];
        }
    }

    private Dictionary<int, HashSet<int>> BossSummons(Dictionary<int,int> canonical, HashSet<int> itemOnly)
    {
        var result = new Dictionary<int, HashSet<int>>();
        void Register(int npc, IEnumerable<int> items)
        {
            if (!npcIds.Contains(npc)) return;
            if (!result.TryGetValue(npc, out var choices)) result[npc] = choices = new();
            choices.UnionWith(items.Where(itemIds.Contains));
        }

        var mod = ModContent.GetInstance<JustEnoughItemsTerraria>();
        foreach (var (key, values) in JsonSerializer.Deserialize<Dictionary<string,string[]>>(mod.GetFileBytes("Data/VanillaBossSummons.json")))
            if (BrowserKeys.TryNpc(key, out int id))
                Register(id, values.Select(k => BrowserKeys.TryItem(k, out int item) ? item : 0));

        foreach (var (part, leader) in JsonSerializer.Deserialize<Dictionary<string,string>>(mod.GetFileBytes("Data/VanillaBossParts.json")))
            if (BrowserKeys.TryNpc(part,out int member) && BrowserKeys.TryNpc(leader,out int boss))
                canonical[member] = boss;

        var liveBosses = new HashSet<int>();
        if (ModLoader.TryGetMod("BossChecklist", out var checklist))
        {
            try
            {
                if (checklist.Call("GetBossInfoDictionary", mod) is Dictionary<string,Dictionary<string,object>> entries)
                    foreach (var entry in entries.Values)
                        if (entry.GetValueOrDefault("isBoss") is true && entry.GetValueOrDefault("npcIDs") is IEnumerable<int> npcs &&
                            entry.GetValueOrDefault("spawnItems") is IEnumerable<int> items)
                        {
                            var hint = (entry.GetValueOrDefault("spawnInfo") as Func<LocalizedText>)?.Invoke()?.Value ?? "";
                            // Only treat a published item-only instruction as evidence when the item's
                            // own use predicate independently confirms that it checks encounter state only.
                            bool simple = System.Text.RegularExpressions.Regex.IsMatch(hint.Trim(),
                                @"^(?:使用\s*|Use (?:an? |the )?)\[i:[^\]]+\][.。]?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            foreach (var group in npcs.Where(npcIds.Contains).GroupBy(Lang.GetNPCNameValue))
                            {
                                int leader = group.OrderByDescending(n => ContentSamples.NpcsByNetId[n].boss)
                                    .ThenByDescending(n => Main.BestiaryDB.FindEntryByNPCID(n)?.Info.Any(e => e is SpawnConditionBestiaryInfoElement or ModBiomeBestiaryInfoElement) == true).First();
                                foreach (int npc in group) { Register(npc,items); canonical[npc] = leader; liveBosses.Add(npc); }
                                if (simple) itemOnly.Add(leader);
                            }
                        }
            }
            catch (Exception error) { ReportIssue("Boss 召唤物登记", error); }
        }
        // Snapshots use registry names. Only fill gaps in the current mod set.
        foreach (var (key, values) in JsonSerializer.Deserialize<Dictionary<string,string[]>>(mod.GetFileBytes("Data/ModBossSummons.json")))
            if (BrowserKeys.TryNpc(key, out int npc))
                Register(canonical.GetValueOrDefault(npc, npc), values.Select(k => BrowserKeys.TryItem(k, out int item) ? item : 0));

        foreach (var (part, leader) in JsonSerializer.Deserialize<Dictionary<string,string>>(mod.GetFileBytes("Data/ModBossParts.json")))
            if (BrowserKeys.TryNpc(part, out int member) && BrowserKeys.TryNpc(leader, out int boss) &&
                !liveBosses.Contains(member) && !liveBosses.Contains(boss))
                canonical.TryAdd(member, boss);

        mod.DiscoveredSummons ??= SummonDiscovery.Build();
        foreach (var (boss, item) in mod.DiscoveredSummons.Concat(mod.RegisteredSummons))
            Register(canonical.GetValueOrDefault(boss, boss), [item]);
        return result;
    }

    private readonly Dictionary<(int,LootDifficulty),BrowserRow> bagCards = new();
    public bool TryRow(string key, out BrowserRow row)
    {
        if (key != null && (key.StartsWith("bag|") || key.StartsWith("crate|")) && BrowserKeys.TryItem(key[(key.IndexOf('|')+1)..],out int bag))
        {
            row = Query(bag,false,true,ItemID.Sets.BossBag[bag] ? BrowserCategory.Bag : BrowserCategory.Crate).FirstOrDefault(r=>r.Key==key);
            return row != null;
        }
        return rowsByKey.TryGetValue(key ?? "",out row);
    }

    internal static LootDifficulty CurrentDifficulty => Main.gameMenu || Main.ActiveWorldFileData == null
        ? LootDifficulty.All : Main.masterMode ? LootDifficulty.Master : Main.expertMode ? LootDifficulty.Expert : LootDifficulty.Classic;

    internal bool PrefersDropSource(int id) => bannerProductsWithNpcDrops.Contains(id) ||
        lookup.TryGetValue((id, false, false, BrowserCategory.Drop), out var rows) && IsCrateDrop(rows[0]);

    private static bool IsCrateDrop(BrowserRow row) =>
        row.Inputs.Length == 1 && ItemID.Sets.IsFishingCrate[row.Inputs[0].ItemId];

    internal bool HasRows(int id, bool npc, bool uses) => Enum.GetValues<BrowserCategory>().Any(category => lookup.ContainsKey((id, npc, uses, category)));

    internal bool HasHarvestSource(int id) => lookup.ContainsKey((id, false, false, BrowserCategory.Harvest));

    public BrowserRow[] Query(int id, bool isNpc, bool uses, BrowserCategory category) =>
        QueryOrdered(id, isNpc, uses, category, null);

    public BrowserRow[] QueryOrdered(int id, bool isNpc, bool uses, BrowserCategory category, Func<BrowserRow, bool> bookmarked)
    {
        if (!lookup.TryGetValue((id, isNpc, uses, category), out var rows))
            return Array.Empty<BrowserRow>();
        if (category is BrowserCategory.Drop or BrowserCategory.Bag or BrowserCategory.Crate)
        {
            var difficulty = CurrentDifficulty;
            var visible = rows.Where(row => (row.Difficulties & difficulty) != 0);
            if (category is BrowserCategory.Bag or BrowserCategory.Crate)
            {
                if (!bagCards.TryGetValue((id,difficulty),out var card))
                {
                    var drops = visible.ToArray();
                    if (drops.Length == 0) return [];
                    bagCards[(id,difficulty)] = card = new BrowserRow {
                        Key = (category==BrowserCategory.Bag ? "bag|" : "crate|") + BrowserKeys.ItemKey(id), Category = category, Inputs = drops[0].Inputs,
                        Note = "", Outputs = drops.Select(r => r.Outputs[0]).ToArray(),
                        BagDetails = drops.Select(r => string.Join("\n",r.Note.Split(" · ").Skip(1).Concat(r.Conditions))).ToArray()
                    };
                }
                return [card];
            }
            return bookmarked == null ? visible.ToArray() : visible.OrderByDescending(bookmarked).ToArray();
        }
        if (rows.Count == 1)
            return rows.ToArray();
        IOrderedEnumerable<BrowserRow> ordered;
        if (category == BrowserCategory.Craft)
        {
            var live = CurrentCraftingState();
            if (live != null) ordered = rows.OrderBy(r => RankCraft(r, live));
            else
            {
                // A headless host has no local inventory. It can still supply an explicit native list.
                bool guide = Main.guideItem != null && !Main.guideItem.IsAir;
                var available = !guide && Main.availableRecipe != null ?
                    Main.availableRecipe.Take(Main.numAvailableRecipes).ToHashSet() : new HashSet<int>();
                ordered = rows.OrderBy(r => r.OriginalIndices.Any(available.Contains) ? 0 : 4);
            }
        }
        else ordered = rows.OrderBy(r => Check(r.PreferredNow) ? 0 : 1);
        return (bookmarked == null ? ordered : ordered.ThenByDescending(bookmarked)).ToArray();
    }

    internal bool Check(Func<bool> check)
    {
        if (check == null) return true;
        try { return check(); }
        catch (Exception e) { ReportIssue("条件", e); return false; }
    }

    private void ReportIssue(string part, Exception error)
    {
        string message = $"{part} / {error.TargetSite?.DeclaringType?.FullName} / {error.GetType().Name}";
        if (errors.Add(message))
            ModContent.GetInstance<AdaptiveBrowserSystem>().Mod.Logger.Warn($"配方浏览器跳过异常登记或条件：{message} — {error.Message}");
    }

    private void Add(string baseKey, BrowserRow row, int? equivalentProduct = null)
    {
        if (row.Npc.HasValue && !npcIds.Contains(row.Npc.Value) ||
            row.SpawnedNpc.HasValue && !npcIds.Contains(row.SpawnedNpc.Value)) return;
        string key = baseKey;
        if (rowsByKey.ContainsKey(baseKey))
        {
            int suffix = nextSuffix.GetValueOrDefault(baseKey, 1);
            do { key = baseKey + "#" + ++suffix; } while (rowsByKey.ContainsKey(key));
            nextSuffix[baseKey] = suffix;
        }
        row.Key = key;
        rowsByKey.Add(key, row);
        foreach (int id in row.Inputs.SelectMany(s => s.Alternatives).Concat(row.PaymentTypes).Distinct()) Link(id, false, true);
        if (row.Harvest is { } plant)
        {
            if (plant.SeedId > 0) Link(plant.SeedId, false, true);
            foreach (int tool in plant.Tools) Link(tool, false, true);
        }
        foreach (int id in SourceItems(row, equivalentProduct)) Link(id, false, false);
        if (row.Npc is int npc) Link(npc, true, true);
        if (row.SpawnedNpc is int spawned)
        {
            Link(spawned, true, false);
            if (row.Category == BrowserCategory.Spawn) Link(spawned, true, true);
        }

        void Link(int id, bool isNpc, bool uses)
        {
            var category = row.Category == BrowserCategory.Drop && uses && !isNpc &&
                row.Inputs.Length == 1 && row.Inputs[0].ItemId == id
                ? ItemID.Sets.BossBag[id] ? BrowserCategory.Bag : ItemID.Sets.IsFishingCrate[id] ? BrowserCategory.Crate : row.Category : row.Category;
            var k = (id, isNpc, uses, category);
            if (!lookup.TryGetValue(k, out var list)) lookup.Add(k, list = new());
            list.Add(row);
        }
    }

    private IEnumerable<int> SourceItems(BrowserRow row, int? equivalentProduct)
    {
        var outputs = row.Outputs.Select(slot => slot.ItemId).Distinct();
        if (!row.IsDecraft) return outputs;

        // Keep the entire refund card in the parent's uses. Only index a refund as a
        // source if the refunded item is not already a crafting dependency of that parent.
        var independent = outputs.ToHashSet();
        int parent = row.Inputs[0].ItemId;
        var visited = new HashSet<int> { parent };
        var pending = new Stack<int>();
        pending.Push(parent);
        // ShimmerCountsAsItem variants may have no crafting recipe of their own.
        if (equivalentProduct is int equivalent && visited.Add(equivalent)) pending.Push(equivalent);
        while (independent.Count > 0 && pending.TryPop(out int product))
        {
            if (!lookup.TryGetValue((product, false, false, BrowserCategory.Craft), out var recipes)) continue;
            foreach (var recipe in recipes)
            foreach (var slot in recipe.Inputs)
            foreach (int material in slot.Alternatives)
            {
                independent.Remove(material);
                if (independent.Count == 0) return independent;
                if (visited.Add(material)) pending.Push(material);
            }
        }
        return independent;
    }

    private static string Describe(Condition condition)
    {
        string text = condition?.Description?.Value;
        if (!string.IsNullOrWhiteSpace(text)) return PublishedCondition(text, () => DropPredicateText.Describe(condition.Predicate));
        return DropPredicateText.Describe(condition?.Predicate) ??
            JeiText.Get("CheckedInGameBy", condition?.Predicate?.Method.DeclaringType?.Assembly.GetName().Name, condition?.Predicate?.Method.Name);
    }
    private static string Join(params string[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private void BuildCrafts()
    {
        var stations = BuildStations();
        var bannerItems = ContentSamples.NpcsByNetId.Values.Select(npc => Item.NPCtoBanner(npc.BannerID()))
            .Where(id => id > 0).Distinct().Select(Item.BannerToItem).Where(itemIds.Contains).ToHashSet();
        var mod = ModContent.GetInstance<JustEnoughItemsTerraria>();
        var environmentIcons = JsonSerializer.Deserialize<Dictionary<string, int>>(mod.GetFileBytes("Data/CraftingEnvironments.json"));
        var groups = RecipeGroup.recipeGroups.ToDictionary(p => p.Key,
            p => p.Value.ValidItems.Where(itemIds.Contains).Order().ToArray());
        var groupNames = RecipeGroup.recipeGroupIDs.OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToLookup(p => p.Value, p => p.Key);
        var ordinary = new Dictionary<string, List<BrowserRow>>(StringComparer.Ordinal);
        var craftHook = typeof(Recipe).GetProperty("OnCraftHooks",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        for (int i = 0; i < Recipe.numRecipes; i++)
        {
            var recipe = Main.recipe[i];
            try
            {
                if (recipe == null || recipe.Disabled || !ValidStack(recipe.createItem) || ExchangeItem(recipe.createItem.type) ||
                    recipe.requiredItem.Any(o => !ValidStack(o)) ||
                    recipe.requiredTile.Any(t => t < 0 || t >= TileLoader.TileCount) ||
                    recipe.acceptedGroups.Any(g => !groups.ContainsKey(g))) continue;
                var inputs = new List<BrowserSlot>(recipe.requiredItem.Count);
                var slotKeys = new List<string>(recipe.requiredItem.Count);
                string slotKey(Item item) => BrowserKeys.ItemKey(item.type) + "*" + item.stack +
                    (item.prefix > 0 ? "~" + PrefixID.Search.GetName(item.prefix) : "");
                foreach (var item in recipe.requiredItem)
                {
                    var slot = BrowserSlot.Of(item.type, item.stack);
                    string slotGroup = "";
                    // tML's CreateRequiredItemQuickLookups lets the last matching group supply this slot.
                    foreach (int group in recipe.acceptedGroups)
                        if (Array.BinarySearch(groups[group], item.type) >= 0)
                        {
                            int[] members = groups[group];
                            slot = slot with { Alternatives = members[0] == item.type ? members :
                                new[] { item.type }.Concat(members.Where(id => id != item.type)).ToArray() };
                            slotGroup = "@" + (groupNames[group].FirstOrDefault() ??
                                string.Join("+", groups[group].Select(BrowserKeys.ItemKey).Order(StringComparer.Ordinal)));
                        }
                    int[] allowed=slot.Alternatives.Where(id=>!ExchangeItem(id)).ToArray();
                    if(allowed.Length==0)break;
                    if(allowed.Length!=slot.Alternatives.Length)slot=slot with { ItemId=allowed[0], Alternatives=allowed };
                    inputs.Add(slot);
                    slotKeys.Add(slotKey(item) + slotGroup);
                }
                if(inputs.Count!=recipe.requiredItem.Count)continue;
                var conditions = recipe.Conditions.ToArray();
                string ConditionLabel(Condition c) =>
                    c?.Description?.Key == "Conditions.InGraveyard" ? JeiText.Get("GraveyardBiome") : Describe(c);
                int EnvironmentIcon(Condition c) => environmentIcons.GetValueOrDefault(c?.Description?.Key ?? "");
                var tipConditions = conditions.OrderByDescending(c => EnvironmentIcon(c) > 0).ToArray();
                string keyOfCondition = tipConditions.FirstOrDefault()?.Description?.Key ?? "";
                string conditionOwner = null;
                if (keyOfCondition.StartsWith("Conditions.", StringComparison.Ordinal)) conditionOwner = "Terraria";
                else if (keyOfCondition.Split('.') is ["Mods", var owner, ..] && ModLoader.TryGetMod(owner, out var conditionMod))
                    conditionOwner = conditionMod.DisplayNameClean;
                var requiredStations = recipe.requiredTile.Distinct().Select(t => stations[t]).ToArray();
                var row = new BrowserRow {
                    Category = BrowserCategory.Craft, RecipeIndex = i,
                    Inputs = inputs.ToArray(), Outputs = new[] { BrowserSlot.Of(recipe.createItem.type, recipe.createItem.stack) },
                    Stations = requiredStations,
                    EnvironmentIcons = conditions.Select(EnvironmentIcon).Where(icon => icon > 0).Distinct().ToArray(),
                    ConditionTooltip = string.Join("\n", tipConditions.Select(ConditionLabel).Distinct()),
                    ConditionOwner = conditionOwner,
                    Conditions = conditions.Where(c => EnvironmentIcon(c) == 0).Select(Describe).Concat(requiredStations.Where(s => s.CombinedOnly)
                        .Select(s => JeiText.Get("RequiresStation") + s.Name)).ToArray()
                };
                string key = string.Join("|", "craft", slotKey(recipe.createItem),
                    string.Join(",", slotKeys.Order(StringComparer.Ordinal)),
                    string.Join(",", recipe.requiredTile.Distinct().Select(t => TileID.Search.GetName(t)).Order(StringComparer.Ordinal)),
                    string.Join(",", conditions.Select(c => c?.Description?.Key + "@" + MethodKey(c?.Predicate))));
                // Only merge when the actual condition predicates agree. Two mods can publish identical
                // text for different world restrictions. Keep all custom recipe callbacks separate.
                bool plain = craftHook != null && ConsumeHooks != null &&
                    craftHook.GetValue(recipe) == null && ConsumeHooks.GetValue(recipe) == null;
                string savedKey = key;
                if (conditions.Length > 0) savedKey += "|from|" + (recipe.Mod?.Name ?? "Terraria");
                if (plain)
                {
                    if (!ordinary.TryGetValue(key, out var candidates)) ordinary[key] = candidates = new();
                    var equivalent = candidates.FirstOrDefault(r => Main.recipe[r.RecipeIndex].Conditions.SequenceEqual(conditions));
                    if (equivalent != null)
                    {
                        equivalent.OriginalIndices.Add(i);
                        crafts[i] = equivalent;
                        rowsByKey.TryAdd(savedKey, equivalent);
                        continue;
                    }
                    candidates.Add(row);
                }
                else savedKey += "|callback|" + (recipe.Mod?.Name ?? "Terraria") + "|" +
                    MethodKey(craftHook?.GetValue(recipe) as Delegate) + "|" + MethodKey(ConsumeHooks?.GetValue(recipe) as Delegate);
                Add(savedKey, row);
                if (inputs.Any(slot => slot.Alternatives.Any(bannerItems.Contains)))
                    bannerProductsWithNpcDrops.Add(recipe.createItem.type);
                row.OriginalIndices.Add(i);
                crafts[i] = row;
            }
            catch (Exception e) { ReportIssue("合成", e); }
        }
    }

    internal static bool ExchangeItem(int type)
    {
        if(ItemID.Sets.BossBag[type] || ItemID.Sets.IsFishingCrate[type])return true;
        var item=ContentSamples.ItemsByType[type];
        string name=item.ModItem?.Name ?? ItemID.Search.GetName(type);
        return item.createTile>=0 && name.Contains("Trophy",StringComparison.OrdinalIgnoreCase);
    }
    private bool ValidStack(Item item) => item != null && item.stack > 0 && itemIds.Contains(item.type);

    private static string MethodKey(Delegate callback) => callback == null ? "" :
        string.Join("+", callback.GetInvocationList().Select(d =>
            d.Method.DeclaringType?.Assembly.GetName().Name + "/" + d.Method.DeclaringType?.FullName + "." + d.Method.Name));

    private Dictionary<int, BrowserStation> BuildStations()
    {
        // The rail describes the exact tile in the recipe. A higher-tier station can satisfy
        // proximity via AdjTiles without being the work block this recipe asks for.
        var tiles = Items.Select(id => ContentSamples.ItemsByType[id]).Where(item => item.createTile >= 0)
            .GroupBy(item => item.createTile).ToDictionary(group => group.Key,
                group => group.OrderBy(item => item.placeStyle != 0).ThenBy(item => item.type)
                    .Select(item => item.type).ToArray());
        var recipes = Main.recipe.Take(Recipe.numRecipes).Where(r => r != null && !r.Disabled).ToArray();
        var required = recipes.SelectMany(r => r.requiredTile).Where(t => t >= 0 && t < TileLoader.TileCount).ToHashSet();
        var adjacency = new Dictionary<int, HashSet<int>>();
        var hooks = ModContent.GetContent<GlobalTile>().Where(g =>
            g.GetType().GetMethod(nameof(GlobalTile.AdjTiles), new[] { typeof(int) })?.DeclaringType != typeof(GlobalTile)).ToArray();
        foreach (int tile in required)
        {
            // These native relationships distinguish upgrades in one station family from
            // combinations of independent stations. Only direct placements go in the rail.
            int[] native = tile switch {
                TileID.Hellforge or TileID.GlassKiln => new int[] { TileID.Furnaces },
                TileID.AdamantiteForge => new int[] { TileID.Furnaces, TileID.Hellforge },
                TileID.MythrilAnvil => new int[] { TileID.Anvils },
                TileID.BewitchingTable or TileID.Tables2 or TileID.PicnicTable => new int[] { TileID.Tables },
                TileID.AlchemyTable => new int[] { TileID.Bottles, TileID.Tables }, _ => Array.Empty<int>()
            };
            var adjacent = native.ToHashSet();
            try { adjacent.UnionWith(TileLoader.GetTile(tile)?.AdjTiles ?? Array.Empty<int>()); }
            catch (Exception e) { ReportIssue("工作站", e); }
            foreach (var hook in hooks)
            {
                try { adjacent.UnionWith(hook.AdjTiles(tile) ?? Array.Empty<int>()); }
                catch (Exception e) { ReportIssue("工作站", e); }
            }
            adjacent.IntersectWith(required);
            adjacent.Remove(tile);
            adjacency[tile] = adjacent;
        }
        var family = new Dictionary<int, HashSet<int>>();
        foreach (var (tile, immediate) in adjacency)
        {
            var reachable = new HashSet<int>();
            var queue = new Stack<int>(immediate);
            while (queue.TryPop(out int id))
                if (reachable.Add(id)) foreach (int next in adjacency[id]) queue.Push(next);
            family[tile] = reachable;
        }
        bool HasIndependentStations(IEnumerable<int> candidates)
        {
            var ids = candidates.Distinct().ToArray();
            for (int a = 0; a < ids.Length; a++)
                for (int b = a + 1; b < ids.Length; b++)
                    if (!family[ids[a]].Contains(ids[b]) && !family[ids[b]].Contains(ids[a])) return true;
            return false;
        }

        var assembled = new HashSet<int>();
        foreach (var recipe in recipes)
        {
            if (!ValidStack(recipe.createItem)) continue;
            int target = recipe.createItem.createTile;
            if (!required.Contains(target)) continue;
            if (HasIndependentStations(recipe.requiredItem.Where(ValidStack).Select(i => i.createTile)
                    .Where(t => t != target && required.Contains(t))))
                assembled.Add(recipe.createItem.type);
        }
        var result = new Dictionary<int, BrowserStation>();
        foreach (int tile in required)
        {
            var direct = tiles.GetValueOrDefault(tile) ?? Array.Empty<int>();
            bool combinedTile = tile >= TileID.Count && HasIndependentStations(adjacency[tile]);
            var options = combinedTile ? Array.Empty<int>() : direct.Where(id => !assembled.Contains(id)).ToArray();
            string name = direct.Length > 0 ? Lang.GetItemNameValue(options.Length > 0 ? options[0] : direct[0])
                : NaturalStationName(tile);
            result[tile] = new BrowserStation(tile, name, options) {
                CombinedOnly = combinedTile || direct.Length > 0 && options.Length == 0
            };
        }
        return result;
    }

    internal static string NaturalStationName(int tile)
    {
        // Use the first map entry for the same base style used by the station portrait.
        // Dedicated servers do not build the client map legend.
        if (!Main.dedServ && Lang._mapLegendCache is { } legend &&
            MapHelper.tileLookup is { } lookup && (uint)tile < (uint)lookup.Length &&
            lookup[tile] > 0 && lookup[tile] < legend.Length)
        {
            var entry = legend[lookup[tile]];
            if (entry != null && !string.IsNullOrWhiteSpace(entry.Value) && entry.Value != entry.Key) return entry.Value;
        }
        if (TileLoader.GetTile(tile) is { } modTile)
        {
            var entry = modTile.CreateMapEntryName();
            if (!string.IsNullOrWhiteSpace(entry.Value) && entry.Value != entry.Key) return entry.Value;
        }
        string name = TileID.Search.GetName(tile).Split('/')[^1];
        string key = "MapObject." + name;
        return Language.Exists(key) ? Language.GetTextValue(key) :
            System.Text.RegularExpressions.Regex.Replace(name, @"(?<=[a-z0-9])(?=[A-Z])", " ");
    }
}
