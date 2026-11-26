using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;

namespace JustEnoughItemsTerraria;

public static partial class BrowserUI
{
    private readonly record struct Subject(int Id, bool Npc = false);
    private readonly record struct View(Subject Target, bool Uses, BrowserCategory Category, int Page, string Anchor = null);
    private readonly record struct Mark(Subject Subject,string Key,BrowserRow Recipe=null,string Group=null,int Product=0,int Have=0,int Need=0);
    private static readonly BrowserCategory[] defaultOrder = Enum.GetValues<BrowserCategory>();
    private static readonly BrowserCategory[] crateUseOrder = [BrowserCategory.Crate, ..defaultOrder.Where(c => c != BrowserCategory.Crate)];
    private static readonly BrowserCategory[] bagUseOrder = [BrowserCategory.Bag, ..defaultOrder.Where(c => c != BrowserCategory.Bag)];
    private static readonly BrowserCategory[] npcSourceOrder =
        { BrowserCategory.Drop, BrowserCategory.Craft, BrowserCategory.Shimmer, BrowserCategory.Shop, BrowserCategory.Harvest };
    private static readonly BrowserCategory[] harvestSourceOrder =
        { BrowserCategory.Harvest, BrowserCategory.Craft, BrowserCategory.Shimmer, BrowserCategory.Drop, BrowserCategory.Shop };
    private static readonly Dictionary<BrowserCategory, BrowserRow[]> results = new();
    private static readonly SoundStyle terminalClick = SoundID.MenuTick with {
        Volume = .25f,
        MaxInstances = 4
    };
    private static readonly SoundStyle pageClickSound = terminalClick with { MaxInstances = 0 };
    private static readonly List<View> history = new();
    private static readonly List<Mark> recent = new();
    private static readonly List<Mark> marks = new();
    private static readonly Dictionary<(BrowserRow, bool), int> scroll = new();
    private static long ingredientCycleStart;
    private static readonly Dictionary<BrowserSlot, int> alternatives = new();
    private static readonly HashSet<int> expandedStations = new();
    private static (int Tile, int Item, long Time)? stationClick;
    private const int StationDoubleClickMs = 350;
    private static BrowserIndex index;
    private static View view;
    private static Subject hovered;
    private static string hoveredBookmarkKey;
    private static bool stationHovered, catalogHovered;
    private static bool showMarks = true, showHistory, bookmarksFirst;
    private static int catalogPage, markPage, stationScroll, historyScroll;
    private static int[] catalog = Array.Empty<int>();
    private static readonly SearchSelection searchSelection = new();
    private static string search = "";
    private static readonly List<string> searchHistory = new();
    private static int recalled = -1;
    private static string draft = "";
    private static KeyboardState historyKeys;

    private static void RememberSearch()
    {
        if (string.IsNullOrWhiteSpace(search) || recalled >= 0) return;
        searchHistory.Remove(search);
        searchHistory.Insert(0, search);
        if (searchHistory.Count > 50) searchHistory.RemoveAt(50);
    }

