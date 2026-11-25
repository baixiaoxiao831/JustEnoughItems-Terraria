using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace JustEnoughItemsTerraria;

public sealed class GuideBottle : ModItem
{
    public override string Texture => "JustEnoughItemsTerraria/Assets/GuideBottle";

    public override void SetDefaults()
    {
        Item.width = 22;
        Item.height = 28;
        Item.maxStack = Item.CommonMaxStack;
        Item.rare = ItemRarityID.Blue;
    }

    public override bool CanRightClick() => true;

    public override void ModifyItemLoot(ItemLoot loot)
    {
        loot.Add(ItemDropRule.Common(ItemID.Torch, 1, 30, 30));
        loot.Add(ItemDropRule.Common(ItemID.Rope, 1, 50, 50));
        loot.Add(ItemDropRule.Common(ItemID.LesserHealingPotion, 1, 5, 5));
        loot.Add(ItemDropRule.Common(ItemID.RecallPotion, 1, 3, 3));
    }
}

public sealed class GuideBottlePlayer : ModPlayer
{
    private readonly HashSet<string> visited = new();

    public override void OnEnterWorld()
    {
        var world = Main.ActiveWorldFileData.UniqueId;
        if (world == Guid.Empty || visited.Contains(world.ToString())) return;
        var bottle = new Item(ModContent.ItemType<GuideBottle>());
        var remainder = Player.GetItem(Player.whoAmI, bottle, GetItemSettings.InventoryUIToInventorySettingsShowAsNew);
        if (!remainder.IsAir)
            Player.QuickSpawnItem(Player.GetSource_Misc("JEIStarterGuide"), remainder.type, remainder.stack);
        visited.Add(world.ToString());
    }

    public override void SaveData(TagCompound tag) => tag["guideWorlds"] = visited.ToList();

    public override void LoadData(TagCompound tag)
    {
        visited.Clear();
        visited.UnionWith(tag.GetList<string>("guideWorlds"));
    }
}
