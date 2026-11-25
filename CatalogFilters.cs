using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ID;
using Terraria.GameContent.Creative;

namespace JustEnoughItemsTerraria;

internal sealed class CatalogFilters
{
    internal Dictionary<string, HashSet<int>> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<int, int> NativeRanks { get; } = new();
    internal int[] OrderedItems { get; }
    private readonly Dictionary<string, string> owners = new(StringComparer.Ordinal);
    private static readonly Regex terms = new(@"[@#][^\s@#]*|[^\s@#]+", RegexOptions.Compiled);

    internal CatalogFilters(IEnumerable<int> ids)
    {
        // Native predicates used by UICreativeInfiniteItemsDisplay, including modded item sets.
        var filters = new List<IItemEntryFilter> {
            new ItemFilters.Weapon(), new ItemFilters.Armor(), new ItemFilters.Vanity(),
            new ItemFilters.BuildingBlock(), new ItemFilters.Furniture(), new ItemFilters.Accessories(),
            new ItemFilters.MiscAccessories(), new ItemFilters.Consumables(), new ItemFilters.Tools(), new ItemFilters.Materials()
        };
        foreach (var filter in filters.Append(new ItemFilters.MiscFallback(filters)))
        {
            string key = filter.GetDisplayNameKey()["CreativePowers.Tab".Length..];
            var members = ids.Where(id => filter.FitsFilter(ContentSamples.ItemsByType[id])).ToHashSet();
            int rank = Tags.Count;
            foreach (int id in members) NativeRanks.TryAdd(id, rank);
            Tags.Add(key, members);
            string localized = Terraria.Localization.Language.GetTextValue(filter.GetDisplayNameKey());
            Tags.TryAdd(localized, members);
        }
        OrderedItems = ids.OrderBy(id => NativeRanks.GetValueOrDefault(id, int.MaxValue)).ToArray();
        foreach (var (key, groupId) in RecipeGroup.recipeGroupIDs)
        {
            if (!Tags.TryGetValue(key, out var items)) Tags[key] = items = new();
            items.UnionWith(RecipeGroup.recipeGroups[groupId].ValidItems);
        }
        foreach (int id in ids)
        {
            Item item = ContentSamples.ItemsByType[id];
            var mod = item.ModItem?.Mod;
            string owner = mod?.Name ?? "Terraria";
            if (!owners.ContainsKey(owner))
            {
                string display = owner == "ModLoader" ? "tModLoader" : mod?.DisplayNameClean;
                owners.Add(owner, display != null && display.All(char.IsAscii) ? owner + " " + display : owner);
            }
        }
    }

    internal IReadOnlyDictionary<string, string> OwnerSearchNames => owners;

    private static readonly Regex ExpressionTokens = new(@"\||-?[@#]?""[^""]*""?|[^\s""|]+", RegexOptions.Compiled);

    internal int[] Filter(int[] ids, string query)
    {
        query = (query ?? "").Normalize(NormalizationForm.FormKC);
        if (query.IndexOfAny(['"', '*', '|', '-']) < 0) return FilterPlain(ids, query);
        var union = new HashSet<int>();
        var plain = new List<string>();
        int[] candidates = ids;
        bool hasTerm = false, hasBranch = false;
        void Flush()
        {
            if (plain.Count > 0) candidates = FilterPlain(candidates, string.Join(" ", plain));
            plain.Clear();
        }
        void Finish()
        {
            Flush();
            if (hasTerm) { union.UnionWith(candidates); hasBranch = true; }
            candidates = ids;
            hasTerm = false;
        }
        foreach (Match match in ExpressionTokens.Matches(query))
        {
            string token = match.Value;
            if (token == "|") { Finish(); continue; }
            bool negative = token.StartsWith('-');
            string expr = negative ? token[1..] : token;
            if (expr.Length == 0) continue;
            char field = expr[0] is '@' or '#' ? expr[0] : '\0';
            string body = field == '\0' ? expr : expr[1..];
            bool quoted = body.StartsWith('"');
            string pattern = BrowserSearch.Normalize(quoted ? body.Trim('"') : body);
            if (quoted && pattern.Length == 0) continue;
            hasTerm = true;
            bool glob = pattern.Contains('*');
            if (!negative && !quoted && !glob) { plain.Add(token); continue; }
            Flush();
            IEnumerable<int> matches;
            if (!quoted && !glob) matches = FilterPlain(candidates, expr);
            else
            {
                var chunks = pattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
                bool Contains(string text)
                {
                    int start = 0;
                    foreach (string chunk in chunks)
                    {
                        int at = text.IndexOf(chunk, start, StringComparison.Ordinal);
                        if (at < 0) return false;
                        start = at + chunk.Length;
                    }
                    return true;
                }
                if (field == '#')
                {
                    var group = Tags.Where(p => Contains(BrowserSearch.Normalize(p.Key))).SelectMany(p => p.Value).ToHashSet();
                    matches = candidates.Where(group.Contains);
                }
                else if (field == '@')
                {
                    var sources = owners.Where(p => Contains(BrowserSearch.Normalize(p.Value))).Select(p => p.Key).ToHashSet();
                    matches = candidates.Where(id => sources.Contains(ContentSamples.ItemsByType[id].ModItem?.Mod.Name ?? "Terraria"));
                }
                else matches = candidates.Where(id => BrowserSearch.MatchesPattern(Lang.GetItemNameValue(id), quoted, Contains));
            }
            candidates = negative ? candidates.Except(matches).ToArray() : matches.ToArray();
        }
        Finish();
        return hasBranch ? ids.Where(union.Contains).ToArray() : ids;
    }
    private int[] FilterPlain(int[] ids, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return ids;
        string[] words = terms.Matches(query.Normalize(NormalizationForm.FormKC)).Select(m => m.Value).ToArray();
        var names = new List<string>();
        var tagFilters = new List<HashSet<int>>();
        var ownerFilters = new List<HashSet<string>>();
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            if (word[0] == '#')
            {
                string term = word[1..];
                if (term.Length == 0) continue;
                // Some mods register group names containing spaces.
                for (int j = i + 1; j < words.Length && words[j][0] is not ('#' or '@'); j++)
                {
                    string candidate = string.Join(" ", words, i, j - i + 1)[1..];
                    if (!Tags.ContainsKey(candidate)) continue;
                    term = candidate;
                }
                i += term.Count(c => c == ' ');
                if (Tags.TryGetValue(term, out var exact)) tagFilters.Add(exact);
                else tagFilters.Add(Tags.Where(p => p.Key.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(p => p.Value).ToHashSet());
            }
            else if (word[0] == '@')
            {
                string term = word[1..];
                HashSet<string> Find(string text) => owners.Where(p => text.Length == 0
                    ? p.Key != "Terraria" : BrowserSearch.Matches(p.Value, text)).Select(p => p.Key).ToHashSet();
                var matches = Find(term);
                while (term.Length > 0 && i + 1 < words.Length && words[i + 1][0] is not ('#' or '@'))
                {
                    string longer = term + words[i + 1];
                    var combined = Find(longer);
                    if (combined.Count == 0) break;
                    matches = combined;
                    term = longer;
                    i++;
                }
                ownerFilters.Add(matches);
            }
            else names.Add(word);
        }
        string name = string.Join(" ", names);
        return ids.Where(id => tagFilters.All(set => set.Contains(id))
            && ownerFilters.All(set => set.Contains(ContentSamples.ItemsByType[id].ModItem?.Mod.Name ?? "Terraria"))
            && (name.Length == 0 || BrowserSearch.Matches(Lang.GetItemNameValue(id), name))).ToArray();
    }
}