    private static void RecallSearch()
    {
        var keys = Keyboard.GetState();
        bool up = keys.IsKeyDown(Keys.Up) && historyKeys.IsKeyUp(Keys.Up);
        bool down = keys.IsKeyDown(Keys.Down) && historyKeys.IsKeyUp(Keys.Down);
        historyKeys = keys;
        if (!Main.hasFocus || up == down || searchHistory.Count == 0) return;
        var ime = ReLogic.OS.Platform.Get<ReLogic.Localization.IME.IImeService>();
        if (ime.IsCandidateListVisible || !string.IsNullOrEmpty(ime.CompositionString)) return;
        if (up)
        {
            if (recalled < 0) draft = search;
            int next = recalled + 1;
            if (next == 0 && searchHistory[0] == draft) next++;
            if (next >= searchHistory.Count) return;
            recalled = next;
        }
        else if (recalled >= 0) recalled--;
        else return;
        SetQueryText(recalled < 0 ? draft : searchHistory[recalled]);
    }
    private static bool previousLeft, previousRight, previousMiddle;
    internal static Func<int,bool> OrderItem;
    internal static Func<int,bool,bool> RecipeBench;
    internal static Func<int,(int Missing,bool Craftable)[]> BenchMaterials;
    private static int previousWheel;
    private static int catalogWheelRemainder;
    private static ulong lastRefresh;
    private static LootDifficulty lastDifficulty;
    internal static Func<int> TerminalWidth;
    internal static Func<int> TerminalItem;
    internal static Func<bool> TerminalToggle;
    private static bool docked, fromBackpack, fromTerminal;
    internal static Action LeaveTerminalForMap;
    private static bool heldAtUpdate;
    internal static bool InputBlocked => heldAtUpdate || !Main.mouseItem.IsAir;
    internal static bool TerminalLayout => IsOpen ? fromTerminal : TerminalWidth?.Invoke() > 0;
    private static ulong? closedTick;
    internal static bool ClosedThisTick => closedTick == Main.GameUpdateCount;
    internal static Func<bool> TerminalGuideOpen;
    internal static bool GuideActive => TerminalGuideOpen?.Invoke() == true;
    internal static bool Visible => !GuideActive && (IsOpen || docked && (InventoryOverview.Active || TerminalWidth?.Invoke() > 0));

    public static bool IsOpen { get; private set; }
    public static bool IsTyping { get; private set; }

    private static void PlayClick()
    {
        if (!Main.dedServ) SoundEngine.PlaySound(in terminalClick);
    }

    // Reopening the current result without changing its page or recipe is silent.
    private static void NavigateWithClick(Action navigate)
    {
        var previousView = view;
        bool wasOpen = IsOpen;
        string previousRecipe = firstVisibleKey;
        navigate();
        if (wasOpen != IsOpen || previousView != view || pendingAnchor != null && previousRecipe != pendingAnchor)
            PlayClick();
    }

    public static void Toggle()
    {
        if (InputBlocked) return;
        if (IsOpen) { Close(); return; }
        if (view.Target.Id == 0 || docked)
        {
            int id = Main.HoverItem is { IsAir: false } item ? item.type : ItemID.Torch;
            index = AdaptiveBrowserSystem.EnsureIndex();
            Open(new(index.HasRows(id, false, false) ? id : ItemID.Torch), false);
        }
        else { Activate(); Refresh(); }
    }

    private static void Activate()
    {
        if (IsOpen) return;
        index = AdaptiveBrowserSystem.EnsureIndex();
        if (docked)
        {
            view = default;
            history.Clear();
        }
        fromTerminal = TerminalWidth?.Invoke() > 0;
        fromBackpack = InventoryOverview.BackpackAvailable;
        IsOpen = true;
        PlayerInput.LockVanillaMouseScroll("JustEnoughItemsTerraria");
        previousLeft = Main.mouseLeft;
        previousRight = Main.mouseRight;
        previousMiddle = Main.mouseMiddle;
        previousWheel = Mouse.GetState().ScrollWheelValue;
        Filter();
        ResolveMarks();
    }

    public static void Close()
    {
        if (IsOpen) closedTick = Main.GameUpdateCount;
        queuedQuery=null;
        queryKeysReady=false;
        IsOpen = false;
        pressedMark = null;
        draggingMark = false;
        catalogWheelRemainder = 0;
        stationClick = null;
        EndTyping();
        hovered = default;
        hoveredBookmarkKey = null;
    }

    private static void EndTyping()
    {
        if (!IsTyping) return;
        RememberSearch();
        recalled = -1;
        IsTyping = false;
        suggestionQuery = null;
        PlayerInput.WritingText = false;
        Main.instance.HandleIME();
        Main.clrInput();
    }

