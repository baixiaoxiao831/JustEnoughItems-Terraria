using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

internal static class FishingOdds
{
    private delegate void ReadPond(int x, int y, out bool lava, out bool honey, out int tiles, out int chum);
    private static readonly ReadPond Pond = typeof(Projectile).GetMethod("GetFishingPondState",
        BindingFlags.NonPublic | BindingFlags.Static)?.CreateDelegate<ReadPond>();

    private static double Roll(int power, int numerator, int minimum) =>
        1d / Math.Max(minimum, numerator / Math.Max(1, power));

    private static string Percent(double value) => (100 * value).ToString("0.##") + "%";

    internal static string[] Describe(BrowserRow row)
    {
        var player = Main.LocalPlayer;
        var conditions = player.GetFishingConditions();
        int power = conditions.FinalFishingLevel;
        var lines = new List<string>();
        if (power <= 0) {
            lines.Add("渔力 0");
            lines.Add(conditions.BaitItemType == ItemID.TruffleWorm ? "松露虫用于召唤鱼龙公爵" :
                conditions.PolePower <= 0 ? "需要鱼竿" : conditions.BaitPower <= 0 ? "需要鱼饵" : "当前渔力为零");
            return lines.ToArray();
        }
        Projectile bobber = null;
        foreach (var projectile in Main.ActiveProjectiles) {
            if (projectile.owner == player.whoAmI && projectile.bobber && projectile.wet) {
                bobber = projectile;
                break;
            }
        }
        bool lava = false, honey = false;
        bool measured = bobber != null && Pond != null && WorldGen.InWorld((int)bobber.Center.X / 16, (int)bobber.Center.Y / 16, 12);
        if (measured) {
            Pond((int)bobber.Center.X / 16, (int)bobber.Center.Y / 16, out lava, out honey, out int tiles, out int chum);
            float size = Main.maxTilesX / 4200f;
            float height = (float)((bobber.position.Y / 16f - (60f + 10f * size * size)) / (Main.worldSurface / 6));
            int needed = (int)(300 * Math.Clamp(height, .25f, 1f));
            power += chum >= 3 ? 20 : chum == 2 ? 17 : chum == 1 ? 11 : 0;
            power = (int)(power * Math.Min(1f, tiles / (float)needed));
            lines.Add($"渔力 {power} · 浮标水量 {tiles}/{needed}");
            if (tiles < 75 || power <= 0) {
                lines.Add("钓点水量或渔力不足 · 无法出匣");
                return lines.ToArray();
            }
        } else lines.Add($"渔力 {power} · 按足量池水、无鱼饵桶估算");

        bool pearl = ModLoader.TryGetMod("CalamityMod", out var calamity) &&
            calamity.TryFind<ModPlayer>("CalamityPlayer", out var prototype) &&
            prototype.GetType().GetField("enchantedPearl")?.GetValue(player.GetModPlayer(prototype)) is true;
        double crate = player.cratePotion ? .25 : .1;
        if (pearl) {
            crate += (1 - crate) / 6;
            lines.Add("已计入灾厄珍珠饰品效果");
        }
        // Values are conditional on entering the catch roll. Luck varies the power of each roll.
        int low = player.luck < 0 ? Math.Max(1, (int)(power * .6)) : power;
        int high = player.luck > 0 ? Math.Max(power, (int)Math.Ceiling(power * 1.4) - 1) : power;
        string Range(Func<int,double> probability) {
            double min = probability(low), max = min;
            for (int p = Math.Min(low, 4500) + 1; p <= Math.Min(high, 4500); p++) {
                double value = probability(p);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
            string from = Percent(min), to = Percent(max);
            return from == to ? from : from + "–" + to;
        }
        double Rare(int p) => Roll(p, pearl ? 840 : 1050, 4);
        double Uncommon(int p) => Roll(p, pearl ? 240 : 300, 3);
        double HighTier(int p) => 1 - (1 - Roll(p, pearl ? 1800 : 2250, 5)) * (1 - Roll(p, 4500, 6));
        lines.Add($"匣子判定 {Percent(crate)} · 匣内稀有 {Range(Rare)}");
        if (low != high) lines.Add("区间含幸运波动");
        int id = row.Outputs[0].ItemId;
        bool known = row.Conditions.Length >= 2;
        if (!known) { lines.Add("此模组匣的最终判定未知"); return lines.ToArray(); }
        bool wrongMode = (row.Conditions[0] == "困难模式") != Main.hardMode;
        bool needsLava = row.Conditions[1].Contains("熔岩");
        bool canLava = player.accLavaFishing || ItemID.Sets.CanFishInLava[conditions.PoleItemType] ||
            ItemID.Sets.IsLavaBait[conditions.BaitItemType];
        if (wrongMode || (needsLava && !canLava) || (measured && (needsLava != lava || honey && !lava && !Main.notTheBeesWorld))) {
            lines.Add("此匣 0% · " + (wrongMode ? "世界阶段不符" : needsLava && !canLava ? "缺少熔岩垂钓能力" : "浮标液体不符"));
            return lines.ToArray();
        }
        // The rare roll checks these habitats in game order. Special seeds can relax a check.
        double remaining = 1, habitat = 0;
        void Zone(int normal, int hard, double weight) {
            if (id == normal || id == hard) habitat += remaining * weight;
            remaining *= 1 - weight;
        }
        double y = (bobber?.Center.Y ?? player.Center.Y) / 16f;
        bool sky = y < Main.worldSurface * .5;
        bool evil = !Main.remixWorld || !sky;
        Zone(ItemID.DungeonFishingCrate, ItemID.DungeonFishingCrateHard, player.ZoneDungeon && NPC.downedBoss3 ? 1 : 0);
        Zone(ItemID.OceanCrate, ItemID.OceanCrateHard, player.ZoneBeach ? 1 :
            Main.remixWorld && y >= Main.rockLayer && y < Main.maxTilesY - 300 ? .25 : 0);
        bool corrupt = player.ZoneCorrupt && evil, crimson = player.ZoneCrimson && evil;
        Zone(ItemID.CorruptFishingCrate, ItemID.CorruptFishingCrateHard, corrupt ? (crimson ? .5 : 1) : 0);
        Zone(ItemID.CrimsonFishingCrate, ItemID.CrimsonFishingCrateHard, crimson ? 1 : 0);
        Zone(ItemID.HallowedFishingCrate, ItemID.HallowedFishingCrateHard, player.ZoneHallow ? 1 : 0);
        Zone(ItemID.JungleFishingCrate, ItemID.JungleFishingCrateHard,
            player.ZoneJungle ? (Main.notTheBeesWorld && !Main.remixWorld ? .5 : 1) : 0);
        Zone(ItemID.FrozenCrate, ItemID.FrozenCrateHard, player.ZoneSnow ? 1 : 0);
        Zone(ItemID.OasisCrate, ItemID.OasisCrateHard, player.ZoneDesert ? 1 : 0);
        Zone(ItemID.FloatingIslandFishingCrate, ItemID.FloatingIslandFishingCrateHard, sky ? 1 : 0);
        double Ordinary(int p) => crate * (1 - Rare(p) * (1 - remaining));
        string rate = id switch {
            ItemID.WoodenCrate or ItemID.WoodenCrateHard => Range(p => Ordinary(p) * (1 - HighTier(p)) * (1 - Uncommon(p))),
            ItemID.IronCrate or ItemID.IronCrateHard => Range(p => Ordinary(p) * (1 - HighTier(p)) * Uncommon(p)),
            ItemID.GoldenCrate or ItemID.GoldenCrateHard => Range(p => Ordinary(p) * HighTier(p)),
            ItemID.LavaCrate or ItemID.LavaCrateHard => Percent(crate / 6),
            _ => Range(p => crate * Rare(p) * (id < ItemID.Count ? habitat : 1))
        };
        lines.Add((id < ItemID.Count ? "当前环境目标判定 " : "满足下列群落时 ") + rate);
        lines.Add("每次进入物品分支计，未扣垃圾、敌怪和模组截获。");
        return lines.ToArray();
    }
}
