using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

public static partial class BrowserUI
{
    private static readonly string[] categoryKeys = { "Crafting", "Shimmer2", "CreatureDrops", "NPCShops", "Harvesting", "NPCSpawns", "TreasureBags", "FishingCrates", "Fishing" };
    private static readonly List<int> pageStarts = new();
    private static readonly List<int> pageTextOffsets = new();
    private static BrowserRow[] pageRows;
    private static float pageHeight, pageUnit;
    private static string firstVisibleKey, pendingAnchor;

    private static void Paginate(BrowserRow[] rows, float available)
    {
        if (rows.Length > 0 && rows[0].Category == BrowserCategory.Fishing)
            RefreshFishingNotes(rows[0]);
        if (pageRows != rows || pageHeight != available || pageUnit != unit)
        {
            if ((pageHeight != available || pageUnit != unit) && pendingAnchor == null) pendingAnchor = firstVisibleKey;
            pageUnit = unit;
            pageRows = rows;
            pageHeight = available;
            pageStarts.Clear();
            pageTextOffsets.Clear();
            float used = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                float height = CardHeight(rows[i]);
                if (height > available && NoteLines(rows[i]).Length > 0)
                {
                    int capacity = Math.Max(1, (int)((available - CardBodyHeight(rows[i]) - NoteBottomPadding) / TextLine));
                    int length = NoteLines(rows[i]).Length;
                    for (int offset = 0; offset < length; offset += capacity)
                    {
                        pageStarts.Add(i);
                        pageTextOffsets.Add(offset);
                    }
                    used = available;
                    continue;
                }
                if (i == 0 || used + 2 + height > available)
                {
                    pageStarts.Add(i);
                    pageTextOffsets.Add(0);
                    used = height;
                }
                else used += 2 + height;
            }
        }
        int page = Math.Clamp(view.Page, 0, pageStarts.Count - 1);
        if (pendingAnchor != null)
        {
            int index = Array.FindIndex(rows, r => r.Key == pendingAnchor);
            if (index >= 0 && pageStarts[page] != index)
            {
                int exact = pageStarts.IndexOf(index);
                page = exact >= 0 ? exact : pageStarts.FindLastIndex(first => first < index);
            }
            pendingAnchor = null;
        }
        view = view with { Page = page };
        firstVisibleKey = rows[pageStarts[page]].Key;
    }

    private static KeyboardState previousQueryKeys;
    private static bool queryKeysReady;
    private static bool PressedQueryKey(Terraria.ModLoader.ModKeybind bind,KeyboardState now)
    {
        foreach(string binding in bind.GetAssignedKeys())
            if(Enum.TryParse<Keys>(binding,out var key) && now.IsKeyDown(key) && previousQueryKeys.IsKeyUp(key))return true;
        return false;
    }

    public static void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || index == null) return;
        batch = spriteBatch;
        int width = Main.screenWidth, height = Main.screenHeight;
        unit = Math.Max(.3f, Math.Min(Math.Min(width / 640f, height / 320f) * Math.Clamp(Main.UIScale, .8f, 1.3f),
            Math.Min(width / 490f, height / 255f)));
        bool overview = !IsOpen && InventoryOverview.Active;
        float freeSide = IsOpen ? 0 : overview ? 276 * Main.UIScale : (width - (TerminalWidth?.Invoke() ?? 0)) / 2f;
        // Keep both sidebars inside the native grid margins, including the outer gutter.
        if (overview) unit = Math.Min(unit, freeSide / 181f);
        if (!IsOpen)
        {
            // Keep the host at its own UI scale. Fit existing JEI controls into each margin.
            if (freeSide < 36) return;
            unit = Math.Min(unit, freeSide / 76f);
        }
        float w = width / unit, h = height / unit;
        bool blocked = InputBlocked;
        if (blocked) Close();
        mouse = blocked ? new Point(-10000, -10000) : new Point(Main.mouseX, Main.mouseY);
        int wheelValue = Mouse.GetState().ScrollWheelValue;
        int wheelDelta = blocked ? 0 : unchecked(previousWheel - wheelValue);
        wheel = Math.Sign(wheelDelta);
        if (wheel != 0) stationClick = null;
        previousWheel = wheelValue;
        leftClick = !blocked && Main.mouseLeft && !previousLeft;
        rightClick = !blocked && Main.mouseRight && !previousRight;
        previousLeft = Main.mouseLeft;
        previousRight = Main.mouseRight;
        SetPlainTooltip(null);
        hovered = default;
        hoveredBookmarkKey = null;
        stationHovered = false;
        catalogHovered = false;
        clicked = null;

        float panelX = (w - 198) / 2;
        float sideWidth = Math.Min(162, panelX - 50);
        if (!IsOpen) sideWidth = Math.Min(162, freeSide / unit - 12);
        float leftTop = overview ? Math.Min(h - 78, 316 * Main.UIScale / unit) : 0;
        bool overSide = Box(6, leftTop, sideWidth, h - leftTop).Contains(mouse);
        float panelUnit = unit;
        float margin = InventoryOverview.RightMargin((int)(width / Main.UIScale)) * Main.UIScale;
        if (IsOpen) margin = Math.Min(margin, (width - 240 * panelUnit) / 2);
        else if (!overview) margin = Math.Min(margin, freeSide);
        float catalogUnit = Math.Min(276 * Main.UIScale / 181f, Math.Min(margin / 76f,
            Math.Min(width / 640f, height / 320f) * Math.Clamp(Main.UIScale, .8f, 1.3f)));
        unit = catalogUnit;
        float catalogWidth = margin / unit - 12;
        float catalogX = width / unit - 6 - catalogWidth, catalogHeight = height / unit;
        float rightTop = overview && catalogX * unit < 610 * Main.UIScale ? leftTop * panelUnit / unit : 0;
        overSide |= Box(catalogX, rightTop, catalogWidth, catalogHeight - rightTop).Contains(mouse);
        var inputRect = SearchField(catalogX, catalogWidth, catalogHeight);
        var popup = SuggestionArea(inputRect, (int)(rightTop * unit), out int popupRow, out int popupCount);
        Point actualMouse = mouse;
        bool overSuggestions = popup.Contains(mouse) && !popup.IsEmpty;
        if (overSuggestions) mouse = new Point(-10000, -10000);
        unit = panelUnit;
        if (IsOpen || overSide)
        {
            Main.LocalPlayer.mouseInterface = true;
            PlayerInput.LockVanillaMouseScroll("JustEnoughItemsTerraria");
        }
        if (IsTyping && (leftClick || rightClick) && !overSuggestions && !inputRect.Contains(mouse)) EndTyping();
        // The owning layer is unscaled. A single point-sampled batch keeps JEI pixels crisp.
        batch.End();
        BrowserFont.BeginFrame();
        BrowserTexturePreload.BeginDraw();
        frameMeasurementsLeft = 12;
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        if (pressedMark != null && Vector2.DistanceSquared(mouse.ToVector2(), markPressPosition.ToVector2()) >= MathF.Pow(Math.Max(4, 2 * unit), 2))
            draggingMark = true;
        DrawSide(6, sideWidth, h, true, wheelDelta, leftTop);
        if (overview)
            Button(50, h - 26, 20, "arrow_previous_v2", JeiText.Get("CloseCraftingOverview"), () => Main.recBigList = false);
        if (!Main.mouseLeft) { pressedMark = null; draggingMark = false; }
        unit = catalogUnit;
        DrawSide(catalogX, catalogWidth, catalogHeight, false, wheelDelta, rightTop);
        unit = panelUnit;
        if (IsOpen)
        {
            if (fromBackpack && view.Category == BrowserCategory.Craft) NativeRecipeMaterials.Refresh();
            DrawCenter(panelX, h);
        }
        mouse = actualMouse;
        unit = catalogUnit;
        popup = SuggestionArea(inputRect, (int)(rightTop * unit), out popupRow, out popupCount);
        DrawSuggestions(popup, popupRow, popupCount, wheelDelta);
        unit = panelUnit;
        bool orderClick = Main.mouseMiddle && !previousMiddle;
        previousMiddle = Main.mouseMiddle;
        if (orderClick && TerminalLayout && !hovered.Npc && hovered.Id > 0 && OrderItem?.Invoke(hovered.Id) == true)
        {
            Close();
            return;
        }
        var keys=Keyboard.GetState();
        if(!blocked && queryKeysReady && !IsTyping && !Main.drawingPlayerChat && !Main.editSign && !Main.editChest && !Main.blockInput &&
            !GuideActive && (hovered.Id!=0 || (!IsOpen && HostItem > 0)) &&
            !keys.IsKeyDown(Keys.LeftControl) && !keys.IsKeyDown(Keys.RightControl) &&
            !keys.IsKeyDown(Keys.LeftAlt) && !keys.IsKeyDown(Keys.RightAlt)){
            if(queuedQuery is bool uses)clicked=()=>QueryHovered(uses);
            else if(PressedQueryKey(JustEnoughItemsTerraria.Sources,keys))clicked=()=>QueryHovered(false);
            else if(PressedQueryKey(JustEnoughItemsTerraria.Uses,keys))clicked=()=>QueryHovered(true);
        }
        queuedQuery=null;previousQueryKeys=keys;queryKeysReady=true;
        if (draggingMark){
            Main.LocalPlayer.mouseInterface=true;
            Main.hoverItemName="";
            int side=(int)MathF.Round((BrowserPreferences.CellSize-2)*unit);
            var ghost=new Rectangle(mouse.X-side/2,mouse.Y-side/2,side,side);
            if(dragSubject.Npc)NpcImage(dragSubject.Id,ghost,true);else ItemImage(dragSubject.Id,ghost);
        }else DrawTip(width, height);
        if (IsTyping)
            Main.instance.DrawWindowsIMEPanel(new Vector2(Math.Max(0, inputRect.Left - 30), Math.Max(0, inputRect.Top - 64)), .8f);
        var action = clicked;
        clicked = null;
        if (stationClick is { } pending)
        {
            if (rightClick || leftClick && (!stationHovered || hovered.Id != pending.Item))
                stationClick = null;
            else if (Environment.TickCount64 - pending.Time >= StationDoubleClickMs)
            {
                stationClick = null;
                action = () => NavigateWithClick(() => Open(new(pending.Item), false));
            }
        }
        if (!InputBlocked) action?.Invoke();
        // GameInterfaceLayer ends this batch and starts a new one for the next layer.
    }

    private static string pressedMark;
    private static Subject dragSubject;
    private static Point markPressPosition;
    private static bool draggingMark;
    private static void ReorderMark(string source, string target, bool after)
    {
        var keys = BrowserPreferences.Bookmarks;
        int from = keys.IndexOf(source), to = keys.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        int insert = to + (after ? 1 : 0);
        if (from < insert) insert--;
        if (from == insert) return;
        keys.RemoveAt(from);
        keys.Insert(insert, source);
        if(MoveOrderGroups!=null && (groupMarks.ContainsKey(source)||groupMarks.ContainsKey(target)))
            MoveOrderGroups(keys.Where(groupMarks.ContainsKey).Select(k=>k[2..]).ToArray());
        ResolveMarks();
        BrowserPreferences.Save();
    }

    private static int ScrollCatalog(int page, int pages, int delta)
    {
        // FNA reports 120 units per detent. A draw can sample several detents together.
        const int unitsPerPage = 120;
        if (Math.Sign(delta) != Math.Sign(catalogWheelRemainder)) catalogWheelRemainder = 0;
        long units = delta + (long)catalogWheelRemainder;
        int next = (int)(((page + units / unitsPerPage) % pages + pages) % pages);
        catalogWheelRemainder = pages>1 ? (int)(units % unitsPerPage) : 0;
        return next;
    }

    private const int SearchHeight = 34;
    private static Rectangle SearchField(float x, float width, float height) =>
        Box(x, height - 6 - SearchHeight, width - SearchHeight - 2, SearchHeight);

    private static void DrawSide(float x, float width, float h, bool left, int wheelDelta, float top)
    {
        int cellSize = BrowserPreferences.CellSize;
        float pitch = cellSize;
        int cols = Math.Max(1, (int)MathF.Floor(width / pitch));
        float availableHeight = h - top - (left ? 56 : 36 + SearchHeight);
        int rows = Math.Max(1, (int)MathF.Floor(availableHeight / pitch));
        if (!left)
        {
            var config = ModContent.GetInstance<BrowserClientConfig>();
            (cols, rows, pitch) = CatalogGrid.Fit(width, availableHeight, unit, cellSize, config.CatalogColumns, config.CatalogRows);
        }
        float icon = Math.Max(1, Math.Min(width / cols, pitch) - 2);
        int historyRows = left && showHistory ? Math.Min(2, (recent.Count + cols - 1) / cols) : 0;
        float historyTop = h - 29 - historyRows * pitch;
        if (historyRows > 0) rows = Math.Max(0, (int)((historyTop - top - 4 - 28) / pitch));
        int count = Math.Max(1, cols * rows);
        int size = left ? marks.Count : catalog.Length;
        int pages = Math.Max(1, (size + count - 1) / count);
        int page = Math.Clamp(left ? markPage : catalogPage, 0, pages - 1);
        void SetPage(int p)
        {
            if (left) markPage = p;
            else { catalogPage = p; catalogWheelRemainder = 0; }
        }
        if (!left || showMarks)
        {
            bool over = Box(x, top + 6, width, (historyRows > 0 ? historyTop - 10 : h - (left ? 32 : 12 + SearchHeight)) - top).Contains(mouse);
            if (!left && !over) catalogWheelRemainder = 0;
            if (over && wheel != 0)
            {
                page = left ? Math.Clamp(page + wheel, 0, pages - 1) : ScrollCatalog(page, pages, wheelDelta);
                wheel = 0;
            }
            if (left) markPage = page; else catalogPage = page;
            Pager(x, top + 8, width, 16, $"{page + 1}/{pages}",
                (left ? page>0 : pages>1) ? () => SetPage((page+pages-1)%pages) : null, (left ? page+1<pages : pages>1) ? () => SetPage((page+1)%pages) : null);
            if(left && draggingMark && !Main.mouseLeft && size>0 &&
                Box(x,top+28,width,rows*pitch).Contains(mouse)){
                int col=(int)((mouse.X-Box(x,0,0,0).X)/(width/cols*unit));
                int row=(int)((mouse.Y-Box(0,top+28,0,0).Y)/(pitch*unit));
                if(page*count+row*cols+col>=size){
                    string source=pressedMark;
                    clicked=()=>ReorderMark(source,marks[^1].Group??marks[^1].Key,true);
                }
            }
            for (int i = 0; i < cols * rows && page * count + i < size; i++)
            {
                int pos = page * count + i;
                Mark? mark = left ? marks[pos] : null;
                Subject target = left ? mark.Value.Subject : new Subject(catalog[pos]);
                float bx = x + i % cols * (width / cols), by = top + 28 + i / cols * pitch;
                var cell = Box(bx, by, width / cols, pitch);
                var art = Box(bx + (width / cols - icon) / 2, by + (pitch - icon) / 2, icon, icon);
                if(mark is { Group: not null } grouped){
                    int hue=Convert.ToInt32(grouped.Group.Substring(2,4),16);
                    Fill(cell,Main.hslToRgb(hue/65536f,.55f,.42f)*.38f);
                }
                if (target.Npc) NpcImage(target.Id, art, true); else ItemImage(target.Id, art);
                if (mark?.Recipe?.Category == BrowserCategory.Craft)
                    ItemImage(ItemID.WorkBench, Box(bx + width / cols - 9, by + pitch - 9, 9, 9));
                else if (mark?.Recipe != null)
                    Icon("recipe_bookmark", Box(bx + width / cols - 7, by + pitch - 7, 7, 7));
                string detail = mark?.Recipe == null ? "" : "\n" + JeiText.Get("RecipeBookmark");
                if(mark is { Group: not null } orderMark){
                    string ratio=$"{orderMark.Have}/{orderMark.Need}";
                    Label(ratio,Box(bx,by+pitch-7,width/cols,7),7,Color.White,true,1);
                    detail="\n" + JeiText.Get("Order")+Lang.GetItemNameValue(orderMark.Product)+"\n" + JeiText.Get("AllocatedNeeded")+ratio;
                    if(CancelBookmarkOrder!=null)detail+="\n" + JeiText.Get("ShiftBookmarkKeyCancelThisGroupAnd");
                }
                else if (mark is { } tracked && AvailableCounts != null && ((ReadOrderGroups==null && orderGoals.TryGetValue(tracked.Key,out int need)) ||
                    BrowserPreferences.Goals.TryGetValue(tracked.Key,out need)))
                {
                    if (countsTick == null || Main.GameUpdateCount - countsTick.Value >= 30)
                    {
                        available = AvailableCounts();
                        countsTick = Main.GameUpdateCount;
                    }
                    int have = available.GetValueOrDefault(target.Id);
                    string ratio = $"{have}/{need}";
                    Label(ratio, Box(bx, by + pitch - 7, width / cols, 7), 7, Color.White, true, 1);
                    detail = "\n" + JeiText.Get("AvailableNeeded") + ratio;
                }
                Hit(target, cell, extra: detail, mark: mark, fromCatalog: !left);
                if (!left && CheatActive && cell.Contains(mouse) && (leftClick || Main.mouseRight)) {
                    bool single = !leftClick;
                    clicked = () => GiveCatalogItem(target.Id, single);
                }
                if (mark is { } bookmark && cell.Contains(mouse))
                {
                    if (leftClick)
                    {
                        pressedMark = bookmark.Group ?? bookmark.Key;
                        dragSubject = target;
                        markPressPosition = mouse;
                        draggingMark = false;
                        clicked = null;
                    }
                    bool after = mouse.X >= cell.Center.X;
                    if (draggingMark && pressedMark != null)
                    {
                        SetPlainTooltip(null);
                        int thickness = Math.Max(1, (int)unit);
                        int edge = after ? cell.Right - thickness : cell.Left;
                        Fill(new Rectangle(edge, cell.Top, thickness, cell.Height), Color.White);
                    }
                    if (!Main.mouseLeft && pressedMark != null)
                    {
                        string source = pressedMark;
                        if (draggingMark) clicked = () => ReorderMark(source, bookmark.Group ?? bookmark.Key, after);
                        else if (source == (bookmark.Group ?? bookmark.Key)) clicked = ItemClick(target, false, bookmark, false);
                    }
                }
            }
        }
        if (left)
        {
            if (historyRows > 0)
            {
                if (Box(x, historyTop - 3, width, historyRows * pitch + 3).Contains(mouse) && wheel != 0)
                {
                    historyScroll += wheel;
                    wheel = 0;
                }
                historyScroll = Math.Clamp(historyScroll, 0, (recent.Count + cols - 1) / cols - historyRows);
                for (float dx = 0; dx < width; dx += 14)
                    Fill(Box(x + dx, historyTop - 3, Math.Min(8, width - dx), 1), Color.Gray);
                float cellWidth = width / cols;
                for (int i = 0; i < cols * historyRows && historyScroll * cols + i < recent.Count; i++)
                {
                    var entry = recent[historyScroll * cols + i];
                    float bx = x + i % cols * cellWidth, by = historyTop + i / cols * pitch;
                    var art = Box(bx + (cellWidth - icon) / 2, by + 1, icon, icon);
                    if (entry.Subject.Npc) NpcImage(entry.Subject.Id, art, true); else ItemImage(entry.Subject.Id, art);
                    Hit(entry.Subject, Box(bx, by, cellWidth, pitch), mark: entry);
                }
            }
            Button(x, h - 26, 20, showMarks ? "bookmark_button_enabled" : "bookmark_button_disabled",
                showMarks ? HideBookmarksTitle : ShowBookmarksTitle, () => { showMarks = !showMarks; markPage = 0; });
            Button(x + 22, h - 26, 20, showHistory ? "history_button_enabled" : "history_button_disabled",
                JeiText.Get("SearchHistory"), () => { showHistory = !showHistory; historyScroll = 0; });
        }
        else
        {
            var field = SearchField(x, width, h);
            int edge = Math.Max(1, (int)MathF.Round(field.Height / 20f));
            Fill(field, Color.Black);
            var bevel = field;
            bevel.Inflate(-edge, -edge);
            Fill(bevel, new Color(86, 86, 85));
            Fill(new Rectangle(bevel.X, bevel.Y, bevel.Width, edge), new Color(172, 172, 172));
            Fill(new Rectangle(bevel.X, bevel.Y, edge, bevel.Height - edge), new Color(172, 172, 172));
            bevel.Inflate(-edge, -edge);
            Fill(bevel, Color.Black);
            if (field.Contains(mouse))
            {
                if (rightClick && (search.Length > 0 || catalogPage != 0))
                {
                    RememberSearch();
                    recalled = -1;
                    search = "";
                    catalogPage = 0;
                    Filter();
                    PlayClick();
                }
                if (leftClick && !IsTyping)
                {
                    Main.clrInput();
                    IsTyping = true;
                    PlayerInput.WritingText = true;
                    Main.instance.HandleIME();
                    searchSelection.Focus(search);
                    historyKeys = Keyboard.GetState();
                    recalled = -1;
                }
            }
            if (IsTyping)
            {
                PlayerInput.WritingText = true;
                Main.instance.HandleIME();
                searchSelection.Mouse(search,field,leftClick && field.Contains(mouse),Main.mouseLeft,
                    Terraria.UI.ItemSlot.ShiftInUse);
                string typed = searchSelection.Read(search);
                if (typed != search)
                {
                    recalled = -1;
                    search = typed.Length <= 128 ? typed : typed[..128];
                    catalogPage = 0; Filter();
                }
                if (Main.hasFocus && Keyboard.GetState().IsKeyDown(Keys.Tab) && historyKeys.IsKeyUp(Keys.Tab))
                {
                    SuggestionArea(field, (int)(top * unit), out _, out int visible);
                    CompleteNextSuggestion(visible);
                }
                RecallSearch();
                if (Main.inputTextEnter || Main.inputTextEscape) EndTyping();
            }
            searchSelection.Draw(batch, field, search, IsTyping, Main.GameUpdateCount);
            DrawWrench(x, width, h);
        }
    }

    private static void Pager(float x, float y, float width, float size, string text, Action previous, Action next)
    {
        Button(x, y, size, "arrow_previous_v2", JeiText.Get("PreviousPage"), previous, iconSize: size * .49f);
        Button(x + width - size, y, size, "arrow_next_v2", JeiText.Get("NextPage"), next, iconSize: size * .49f);
        Label(text, Box(x + size, y, width - size * 2, size), Math.Min(12, size), Color.White, true,
            face: BrowserFontFace.Numbers);
    }

    private static void DrawCenter(float x, float h)
    {
        const float y = 26, width = 198;
        float height = h - 13 - y;
        Panel("gui_background_v2", Box(x, y, width, height), 16);
        float tabX = x + 3;
        foreach (var category in Categories)
        {
            if (!results.ContainsKey(category)) continue;
            bool active = category == view.Category;
            float tabY = y - (active ? 20 : 18);
            var tab = Box(tabX, tabY, 23, y - tabY + 1);
            var texture = Graphic(active ? "tab_selected" : "tab_unselected");
            var source = texture.Bounds;
            if (!active) source.Height -= 3; // Remove the dark bottom stroke and two transparent rows at the panel join.
            batch.Draw(texture, tab, source, Color.White);
            var art = Box(tabX + 4, tabY + 2, 15, 15);
            switch (category)
            {
                case BrowserCategory.Fishing: ItemImage(ItemID.WoodFishingPole, art); break;
                case BrowserCategory.Crate: ItemImage(ItemID.WoodenCrate, art); break;
                case BrowserCategory.Bag: ItemImage(ItemID.KingSlimeBossBag, art); break;
                case BrowserCategory.Craft: ItemImage(ItemID.WorkBench, art); break;
                case BrowserCategory.Shimmer: ItemImage(ItemID.BottomlessShimmerBucket, art); break;
                case BrowserCategory.Drop: NpcImage(NPCID.BlueSlime, art); break;
                case BrowserCategory.Shop: NpcImage(NPCID.Merchant, art, true); break;
                case BrowserCategory.Spawn: ItemImage(ItemID.LifeformAnalyzer, art); break;
                case BrowserCategory.Harvest:
                    var staff = Graphic("harvest_tab");
                    int scale = Math.Max(1, Math.Min(art.Width / staff.Width, art.Height / staff.Height));
                    int sw = staff.Width * scale, sh = staff.Height * scale;
                    batch.Draw(staff, new Rectangle(art.X + (art.Width - sw) / 2, art.Y + (art.Height - sh) / 2, sw, sh), Color.White);
                    break;
            }
            if (tab.Contains(mouse))
            {
                SetPlainTooltip(JeiText.Get(categoryKeys[(int)category]));
                if (leftClick && !active)
                    clicked = () => NavigateWithClick(() => Select(view with { Category = category, Page = 0 }, true));
            }
            tabX += 23;
        }
        var header = Box(x + 6, y + 4, width - 12, 13);
        Fill(header, new Color(161, 161, 161));
        string direction = view.Uses ? JeiText.Get("Uses") : JeiText.Get("Sources");
        string caption = results.Count == 0 ? direction : JeiText.Get(categoryKeys[(int)view.Category]);
        var titleBox = Box(x + 20, y + 4, width - 40, 13);
        if (view.Category == BrowserCategory.Shimmer && results.Count > 0)
            caption = JeiText.Get("Shimmer", direction);
        Label(caption, titleBox, 12, Color.White, true);
        void Switch(int delta)
        {
            var choices = Categories.Where(results.ContainsKey).ToArray();
            int next = (Array.IndexOf(choices, view.Category) + choices.Length + delta) % choices.Length;
            Select(view with { Category = choices[next], Page = 0 }, true);
        }
        Button(x + 6, y + 4, 13, "arrow_previous_v2", JeiText.Get("PreviousCategory"), results.Count > 1 ? () => Switch(-1) : null, iconSize: 7);
        Button(x + width - 19, y + 4, 13, "arrow_next_v2", JeiText.Get("NextCategory"), results.Count > 1 ? () => Switch(1) : null, iconSize: 7);
        if (!results.TryGetValue(view.Category, out var items))
        {
            Label(JeiText.Get("NoFoundFor", Name(view.Target), direction), Box(x + 9, y + 24, width - 18, 18), 12, ink);
            return;
        }
        Paginate(items, height - 35 - 4);
        int pages = pageStarts.Count;
        Action prev = view.Page > 0 ? () => Select(view with { Page = view.Page - 1 }, true) : null;
        Action nextPage = view.Page + 1 < pages ? () => Select(view with { Page = view.Page + 1 }, true) : null;
        Fill(Box(x + 6, y + 19, width - 12, 13), new Color(161, 161, 161));
        Pager(x + 6, y + 19, width - 12, 13, $"{view.Page + 1}/{pages}", prev, nextPage);
        int first = pageStarts[view.Page];
        int end = view.Page + 1 < pages ? pageStarts[view.Page + 1] : items.Length;
        bool continues = end == first;
        if (continues) end = first + 1;
        DrawStations(x, y, height, items, first, end);
        Panel("recipe_options_tab_v2", Box(x - 22, y + height - 41, 23, 41), 8, 9, flushRight: true);
        Button(x - 17, y + height - 37, 16, "bookmarks_first", JeiText.Get("ShowBookmarkedRecipesFirst"),
            () => { bookmarksFirst = !bookmarksFirst; Refresh(); }, bookmarksFirst);
        Button(x - 17, y + height - 21, 16, "craftable_first", JeiText.Get("RefreshAvailableRecipesUsingCurrentMaterialsAnd"),
            () => { view = view with { Page = 0 }; Refresh(); });
        float rowY = y + 35;
        for (int i = first; i < end; i++)
        {
            int offset = i == first ? pageTextOffsets[view.Page] : 0;
            int limit = continues ? pageTextOffsets[view.Page + 1] - offset : int.MaxValue;
            rowY += DrawRecipe(items[i], x + width / 2, rowY, offset, limit) + 2;
        }
        if (wheel != 0 && Box(x + 4, y + 33, width - 8, height - 35).Contains(mouse))
        {
            clicked = wheel > 0 ? nextPage : prev;
            wheel = 0;
        }
    }

    private static void DrawStations(float x, float y, float height, BrowserRow[] rows, int first, int end)
    {
        var stations = rows.Skip(first).Take(end - first).SelectMany(r => r.Stations)
            .Where(s => !s.CombinedOnly).DistinctBy(s => s.TileId).ToArray();
        if (stations.Length == 0) return;
        var entries = new List<(BrowserStation Station, int Item, bool Primary)>();
        foreach (var station in stations)
        {
            entries.Add((station, station.Items.FirstOrDefault(), true));
            if (expandedStations.Contains(station.TileId))
                for (int n = 1; n < station.Items.Length; n++) entries.Add((station, station.Items[n], false));
        }
        int cols = stations.Length > 1 ? 2 : 1;
        int total = (entries.Count + cols - 1) / cols;
        int visible = Math.Min(total, Math.Max(1, (int)((height - 48) / 18)));
        stationScroll = Math.Clamp(stationScroll, 0, total - visible);
        float railWidth = cols * 18 + 4;
        var rail = Box(x - railWidth + 1, y, railWidth, visible * 18 + 8);
        Panel("catalyst_tab_v2", rail, 8, 9, flushRight: true);
        int start = stationScroll * cols;
        for (int i = 0; i < visible * cols && start + i < entries.Count; i++)
        {
            var (station, id, primary) = entries[start + i];
            float sx = x - railWidth + 3 + i % cols * 18, sy = y + 4 + i / cols * 18;
            var area = Box(sx, sy, 18, 18);
            if (id > 0)
            {
                bool canExpand = primary && station.Items.Length > 1;
                bool expanded = expandedStations.Contains(station.TileId);
                ItemImage(id, Box(sx + 1, sy + 1, 16, 16));
                if (canExpand)
                {
                    Fill(Box(sx + 11, sy + 13, 7, 3), ink);
                    Fill(Box(sx + 13, sy + 11, 3, 7), ink);
                    Fill(Box(sx + 12, sy + 14, 5, 1), Color.White);
                    Fill(Box(sx + 14, sy + 12, 1, 5), Color.White);
                }
                string extra = canExpand && area.Contains(mouse) ?
                    "\n" + (expanded ? JeiText.Get("DoubleClickToCollapseAlternativeStations", station.Items.Length) : JeiText.Get("DoubleClickToExpandAlternativeStations", station.Items.Length)) : "";
                Hit(new(id), area, station: true, extra: extra);
                if (canExpand && leftClick && area.Contains(mouse))
                    clicked = () => ClickStationGroup(station.TileId, id);
            }
            else
            {
                if (!DrawStationTile(station.TileId, Box(sx + 1, sy + 1, 16, 16)))
                    Label(station.Name, area, 10, ink);
                if (area.Contains(mouse))
                    SetNamedTooltip(station.Name, TileLoader.GetTile(station.TileId)?.Mod.DisplayNameClean ?? "Terraria", JeiText.Get("CraftingStation"));
            }
        }
        if (rail.Contains(mouse) && wheel != 0)
        {
            stationScroll = Math.Clamp(stationScroll + wheel, 0, total - visible);
            wheel = 0;
        }
    }

    private static void ClickStationGroup(int tile, int item)
    {
        if (stationClick is { } pending && pending.Tile == tile)
        {
            stationClick = null;
            if (!expandedStations.Add(tile)) expandedStations.Remove(tile);
            PlayClick();
        }
        else stationClick = (tile, item, Environment.TickCount64);
    }
}