    public static void Reset()
    {
        Close();
        docked = false;
        heldAtUpdate = false;
        cheatEnabled = false;
        closedTick = null;
        index = null;
        suggestionCatalog = null;
        suggestionQuery = null;
        suggestions = [];
        view = default;
        results.Clear();
        pageRows = null;
        pageStarts.Clear();
        cardNotes.Clear();
        wrappedLines.Clear();
        firstVisibleKey = pendingAnchor = null;
        history.Clear();
        recent.Clear();
        marks.Clear();
        previousGroups=null;
        groupMarks.Clear();
        previousGoals=null;
        orderGoals.Clear();
        hiddenGoals.Clear();
        available.Clear();
        countsTick = null;
        scroll.Clear();
        ingredientCycleStart = Environment.TickCount64;
        alternatives.Clear();
        expandedStations.Clear();
        displayItems.Clear();
        failedInventoryDraws.Clear();
        catalog = Array.Empty<int>();
        catalogPage = markPage = stationScroll = 0;
        showMarks = true;
        showHistory = bookmarksFirst = false;
        search = "";
    }

    public static void Update()
    {
        heldAtUpdate = !Main.mouseItem.IsAir;
        if (heldAtUpdate) Close();
        RefreshOrderGoals();
        bool hasHost = InventoryOverview.Active || TerminalWidth?.Invoke() > 0 && !Main.LocalPlayer.dead;
        if (hasHost != docked)
        {
            docked = hasHost;
            if (docked)
            {
                index = AdaptiveBrowserSystem.EnsureIndex();
                Filter();
                ResolveMarks();
                previousLeft = Main.mouseLeft;
                previousRight = Main.mouseRight;
                previousMiddle = Main.mouseMiddle;
                previousWheel = Mouse.GetState().ScrollWheelValue;
            }
            else Close();
        }
        if (!IsOpen) return;
        if (Main.gameMenu || Main.LocalPlayer.dead ||
            (Main.keyState.IsKeyDown(Keys.Escape) && Main.oldKeyState.IsKeyUp(Keys.Escape))) { Close(); return; }
        Main.LocalPlayer.mouseInterface = true;
        PlayerInput.LockVanillaMouseScroll("JustEnoughItemsTerraria");
        // The fullscreen map handles wheel input while Terraria skips drawing UI layers.
        if (Main.mapFullscreen)
        {
            previousWheel = Mouse.GetState().ScrollWheelValue;
            catalogWheelRemainder = 0;
        }
        if (IsTyping) PlayerInput.WritingText = true;
        if (lastDifficulty != BrowserIndex.CurrentDifficulty)
        {
            view = view with { Page = 0 };
            ResolveMarks();
            Refresh();
        }
        else if (Main.GameUpdateCount - lastRefresh >= 120 &&
            results.ContainsKey(view.Category))
        {
            results[view.Category] = Query(view.Category);
            lastRefresh = Main.GameUpdateCount;
        }
    }

    public static void OpenItem(int type, bool uses = false)
    {
        if (!InputBlocked && type > 0) Open(new(type), uses);
    }

    private static void Open(Subject target, bool uses)
    {
        index ??= AdaptiveBrowserSystem.EnsureIndex();
        if (!index.HasRows(target.Id, target.Npc, uses)) return;
        Activate();
        EndTyping();
        var category = PreferredCategory(target, uses) ?? view.Category;
        Select(new(target, uses, category, 0), true);
    }

    private static BrowserCategory? PreferredCategory(Subject target, bool uses)
    {
        if (target.Npc) return null;
        if (uses) return ItemID.Sets.BossBag[target.Id] ? BrowserCategory.Bag : ItemID.Sets.IsFishingCrate[target.Id] ? BrowserCategory.Crate : null;
        if (ItemID.Sets.IsFishingCrate[target.Id]) return BrowserCategory.Fishing;
        if (index.PrefersDropSource(target.Id)) return BrowserCategory.Drop;
        return index.HasHarvestSource(target.Id) ? BrowserCategory.Harvest : null;
    }

    private static BrowserCategory[] Categories =>
        PreferredCategory(view.Target, view.Uses) switch {
            BrowserCategory.Bag => bagUseOrder,
            BrowserCategory.Crate => crateUseOrder,
            BrowserCategory.Drop => npcSourceOrder,
            BrowserCategory.Harvest => harvestSourceOrder,
            _ => defaultOrder
        };

