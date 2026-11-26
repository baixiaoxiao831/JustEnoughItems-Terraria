using System.Collections.Generic;
using System.Linq;
using System;
using Terraria.ModLoader;
using System.Runtime.CompilerServices;
using Terraria;

namespace JustEnoughItemsTerraria;

internal static class NativeRecipeMaterials
{
    // This is the same count table vanilla fills when it rebuilds availableRecipe.
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "_ownedItems")]
    private static extern ref Dictionary<int, int> OwnedItems(Recipe owner);

    private static readonly HashSet<int> available = new();
    private static readonly Dictionary<(int Recipe, int Slot, bool Recursive), Shortage> preview = new();
    private static ulong previewTick;

    internal static void Refresh()
    {
        available.Clear();
        if (Main.GameUpdateCount - previewTick >= 10) { preview.Clear(); previewTick = Main.GameUpdateCount; }
        for (int i = 0; i < Main.numAvailableRecipes; i++)
        {
            int recipe = Main.availableRecipe[i];
            available.Add(recipe);

        }
    }

    internal static int AvailableRecipe(BrowserRow row) =>
        row.OriginalIndices.FirstOrDefault(available.Contains, -1);

    internal readonly record struct Shortage(long Missing, bool Craftable);

    internal static Shortage Check(BrowserRow row, int slot)
    {
        var accepted = row.Inputs[slot].Alternatives;
        long have = 0;
        var counts = OwnedItems(null);
        foreach (int type in accepted) have += counts.GetValueOrDefault(type);
        long missing = Main.recipe[row.RecipeIndex].requiredItem[slot].stack - have;
        if (missing <= 0) return default;
        bool recursive = MultiStepEnabled;
        var key = (row.RecipeIndex, slot, recursive);
        if (preview.TryGetValue(key, out var cached) && cached.Missing == missing) return cached;
        var path = new HashSet<int> { Main.recipe[row.RecipeIndex].createItem.type };
        int attempts = 0;
        bool craftable = accepted.Any(type => Make(type, missing, counts.ToDictionary(p => p.Key, p => (long)p.Value), path, ref attempts, recursive));
        return preview[key] = new(missing, craftable);
    }

    // Simulate batches in private stock. Failed alternatives leave the caller's stock intact.
    private static bool Make(int type, long count, Dictionary<int,long> stock, HashSet<int> path, ref int attempts, bool recursive, List<(Recipe Recipe, int Batches)> steps = null, BrowserRow selected = null)
    {
        if (path.Count >= 100 || !path.Add(type)) return false;
        try
        {
            foreach (var row in selected == null ? AdaptiveBrowserSystem.Index.Query(type, false, false, BrowserCategory.Craft) : new[] { selected })
            foreach (var index in row.OriginalIndices)
            {
                if (++attempts > 20000) return false;
                var recipe = Main.recipe[index];
                if (recipe.Disabled || recipe.createItem.type != type ||
                    recipe.requiredTile.Any(t => t >= 0 && !Main.LocalPlayer.adjTile[t]) ||
                    !RecipeLoader.RecipeAvailable(recipe)) continue;
                long batches = 1 + (count - 1) / recipe.createItem.stack;
                var next = new Dictionary<int,long>(stock);
                int start = steps?.Count ?? 0;
                bool possible = true;
                for (int i = 0; i < row.Inputs.Length; i++)
                {
                    if (batches > int.MaxValue / recipe.requiredItem[i].stack) { possible = false; break; }
                    long need = batches * recipe.requiredItem[i].stack;
                    var choices = row.Inputs[i].Alternatives;
                    foreach (int choice in choices)
                    {
                        long take = Math.Min(need, next.GetValueOrDefault(choice));
                        next[choice] = next.GetValueOrDefault(choice) - take;
                        need -= take;
                        if (need == 0) break;
                    }
                    if (need == 0) continue;
                    if (!recursive) { possible = false; break; }
                    bool found = false;
                    foreach (int choice in choices)
                        if (Make(choice, need, next, path, ref attempts, true, steps)) { found = true; break; }
                    if (!found) { possible = false; break; }
                }
                if (!possible)
                {
                    steps?.RemoveRange(start, steps.Count - start);
                    continue;
                }
                steps?.Add((recipe, (int)batches));
                next[type] = next.GetValueOrDefault(type) + batches * recipe.createItem.stack - count;
                stock.Clear();
                foreach (var pair in next) stock.Add(pair.Key, pair.Value);
                return true;
            }
            return false;
        }
        finally { path.Remove(type); }
    }

    internal static bool MultiStepEnabled => ModContent.GetInstance<BrowserClientConfig>().BackpackMultiStepCrafting;

    internal static bool CraftChain(BrowserRow row, bool commit)
    {
        if (!MultiStepEnabled || !InventoryOverview.BackpackAvailable || !Main.mouseItem.IsAir) return false;
        if (commit) Recipe.FindRecipes();
        var stock = OwnedItems(null).ToDictionary(p => p.Key, p => (long)p.Value);
        var steps = new List<(Recipe Recipe, int Batches)>();
        int attempts = 0;
        var output = Main.recipe[row.RecipeIndex].createItem;
        if (!Make(output.type, output.stack, stock, new(), ref attempts, true, steps, row)) return false;
        if (!commit) return true;
        for (int i = 0; i < steps.Count; i++)
        {
            var (recipe, batches) = steps[i];
            for (int batch = 0; batch < batches; batch++)
            {
                Recipe.FindRecipes();
                if (Array.IndexOf(Main.availableRecipe, recipe.RecipeIndex, 0, Main.numAvailableRecipes) < 0)
                    return false;
                Main.CraftItem(recipe);
                if (i == steps.Count - 1) return !Main.mouseItem.IsAir;
                Main.mouseItem = Main.LocalPlayer.GetItem(Main.myPlayer, Main.mouseItem,
                    GetItemSettings.InventoryUIToInventorySettingsShowAsNew);
                if (!Main.mouseItem.IsAir) return false;
            }
        }
        return false;
    }

    internal static string CraftableHint => JeiText.Get("HighlightedItemsCanBeCrafted");
    internal static string MissingHint => JeiText.Get("MissingIngredients");
    internal static string Legend(bool blue, bool red) => blue && red ? CraftableHint + "\n" + MissingHint :
        blue ? CraftableHint : red ? MissingHint : "";

    internal static string Explain(BrowserRow row)
    {
        var reasons = new List<string>();
        var shortages = Enumerable.Range(0, row.Inputs.Length).Select(slot => Check(row,slot)).ToArray();
        string legend = Legend(shortages.Any(s => s.Craftable), shortages.Any(s => s.Missing > 0 && !s.Craftable));
        if (legend.Length > 0) reasons.Add(legend);
        foreach (var station in row.Stations)
            if (!Main.LocalPlayer.adjTile[station.TileId]) reasons.Add(JeiText.Get("MissingStation") + station.Name);
        foreach (var condition in Main.recipe[row.RecipeIndex].Conditions)
        {
            try { if (!condition.IsMet()) reasons.Add(JeiText.Get("UnmetCondition") + condition.Description.Value); }
            catch { reasons.Add(JeiText.Get("CouldNotCheckCondition") + condition.Description.Value); }
        }
        return reasons.Count == 0 ? JeiText.Get("ThisRecipeIsCurrentlyUnavailableInThe") : string.Join("\n", reasons);
    }
}
