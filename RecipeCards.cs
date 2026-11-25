using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;

namespace JustEnoughItemsTerraria;

public static partial class BrowserUI
{
    private static bool ShimmerGrid(BrowserRow row) => (row.Category == BrowserCategory.Shimmer || row.BagDetails != null) && row.Outputs.Length > 1;

    private static bool PairedShimmerSlots(BrowserRow row) => row.Category == BrowserCategory.Shimmer &&
        row.Inputs.Length == 1 && row.Outputs.Length == 1 && !row.Npc.HasValue && !row.SpawnedNpc.HasValue;

    private static int RelationSlotSize(BrowserRow row, bool output) =>
        output || PairedShimmerSlots(row) ? 26 : row.Category == BrowserCategory.Shimmer && !row.Npc.HasValue ? 36 : 18;

    private const float RelationWidth = 149;
    private const int HarvestWidth = 164, TextLine = 14, NoteBottomPadding = 7;
    private static readonly Color cardInk = new(84, 84, 84);
    private static readonly Dictionary<BrowserRow, string[]> cardNotes = new();
    private static float cardTextUnit;
    private static readonly Dictionary<(string Text, int Width, int Pixels), string[]> wrappedLines = new();
    // Four units of outer padding, then a three-unit gap on either side of the arrow.
    private const float RelationSidesWidth = RelationWidth - 8 - 6 - 16;

    private static int ItemsWidth(int count, int single) => count switch {
        0 => 0, 1 => single, > 9 => 59, _ => 54
    };

    private static (float LeftWidth, bool StackLeft, bool StackRight) RelationLayout(BrowserRow row)
    {
        static int WithActor(int width, bool hasNpc) => hasNpc ? (width == 0 ? 26 : width + 29) : width;

        int leftItems = ItemsWidth(row.Inputs.Length, RelationSlotSize(row, false));
        int rightItems = ItemsWidth(row.Outputs.Length, 26);
        int left = WithActor(leftItems, row.Npc.HasValue), right = WithActor(rightItems, row.SpawnedNpc.HasValue);
        bool stackLeft = left + right > RelationSidesWidth && row.Npc.HasValue && leftItems > 0;
        if (stackLeft) left = Math.Max(26, leftItems);
        bool stackRight = left + right > RelationSidesWidth && row.SpawnedNpc.HasValue && rightItems > 0;
        if (stackRight) right = Math.Max(26, rightItems);
        float desired = RelationSidesWidth / 2;
        if (row.Npc.HasValue && !row.SpawnedNpc.HasValue)
            desired = RelationSidesWidth - Math.Max(36, right);
        else if (row.SpawnedNpc.HasValue && !row.Npc.HasValue)
            desired = Math.Max(36, left);
        return (Math.Clamp(desired, left, RelationSidesWidth - right), stackLeft, stackRight);
    }

