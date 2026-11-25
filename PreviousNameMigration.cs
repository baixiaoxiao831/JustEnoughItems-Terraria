using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace JustEnoughItemsTerraria;

// Translate only while reading player/world saves. The normal tML save writes the new identifiers.
internal sealed class PreviousNameMigration : ModSystem
{
    private const string Old = "AdaptiveRecipeBrowser";
    private const string Current = "JustEnoughItemsTerraria";

    internal static void CopySettings()
    {
        var directory = Path.Combine(Main.SavePath, "ModConfigs");
        foreach (var suffix in new[] { ".bookmarks.json", "_BrowserClientConfig.json", "_BrowserServerConfig.json" })
        {
            var source = Path.Combine(directory, Old + suffix);
            var target = Path.Combine(directory, Current + suffix);
            if (File.Exists(source) && !File.Exists(target))
                File.Copy(source, target);
        }
    }

    public override void Load()
    {
        if (ModLoader.HasMod(Old))
            throw new InvalidOperationException("Disable the old test mod " + Old + " before loading " + Current + ".");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var assembly = typeof(TagIO).Assembly;
        var playerLoad = assembly.GetType("Terraria.ModLoader.IO.PlayerIO", true).GetMethod("Load", flags, null, new[]{typeof(Player), typeof(TagCompound)}, null);
        var worldLoad = assembly.GetType("Terraria.ModLoader.IO.WorldIO", true).GetMethod("Load", flags, null, new[]{typeof(string), typeof(bool)}, null);
        MonoModHooks.Modify(playerLoad, il => {
            var cursor = new ILCursor(il);
            cursor.Emit(OpCodes.Ldarg_1);
            cursor.EmitDelegate<Action<TagCompound>>(Translate);
        });
        MonoModHooks.Modify(worldLoad, il => {
            var cursor = new ILCursor(il);
            cursor.GotoNext(MoveType.After, instruction => instruction.MatchCall(typeof(TagIO), nameof(TagIO.FromStream)));
            cursor.Emit(OpCodes.Dup);
            cursor.EmitDelegate<Action<TagCompound>>(Translate);
        });
    }

    private static void Translate(TagCompound tag)
    {
        foreach (var entry in tag.ToArray())
        {
            switch (entry.Value)
            {
                case TagCompound child:
                    Translate(child);
                    break;
                case IList<TagCompound> children:
                    foreach (var child in children) Translate(child);
                    break;
                case IList<string> names when entry.Key == "usedMods":
                    for (int i = 0; i < names.Count; i++)
                        if (names[i] == Old) names[i] = Current;
                    break;
                case string name when (entry.Key == "mod" || entry.Key == "provider") && name == Old:
                    tag[entry.Key] = Current;
                    break;
                case string key when entry.Key is "result" or "item" or "key" or "choice" or "recipe":
                    if (key.Contains(Old + "/", StringComparison.Ordinal) || key.StartsWith(Old + "|", StringComparison.Ordinal))
                        tag[entry.Key] = key.Replace(Old + "/", Current + "/").Replace(Old + "|", Current + "|");
                    break;
            }
        }
    }
}