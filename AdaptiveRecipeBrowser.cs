using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace JustEnoughItemsTerraria;

public sealed class JustEnoughItemsTerraria : Mod
{
    public JustEnoughItemsTerraria() => PreviousNameMigration.CopySettings();
    internal static ModKeybind Toggle, Sources, Uses, Bookmark, Back;

    public override void HandlePacket(System.IO.BinaryReader reader, int whoAmI)
    {
        if (!BrowserUI.CheatsAllowed || reader.BaseStream.Length - reader.BaseStream.Position < 8) return;
        int id = reader.ReadInt32(), count = reader.ReadInt32();
        if (id <= 0 || !Terraria.ID.ContentSamples.ItemsByType.TryGetValue(id, out var sample) ||
            sample.IsAir || sample.type != id || count <= 0 || count > sample.maxStack) return;
        if (Main.netMode == Terraria.ID.NetmodeID.Server) {
            if (whoAmI < 0 || whoAmI >= Main.maxPlayers || !Main.player[whoAmI].active) return;
            var grant = GetPacket();
            grant.Write(id);
            grant.Write(count);
            grant.Send(whoAmI);
        } else if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) {
            BrowserUI.ReceiveItems(new Item(id), count);
        }
    }

    internal HashSet<(int Boss,int Item)> DiscoveredSummons;
    internal readonly HashSet<(int Boss,int Item)> RegisteredSummons = new();

    public override object Call(params object[] args)
    {
        if (args is ["RegisterBossSummon", int npcType, int itemType] && npcType > 0 && itemType > 0)
        {
            if (RegisteredSummons.Add((npcType,itemType)) && AdaptiveBrowserSystem.Index != null) AdaptiveBrowserSystem.Invalidate();
            return true;
        }
        if (args is ["ItemHoverDetails"]) return (Func<Item,string>)EquipmentPreview.HoverDetails;
        if (args is ["RegisterBenchMaterials", Func<int,(int,bool)[]> status]) { BrowserUI.BenchMaterials=status; return true; }
        if(args is ["RegisterRecipeBench", Func<int,bool,bool> bench]) { BrowserUI.RecipeBench=bench;return true; }
        if (args is ["TerminalSearch"])
            return ((Func<string>)(()=>BrowserUI.QueryText), (Action<string>)BrowserUI.SetQueryText, (Func<int,bool>)BrowserUI.QueryMatches);
        if (args is ["RegisterTerminalGuide", Func<bool> isOpen])
        {
            BrowserUI.TerminalGuideOpen = isOpen;
            return true;
        }
        if(args is ["RegisterOrderBookmarkCancel",Action<string> cancel]){
            BrowserUI.CancelBookmarkOrder=cancel;return true;
        }
        if(args is ["RegisterOrderBookmarkGroups",Func<(string,int,int,int,int)[]> groups,Action<string[]> reorder]){
            BrowserUI.ReadOrderGroups=groups;
            BrowserUI.MoveOrderGroups=reorder;
            return (Func<bool>)(()=>BrowserPreferences.NewBookmarksFirst);
        }
        if (args is ["RegisterOrderBookmarks",Func<(int,int)[]> goals,Func<Dictionary<int,int>> stock])
        {
            BrowserUI.OrderGoals=goals;
            BrowserUI.AvailableCounts=stock;
            return true;
        }
        if (args is ["RegisterMissingBookmarks", Func<Dictionary<int,int>> counts])
        {
            BrowserUI.AvailableCounts = counts;
            return (Action<(int,int)[]>)BrowserUI.BookmarkMissing;
        }
        if (args is ["RegisterTerminalMapExit", Action leave]) { BrowserUI.LeaveTerminalForMap = leave; return true; }
        if (args is ["RegisterTerminalOrders", Func<int,bool> order])
        {
            BrowserUI.OrderItem = order;
            return true;
        }
        if (args is ["RegisterTerminalSidebars", Func<int> width, Func<int> item, Func<bool> toggle])
        {
            BrowserUI.TerminalWidth = width;
            BrowserUI.TerminalItem = item;
            BrowserUI.TerminalToggle = toggle;
            return ((Func<bool>)(() => BrowserUI.IsOpen), (Func<bool>)(() => BrowserUI.IsOpen || BrowserUI.IsTyping || BrowserUI.ClosedThisTick));
        }
        return base.Call(args);
    }

    public override void Load()
    {
        if (Main.dedServ) return;
        Toggle = KeybindLoader.RegisterKeybind(this, "ToggleBrowser", Keys.O);
        Sources = KeybindLoader.RegisterKeybind(this, "ShowSources", Keys.R);
        Uses = KeybindLoader.RegisterKeybind(this, "ShowUses", Keys.U);
        Bookmark = KeybindLoader.RegisterKeybind(this, "Bookmark", Keys.A);
        Back = KeybindLoader.RegisterKeybind(this, "Back", Keys.Back);
        BrowserTexturePreload.LoadSkins(this);
        BrowserFont.Load(this);
        BrowserSearch.Load(this);
        BrowserPreferences.Load();
    }

    public override void Unload()
    {
        BrowserUI.TerminalGuideOpen = null;
        BrowserUI.TerminalWidth = null;
        BrowserUI.LeaveTerminalForMap = null;
        BrowserUI.OrderItem = null;
        BrowserUI.RecipeBench = null;
        BrowserUI.BenchMaterials = null;
        BrowserUI.ReadOrderGroups=null;
        BrowserUI.MoveOrderGroups=null;BrowserUI.CancelBookmarkOrder=null;
        BrowserUI.OrderGoals = null;
        BrowserUI.AvailableCounts = null;
        BrowserUI.TerminalItem = null;
        BrowserUI.TerminalToggle = null;
        Toggle = Sources = Uses = Bookmark = Back = null;
        BrowserTexturePreload.Unload();
        if (!Main.dedServ)
        {
            if (ReLogic.Content.AssetRepository.IsMainThread) BrowserFont.Unload();
            else Main.RunOnMainThread(BrowserFont.Unload).GetAwaiter().GetResult();
        }
        BrowserSearch.Unload();
        BrowserPreferences.Bookmarks.Clear();
    }
}

