using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using Terraria.UI;

namespace JustEnoughItemsTerraria;

public static partial class BrowserUI
{
    private static readonly Dictionary<int, Item> displayItems = new();
    private static readonly HashSet<int> failedInventoryDraws = new();
    private readonly record struct SpriteFrame(Rectangle Source, int TexelsPerPixel);
    private static readonly ConditionalWeakTable<Texture2D, Dictionary<Rectangle, SpriteFrame>> spriteFrames = new();
    private static SpriteBatch batch;
    private static float unit;
    private static Point mouse;
    private static string tooltip;
    private static string tooltipOwner;
    private static Color? tooltipQuality;
    private static Action clicked;
    private static bool leftClick, rightClick;
    private static int wheel;
    private static readonly Color ink = new(47, 47, 47);

    // FNA does not expose a draw count on SpriteBatch. Read its deferred queue so
    // an intentionally suppressed icon or a pending transparent asset can fall
    // back without a render target, GPU readback, or drawing over real custom art.
    private const BindingFlags BatchFields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly bool canInspectBatch =
        typeof(SpriteBatch).GetField("numSprites", BatchFields)?.FieldType == typeof(int) &&
        typeof(SpriteBatch).GetField("bufferOffset", BatchFields)?.FieldType == typeof(int) &&
        typeof(SpriteBatch).GetField("textureInfo", BatchFields)?.FieldType == typeof(Texture2D[]);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "numSprites")]
    private static extern ref int QueuedSprites(SpriteBatch batch);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "bufferOffset")]
    private static extern ref int BufferOffset(SpriteBatch batch);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "textureInfo")]
    private static extern ref Texture2D[] QueuedTextures(SpriteBatch batch);

    private static Rectangle Box(float x, float y, float w, float h)
    {
        int left = (int)MathF.Round(x * unit), top = (int)MathF.Round(y * unit);
        return new(left, top, (int)MathF.Round((x + w) * unit) - left,
            (int)MathF.Round((y + h) * unit) - top);
    }

    private static Texture2D Graphic(string key) => BrowserTexturePreload.Skin(key);

    // Both adjacent slices refer to the same rounded pixel coordinate.
    private static void Panel(string texture, Rectangle rect, int border, int right = -1, Color? rim = null, bool flushRight = false)
    {
        var tex = Graphic(texture);
        right = right < 0 ? border : right;
        var leftPx = Math.Min((int)MathF.Round(border * unit), rect.Width / 2);
        var rightPx = flushRight ? 0 : Math.Min((int)MathF.Round(right * unit), rect.Width / 2);
        var topPx = Math.Min((int)MathF.Round(border * unit), rect.Height / 2);
        Span<int> sx = stackalloc int[] { 0, border, tex.Width - right, tex.Width };
        Span<int> sy = stackalloc int[] { 0, border, tex.Height - border, tex.Height };
        Span<int> dx = stackalloc int[] { rect.Left, rect.Left + leftPx, rect.Right - rightPx, rect.Right };
        Span<int> dy = stackalloc int[] { rect.Top, rect.Top + topPx, rect.Bottom - topPx, rect.Bottom };
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                var dest = new Rectangle(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]);
                var src = new Rectangle(sx[x], sy[y], sx[x + 1] - sx[x], sy[y + 1] - sy[y]);
                if (dest.Width > 0 && dest.Height > 0 && src.Width > 0 && src.Height > 0)
                    batch.Draw(tex, dest, src, x == 1 && y == 1 ? Color.White : rim ?? Color.White);
            }
    }

    private static void Fill(Rectangle box, Color color) => batch.Draw(TextureAssets.MagicPixel.Value, box, color);
    private static void Icon(string key, Rectangle box) => batch.Draw(Graphic(key), box, Color.White);

    private static void Fit(Texture2D tex, Rectangle source, Rectangle area, Color tint, int texelsPerPixel = 0)
    {
        if (area.Width < 1 || area.Height < 1 || source.Width < 1 || source.Height < 1) return;
        float scale = Math.Min(area.Width / (float)source.Width, area.Height / (float)source.Height);
        if (texelsPerPixel > 0 && scale * texelsPerPixel >= 1)
            scale = MathF.Floor(scale * texelsPerPixel) / texelsPerPixel;
        int w = Math.Max(1, (int)MathF.Round(source.Width * scale));
        int h = Math.Max(1, (int)MathF.Round(source.Height * scale));
        batch.Draw(tex, new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h), source, tint);
    }

    private static void ItemImage(int id, Rectangle area)
    {
        if (area.Width < 1 || area.Height < 1 || (uint)id >= (uint)TextureAssets.Item.Length ||
            TextureAssets.Item[id]?.IsLoaded != true) return;
        if (!displayItems.TryGetValue(id, out var item))
        {
            if (!ContentSamples.ItemsByType.TryGetValue(id, out var sample) || sample.IsAir) return;
            // Inventory draw hooks may animate or otherwise change their Item instance.
            displayItems[id] = item = sample.Clone();
        }
        Main.GetItemDrawFrame(id, out var texture, out var frame);
        if (frame.Width <= 0 || frame.Height <= 0) return;
        var visible = MeasureFrame(texture, frame).Source;
        // One standard inventory icon spans 32 Terraria texture pixels at native size.
        float nativeScale = Math.Min(area.Width, area.Height) / 32f;
        float scale = Math.Min(nativeScale, Math.Min(area.Width / (float)visible.Width, area.Height / (float)visible.Height));
        var topLeft = new Vector2(area.X + MathF.Round((area.Width - visible.Width * scale) / 2f),
            area.Y + MathF.Round((area.Height - visible.Height * scale) / 2f));
        // Native inventory hooks receive the center of the original, untrimmed frame.
        var center = topLeft + new Vector2(frame.Width * .5f - (visible.X - frame.X),
            frame.Height * .5f - (visible.Y - frame.Y)) * scale;
        if (canInspectBatch && !failedInventoryDraws.Contains(id))
        {
            try
            {
                // Use the same registered texture, animation, glow and mod draw hooks as the backpack.
                int first = QueuedSprites(batch), offset = BufferOffset(batch);
                ItemSlot.DrawItemIcon(item, ItemSlot.Context.InventoryItem, batch, center, scale,
                    Math.Max(frame.Width, frame.Height), Color.White);
                int end = QueuedSprites(batch);
                // A shader hook can end our batch, draw an effect, and start a
                // new batch whose final item layer still holds an async placeholder.
                bool flushed = end < first || BufferOffset(batch) != offset;
                var textures = QueuedTextures(batch);
                bool hasArt = flushed && end == 0;
                for (int i = flushed ? 0 : first; i < end && !hasArt; i++)
                    hasArt = textures[i] != null && textures[i] != Asset<Texture2D>.DefaultValue;
                if (flushed) RestoreIconBatch();
                if (hasArt) return;
            }
            catch (Exception e)
            {
                failedInventoryDraws.Add(id);
                ModContent.GetInstance<JustEnoughItemsTerraria>().Logger.Warn(
                    $"Using the registered texture for {item.ModItem?.FullName ?? ItemID.Search.GetName(id)} after an inventory draw error.", e);
                // A custom draw hook may have switched or ended the batch before failing.
                RestoreIconBatch();
            }
        }
        batch.Draw(texture, center, frame, Color.White, 0f,
            new Vector2(frame.Width, frame.Height) / 2f, scale, SpriteEffects.None, 0f);
    }

    private static void RestoreIconBatch()
    {
        try { batch.End(); }
        catch (InvalidOperationException) { }
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);
    }

    private static bool DrawStationTile(int id, Rectangle area)
    {
        if ((uint)id >= (uint)TextureAssets.Tile.Length || TextureAssets.Tile[id]?.IsLoaded != true) return false;
        var texture = TextureAssets.Tile[id].Value;
        var data = TileObjectData.GetTileData(id, 0, 0);
        // Ordinary terrain has no furniture placement data. Its atlas starts with one tile frame.
        if (data == null)
        {
            var tile = new Rectangle(0, 0, Math.Min(16, texture.Width), Math.Min(16, texture.Height));
            Fit(texture, tile, area, Color.White);
            return true;
        }
        int columns = data.Width, rows = data.Height, cellWidth = data.CoordinateWidth, padding = data.CoordinatePadding;
        var heights = data.CoordinateHeights;
        if (columns <= 0 || rows <= 0 || cellWidth <= 0 || padding < 0 || heights == null || heights.Length < rows)
            return false;
        int style = data.CalculatePlacementStyle(0, 0, 0);
        int wrap = data.StyleWrapLimit;
        int along = wrap > 0 ? style % wrap : style;
        long across = wrap > 0 ? (long)(style / wrap) * data.StyleLineSkip : 0;
        long atlasX = (data.StyleHorizontal ? along : across) * data.CoordinateFullWidth;
        long atlasY = (data.StyleHorizontal ? across : along) * data.CoordinateFullHeight;
        long objectWidth = (long)columns * cellWidth, objectHeight = 0;
        for (int row = 0; row < rows; row++)
        {
            if (heights[row] <= 0) return false;
            objectHeight += heights[row];
        }
        if (atlasX < 0 || atlasY < 0 || atlasX + objectWidth + (long)(columns - 1) * padding > texture.Width ||
            atlasY + objectHeight + (long)(rows - 1) * padding > texture.Height) return false;

        float scale = Math.Min(area.Width / (float)objectWidth, area.Height / (float)objectHeight);
        int width = Math.Max(1, (int)MathF.Round(objectWidth * scale));
        int height = Math.Max(1, (int)MathF.Round(objectHeight * scale));
        int left = area.X + (area.Width - width) / 2, top = area.Y + (area.Height - height) / 2;
        // Sprite-sheet padding separates tile cells; stitch their real pixels edge to edge.
        int placedY = 0;
        for (int row = 0; row < rows; row++)
        {
            int y1 = top + (int)MathF.Round(placedY * (float)height / objectHeight);
            int y2 = top + (int)MathF.Round((placedY + heights[row]) * (float)height / objectHeight);
            for (int column = 0; column < columns; column++)
            {
                int x1 = left + (int)MathF.Round(column * (float)width / columns);
                int x2 = left + (int)MathF.Round((column + 1) * (float)width / columns);
                if (x1 == x2 || y1 == y2) continue;
                var source = new Rectangle((int)atlasX + column * (cellWidth + padding),
                    (int)atlasY + placedY + row * padding, cellWidth, heights[row]);
                batch.Draw(texture, new Rectangle(x1, y1, x2 - x1, y2 - y1), source, Color.White);
            }
            placedY += heights[row];
        }
        return true;
    }

    private static void PlantImage(BrowserHarvest plant, Rectangle area)
    {
        if ((uint)plant.TileId < (uint)TextureAssets.Tile.Length &&
            TextureAssets.Tile[plant.TileId] is { IsLoaded: true, Value: var texture } &&
            plant.Frame.Width > 0 && plant.Frame.Height > 0 && texture.Bounds.Contains(plant.Frame))
            Fit(texture, plant.Frame, area, Color.White, 1);
        else ItemImage(plant.ItemId, area);
    }

    private static readonly Dictionary<int, Texture2D> catalogHeads = new();
    internal static Texture2D MapHead(NPC npc) => catalogHeads.GetValueOrDefault(npc.netID);
    internal static void ClearPortraits() => catalogHeads.Clear();

    private static Texture2D ResolveMapHead(NPC npc)
    {
        static Texture2D Ready(Asset<Texture2D>[] textures, int index) =>
            (uint)index < (uint)textures.Length && textures[index] is { IsLoaded: true, Value: { IsDisposed: false } texture }
                ? texture : null;

        int type = npc.type;
        // Some mods register their portraits by phase and supply the slot only through BossHeadSlot.
        if (npc.ModNPC != null || npc.boss)
        {
            try
            {
                if (Ready(TextureAssets.NpcHeadBoss, npc.GetBossHeadTextureIndex()) is { } current) return current;
            }
            catch (Exception) { /* A third-party map hook may require a live world entity. */ }
        }
        int boss = (uint)type < (uint)NPCID.Sets.BossHeadTextures.Length ? NPCID.Sets.BossHeadTextures[type] : -1;
        if (Ready(TextureAssets.NpcHeadBoss, boss) is { } registered) return registered;
        // Map hooks may hide an inactive or transparent sample. Use the registered
        // base portrait for a static card even in that case.
        if (npc.ModNPC is { } modNpc && Ready(TextureAssets.NpcHeadBoss,
                NPCHeadLoader.GetBossHeadSlot(modNpc.BossHeadTexture)) is { } baseHead) return baseHead;
        return Ready(TextureAssets.NpcHead, NPC.TypeToDefaultHeadIndex(type));
    }

    private static void NpcImage(int id, Rectangle area, bool head = false)
    {
        if (!ContentSamples.NpcsByNetId.TryGetValue(id, out var npc)) return;
        if (head && MapHead(npc) is { } headTexture)
        {
            var headArt = Frame(headTexture, 1);
            Fit(headTexture, headArt.Source, area, Color.White, headArt.TexelsPerPixel);
            return;
        }
        if ((uint)npc.type >= (uint)TextureAssets.Npc.Length || TextureAssets.Npc[npc.type]?.IsLoaded != true) return;
        var texture = TextureAssets.Npc[npc.type].Value;
        if (texture == null) return;
        var body = Frame(texture, Math.Max(1, Main.npcFrameCount[npc.type]));
        Fit(texture, body.Source, area, npc.color.A > 0 ? npc.color : Color.White, body.TexelsPerPixel);
    }

    private static SpriteFrame Frame(Texture2D texture, int frameCount)
    {
        var frame = texture.Bounds;
        frame.Height = Math.Max(1, frame.Height / frameCount);
        return MeasureFrame(texture, frame);
    }

    internal static void PreloadCatalogFrames()
    {
        catalogHeads.Clear();
        foreach (var npc in ContentSamples.NpcsByNetId.Values)
        {
            if (npc == null) continue;
            if (ResolveMapHead(npc) is { } portrait)
            {
                catalogHeads[npc.netID] = portrait;
                Frame(portrait, 1);
            }
            if ((uint)npc.type < (uint)TextureAssets.Npc.Length && TextureAssets.Npc[npc.type] is { IsLoaded: true } asset)
                Frame(asset.Value, Math.Max(1, Main.npcFrameCount[npc.type]));
        }
        foreach (var id in ContentSamples.ItemsByType.Keys)
        {
            if (id <= 0 || (uint)id >= (uint)TextureAssets.Item.Length || TextureAssets.Item[id]?.IsLoaded != true) continue;
            Main.GetItemDrawFrame(id, out var texture, out var frame);
            if (frame.Width > 0 && frame.Height > 0) MeasureFrame(texture, frame);
        }
    }

    private static SpriteFrame MeasureFrame(Texture2D texture, Rectangle source)
    {
        var frames = spriteFrames.GetOrCreateValue(texture);
        if (frames.TryGetValue(source, out var cached)) return cached;
        var frame = source;
        int texels = 1;
        // Texture packs can supply compressed or very large atlases. Their frame rectangle also fits.
        if (texture.Format == SurfaceFormat.Color && (long)frame.Width * frame.Height <= 1024 * 1024)
        {
            var pixels = new Color[frame.Width * frame.Height];
            texture.GetData(0, frame, pixels, 0, pixels.Length);
            int stride = frame.Width;
            int x1 = frame.Width, y1 = frame.Height, x2 = -1, y2 = -1;
            for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                if (pixels[y * stride + x].A == 0) continue;
                x1 = Math.Min(x, x1); x2 = Math.Max(x, x2);
                y1 = Math.Min(y, y1); y2 = Math.Max(y, y2);
            }
            if (x2 >= x1)
            {
                frame = new(source.X + x1, source.Y + y1, x2 - x1 + 1, y2 - y1 + 1);
                // Terraria often stores each painted pixel as a 2×2 block. Allow 1.5× texture
                // scaling here only when it produces exactly three screen pixels per painted pixel.
                bool doubled = frame.Width % 2 == 0 && frame.Height % 2 == 0;
                for (int y = y1; doubled && y <= y2; y += 2)
                for (int x = x1; doubled && x <= x2; x += 2)
                {
                    int p = y * stride + x;
                    var c = pixels[p];
                    doubled = c == pixels[p + 1] && c == pixels[p + stride] && c == pixels[p + stride + 1];
                }
                if (doubled) texels = 2;
            }
        }
        return frames[source] = new(frame, texels);
    }

    private static string Name(Subject target) => target.Npc
        ? (ContentSamples.NpcsByNetId.TryGetValue(target.Id, out var npc) ? npc.TypeName : target.Id.ToString())
        : Lang.GetItemNameValue(target.Id);

    private static string Owner(Subject target)
    {
        Mod owner = target.Npc ? ContentSamples.NpcsByNetId.GetValueOrDefault(target.Id)?.ModNPC?.Mod
            : ContentSamples.ItemsByType.GetValueOrDefault(target.Id)?.ModItem?.Mod;
        return owner?.DisplayNameClean ?? "Terraria";
    }

    private static int FontPixels(float height, BrowserFontFace face = BrowserFontFace.Body)
    {
        float em = face switch { BrowserFontFace.Search => .62f, BrowserFontFace.Numbers => .73f, _ => .65f };
        return Math.Max(1, (int)MathF.Round(height * unit * em));
    }

    private static void Label(string value, Rectangle area, float height, Color color,
        bool shadow = false, float align = .5f, BrowserFontFace face = BrowserFontFace.Body)
    {
        if (shadow) { area.Width -= ShadowPixels; area.Height -= ShadowPixels; }
        if (string.IsNullOrEmpty(value) || area.Width <= 0 || area.Height <= 0) return;
        int pixels = FontPixels(height, face);
        var size = BrowserFont.Measure(value, face, pixels);
        float scale = Math.Min(1, Math.Min(area.Height / Math.Max(1, size.Y), area.Width / Math.Max(1, size.X)));
        if (scale < 1 && pixels > 1)
        {
            pixels = Math.Max(1, (int)(pixels * scale));
            size = BrowserFont.Measure(value, face, pixels);
            scale = Math.Min(1, Math.Min(area.Height / Math.Max(1, size.Y), area.Width / Math.Max(1, size.X)));
        }
        var position = new Vector2(MathF.Round(area.Left + (area.Width - size.X * scale) * align),
            MathF.Round(area.Top + (area.Height - size.Y * scale) / 2));
        if (shadow)
        {
            if (color == Color.White) color = new Color(252, 252, 252);
            DrawTextShadow(value, face, pixels, position, color, scale);
        }
        BrowserFont.Draw(batch, value, face, pixels, position, color, scale);
    }

    private static int ShadowPixels => Math.Max(1, (int)MathF.Round(unit * .55f));

    private static void DrawTextShadow(string text, BrowserFontFace face, int pixels, Vector2 position, Color color, float scale = 1)
    {
        var offset = new Vector2(ShadowPixels);
        var dark = new Color(color.R * 62 / 252, color.G * 62 / 252, color.B * 62 / 252);
        BrowserFont.Draw(batch, text, face, pixels, position + offset, dark, scale);
    }

    private static void Button(float x, float y, float size, string icon, string tip,
        Action action = null, bool selected = false, bool dim = true, float iconSize = 0)
    {
        var rect = Box(x, y, size, size);
        bool over = rect.Contains(mouse);
        string frame = action == null && dim ? "button_disabled_v2" :
            selected || over && action != null ? "button_highlight_v2" : "button_enabled_v2";
        Icon(frame, rect);
        float side = iconSize > 0 ? iconSize : size * .74f;
        var tex = Graphic(icon);
        Fit(tex, tex.Bounds, Box(x + (size - side) / 2, y + (size - side) / 2, side, side), Color.White);
        if (!over) return;
        SetPlainTooltip(tip);
        if (leftClick && action != null)
            clicked = () => {
                action();
                if(icon is "arrow_previous_v2" or "arrow_next_v2") Terraria.Audio.SoundEngine.PlaySound(in pageClickSound);
                else PlayClick();
            };
    }

    private static void Hit(Subject target, Rectangle rect, bool station = false, string extra = "",
        Mark? mark = null, bool primaryUses = false, bool fromCatalog = false)
    {
        if (!rect.Contains(mouse)) return;
        hovered = target;
        hoveredBookmarkKey = mark?.Key;
        stationHovered = station;
        catalogHovered = fromCatalog;
        Fill(rect, Color.White * .16f);
        SetNamedTooltip(Name(target), Owner(target),
            (station ? "工作方块 · 查看来源" : "") + extra, target.Npc ? 0 : target.Id);
        if (leftClick || rightClick)
            clicked = ItemClick(target, leftClick ? primaryUses : !station, leftClick ? mark : null, fromCatalog);
    }

    private static Action ItemClick(Subject target, bool uses, Mark? mark, bool fromCatalog)
    {
        if (mark is Mark entry) return () => NavigateWithClick(() => OpenMark(entry));
        return () => OpenFromSlot(target, uses, fromCatalog);
    }

    private static void OpenFromSlot(Subject target, bool uses, bool fromCatalog)
    {
        if (fromCatalog) Open(target, uses);
        else NavigateWithClick(() => Open(target, uses));
    }

    private static bool tooltipList;
    private static void SetPlainTooltip(string text, bool list = false)
    {
        tooltipList = list;
        tooltip = text;
        tooltipOwner = null;
        tooltipQuality = null;
    }

    internal static Color ItemQualityColor(int id)
    {
        var item = ContentSamples.ItemsByType[id];
        if (item.expert || item.rare == ItemRarityID.Expert) return Main.DiscoColor;
        if (item.master || item.rare == ItemRarityID.Master) return new Color(255, (int)(Main.masterColor * 200f), 0);
        if (item.rare >= ItemRarityID.Count) return RarityLoader.GetRarity(item.rare).RarityColor;
        return item.rare == ItemRarityID.White ? Color.White : Terraria.GameContent.UI.ItemRarity.GetColor(item.rare);
    }

    private static void SetNamedTooltip(string name, string owner, string extra = "", int itemId = 0)
    {
        tooltipList = false;
        if (itemId > 0)
        {
            string effect = EquipmentPreview.HoverDetails(ContentSamples.ItemsByType[itemId]);
            if (!string.IsNullOrWhiteSpace(effect)) extra = effect + (string.IsNullOrWhiteSpace(extra) ? "" : "\n" + extra.Trim());
        }
        tooltip = string.IsNullOrWhiteSpace(extra) ? name : name + "\n" + extra.Trim();
        tooltipOwner = owner;
        tooltipQuality = itemId > 0 ? ItemQualityColor(itemId) : null;
    }

    private const string HideBookmarksTitle = "隐藏JEI书签";
    private const string ShowBookmarksTitle = "显示JEI书签";
    private enum TipLine { Title, Owner, Detail, Instruction, Craftable, Missing }

    private static string lastPagedTooltip;
    private static int tooltipPage;
    private static bool pageUpHeld, pageDownHeld;

    private static void DrawTip(int width, int height)
    {
        if (string.IsNullOrWhiteSpace(tooltip)) return;
        int pixels = tooltipList ? Math.Max(20, FontPixels(17)) : Math.Max(9, FontPixels(12));
        int edge = Math.Max(1, (int)MathF.Round(unit));
        int paddingX = Math.Max(4, (int)MathF.Round(5 * unit));
        int paddingY = Math.Max(3, (int)MathF.Round(2.5f * unit));
        int shadow = 2 * edge;
        bool bookmarkHelp = tooltip is HideBookmarksTitle or ShowBookmarksTitle;
        int availableWidth = width - 2 * paddingX - shadow - 4;
        int maxWidth = bookmarkHelp || tooltipList ? availableWidth : Math.Min(availableWidth, Math.Max(180, (int)((tooltipQuality.HasValue ? 240 : 160) * unit)));
        int lineHeight = (int)MathF.Ceiling(Math.Max(BrowserFont.Measure("", BrowserFontFace.Body, pixels).Y,
            BrowserFont.Measure("", BrowserFontFace.Italic, pixels).Y));
        if (tooltipList) lineHeight += Math.Max(2, pixels / 8);
        var lines = new List<(string Text, TipLine Kind)>();
        static BrowserFontFace Style(TipLine kind) => kind is TipLine.Owner or TipLine.Instruction ? BrowserFontFace.Italic : BrowserFontFace.Body;
        void Wrap(string paragraph, TipLine kind)
        {
            if (BrowserFont.Measure(paragraph, Style(kind), pixels).X <= maxWidth)
            {
                lines.Add((paragraph, kind));
                return;
            }
            var line = new StringBuilder();
            var elements = StringInfo.GetTextElementEnumerator(paragraph);
            while (elements.MoveNext())
            {
                int previous = line.Length;
                string glyph = elements.GetTextElement();
                line.Append(glyph);
                if (previous > 0 && BrowserFont.Measure(line.ToString(), Style(kind), pixels).X > maxWidth)
                {
                    line.Length = previous;
                    lines.Add((line.ToString(), kind));
                    line.Clear().Append(glyph);
                }
            }
            lines.Add((line.ToString(), kind));
        }
        var paragraphs = tooltip.Split('\n');
        Wrap(tooltipList ? "合成物品" : paragraphs[0], TipLine.Title);
        int titleLines = lines.Count;
        if (!string.IsNullOrWhiteSpace(tooltipOwner)) Wrap(tooltipOwner, TipLine.Owner);
        for (int n = tooltipList ? 0 : 1; n < paragraphs.Length; n++)
        {
            var kind = tooltipList ? paragraphs[n] switch {
                NativeRecipeMaterials.CraftableHint => TipLine.Craftable,
                NativeRecipeMaterials.MissingHint => TipLine.Missing, _ => TipLine.Detail
            } : TipLine.Detail;
            Wrap(paragraphs[n], kind);
        }
        if (bookmarkHelp)
        {
            var keys = JustEnoughItemsTerraria.Bookmark.GetAssignedKeys();
            string key = keys.Count > 0 ? keys[0] : "未绑定";
            Wrap($"将鼠标悬停在一个原料上，按“{key}”将其添加为书签。", TipLine.Instruction);
        }
        int w = 1;
        int titleGap = !tooltipList && !bookmarkHelp && string.IsNullOrWhiteSpace(tooltipOwner) ? 0 : 2 * edge;
        int maxLines = Math.Max(1, (height - 2 * paddingY - titleGap - shadow - 4) / lineHeight);
        if (tooltipQuality.HasValue && lines.Count > maxLines && maxLines >= 3)
        {
            var keys = Microsoft.Xna.Framework.Input.Keyboard.GetState();
            bool up = keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.PageUp);
            bool down = keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.PageDown);
            int header = Math.Min(titleLines, maxLines - 2);
            int perPage = maxLines - header - 1;
            int pages = (lines.Count - header + perPage - 1) / perPage;
            if (lastPagedTooltip != tooltip) tooltipPage = 0;
            else tooltipPage = Math.Clamp(tooltipPage + (down && !pageDownHeld ? 1 : 0) - (up && !pageUpHeld ? 1 : 0), 0, pages - 1);
            lastPagedTooltip = tooltip;
            pageUpHeld = up;
            pageDownHeld = down;
            int start = header + tooltipPage * perPage;
            var page = lines.GetRange(start, Math.Min(perPage, lines.Count - start));
            lines.RemoveRange(header, lines.Count - header);
            lines.AddRange(page);
            lines.Add(($"PgUp / PgDn   {tooltipPage + 1}/{pages}", TipLine.Instruction));
        }
        else
        {
            lastPagedTooltip = null;
            if (lines.Count > maxLines) { lines.RemoveRange(maxLines, lines.Count - maxLines); lines[^1] = ("…", TipLine.Detail); }
        }
        if (lines.Count <= titleLines) titleGap = 0;
        foreach (var line in lines)
            w = Math.Max(w, (int)MathF.Ceiling(BrowserFont.Measure(line.Text, Style(line.Kind), pixels).X));
        w += 2 * paddingX;
        int h = lines.Count * lineHeight + 2 * paddingY + titleGap;
        int x = Math.Clamp(mouse.X + 14, 2, Math.Max(2, width - w - shadow - 2));
        int y = mouse.Y + h + shadow + 20 >= height ? Math.Max(2, mouse.Y - h - shadow - 8) : mouse.Y + 18;
        var borderColor = tooltipQuality ?? new Color(166, 159, 180);
        Fill(new Rectangle(x + shadow, y + shadow, w, h), Color.Black * 0.55f);
        Fill(new Rectangle(x, y, w, h), borderColor);
        Fill(new Rectangle(x + edge, y + edge, w - 2 * edge, h - 2 * edge), new Color(24, 21, 32, 250));
        int textY = y + paddingY;
        for (int i = 0; i < lines.Count; i++)
        {
            if (i == titleLines && titleGap > 0)
            {
                Fill(new Rectangle(x + 2 * edge, textY, w - paddingX - 2 * edge, edge), borderColor * 0.65f);
                textY += titleGap;
            }
            var line = lines[i];
            float textX = (tooltipList || bookmarkHelp) && line.Kind == TipLine.Title
                ? x + MathF.Round((w - BrowserFont.Measure(line.Text, Style(line.Kind), pixels).X) / 2)
                : x + paddingX;
            var position = new Vector2(textX, textY);
            Color color = line.Kind switch
            {
                TipLine.Craftable => new Color(104,125,255), TipLine.Missing => new Color(255,78,78),
                TipLine.Title => tooltipQuality ?? new Color(252, 252, 252), TipLine.Owner => new Color(84, 84, 252), TipLine.Instruction => new Color(170, 170, 170), _ => Color.LightGray
            };
            if (!tooltipList) DrawTextShadow(line.Text, Style(line.Kind), pixels, position, color);
            BrowserFont.Draw(batch, line.Text, Style(line.Kind), pixels, position, color);
            textY += lineHeight;
        }
    }
}