    internal static bool? queuedQuery;
    internal static void HandleQueryBinding(bool uses)
    {
        if (InputBlocked) return;
        if (Visible) queuedQuery=uses;
        else QueryHovered(uses);
    }

    private static int HostItem => InventoryOverview.Active ? (Main.HoverItem?.type ?? 0) : (TerminalItem?.Invoke() ?? 0);

    public static void QueryHovered(bool uses)
    {
        if (InputBlocked) return;
        Subject target = Visible ? hovered : Main.HoverItem is { IsAir:false } item ? new(item.type) : default;
        if(Visible && !IsOpen && target.Id==0)target=new(HostItem);
        if(target.Id==0)return;
        Open(target, uses && !target.Npc);
    }

    public static void BookmarkHovered()
    {
        if (InputBlocked || IsTyping) return;
        Subject target = Visible ? (docked && !IsOpen && hovered.Id == 0 ? new(HostItem) : hovered) : Main.HoverItem is { IsAir: false } item ? new(item.type) : default;
        if (target.Id == 0) return;
        string key = target.Npc ? "n:" + BrowserKeys.NpcKey(target.Id) : "i:" + BrowserKeys.ItemKey(target.Id);
        ToggleMark(Visible && !string.IsNullOrEmpty(hoveredBookmarkKey) ? hoveredBookmarkKey : key);
    }

    public static void Back() => NavigateWithClick(ReturnToHistory);

    private static void ReturnToHistory()
    {
        if (InputBlocked || IsTyping || !IsOpen) return;
        if (history.Count == 0)
        {
            if (docked) Close();
            return;
        }
        var last = history[^1];
        history.RemoveAt(history.Count - 1);
        Select(last, false);
    }

    private static void Select(View next, bool remember)
    {
        catalogWheelRemainder = 0;
        if (remember && view.Target.Id != 0 && view != next)
        {
            if (history.Count == 80) history.RemoveAt(0);
            history.Add(view with { Anchor = firstVisibleKey });
        }
        view = next with { Anchor = null };
        pendingAnchor = next.Anchor;
        firstVisibleKey = null;
        Refresh();
        recent.RemoveAll(m => m.Subject == next.Target);
        recent.Insert(0, new(next.Target, ""));
        historyScroll = 0;
        if (recent.Count > 80) recent.RemoveAt(80);
    }

    private static void Refresh()
    {
        lastDifficulty = BrowserIndex.CurrentDifficulty;
        results.Clear();
        foreach (var category in Categories)
        {
            var rows = Query(category);
            if (rows.Length == 0) continue;
            results.Add(category, rows);
        }
        if (results.Count > 0 && !results.ContainsKey(view.Category))
            view = view with { Category = results.Keys.First(), Page = 0 };
        scroll.Clear();
        ingredientCycleStart = Environment.TickCount64;
        alternatives.Clear();
        expandedStations.Clear();
        stationClick = null;
        stationScroll = 0;
        lastRefresh = Main.GameUpdateCount;
    }

    private static BrowserRow[] Query(BrowserCategory category) => index.QueryOrdered(
        view.Target.Id, view.Target.Npc, view.Uses, category,
        bookmarksFirst ? static r => BrowserPreferences.Bookmarks.Contains("r:" + r.Key) : null);

    internal static string QueryText => search;
    private static HashSet<int> searchResults = new();
    internal static bool QueryMatches(int type) => search.Length == 0 || searchResults.Contains(type);
    internal static void SetQueryText(string value)
    {
        if (search == value) return;
        search = value;
        searchSelection.Focus(search);
        catalogPage = 0;
        if (index != null) Filter();
    }

    private static void Filter()
    {
        catalogWheelRemainder = 0;
        catalog = index.CatalogFilters.Filter(index.CatalogFilters.OrderedItems, search);
        searchResults = search.Length == 0 ? new() : catalog.ToHashSet();
    }

