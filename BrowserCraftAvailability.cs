using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

public sealed partial class BrowserIndex
{
    private sealed record CraftingState(Player Player, ulong Tick, bool StationsKnown, Dictionary<int, long> Amounts);
    private CraftingState craftingState;

    private CraftingState CurrentCraftingState()
    {
        if (Main.gameMenu || Main.dedServ || Main.myPlayer < 0 || Main.myPlayer >= Main.player.Length) return null;
        var player = Main.LocalPlayer;
        if (player == null || !player.active) return null;
        if (craftingState?.Player == player && craftingState.Tick == Main.GameUpdateCount) return craftingState;

        // Terraria updates adjacency while its inventory is visible. When it is closed the same
        // routine updates station and liquid flags and returns before invoking Recipe.FindRecipes.
        bool stationsKnown = Main.playerInventory || Check(() => { player.AdjTiles(); return true; });
        var amounts = new Dictionary<int, long>();
        void Collect(IEnumerable<Item> items)
        {
            if (items == null) return;
            foreach (var item in items)
                if (item is { stack: > 0 } && item.netID > 0)
                    amounts[item.netID] = amounts.GetValueOrDefault(item.netID) + item.stack;
        }
        Collect(player.inventory.Take(58));
        if (player.useVoidBag() && player.chest != -5) Collect(player.bank4.item.Take(40));
        var open = player.chest switch {
            >= 0 when player.chest < Main.chest.Length => Main.chest[player.chest],
            -2 => player.bank, -3 => player.bank2, -4 => player.bank3, -5 => player.bank4, _ => null
        };
        if (open != null) Collect(open.item.Take(40));
        // This is the public hook used by PlayerLoader.GetModdedCraftingMaterials. Each provider's
        // enumeration is isolated; no item-consumption callback is ever invoked by the browser.
        foreach (var provider in player.ModPlayers)
        {
            try
            {
                // A broken iterator may throw partway through. Only commit a complete provider response.
                var items = provider.AddMaterialsForCrafting(out _);
                if (items != null) Collect(items.ToArray());
            }
            catch (Exception e) { ReportIssue("额外合成材料 " + provider.FullName, e); }
        }
        return craftingState = new CraftingState(player, Main.GameUpdateCount, stationsKnown, amounts);
    }

    private int RankCraft(BrowserRow row, CraftingState live)
    {
        var recipe = Main.recipe[row.RecipeIndex];
        if (!Check(() => RecipeLoader.RecipeAvailable(recipe))) return 4;
        bool nearby = row.Stations.Length == 0 || live.StationsKnown &&
            row.Stations.All(s => s.TileId < live.Player.adjTile.Length && live.Player.adjTile[s.TileId]);
        bool materials = true;
        for (int i = 0; i < row.Inputs.Length; i++)
        {
            long count = 0;
            foreach (int type in row.Inputs[i].Alternatives) count += live.Amounts.GetValueOrDefault(type);
            if (count >= recipe.requiredItem[i].stack) continue;
            materials = false;
            break;
        }
        if (!live.StationsKnown && recipe.Conditions.Count > 0) return 4;
        return materials && nearby ? 0 : materials ? 1 : nearby ? 2 : 3;
    }
}
