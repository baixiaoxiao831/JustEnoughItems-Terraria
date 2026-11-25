using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

public sealed partial class BrowserIndex
{
    private static readonly Dictionary<string,string> DropLabels = JsonSerializer.Deserialize<Dictionary<string,string>>(
        ModContent.GetInstance<JustEnoughItemsTerraria>().GetFileBytes("Data/DropLabels.json"));

    internal static bool IsGenericConditionLabel(string text) =>
        !string.IsNullOrWhiteSpace(text) && Regex.IsMatch(text,
            @"对应(?:的)?\s*(?:boss|首领)|(?:corresponding|respective) boss|(?:未公开|未提供)说明", RegexOptions.IgnoreCase);

    private static string PublishedCondition(string description, Func<string> readPredicate)
    {
        string text = description.Trim();
        if (IsGenericConditionLabel(text) && readPredicate() is { Length: > 0 } specific) return specific;
        if (DropLabels.TryGetValue(text, out var label)) return label;
        // Inventory chat tags render as pictures in the game chat. Card text needs their
        // registered display name, unless the localization already prints it beside the icon.
        text = Regex.Replace(text, @"\[(?:i|item)(?:/[^:\]]+)?:(?<key>[^\]]+)\]", match => {
            string key = match.Groups["key"].Value;
            if (!int.TryParse(key, out int id) && !ItemID.Search.TryGetId(key, out id)) return key;
            if (!ContentSamples.ItemsByType.ContainsKey(id)) return key;
            string name = Lang.GetItemNameValue(id);
            return text.AsSpan(match.Index + match.Length).TrimStart().StartsWith(name, StringComparison.Ordinal)
                ? "" : name;
        });
        text = Regex.Replace(text, @"\[c/[0-9a-fA-F]{6}:(?<body>[^\]]*)\]", "${body}");
        text = text.Replace("这是专家模式下的掉落率", "专家模式", StringComparison.Ordinal);
        if (!text.Any(char.IsAsciiLetter) || text.Any(c => c >= '\u3400' && c <= '\u9fff')) return text;
        return PredicateVocabulary.Published(text) ?? readPredicate() ?? text;
    }
}
