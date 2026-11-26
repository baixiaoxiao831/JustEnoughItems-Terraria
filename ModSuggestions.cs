using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace JustEnoughItemsTerraria;

internal sealed record ModSuggestion(string Id, string Name);

internal static class ModSuggestions
{
    // Keep quoted item names intact so an @ inside a quoted phrase is ordinary text.
    private static readonly Regex Tokens = new(@"-?[@#]?""[^""]*""?|-?[@#][^\s@#|""]*|[^\s@#|""]+", RegexOptions.Compiled);

    internal static bool TryToken(string text, int caret, out int start, out int end, out string prefix)
    {
        start = end = 0;
        prefix = "";
        foreach (Match token in Tokens.Matches(text))
        {
            int at = token.Index + (token.Value.StartsWith("-@") ? 1 : 0);
            int limit = token.Index + token.Length;
            if (text[at] != '@' || caret <= at || caret > limit) continue;
            start = at;
            end = limit;
            prefix = text[(at + 1)..caret].Trim('"');
            return true;
        }
        return false;
    }

    internal static ModSuggestion[] Sort(IEnumerable<ModSuggestion> mods) =>
        mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();

    internal static ModSuggestion[] Find(ModSuggestion[] mods, string prefix) =>
        prefix.Length == 0 ? mods : mods.Where(m =>
            m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            m.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
}