public sealed class AdaptiveBrowserSystem : ModSystem
{
    public static BrowserIndex Index { get; private set; }

    // First use is after content registration and recipe edits by every loaded mod.
    public static BrowserIndex EnsureIndex() => Index ??= BrowserIndex.Build();

    internal static void Invalidate()
    {
        NpcMapMarker.Clear();
        EquipmentPreview.Clear();
        Index = null;
        if (!Main.dedServ) BrowserUI.Reset();
    }

    public override void PostSetupRecipes()
    {
        Invalidate();
        BrowserSearch.IndexCurrentNames();
        if (!Main.dedServ) BrowserTexturePreload.LoadCatalog(Mod);
    }
    public override void OnLocalizationsLoaded() { Invalidate(); BrowserSearch.IndexCurrentNames(); }
    public override void OnWorldLoad() => Invalidate();
    public override void OnWorldUnload() => Invalidate();
    public override void Unload() => Invalidate();

    public override void UpdateUI(GameTime gameTime)
    {
        NpcMapMarker.Update();
        if (!Main.gameMenu && !BrowserUI.GuideActive) BrowserUI.Update();
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        if (!BrowserUI.Visible) return;
        // The vanilla inventory handles slot clicks in its draw layer. tML resets Active
        // each frame, so the open chest, held item and underlying inventory stay intact.
        if (BrowserUI.IsOpen)
        {
            foreach (var layer in layers)
            {
                if (layer.Name is "Vanilla: Inventory" or "Vanilla: Hotbar" or
                "Vanilla: Settings Button" or "Vanilla: Map / Minimap" or
                "Vanilla: Builder Accessories Bar" or "Vanilla: Radial Hotbars" or
                "Vanilla: Info Accessories Bar" or "Vanilla: NPC / Sign Dialog" or
                "Vanilla: Hair Window" or "Vanilla: Dresser Window" or
                "Vanilla: Town NPC House Banners" or "Vanilla: Mouse Text" or
                "Vanilla: Mouse Over" or "Vanilla: Interact Item Icon")
                    layer.Active = false;
            }
        }
        int at = layers.FindIndex(layer => layer.Name == "Vanilla: Cursor");
        layers.Insert(at >= 0 ? at : layers.Count, new LegacyGameInterfaceLayer(
            "Adaptive Recipe Browser: Query", () =>
            {
                if (!Main.gameMenu && BrowserUI.Visible) BrowserUI.Draw(Main.spriteBatch);
                return true;
            }, InterfaceScaleType.None));
    }
}

public sealed class BrowserInputPlayer : ModPlayer
{
    public override void OnEnterWorld()
    {
        // Build after the world, player and mod recipes exist, before the first sidebar draw.
        if (!Main.dedServ && Player.whoAmI == Main.myPlayer)
            AdaptiveBrowserSystem.EnsureIndex();
    }

    public override void SetControls()
    {
        if (!BrowserUI.IsOpen || Player.whoAmI != Main.myPlayer) return;
        Player.controlUseItem = false;
        Player.controlUseTile = false;
        Player.controlThrow = false;
    }

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        if (Main.dedServ || Main.gameMenu || BrowserUI.InputBlocked || BrowserUI.GuideActive || Main.drawingPlayerChat || Main.editSign ||
            Main.editChest || Main.blockInput || PlayerInput.WritingText || BrowserUI.IsTyping) return;

        if (JustEnoughItemsTerraria.Toggle.JustPressed)
        {
            if (BrowserUI.IsOpen || BrowserUI.TerminalToggle?.Invoke() != true) BrowserUI.Toggle();
        }
        else if (JustEnoughItemsTerraria.Sources.JustPressed) BrowserUI.HandleQueryBinding(false);
        else if (JustEnoughItemsTerraria.Uses.JustPressed) BrowserUI.HandleQueryBinding(true);
        else if (JustEnoughItemsTerraria.Bookmark.JustPressed) BrowserUI.BookmarkHovered();
        else if (BrowserUI.IsOpen && JustEnoughItemsTerraria.Back.JustPressed) BrowserUI.Back();
    }
}