    internal static Func<(string Ticket,int Product,int Type,int Have,int Need)[]> ReadOrderGroups;
    internal static Action<string[]> MoveOrderGroups;
    internal static Action<string> CancelBookmarkOrder;
    private static (string Ticket,int Product,int Type,int Have,int Need)[] previousGroups;
    private static readonly Dictionary<string,List<Mark>> groupMarks=new();

    private static void RefreshGroups()
    {
        if(ReadOrderGroups==null)return;
        var rows=ReadOrderGroups();
        if(ReferenceEquals(previousGroups,rows))return;
        if(previousGroups!=null && rows.SequenceEqual(previousGroups)){previousGroups=rows;return;}
        previousGroups=rows;
        var existing=groupMarks.Keys.ToHashSet();
        groupMarks.Clear();
        foreach(var row in rows){
            string group="g:"+row.Ticket;
            if(!groupMarks.TryGetValue(group,out var marksInGroup))groupMarks[group]=marksInGroup=new();
            if(row.Type>0)marksInGroup.Add(new(new(row.Type),group+"/"+BrowserKeys.ItemKey(row.Type),null,group,row.Product,row.Have,row.Need));
        }
        var layout=BrowserPreferences.Bookmarks;
        bool changed=layout.RemoveAll(key=>existing.Contains(key) && !groupMarks.ContainsKey(key))>0;
        var order=rows.Where(r=>r.Type==0).Select(r=>"g:"+r.Ticket).ToArray();
        var added=order.Where(key=>!layout.Contains(key)).ToArray();
        if(added.Length>0){
            layout.InsertRange(BrowserPreferences.NewBookmarksFirst?0:layout.Count,added);
            showMarks=true;markPage=BrowserPreferences.NewBookmarksFirst?0:int.MaxValue;
            changed=true;
        }
        // Keep the character's saved priority when the bookmark file predates it.
        int rank=0;
        for(int i=0;i<layout.Count;i++)if(groupMarks.ContainsKey(layout[i])){
            string expected=order[rank++];
            if(layout[i]!=expected){layout[i]=expected;changed=true;}
        }
        hiddenGoals.RemoveWhere(key=>key.StartsWith("g:") && !groupMarks.ContainsKey(key.Split('/')[0]));
        if(changed)BrowserPreferences.Save();
        if(index!=null)ResolveMarks();
    }

    internal static Func<(int Type,int Need)[]> OrderGoals;
    private static (int Type,int Need)[] previousGoals;
    private static readonly Dictionary<string,int> orderGoals=new();
    private static readonly HashSet<string> hiddenGoals=new();

    private static void RefreshOrderGoals()
    {
        RefreshGroups();
        if(OrderGoals==null)return;
        var goals=OrderGoals();
        if(ReferenceEquals(goals,previousGoals))return;
        // Old AE versions persisted tracked quantities in the manual bookmark file.
        if(BrowserPreferences.Goals.Count>0){
            BrowserPreferences.Bookmarks.RemoveAll(BrowserPreferences.Goals.ContainsKey);
            BrowserPreferences.Goals.Clear();
            BrowserPreferences.Save();
        }
        if(previousGoals!=null && goals.SequenceEqual(previousGoals)){previousGoals=goals;return;}
        previousGoals=goals;
        var keys=goals.ToDictionary(row=>"i:"+BrowserKeys.ItemKey(row.Type),row=>row.Need);
        if(ReadOrderGroups==null && keys.Keys.Any(key=>!orderGoals.ContainsKey(key))){showMarks=true;markPage=0;}
        orderGoals.Clear();
        foreach(var pair in keys)orderGoals.Add(pair.Key,pair.Value);
        hiddenGoals.RemoveWhere(key=>!key.StartsWith("g:") && !orderGoals.ContainsKey(key));
        countsTick=null;
        if(index!=null)ResolveMarks();
    }

    internal static Func<Dictionary<int,int>> AvailableCounts;
    private static Dictionary<int,int> available = new();
    private static ulong? countsTick;

