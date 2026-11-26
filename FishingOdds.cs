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
            lines.Add(JeiText.Get("FishingPower0"));
            lines.Add(conditions.BaitItemType == ItemID.TruffleWorm ? JeiText.Get("TruffleWormsSummonDukeFishron") :
                conditions.PolePower <= 0 ? JeiText.Get("RequiresAFishingPole") : conditions.BaitPower <= 0 ? JeiText.Get("RequiresBait") : JeiText.Get("CurrentFishingPowerIsZero"));
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
            lines.Add(JeiText.Get("FishingPowerPondSize", power, tiles, needed));
            if (tiles < 75 || power <= 0) {
                lines.Add(JeiText.Get("InsufficientPondSizeOrFishingPowerTo"));
                return lines.ToArray();
            }
        } else lines.Add(JeiText.Get("FishingPowerAssumesAFullPondAnd", power));

        bool pearl = ModLoader.TryGetMod("CalamityMod", out var calamity) &&
            calamity.TryFind<ModPlayer>("CalamityPlayer", out var prototype) &&
            prototype.GetType().GetField("enchantedPearl")?.GetValue(player.GetModPlayer(prototype)) is true;
        double crate = player.cratePotion ? .25 : .1;
        if (pearl) {
            crate += (1 - crate) / 6;
            lines.Add(JeiText.Get("IncludesTheCalamityPearlAccessoryBonus"));
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
        lines.Add(JeiText.Get("CrateRollRareRollWithinCrates", Percent(crate), Range(Rare)));
        if (low != high) lines.Add(JeiText.Get("RangeIncludesLuckVariation"));
        int id = row.Outputs[0].ItemId;
        bool known = row.Conditions.Length >= 2;
        if (!known) { lines.Add(JeiText.Get("FinalCatchChanceForThisModdedCrate")); return lines.ToArray(); }
        bool wrongMode = (row.Conditions[0] == JeiText.Get("Hardmode")) != Main.hardMode;
        bool needsLava = row.LavaCrate;
        bool canLava = player.accLavaFishing || ItemID.Sets.CanFishInLava[conditions.PoleItemType] ||
            ItemID.Sets.IsLavaBait[conditions.BaitItemType];
        if (wrongMode || (needsLava && !canLava) || (measured && (needsLava != lava || honey && !lava && !Main.notTheBeesWorld))) {
            lines.Add(JeiText.Get("ThisCrate0") + (wrongMode ? JeiText.Get("WrongWorldProgression") : needsLava && !canLava ? JeiText.Get("LavaFishingAbilityRequired") : JeiText.Get("BobberIsInTheWrongLiquid")));
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
        lines.Add((id < ItemID.Count ? JeiText.Get("ChanceInThisEnvironment") : JeiText.Get("ChanceInTheListedBiome")) + rate);
        lines.Add(JeiText.Get("PerItemRollBeforeJunkEnemiesAnd"));
        return lines.ToArray();
    }
}
