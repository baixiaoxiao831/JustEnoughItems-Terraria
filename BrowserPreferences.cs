using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

public static class BrowserKeys
{
    public static string ItemKey(int type) =>
        ContentSamples.ItemPersistentIdsByNetIds.GetValueOrDefault(type) ?? string.Empty;

    public static string NpcKey(int type) =>
        ContentSamples.NpcPersistentIdsByNetIds.GetValueOrDefault(type) ?? string.Empty;

    public static bool TryItem(string key, out int type)
    {
        type = 0;
        return key != null && ContentSamples.ItemNetIdsByPersistentIds.TryGetValue(key, out type) && type > 0;
    }

    public static bool TryNpc(string key, out int type)
    {
        type = 0;
        return key != null && ContentSamples.NpcNetIdsByPersistentIds.TryGetValue(key, out type) && type != 0;
    }
}

internal static class BrowserPreferences
{
    internal static readonly List<string> Bookmarks = new();
    internal static readonly Dictionary<string,int> Goals = new();
    private static BrowserClientConfig Settings => ModContent.GetInstance<BrowserClientConfig>();
    internal static int Density => Settings.Density;
    internal static bool NewBookmarksFirst => Settings.NewBookmarksFirst;
    internal static int CellSize => (BrowserUI.TerminalLayout ? Settings.TerminalDensity : Density) switch
    {
        < 0 => 15, > 0 => 22, _ => 18
    };
    private sealed record Saved(List<string> Bookmarks, Dictionary<string,int> Goals, int Density = 0, bool NewBookmarksFirst = false);
    private static string FilePath => Path.Combine(Main.SavePath, "ModConfigs", "JustEnoughItemsTerraria.bookmarks.json");

    internal static void Load()
    {
        Bookmarks.Clear();
        Goals.Clear();
        try
        {
            if (!File.Exists(FilePath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = doc.RootElement;
            var saved = root.ValueKind == JsonValueKind.Object ? root.Deserialize<Saved>() : null;
            string configPath = Path.Combine(Path.GetDirectoryName(FilePath), "JustEnoughItemsTerraria_BrowserClientConfig.json");
            if (saved != null && !File.Exists(configPath) && (saved.Density != 0 || saved.NewBookmarksFirst))
            {
                var pending = (BrowserClientConfig)Terraria.ModLoader.Config.ConfigManager.GeneratePopulatedClone(Settings);
                pending.Density = Math.Clamp(saved.Density, -1, 1);
                pending.TerminalDensity = pending.Density;
                pending.NewBookmarksFirst = saved.NewBookmarksFirst;
                Settings.SaveChanges(pending, silent: true);
            }
            var entries = root.ValueKind == JsonValueKind.Array ? root.Deserialize<List<string>>() : saved?.Bookmarks;
            if (saved?.Goals != null && entries != null)
                foreach (var (key, goal) in saved.Goals)
                    if (goal > 0 && entries.Contains(key)) Goals[CurrentKey(key)] = goal;
            if (entries != null) Bookmarks.AddRange(entries.Where(s => !string.IsNullOrEmpty(s)).Select(CurrentKey).Distinct());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ModContent.GetInstance<JustEnoughItemsTerraria>().Logger.Warn("Could not load bookmarks", ex);
        }
    }

    private static string CurrentKey(string key) => key.Replace("TerminalUiProbe/", "AEBeyondDimensions/").Replace("AdaptiveRecipeBrowser/", "JustEnoughItemsTerraria/");

    internal static void Save()
    {
        string path = FilePath;
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temp, JsonSerializer.Serialize(new Saved(Bookmarks, Goals, Density, NewBookmarksFirst)));
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModContent.GetInstance<JustEnoughItemsTerraria>().Logger.Warn("Could not save bookmarks", ex);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