    internal static void BookmarkMissing((int Type,int Need)[] rows)
    {
        if (rows.Length == 0) return;
        var keys = new List<string>();
        foreach (var (type,need) in rows)
        {
            string key = "i:" + BrowserKeys.ItemKey(type);
            if (!keys.Contains(key)) keys.Add(key);
            BrowserPreferences.Goals[key] = need;
        }
        BrowserPreferences.Bookmarks.RemoveAll(new HashSet<string>(keys).Contains);
        BrowserPreferences.Bookmarks.InsertRange(0, keys);
        BrowserPreferences.Save();
        countsTick = null;
        markPage = 0;
        showMarks = true;
        if (index != null) ResolveMarks();
    }

    private static void ToggleMark(string key)
    {
        if(CancelBookmarkOrder!=null && key.StartsWith("g:")){
            int separator=key.IndexOf('/');
            string group=separator<0?key:key[..separator];
            var keys=Keyboard.GetState();
            if(groupMarks.TryGetValue(group,out var members) && (separator<0 || keys.IsKeyDown(Keys.LeftShift) ||
                keys.IsKeyDown(Keys.RightShift) || members.All(m=>m.Key==key || hiddenGoals.Contains(m.Key)))){
                CancelBookmarkOrder(group[2..]);RefreshGroups();return;
            }
        }
        if(key.StartsWith("g:") || ReadOrderGroups==null && orderGoals.ContainsKey(key) && !BrowserPreferences.Bookmarks.Contains(key)){
            if(!hiddenGoals.Add(key))hiddenGoals.Remove(key);
            if(index!=null)ResolveMarks();
            return;
        }
        BrowserPreferences.Goals.Remove(key);
        if (!BrowserPreferences.Bookmarks.Remove(key))
        {
            if (BrowserPreferences.NewBookmarksFirst) BrowserPreferences.Bookmarks.Insert(0, key);
            else BrowserPreferences.Bookmarks.Add(key);
            markPage = BrowserPreferences.NewBookmarksFirst ? 0 : int.MaxValue;
            showMarks = true;
        }
        BrowserPreferences.Save();
        if (!Visible) return;
        ResolveMarks();
        if (bookmarksFirst && IsOpen) Refresh();
    }

    private static Mark RecipeMark(string key, BrowserRow row)
    {
        var target = row.IsDecraft || row.BagDetails != null ? new Subject(row.Inputs[0].ItemId) :
            row.Outputs.Length > 0 ? new Subject(row.Outputs[0].ItemId) :
            row.SpawnedNpc is int npc ? new Subject(npc, true) :
            row.Inputs.Length > 0 ? new Subject(row.Inputs[0].ItemId) : new Subject(row.Npc ?? 0, true);
        return new(target, key, row);
    }

    private static void ResolveMarks()
    {
        marks.Clear();
        IEnumerable<string> layout=BrowserPreferences.Bookmarks;
        if(ReadOrderGroups==null)layout=orderGoals.Keys.Where(key=>!hiddenGoals.Contains(key)).Union(layout);
        foreach (string key in layout)
        {
            if(groupMarks.TryGetValue(key,out var group)){marks.AddRange(group.Where(m=>!hiddenGoals.Contains(m.Key)));continue;}
            if (key.StartsWith("i:") && BrowserKeys.TryItem(key[2..], out int id))
                marks.Add(new(new(id), key));
            else if (key.StartsWith("n:") && BrowserKeys.TryNpc(key[2..], out int npc))
                marks.Add(new(new(npc, true), key));
            else if (key.StartsWith("r:") && index.TryRow(key[2..], out var row) &&
                (row.Difficulties & BrowserIndex.CurrentDifficulty) != 0)
                marks.Add(RecipeMark(key, row));
        }
    }

    private static void OpenMark(Mark mark)
    {
        if (mark.Recipe is not { } row) { Open(mark.Subject, mark.Subject.Npc); return; }
        bool uses = row.IsDecraft || row.BagDetails != null || row.Outputs.Length == 0 && row.SpawnedNpc == null;
        Activate();
        EndTyping();
        Select(new(mark.Subject, uses, row.Category, 0, row.Key), true);
    }
}
