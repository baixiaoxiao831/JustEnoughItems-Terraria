using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

public sealed record BrowserHarvest(int ItemId, int SeedId, string Name, string Stage, string Habitat,
    string Condition, int[] Tools, int TileId, Rectangle Frame);

public sealed partial class BrowserIndex
{
    private sealed record HarvestCatalog(Plant[] Plants);
    private sealed record Plant(string Key, int Item, int Seed, string Habitat, HarvestStage[] Rows);
    private sealed record HarvestStage(string Key, string Stage, string Condition, int[] Tools,
        int Tile, int[] Frame, JsonElement[][] Outputs);
    private static Plant[] harvestPlants;

    private static Plant[] ReadHarvestPlants(Mod mod) => harvestPlants ??=
        JsonSerializer.Deserialize<HarvestCatalog>(mod.GetFileBytes("Data/HarvestSources.json"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).Plants;

    private void BuildHarvests()
    {
        // Wild and planted herbs share a tile. Seeds and tools are searchable uses,
        // but neither is a consumed ingredient in the act of harvesting.
        foreach (var plant in ReadHarvestPlants(ModContent.GetInstance<JustEnoughItemsTerraria>()))
        foreach (var stage in plant.Rows)
        {
            var outputs = stage.Outputs.Select(pair => {
                int id = pair[0].GetInt32();
                return new BrowserSlot(id, pair[1].GetString(), new[] { id });
            }).ToArray();
            if (!itemIds.Contains(plant.Item) || plant.Seed != 0 && !itemIds.Contains(plant.Seed) ||
                outputs.Any(slot => !itemIds.Contains(slot.ItemId)) || stage.Tools.Any(id => !itemIds.Contains(id))) continue;
            int[] f = stage.Frame;
            var plantFrame = new Rectangle(f[0], f[1], f[2], f[3]);
            Add($"harvest|{plant.Key}|{stage.Key}", new BrowserRow {
                Category = BrowserCategory.Harvest, Outputs = outputs,
                Harvest = new(plant.Item, plant.Seed, Lang.GetItemNameValue(plant.Item), JeiText.Get(stage.Stage),
                    JeiText.Get(plant.Habitat), JeiText.Get(stage.Condition), stage.Tools, stage.Tile, plantFrame)
            });
        }
    }
}
