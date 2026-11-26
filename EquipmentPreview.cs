using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI.Chat;

namespace JustEnoughItemsTerraria;

internal static class EquipmentPreview
{

    private static readonly Dictionary<int,string[]> setText = new();
    private static readonly HashSet<int> failed = new();
    private static int lastId;
    private static ulong lastTick;
    private static string lastText;

    internal static void Clear()
    {

        ArmorBonusIndex.Clear();
        setText.Clear();
        failed.Clear();
        lastId = 0;
        lastText = null;
    }

    private static bool Armor(Item item) => !item.vanity && (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0);

    // Inline item icons carry a bracketed label in tML's text-only representation.
    // Keep standalone icon labels, but omit an icon if adjacent text already names it.
    private static string PlainDescription(string paragraph)
    {
        var snippets = ChatManager.ParseMessage(paragraph, Color.White).ToArray();
        var output = new System.Text.StringBuilder();
        for (int i = 0; i < snippets.Length; i++)
        {
            var snippet = snippets[i];
            string text = snippet.Text;
            if (snippet.DeleteWhole && text.Length > 2 && text[0] == '[' && text[^1] == ']')
            {
                string name = text[1..^1];
                int next = i + 1;
                while (next < snippets.Length && string.IsNullOrWhiteSpace(snippets[next].Text)) next++;
                if (next < snippets.Length && snippets[next].Text.TrimStart().StartsWith(name, StringComparison.Ordinal) ||
                    output.ToString().TrimEnd().EndsWith(name, StringComparison.Ordinal))
                    continue;
            }
            output.Append(text);
        }
        return output.ToString().Replace("  ", " ").Trim();
    }

    internal static string HoverDetails(Item item)
    {
        var keys = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        return keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) || keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift)
            ? DescribeItem(item) : JeiText.Get("HoldShiftForFullDetails");
    }

    internal static string Describe(int id) => DescribeItem(ContentSamples.ItemsByType[id]);

    private static string DescribeItem(Item sample)
    {
        int id = sample.type;
        bool catalogItem = ReferenceEquals(sample, ContentSamples.ItemsByType[id]);
        if (catalogItem && id == lastId && Main.GameUpdateCount - lastTick < 15) return lastText;
        var parts = new List<string>();
        var item = sample.Clone();
        try
        {
            int count = 1, yoyo = -1, research = -1;
            var names = new string[30 + item.ToolTip.Lines];
            var text = new string[names.Length];
            var modifiers = new bool[names.Length];
            var bad = new bool[names.Length];
            Main.MouseText_DrawItemTooltip_GetLinesInfo(item, ref yoyo, ref research, item.knockBack, ref count,
                text, modifiers, bad, names, out int prefix);
            var lines = ItemLoader.ModifyTooltips(item, ref count, names, ref text, ref modifiers, ref bad, ref yoyo, out _, prefix);
            foreach (var line in lines)
            {
                if (!line.Visible || line.Name is "ItemName" or "SetBonus" or "ShimmerResult") continue;
                foreach (var paragraph in line.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    string value = PlainDescription(paragraph);
                    if (value.Length > 0) parts.Add(value);
                }
            }
            if (Armor(item))
            {
                if (!setText.TryGetValue(id, out var bonuses))
                    setText[id] = bonuses = Bonuses(item).Distinct().ToArray();
                bool worn = Main.LocalPlayer.armor.Take(3).Any(i => i.type == id);
                var active = Main.LocalPlayer.setBonus;
                if (worn && !string.IsNullOrWhiteSpace(active))
                    parts.Add(Lang.tip[48].Value + " " + active);
                else parts.AddRange(bonuses.Select(b => Lang.tip[48].Value + " " + b));
            }
        }
        catch (Exception ex)
        {
            if (failed.Add(id)) ModContent.GetInstance<JustEnoughItemsTerraria>().Logger.Warn($"读取物品效果失败 {id}: {ex.Message}");
            if (parts.Count == 0) for (int n = 0; n < item.ToolTip.Lines; n++) parts.Add(item.ToolTip.GetLine(n));
        }
        string result = string.Join("\n", parts.Distinct());
        if (!catalogItem) return result;
        lastId = id;
        lastTick = Main.GameUpdateCount;
        return lastText = result;
    }

    private static IEnumerable<string> Bonuses(Item item)
    {
        if (item.ModItem == null)
        {
            foreach (string bonus in ArmorBonusIndex.For(item)) yield return bonus;
            yield break;
        }

        // Mods commonly publish set descriptions on the headpiece and shared bonuses on the chest.
        // Verify the three actual items with the mod's set predicate before associating a description.
        var family = ContentSamples.ItemsByType.Values.Where(i => i.ModItem?.Mod == item.ModItem.Mod &&
            i.ModItem.GetType().Namespace == item.ModItem.GetType().Namespace && Armor(i)).ToArray();
        foreach (var head in family.Where(i => i.headSlot >= 0 && (item.headSlot < 0 || i.type == item.type)))
        foreach (var body in family.Where(i => i.bodySlot >= 0 && (item.bodySlot < 0 || i.type == item.type)))
        foreach (var legs in family.Where(i => i.legSlot >= 0 && (item.legSlot < 0 || i.type == item.type)))
        {
            var pieces = new[] {head, body, legs};
            if (!pieces.Any(p => p.ModItem.IsArmorSet(head, body, legs))) continue;
            var descriptions = pieces.SelectMany(p => new[]{"SetBonus","CommonSetBonus"}.Select(p.ModItem.GetLocalizationKey))
                .Where(Language.Exists).Select(k=>Language.GetTextValue(k)).Where(t=>!t.Contains("{0")).Distinct();
            string description = string.Join("\n", descriptions);
            if (description.Length > 0) yield return (item.headSlot < 0 ? head.Name + "：" : "") + description;
        }
    }
}
