using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
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
    // Texture packs can replace a vanilla slot with a handle owned by a mod.
    private static readonly Dictionary<Asset<Texture2D>, IAssetRepository> modOwners = new();
    private static readonly HashSet<Asset<Texture2D>> failed = new();
    private static int requestsLeft;

    internal static void IndexOwners()
    {
        modOwners.Clear();
        foreach (var mod in ModLoader.Mods)
            foreach (var texture in mod.Assets.GetLoadedAssets().OfType<Asset<Texture2D>>())
                if (!texture.IsLoaded) modOwners[texture] = mod.Assets;
    }

    internal static void BeginDraw() => requestsLeft = 24;

    internal static bool Ready(Asset<Texture2D> texture)
    {
        if (texture == null || texture == Asset<Texture2D>.Empty) return false;
        if (texture.IsLoaded) return true;
        if (requestsLeft == 0 || texture.State != AssetState.NotLoaded || failed.Contains(texture)) return false;
        requestsLeft--;
        try
        {
            var repository = modOwners.GetValueOrDefault(texture) ?? Main.Assets;
            repository.Request<Texture2D>(texture.Name, AssetRequestMode.AsyncLoad);
        }
        catch (Exception error)
        {
            failed.Add(texture);
            ModContent.GetInstance<JustEnoughItemsTerraria>().Logger.Warn($"Could not load catalog texture {texture.Name}", error);
        }
        return texture.IsLoaded;
    }

    internal static void Unload()
    {
        skins.Clear();
        modOwners.Clear();
        failed.Clear();
    }
}
