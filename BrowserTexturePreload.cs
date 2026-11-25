using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

internal static class BrowserTexturePreload
{
    private static readonly Dictionary<string, Asset<Texture2D>> skins = new(StringComparer.Ordinal);

    internal static void LoadSkins(Mod mod)
    {
        foreach (string file in mod.GetFileNames())
        {
            if (!file.StartsWith("Assets/JEI/", StringComparison.Ordinal) ||
                Path.GetExtension(file) is not (".png" or ".rawimg" or ".xnb")) continue;
            string key = Path.GetFileNameWithoutExtension(file);
            skins[key] = mod.Assets.Request<Texture2D>("Assets/JEI/" + key, AssetRequestMode.AsyncLoad);
        }
    }

    internal static Texture2D Skin(string key) => skins[key].Value;
    internal static void Unload() { skins.Clear(); BrowserUI.ClearPortraits(); }

    internal static void LoadCatalog(Mod mod)
    {
        var elapsed = Stopwatch.StartNew();
        var items = ContentSamples.ItemsByType
            .Where(p => p.Key > 0 && p.Value != null && p.Key == p.Value.type && !p.Value.IsAir)
            .Select(p => GetAsset(TextureAssets.Item, p.Key)).ToArray();
        var npcs = ContentSamples.NpcsByNetId.Values.Where(n => n != null)
            .Select(n => n.type).Where(id => id > 0).Distinct().Select(id => GetAsset(TextureAssets.Npc, id)).ToArray();
        var heads = TextureAssets.NpcHead.Concat(TextureAssets.NpcHeadBoss).Where(a => a != null && a != Asset<Texture2D>.Empty).ToArray();
        var placedTiles = ContentSamples.ItemsByType.Values.Where(i => i != null && !i.IsAir && i.createTile >= 0)
            .Select(i => i.createTile).ToHashSet();
        var stationTiles = Main.recipe.Take(Recipe.numRecipes).Where(r => r != null && !r.Disabled)
            .SelectMany(r => r.requiredTile).Distinct()
            .Where(tile => (uint)tile < (uint)TextureAssets.Tile.Length && !placedTiles.Contains(tile))
            .Select(tile => GetAsset(TextureAssets.Tile, tile)).ToArray();
        var plantTiles = BrowserIndex.HarvestTileIds(mod).Distinct().Select(tile => GetAsset(TextureAssets.Tile, tile)).ToArray();
        var pending = items.Concat(npcs).Concat(heads).Concat(stationTiles).Concat(plantTiles).Concat(skins.Values)
            .Where(a => a != null && !a.IsLoaded && a != Asset<Texture2D>.Empty).ToHashSet();
        int initial = pending.Count;
        var issues = new List<string>();
        void RecordIssue(Asset<Texture2D> asset, Exception error)
        {
            if (issues.Count < 5) issues.Add(asset.Name + " (" + error.GetType().Name + ")");
        }

        // A resource pack or mod can replace a vanilla slot with an asset belonging to another
        // repository. Match the actual registered handle, including aliases, rather than guessing a path.
        foreach (var repository in ModLoader.Mods.Select(m => (IAssetRepository)m.Assets)
                     .Prepend(Main.Assets).OfType<AssetRepository>().Distinct())
        {
            if (pending.Count == 0) break;
            var owned = repository.GetLoadedAssets().OfType<Asset<Texture2D>>().Where(pending.Remove);
            foreach (var batch in owned.Chunk(128))
            {
                foreach (var asset in batch)
                {
                    if (asset.State != AssetState.NotLoaded) continue;
                    try { repository.Request<Texture2D>(asset.Name, AssetRequestMode.AsyncLoad); }
                    catch (Exception e) { RecordIssue(asset, e); }
                }
                // ReLogic performs decoding on its workers and marshals transfers to the graphics
                // thread. Bound outstanding XNB/image decodes while the mod loading screen is active.
                if (AssetRepository.IsMainThread) repository.TransferAllAssets();
                else WaitFor(batch);
            }
        }

        // Untracked assets have no path in a repository. Their own loader owns their completion.
        WaitFor(pending);
        if (AssetRepository.IsMainThread) BrowserUI.PreloadCatalogFrames();
        else Main.RunOnMainThread(BrowserUI.PreloadCatalogFrames).GetAwaiter().GetResult();
        void WaitFor(IEnumerable<Asset<Texture2D>> assets)
        {
            foreach (var asset in assets)
            {
                if (asset.IsLoaded || asset.Wait == null) continue;
                try { asset.Wait(); }
                catch (Exception e) { RecordIssue(asset, e); }
            }
        }

        static int Ready(IEnumerable<Asset<Texture2D>> assets) => assets.Count(a => a?.IsLoaded == true);
        int readyItems = Ready(items), readyNpcs = Ready(npcs), readyHeads = Ready(heads), readySkins = Ready(skins.Values);
        int readyTiles = Ready(stationTiles), readyPlants = Ready(plantTiles);
        string status = $"Texture preload complete: items {readyItems}/{items.Length}, NPCs {readyNpcs}/{npcs.Length}, " +
            $"heads {readyHeads}/{heads.Length}, UI {readySkins}/{skins.Count}, station tiles {readyTiles}/{stationTiles.Length}, " +
            $"plant tiles {readyPlants}/{plantTiles.Length}; " +
            $"{initial} unique textures initially pending, {elapsed.ElapsedMilliseconds} ms.";
        if (readyItems == items.Length && readyNpcs == npcs.Length && readyHeads == heads.Length &&
            readySkins == skins.Count && readyTiles == stationTiles.Length && readyPlants == plantTiles.Length)
            mod.Logger.Info(status);
        else mod.Logger.Warn(status + " " + string.Join(", ", issues));
    }

    private static Asset<Texture2D> GetAsset(Asset<Texture2D>[] table, int id) =>
        (uint)id < (uint)table.Length ? table[id] : null;
}
