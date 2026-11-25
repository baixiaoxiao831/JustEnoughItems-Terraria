using System;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace JustEnoughItemsTerraria;

[Autoload(Side = ModSide.Client)]
public sealed class InventoryOverview : ModSystem
{
    internal static bool Active => !Main.gameMenu && Main.playerInventory && Main.recBigList &&
        !Main.LocalPlayer.dead && !Main.ingameOptionsWindow && !Main.mapFullscreen &&
        !Main.InGuideCraftMenu && !Main.InReforgeMenu && !Main.LocalPlayer.tileEntityAnchor.InUse &&
        !Main.LocalPlayer.creativeInterface && !BrowserUI.GuideActive && !(BrowserUI.TerminalWidth?.Invoke() > 0);

    private static int highlightedRecipe = -1;
    internal static bool BackpackAvailable => Main.playerInventory && !Main.gameMenu && !Main.LocalPlayer.dead &&
        !Main.InGuideCraftMenu && !Main.InReforgeMenu && !Main.LocalPlayer.tileEntityAnchor.InUse &&
        !Main.LocalPlayer.creativeInterface && !BrowserUI.GuideActive && !(BrowserUI.TerminalWidth?.Invoke() > 0);

    internal static bool SelectRecipe(int recipe, bool commit)
    {
        if (!BackpackAvailable) return false;
        if (commit) Recipe.FindRecipes();
        int position = Array.IndexOf(Main.availableRecipe, recipe, 0, Main.numAvailableRecipes);
        if (position < 0) return false;
        if (commit)
        {
            Main.focusRecipe = position;
            Main.recFastScroll = true;
            Main.recBigList = false;
            highlightedRecipe = recipe;
            Main.mouseLeftRelease = false;
            BrowserUI.Close();
        }
        return true;
    }

    private static bool HighlightSelected => highlightedRecipe >= 0 && Main.playerInventory &&
        !Main.recBigList && Main.focusRecipe >= 0 && Main.focusRecipe < Main.numAvailableRecipes &&
        Main.availableRecipe[Main.focusRecipe] == highlightedRecipe;

    public override void UpdateUI(GameTime gameTime)
    {
        if (!HighlightSelected) highlightedRecipe = -1;
    }

    public override void OnWorldUnload() => highlightedRecipe = -1;

    // Both native callers compute columns in UI coordinates. Leave at least one column.
    internal static int RightMargin(int uiWidth) =>
        Math.Max(280, Math.Min((int)Math.Ceiling(uiWidth * .25), uiWidth - 352));

    private static void ReserveCatalogSpace(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdcI4(280), i => i.OpCode == OpCodes.Sub))
        {
            ModContent.GetInstance<JustEnoughItemsTerraria>().Logger.Info(
                $"Keeping the existing recipe grid margin in {il.Method.Name} because its layout has changed.");
            return;
        }
        cursor.Index--; // insert after the margin constant, before subtraction
        cursor.EmitDelegate((int original) => Active ? RightMargin(Main.screenWidth) : original);
    }

    public override void Load()
    {
        IL_Main.DrawInventory += DrawBackpackAndRecipes;
        IL_Main.DrawInventory += ReserveCatalogSpace;
        IL_Main.DoUpdate_WhilePaused += ReserveCatalogSpace;
        On_GameInterfaceLayer.Draw += DrawLayer;
    }

    public override void PostSetupContent()
    {
        GuardHudMethod("SummonersAssociation", "SummonersAssociation.UISystem", "PostDrawInterface");
        GuardHudMethod("BossChecklist", "BossChecklist.Systems.BossLogSystem", "UpdateUI");
        if (ModLoader.TryGetMod("ImproveGame", out var improve))
        {
            var getter = improve.Code.GetType("ImproveGame.UI.RecipeSearch.RecipeSearchUI")?
                .GetProperty("Enabled")?.GetGetMethod();
            if (getter != null) MonoModHooks.Modify(getter, il => {
                var cursor = new ILCursor(il);
                var original = cursor.DefineLabel();
                cursor.EmitDelegate(() => Active);
                cursor.Emit(OpCodes.Brfalse, original);
                cursor.Emit(OpCodes.Ldc_I4_0);
                cursor.Emit(OpCodes.Ret);
                cursor.MarkLabel(original);
            });
        }
    }

    private static bool CoversHud => !Main.gameMenu && !Main.ingameOptionsWindow && !Main.mapFullscreen &&
        (Active || BrowserUI.Visible || BrowserUI.TerminalWidth?.Invoke() > 0);

    private static void GuardHudMethod(string modName, string typeName, string methodName)
    {
        if (!ModLoader.TryGetMod(modName, out var mod)) return;
        var method = mod.Code.GetType(typeName)?.GetMethod(methodName);
        if (method?.DeclaringType?.FullName != typeName || method.ReturnType != typeof(void)) return;
        MonoModHooks.Modify(method, il => {
            var cursor = new ILCursor(il);
            var original = cursor.DefineLabel();
            cursor.EmitDelegate(() => CoversHud);
            cursor.Emit(OpCodes.Brfalse, original);
            cursor.Emit(OpCodes.Ret);
            cursor.MarkLabel(original);
        });
    }

    private static bool DrawLayer(On_GameInterfaceLayer.orig_Draw original, GameInterfaceLayer layer)
    {
        if (CoversHud && layer.Name.StartsWith("BossChecklist:", StringComparison.Ordinal)) return true;
        if (BrowserUI.IsOpen && layer.Name == "Vanilla: Mouse Item / NPC Head" && Main.mouseItem.IsAir) return true;
        if (!Active) return original(layer);
        if (layer.Name is "Adaptive Recipe Browser: Query" or "Vanilla: Cursor" or
            "Vanilla: Inventory" or "Vanilla: Mouse Text" or "Vanilla: Mouse Item / NPC Head" ||
            layer.Name.StartsWith("Vanilla: Interface Logic ", StringComparison.Ordinal))
            return original(layer);
        return true;
    }

    private static void DrawBackpackAndRecipes(ILContext il)
    {
        var c = new ILCursor(il);
        c.GotoNext(MoveType.After, i => i.MatchCall<Main>("get_ShouldPVPDraw"));
        c.EmitDelegate((bool draw) => draw && !Active);

        // Equipment, map buttons and accessory slots share DrawInventory with the backpack.
        c.GotoNext(i => i.MatchLdsfld<Main>("mapEnabled"));
        int start = c.Index;
        c.GotoNext(MoveType.After, i => i.MatchCallvirt<AccessorySlotLoader>("DrawAccSlots"));
        var afterEquipment = c.MarkLabel();
        c.Index = start;
        c.MoveAfterLabels();
        c.EmitDelegate(() => Active);
        c.Emit(OpCodes.Brtrue, afterEquipment);

        // Skip the compact recipe strip and its toggle. The expanded grid follows this block.
        c.GotoNext(i => i.MatchLdsfld<Main>("hidePlayerCraftingMenu"));
        c.GotoNext(i => i.MatchLdsfld<Main>("InReforgeMenu"));
        var endCompact = (ILLabel)c.Next.Next.Operand;
        c.MoveAfterLabels();
        c.EmitDelegate(() => Active);
        c.Emit(OpCodes.Brtrue, endCompact);

        // Reuse the native gold recipe-slot background even for mouse players.
        c.GotoNext(MoveType.After, i => i.Operand is Mono.Cecil.MethodReference method &&
            method.Name == "get_HighlightThingsForMouse");
        c.EmitDelegate((bool nativeHighlight) => nativeHighlight || HighlightSelected);
    }
}
