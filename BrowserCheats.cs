using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.UI;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

public static partial class BrowserUI
{
    private static bool cheatEnabled;
    internal static bool CheatsAllowed => ModContent.GetInstance<BrowserServerConfig>().AllowCheatItems;
    private static bool CheatActive => cheatEnabled && CheatsAllowed;
    internal static void DisableCheats() => cheatEnabled = false;

    private static void DrawWrench(float x, float width, float height)
    {
        var keys = Keyboard.GetState();
        bool ctrl = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        Button(x + width - SearchHeight, height - 6 - SearchHeight, SearchHeight, CheatActive ? "config_button_cheat" : "config_button",
            null, ctrl && CheatsAllowed ? ToggleCheats : null, dim: !CheatsAllowed);
    }

    private static void ToggleCheats()
    {
        if (CheatsAllowed) cheatEnabled = !cheatEnabled;
    }

    private static void GiveCatalogItem(int id, bool single)
    {
        if (InputBlocked || !CheatActive || single && Main.stackSplit > 1) return;
        var item = new Item(id);
        int count = single ? Math.Min(item.maxStack, Main.superFastStack + 1) : item.maxStack;
        count = Math.Min(count, CursorCapacity(item));
        if (count <= 0) return;
        if (single) ItemSlot.RefreshStackSplitCooldown();
        if (Main.netMode == NetmodeID.MultiplayerClient) {
            var packet = ModContent.GetInstance<JustEnoughItemsTerraria>().GetPacket();
            packet.Write(id);
            packet.Write(count);
            packet.Send();
            return;
        }
        ReceiveItems(item, count);
    }

    private static int CursorCapacity(Item item)
    {
        var held = Main.mouseItem;
        if (held.IsAir) return item.maxStack;
        if (held.type != item.type || held.stack >= held.maxStack || !ItemLoader.CanStack(held, item)) return 0;
        return held.maxStack - held.stack;
    }

    internal static void ReceiveItems(Item item, int count)
    {
        item.stack = Math.Min(count, CursorCapacity(item));
        if (item.stack <= 0) return;
        item.OnCreated(new JourneyDuplicationItemCreationContext());
        if (Main.mouseItem.IsAir) Main.mouseItem = item;
        else if (CursorCapacity(item) > 0) ItemLoader.StackItems(Main.mouseItem, item, out _);
    }
}