    private static string[] WrapCardText(string text, float width, float fontHeight = 13)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        int limit = Math.Max(1, (int)MathF.Floor(width * unit)), pixels = FontPixels(fontHeight);
        var key = (text, limit, pixels);
        if (wrappedLines.TryGetValue(key, out var saved)) return saved;
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            if (BrowserFont.Measure(paragraph, BrowserFontFace.Body, pixels).X <= limit)
            {
                lines.Add(paragraph);
                continue;
            }
            var offsets = StringInfo.ParseCombiningCharacters(paragraph).Append(paragraph.Length).ToArray();
            for (int start = 0; start < offsets.Length - 1;)
            {
                int lo = start + 1, hi = offsets.Length - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (BrowserFont.Measure(paragraph[offsets[start]..offsets[mid]], BrowserFontFace.Body, pixels).X <= limit) lo = mid;
                    else hi = mid - 1;
                }
                int cut = lo;
                // Keep Latin words and decimal percentages together when they fit on the next line.
                bool IsToken(char ch) => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '%';
                while (cut < offsets.Length - 1 && cut > start &&
                    (IsToken(paragraph[offsets[cut] - 1]) && IsToken(paragraph[offsets[cut]]) ||
                     "，。、；：！？）】".Contains(paragraph[offsets[cut]]))) cut--;
                if (cut == start) cut = lo;
                lines.Add(paragraph[offsets[start]..offsets[cut]].Trim());
                start = cut;
            }
        }
        return wrappedLines[key] = lines.ToArray();
    }

    private static float ActorWidth(BrowserRow row, bool output, float width, bool stacked)
    {
        int count = (output ? row.Outputs : row.Inputs).Length;
        return count == 0 || stacked ? width : width - ItemsWidth(count, RelationSlotSize(row, output)) - 3;
    }

    // Very long actor captions use the full-width text area, which can continue on the next page.
    private static string[] InlineActorName(int id, float actorWidth)
    {
        var lines = WrapCardText(Name(new Subject(id, true)), actorWidth);
        return lines.Length <= 3 ? lines : Array.Empty<string>();
    }

    private static int SideHeight(BrowserRow row, bool output, float width, bool stacked)
    {
        if ((output ? row.SpawnedNpc : row.Npc) is not int id) return 54;
        int nameLines = InlineActorName(id, ActorWidth(row, output, width, stacked)).Length;
        return Math.Max(54, 39 + nameLines * 12) + (stacked ? 57 : 0);
    }

    private static bool CanLocate(BrowserRow row, int? id) =>
        id.HasValue && ContentSamples.NpcsByNetId.TryGetValue(id.Value, out var npc) &&
        npc.friendly && (npc.isLikeATownNPC || (row.Category == BrowserCategory.Shop && row.Npc == id));

    private const int LocatorRowHeight = 20;
    private static int CardBodyHeight(BrowserRow row) => DiagramHeight(row) +
        (CanLocate(row, row.Npc) || CanLocate(row, row.SpawnedNpc) ? LocatorRowHeight : 0);

    private static int DiagramHeight(BrowserRow row)
    {
        if (row.Harvest != null) return HarvestDetailsTop(row);
        if (row.Category == BrowserCategory.Craft) return 62;
        if (ShimmerGrid(row)) return 86;
        var layout = RelationLayout(row);
        return 8 + Math.Max(SideHeight(row, false, layout.LeftWidth, layout.StackLeft),
            SideHeight(row, true, RelationSidesWidth - layout.LeftWidth, layout.StackRight));
    }

    private static int CardWidth(BrowserRow row) => row.Category == BrowserCategory.Craft ? 124 :
        row.Harvest != null ? HarvestWidth : (int)RelationWidth;

    private static string[] NoteLines(BrowserRow row)
    {
        if (cardTextUnit != unit)
        {
            cardNotes.Clear();
            wrappedLines.Clear();
            cardTextUnit = unit;
        }
        if (cardNotes.TryGetValue(row, out var saved)) return saved;
        var lines = new List<string>();
        void Add(string label, IEnumerable<string> values)
        {
            var content = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
            if (content.Length == 0) return;
            lines.Add(label + "：");
            foreach (string value in content) lines.AddRange(WrapCardText(value, CardWidth(row) - 12));
        }
        var notes = row.Note.Split(" · ", StringSplitOptions.RemoveEmptyEntries);
        if (row.Npc.HasValue || row.SpawnedNpc.HasValue)
        {
            var layout = RelationLayout(row);
            void AddLongName(int? actor, bool output, float width, bool stacked)
            {
                if (actor is int id && InlineActorName(id, ActorWidth(row, output, width, stacked)).Length == 0)
                    Add(output ? "产出生物" : "来源生物", [Name(new Subject(id, true))]);
            }
            AddLongName(row.Npc, false, layout.LeftWidth, layout.StackLeft);
            AddLongName(row.SpawnedNpc, true, RelationSidesWidth - layout.LeftWidth, layout.StackRight);
        }
        if (row.Harvest is { } plant)
        {
            Add("生长地", [plant.Habitat]);
            Add("条件", [plant.Condition]);
            Add("工具", plant.Tools.Select(Lang.GetItemNameValue));
        }
        else if (row.Category == BrowserCategory.Drop)
        {
            Add("方式", notes.Where(n => !n.StartsWith("基础掉率 ")));
            Add("基础掉率", notes.Where(n => n.StartsWith("基础掉率 ")).Select(n => n[5..]));
        }
        else if (row.Category == BrowserCategory.Shop)
        {
            if (notes.Contains("免费")) Add("价格", ["免费"]);
            else Add("基础价", row.Inputs.Select(slot => Lang.GetItemNameValue(slot.ItemId) + " ×" + slot.Count)
                .Concat(notes.Where(n => n.StartsWith("另需"))));
            Add("说明", notes.Where(n => n != "免费" && n != "基础价" && !n.StartsWith("另需")));
        }
        else if (row.Category == BrowserCategory.Shimmer)
        {
            Add("说明", notes.Where(n => n is not ("微光转换" or "微光拆解")));
        }
        else if (row.Category == BrowserCategory.Fishing)
        {
            foreach (var line in fishingNotes) lines.AddRange(WrapCardText(line, CardWidth(row) - 12));
        }
        else Add("说明", notes);
        if (row.Category == BrowserCategory.Spawn && row.Inputs.Length > 0)
            Add("召唤物", row.Inputs.Select(slot => Lang.GetItemNameValue(slot.ItemId)));
        if (row.Category == BrowserCategory.Spawn)
            Add("图鉴环境与条件", row.Conditions.Length > 0 ? row.Conditions : ["图鉴未登记生成环境"]);
        else Add("条件", row.Conditions);
        return cardNotes[row] = lines.ToArray();
    }

    private static BrowserRow fishingRow;
    private static ulong fishingTick;
    private static string[] fishingNotes = Array.Empty<string>();

    private static void RefreshFishingNotes(BrowserRow row)
    {
        if (row == fishingRow && Main.GameUpdateCount - fishingTick < 30) return;
        var current = FishingOdds.Describe(row);
        fishingTick = Main.GameUpdateCount;
        if (row != fishingRow || !current.SequenceEqual(fishingNotes)) {
            cardNotes.Remove(row);
            pageRows = null;
            fishingNotes = current;
        }
        fishingRow = row;
    }

    private static int CardHeight(BrowserRow row)
    {
        int lines = NoteLines(row).Length;
        return CardBodyHeight(row) + (lines == 0 ? 0 : lines * TextLine + NoteBottomPadding);
    }

    private static float DrawRecipe(BrowserRow row, float center, float y, int firstLine, int lineLimit)
    {
        bool craft = row.Category == BrowserCategory.Craft;
        float w = CardWidth(row), x = center - w / 2;
        int body = DiagramHeight(row);
        int notesTop = CardBodyHeight(row);
        var lines = NoteLines(row);
        int lineCount = Math.Min(lineLimit, lines.Length - firstLine);
        int height = notesTop + lineCount * TextLine + (lineCount > 0 ? NoteBottomPadding : 0);
        Panel("single_recipe_background_v2", Box(x, y, w, height), 16);
        var benchMaterials = craft && !fromBackpack ? BenchMaterials?.Invoke(row.RecipeIndex) : null;
        if (craft)
        {
            Grid(row, false, x + 4, y + 4, benchMaterials:benchMaterials);
            Icon("recipe_arrow", Box(x + 68, y + 23, 22, 16));
            int icons = Math.Min(2, row.EnvironmentIcons.Length);
            int size = icons == 1 ? 16 : 13;
            for (int i = 0; i < icons; i++)
                ItemImage(row.EnvironmentIcons[i], Box(x + 79 - icons * size / 2f + i * size, y + 26 - size, size, size));
            if (Box(x + 65, y + 9, 28, 31).Contains(mouse)) {
                var mods = row.OriginalIndices.Select(i => Main.recipe[i].Mod?.DisplayNameClean)
                    .Where(name => !string.IsNullOrEmpty(name)).Distinct();
                string owners = string.Join("、", mods);
                if (row.ConditionTooltip.Length > 0)
                    SetNamedTooltip(row.ConditionTooltip, row.ConditionOwner, owners.Length > 0 ? "配方来源：" + owners : "");
                else if (owners.Length > 0)
                    SetNamedTooltip("配方来源", owners);
            }
            if (row.Outputs.Length > 0) Slot(row.Outputs[0], x + 94, y + 18, true);
        }
        else if (row.Category == BrowserCategory.Spawn && row.Inputs.Length == 0)
            RelationSide(row, true, x + 4, y + 4, w - 8, body - 8, false);
        else if(row.Category==BrowserCategory.Fishing){
            ItemImage(Terraria.ID.ItemID.WoodFishingPole,Box(x+12,y+(body-20)/2,20,20));
            Icon("recipe_arrow",Box(x+47,y+(body-12)/2,16,12));
            RelationSide(row,true,x+67,y+4,w-71,body-8,false);
        }
        else if (row.Harvest is { } plant)
            DrawHarvest(row, plant, x, y);
        else if (ShimmerGrid(row))
        {
            RelationSide(row, false, x + 3, y + (body - 54) / 2, 39, 54, false);
            Icon("recipe_arrow", Box(x + 45, y + (body - 12) / 2, 16, 12));
            Grid(row, true, x + 63, y + 4, 26);
        }
        else
        {
            var layout = RelationLayout(row);
            float arrowX = x + 4 + layout.LeftWidth + 3;
            RelationSide(row, false, x + 4, y + 4, layout.LeftWidth, body - 8, layout.StackLeft);
            Icon("recipe_arrow", Box(arrowX, y + (body - 12) / 2, 16, 12));
            RelationSide(row, true, arrowX + 19, y + 4, RelationSidesWidth - layout.LeftWidth, body - 8, layout.StackRight);
        }
        bool sourceLocator = CanLocate(row, row.Npc);
        bool resultLocator = CanLocate(row, row.SpawnedNpc);
        bool twoActors = sourceLocator && resultLocator;
        if (sourceLocator && row.Npc is int source)
            DrawLocateButton(row, source, x + 6, y + body, twoActors ? "定位来源" : "地图定位");
        if (resultLocator && row.SpawnedNpc is int result)
            DrawLocateButton(row, result, x + (twoActors ? w / 2 : 6), y + body, twoActors ? "定位产出" : "地图定位");
        bool saved = BrowserPreferences.Bookmarks.Contains("r:" + row.Key);
        float bookmarkY=y+height-(craft?28:13);
        Button(x + w + 2, bookmarkY, 13, "recipe_bookmark",
            saved ? "从书签中移除该配方。" : "添加该配方到书签。",
            () => ToggleMark("r:" + row.Key), saved, iconSize: 9);
        if (saved) Fill(Box(x + w + 2, bookmarkY, 13, 13), Color.Lime * (1f / 15f));
        if(craft){
            if (fromBackpack)
            {
                int recipe = NativeRecipeMaterials.AvailableRecipe(row);
                bool ready = recipe >= 0 && InventoryOverview.BackpackAvailable;
                var transfer = Box(x + w + 2, y + height - 13, 13, 13);
                string tip = ready ? "选中并高亮原版制作栏的这条配方" :
                    transfer.Contains(mouse) ? NativeRecipeMaterials.Explain(row) : null;
                DrawTransfer(row, transfer, x+w+2, y+height-13, tip, ready,
                    () => InventoryOverview.SelectRecipe(recipe, true));
            }
            else
            {
                bool ready = RecipeBench?.Invoke(row.RecipeIndex, false) == true;
                bool missing = !ready && benchMaterials?.Any(s => s.Missing > 0) == true;
                string tip = ready ? "填入 AE 工作台" : missing ? NativeRecipeMaterials.Legend(benchMaterials.Any(s => s.Craftable), benchMaterials.Any(s => s.Missing > 0 && !s.Craftable)) : "需要可用的 AE 工作台、工作站和配方条件";
                DrawTransfer(row, Box(x+w+2,y+height-13,13,13), x+w+2, y+height-13, tip, ready,
                    () => { if (RecipeBench(row.RecipeIndex, true)) Close(); });
            }
        }
        for (int i = 0; i < lineCount; i++)
            Label(lines[firstLine + i], Box(x + 6, y + notesTop + i * TextLine, w - 12, TextLine), 13, cardInk, align: 0);
        return height;
    }

    private static void DrawTransfer(BrowserRow row, Rectangle area, float x, float y, string tip, bool ready, Action transfer)
    {
        Action action = ready ? transfer : null;
        bool over = area.Contains(mouse);
        if (over && fromBackpack && NativeRecipeMaterials.MultiStepEnabled && NativeRecipeMaterials.CraftChain(row, false))
        {
            tip += "\nCtrl＋左键：在背包中合成物品";
            var keys = Keyboard.GetState();
            if (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl))
                action = () => { if (NativeRecipeMaterials.CraftChain(row, true)) Close(); };
        }
        if (over && !fromBackpack && OrderItem != null && row.Outputs.Length > 0)
        {
            bool craftableIntermediate = tip == NativeRecipeMaterials.CraftableHint;
            if (ready || craftableIntermediate)
            {
                tip += "\nCtrl＋左键：打开下单界面";
                var keys = Keyboard.GetState();
                if (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl))
                    action = () => { if (OrderItem(row.Outputs[0].ItemId)) Close(); };
            }
        }
        Button(x, y, 13, "recipe_transfer", tip, action);
        if (over) SetPlainTooltip(tip, list:true);
    }

    private static int HarvestDetailsTop(BrowserRow row) => row.Outputs.Length <= 2 ? 56 : 79;

    private static void DrawHarvest(BrowserRow row, BrowserHarvest plant, float x, float y)
    {
        const int methodWidth = 34;
        var heading = Box(x + 7, y + 3, HarvestWidth - 14 - methodWidth, 15);
        var method = Box(x + HarvestWidth - 7 - methodWidth, y + 3, methodWidth, 15);
        string stage = " · " + plant.Stage;
        int stageWidth = Math.Min(heading.Width / 2, (int)MathF.Ceiling(BrowserFont.Measure(stage, BrowserFontFace.Body, FontPixels(11)).X));
        int nameWidth = Math.Min(heading.Width - stageWidth,
            (int)MathF.Ceiling(BrowserFont.Measure(plant.Name, BrowserFontFace.Body, FontPixels(14)).X));
        Label(plant.Name, new Rectangle(heading.X, heading.Y, nameWidth, heading.Height), 14, cardInk, align: 0);
        Label(stage, new Rectangle(heading.X + nameWidth, heading.Y, stageWidth, heading.Height), 11, cardInk, align: 0);
        Label(plant.Tools.Length == 0 ? "普通采收" : "工具采收", method, 10, cardInk, align: 1);

        var art = Box(x + 22, y + 20, 34, 30);
        PlantImage(plant, art);
        if (art.Contains(mouse) || heading.Contains(mouse))
            SetNamedTooltip(plant.Name, Owner(new(plant.ItemId)), plant.Stage, plant.ItemId);
        Icon("recipe_arrow", Box(x + 74, y + 29, 16, 12));
        if (row.Outputs.Length <= 2)
        {
            float first = x + 123 - (row.Outputs.Length * 28 - 2) / 2f;
            for (int i = 0; i < row.Outputs.Length; i++)
                Slot(row.Outputs[i], first + i * 28, y + 22, true, showOne: true);
        }
        else Grid(row, true, x + 96, y + 20);
    }

    private static void RelationSide(BrowserRow row, bool output, float x, float y, float width, int height, bool stackActor)
    {
        int? npc = output ? row.SpawnedNpc : row.Npc;
        var entries = output ? row.Outputs : row.Inputs;
        float gridY = y + (height - 54) / 2f;
        if (npc is int id)
        {
            float npcWidth = ActorWidth(row, output, width, stackActor);
            var subject = new Subject(id, true);
            var nameLines = InlineActorName(id, npcWidth);
            int actorHeight = 39 + nameLines.Length * 12;
            y += (height - actorHeight - (stackActor ? 57 : 0)) / 2f;
            NpcImage(id, Box(x + 1, y, npcWidth - 2, 38), head: row.Category == BrowserCategory.Spawn || !output && row.Category == BrowserCategory.Drop);
            for (int i = 0; i < nameLines.Length; i++)
                Label(nameLines[i], Box(x, y + 39 + i * 12, npcWidth, 12), 13, cardInk);
            var actorArea = Box(x, y, npcWidth, actorHeight);
            if (actorArea.Contains(mouse))
            {
                var target = LocateNpc(row, id, out string hint);
                Hit(subject, actorArea, extra: hint, primaryUses: !output);
                var keys = Keyboard.GetState();
                if (CanLocate(row, id) && leftClick && (keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift)))
                    clicked = target == null ? null : () => ShowNpcOnMap(row, id);
            }

            if (stackActor) gridY = y + actorHeight + 3;
            else if (entries.Length > 0)
            {
                x += npcWidth + 3;
                width -= npcWidth + 3;
            }
        }
        if (entries.Length == 0) return;
        if (entries.Length == 1)
        {
            int size = RelationSlotSize(row, output);
            Slot(entries[0], x + (width - size) / 2, gridY + (54 - size) / 2, output, sizeOverride: size, matchedPadding: PairedShimmerSlots(row), detail: output ? row.BagDetails?.FirstOrDefault() : null);
            return;
        }
        int gridWidth = entries.Length > 9 ? 59 : 54;
        Grid(row, output, x + (width - gridWidth) / 2, gridY);
    }

    private static void DrawLocateButton(BrowserRow row, int id, float x, float y, string label)
    {
        var rect = Box(x, y, 52, 16);
        bool over = rect.Contains(mouse);
        var target = LocateNpc(row, id, out string hint);
        string frame = target == null ? "button_disabled_v2" : over ? "button_highlight_v2" : "button_enabled_v2";
        Panel(frame, rect, 3);
        Label(label, rect, 14, target == null ? Color.Silver : Color.White, shadow: true);
        if (!over) return;
        SetPlainTooltip(target == null ? hint : "在地图上标出" + Name(new Subject(id, true)));
        if (leftClick && target != null)
            clicked = () => { ShowNpcOnMap(row, id); PlayClick(); };
    }

    private static NPC LocateNpc(BrowserRow row, int id, out string hint)
    {
        hint = "";
        if (!CanLocate(row, id)) return null;
        hint = "地图已禁用";
        if (!Main.mapEnabled) return null;
        NPC nearest = null;
        foreach (var npc in Main.ActiveNPCs)
            if ((npc.type == id || npc.netID == id) && npc.friendly && npc.life > 0 && (nearest == null ||
                npc.DistanceSQ(Main.LocalPlayer.Center) < nearest.DistanceSQ(Main.LocalPlayer.Center)))
                nearest = npc;
        hint = nearest == null ? "该 NPC 当前不在世界中" : "Shift＋左键：在地图上定位";
        return nearest;
    }

    private static void ShowNpcOnMap(BrowserRow row, int id)
    {
        var npc = LocateNpc(row, id, out _);
        if (npc == null) return;
        if (TerminalWidth?.Invoke() > 0) LeaveTerminalForMap?.Invoke();
        Close();
        NpcMapMarker.Track(npc, id);
    }
    private static void Grid(BrowserRow row, bool output, float x, float y, int cell = 18, (int Missing,bool Craftable)[] benchMaterials = null)
    {
        const int cols = 3, rows = 3;
        var slots = output ? row.Outputs : row.Inputs;
        var key = (row, output);
        int max = Math.Max(0, (slots.Length + cols - 1) / cols - rows);
        bool refund = output && ShimmerGrid(row);
        if (!scroll.TryGetValue(key, out int offset))
        {
            if (refund && !view.Uses && !view.Target.Npc)
            {
                int match = Array.FindIndex(slots, slot => slot.ItemId == view.Target.Id);
                if (match >= 0) offset = Math.Clamp(match / cols - rows + 1, 0, max);
            }
            scroll[key] = offset;
        }
        for (int i = 0; i < cols * rows; i++)
        {
            int slot = offset * cols + i;
            float gx = x + i % cols * cell, gy = y + i / cols * cell;
            if (slot < slots.Length) Slot(slots[slot], gx, gy, output,
                refund && !view.Uses && !view.Target.Npc && slots[slot].ItemId == view.Target.Id, sizeOverride: cell,
                showOne: output && row.Harvest != null, detail: output ? row.BagDetails?[slot] : null,
                shortage: benchMaterials != null && slot < benchMaterials.Length ? new(benchMaterials[slot].Missing,benchMaterials[slot].Craftable) : fromBackpack && !output && row.Category == BrowserCategory.Craft &&
                    NativeRecipeMaterials.AvailableRecipe(row) < 0 ? NativeRecipeMaterials.Check(row, slot) : default);
            else Icon(output ? "output_slot" : "slot", Box(gx, gy, cell, cell));
        }
        if (max == 0)
        {
            if (refund && Box(x, y, cols * cell, rows * cell).Contains(mouse)) wheel = 0;
            return;
        }
        float railX = x + cell * cols + 1, height = cell * rows;
        var track = Box(railX, y, 3.5f, height);
        Fill(track, new Color(158,158,158));
        float thumb = Math.Max(4, height * rows / (rows + max));
        Fill(Box(railX, y + (height - thumb) * offset / max, 3.5f, thumb), new Color(76,76,76));
        if (track.Contains(mouse) && Main.mouseLeft)
        {
            scroll[key] = Math.Clamp((int)MathF.Round(max * ((mouse.Y / unit - y - thumb / 2) / (height - thumb))), 0, max);
            SetPlainTooltip($"第 {scroll[key] + 1}–{scroll[key] + rows} 行，共 {max + rows} 行");
        }
        if (wheel != 0 && Box(x, y, cols * cell + 5, height).Contains(mouse))
        {
            scroll[key] = Math.Clamp(offset + wheel, 0, max);
            wheel = 0;
        }
    }

    private static void Slot(BrowserSlot slot, float x, float y, bool output, bool highlight = false, int sizeOverride = 0, bool showOne = false, bool matchedPadding = false, string detail = null, NativeRecipeMaterials.Shortage shortage = default)
    {
        int size = sizeOverride > 0 ? sizeOverride : output ? 26 : 18;
        var area = Box(x, y, size, size);
        Icon(output ? "output_slot" : "slot", area);
        if (shortage.Missing > 0)
        {
            Color tint = shortage.Craftable ? new Color(38, 100, 222) : new Color(197, 52, 54);
            Fill(Box(x + 1, y + 1, size - 2, size - 2), tint * .82f);
            detail = $"缺少数量：{shortage.Missing}";
        }
        if (highlight)
        {
            var gold = new Color(247, 200, 76);
            Fill(Box(x, y, size, 1), gold);
            Fill(Box(x, y + size - 1, size, 1), gold);
            Fill(Box(x, y + 1, 1, size - 2), gold);
            Fill(Box(x + size - 1, y + 1, 1, size - 2), gold);
        }
        int id = slot.ItemId;
        string extra = detail ?? "";
        if (slot.Alternatives.Length > 1)
        {
            int count = slot.Alternatives.Length;
            int start = view.Uses && !view.Target.Npc ? Math.Max(0, Array.IndexOf(slot.Alternatives, view.Target.Id)) : 0;
            int shift = alternatives.GetValueOrDefault(slot);
            if (area.Contains(mouse) && wheel != 0 &&
                (Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift)))
            {
                shift = (shift + wheel % count + count) % count;
                alternatives[slot] = shift;
                wheel = 0;
            }
            int chosen = (int)((start + shift + (Environment.TickCount64 - ingredientCycleStart) / 2000) % count);
            id = slot.Alternatives[chosen];
        }
        float pad = output || matchedPadding ? 4 : 1.5f;
        ItemImage(id, Box(x + pad, y + pad, size - 2 * pad, size - 2 * pad));
        if (slot.Alternatives.Length > 1) Icon("tag_badge", Box(x + size - 7, y + 1, 6, 6));
        if ((showOne || slot.Count != "1") && slot.Count.Length > 0)
            Label(slot.Count, Box(x, y + size - 9, size - 1, 9), 10, Color.White, true, 1, BrowserFontFace.Numbers);
        Hit(new(id), area, extra: extra);
    }
}
