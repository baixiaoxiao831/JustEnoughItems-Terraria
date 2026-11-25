using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

internal static class BrowserSearch
{
    private sealed record SearchIndex(string[] Forms, string[] Readings);
    private static readonly Dictionary<string, SearchIndex> Forms = new(StringComparer.Ordinal);
    private static readonly Regex Words = new(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled);
    private static Dictionary<char, KeyValuePair<string, string[]>[]> phrases = new();
    private static readonly Dictionary<int,string> readings = new();
    private static string lastQuery;
    private static string[] queryWords;

    internal static void Load(Mod mod)
    {
        if (Terraria.Main.dedServ) return;
        using var reader = new System.IO.StringReader(Encoding.UTF8.GetString(mod.GetFileBytes("Data/MandarinReadings.tsv")));
        while (reader.ReadLine() is string line)
        {
            int tab = line.IndexOf('\t');
            int scalar = int.Parse(line.AsSpan(0,tab), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            readings[scalar] = Normalize(line[(tab + 1)..]);
        }
        var phraseMap = JsonSerializer.Deserialize<Dictionary<string, string>>(mod.GetFileBytes("Data/PhraseReadings.json"));
        phrases = phraseMap.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Split(' ')))
            .GroupBy(pair => pair.Key[0]).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Key.Length).ToArray());
    }

    internal static void Unload()
    {
        Forms.Clear();
        phrases.Clear();
        readings.Clear();
        lastQuery = null;
        queryWords = null;
    }

    internal static void IndexCurrentNames()
    {
        Forms.Clear();
        if (Terraria.Main.dedServ || readings.Count == 0) return;
        void Index(string name)
        {
            if (!string.IsNullOrEmpty(name) && !Forms.ContainsKey(name)) Forms.Add(name, MakeForms(name));
        }
        foreach (var item in Terraria.ID.ContentSamples.ItemsByType.Values) Index(item.Name);
        foreach (var npc in Terraria.ID.ContentSamples.NpcsByNetId.Values) Index(npc.TypeName);
    }

    internal static bool Matches(string name, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (string.IsNullOrEmpty(name)) return false;
        if (!string.Equals(lastQuery, query, StringComparison.Ordinal))
        {
            queryWords = Words.Matches(Normalize(query)).Select(m => m.Value).ToArray();
            lastQuery = query;
        }
        if (!Forms.TryGetValue(name, out var forms))
            Forms[name] = forms = MakeForms(name);
        return queryWords.All(word => Array.Exists(forms.Forms, form => form.Contains(word, StringComparison.Ordinal)) || MatchesMixed(forms.Readings, word));
    }

    private static bool MatchesMixed(string[] parts, string word)
    {
        if (parts.Length < 2 || word.Length < 2 || !word.All(char.IsAsciiLetterOrDigit)) return false;
        Span<bool> next = word.Length <= 256 ? stackalloc bool[word.Length + 1] : new bool[word.Length + 1];
        next.Clear();
        next[word.Length] = true;
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            string part = parts[i];
            for (int start = 0; start < word.Length; start++)
            {
                int n = 0;
                while (n < part.Length && start + n < word.Length &&
                    (word[start+n] == part[n] || (word[start+n] == 'u' && part[n] == 'v'))) n++;
                next[start] = n > 0 && (start+n == word.Length || next[start+1] || (n == part.Length && next[start+n]));
            }
            if (next[0]) return true;
        }
        return false;
    }

    internal static bool MatchesPattern(string name, bool quoted, Func<string,bool> contains)
    {
        if (contains(Normalize(name))) return true;
        if (quoted) return false;
        if (!Forms.TryGetValue(name, out var forms)) Forms[name] = forms = MakeForms(name);
        return forms.Forms.Any(contains);
    }

    internal static string Normalize(string value)
    {
        string decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormKD)
            .Replace("u\u0308", "v").Replace("u:", "v");
        return string.Concat(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
    }

    private static SearchIndex MakeForms(string name)
    {
        string clean = Normalize(name);
        var full = new StringBuilder();
        var initials = new StringBuilder();
        var parts = new List<string>();
        void Add(string reading)
        {
            parts.Add(reading);
            full.Append(reading);
            initials.Append(reading[0]);
        }
        for (int i = 0; i < clean.Length;)
        {
            char c = clean[i];
            if (phrases.TryGetValue(c, out var options))
            {
                var phrase = Array.Find(options, p => clean.AsSpan(i).StartsWith(p.Key, StringComparison.Ordinal));
                if (phrase.Key != null)
                {
                    foreach (string reading in phrase.Value) Add(reading);
                    i += phrase.Key.Length;
                    continue;
                }
            }
            if (char.IsAsciiLetterOrDigit(c))
            {
                int start = i++;
                while (i < clean.Length && char.IsAsciiLetterOrDigit(clean[i])) i++;
                Add(clean[start..i]);
                continue;
            }
            if (!Rune.TryGetRuneAt(clean, i, out var scalar)) { i++; continue; }
            i += scalar.Utf16SequenceLength;
            if (readings.TryGetValue(scalar.Value, out string readingForCharacter)) Add(readingForCharacter);
        }
        string pinyin = full.ToString();
        return new SearchIndex([string.Concat(Words.Matches(clean).Select(m => m.Value)), pinyin, pinyin.Replace('v', 'u'), initials.ToString()], parts.ToArray());
    }
}
