using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace JustEnoughItemsTerraria;

internal static partial class PredicateVocabulary
{
    internal static string Published(string text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        var boss = Regex.Match(text, @"^After (?:the )?(.+?) (?:has|have) been defeated$", RegexOptions.IgnoreCase);
        if (boss.Success)
        {
            var names = ModLoader.Mods.Select(mod => Registries.GetValue(mod, owner => new RegistryNames(owner))
                    .Direct(Normalize(boss.Groups[1].Value))).Where(name => name != null).Distinct().Take(2).ToArray();
            if (names.Length == 1) return JeiText.Get("Defeated2") + names[0];
        }
        var zone = Regex.Match(text, @"^While in (?:the )?(.+?)(?: biome)?$", RegexOptions.IgnoreCase);
        if (zone.Success)
        {
            string area = zone.Groups[1].Value;
            string native = area.ToLowerInvariant() switch { "jungle" => JeiText.Get("Jungle"), "underworld" => JeiText.Get("Underworld"),
                "jungle lab" => JeiText.Get("JungleLab"), "underworld lab" => JeiText.Get("UnderworldLab"), _ => null };
            if (native != null) return JeiText.Get("In2") + native;
            string[] biomes = ModContent.GetContent<ModBiome>().Where(b => Normalize(b.Name) == Normalize(area) ||
                 Normalize(b.Name) == Normalize(area) + "BIOME").Select(b => b.DisplayName.Value).Distinct().Take(2).ToArray();
            if (biomes.Length == 1) return JeiText.Get("In2") + biomes[0];
        }
        return null;
    }

    internal static string Vanilla(IItemDropRuleCondition condition)
    {
        string moon = condition switch {
            Conditions.FrostMoonDropGateForTrophies => JeiText.Get("FrostMoon"),
            Conditions.PumpkinMoonDropGateForTrophies => JeiText.Get("PumpkinMoon"), _ => null
        };
        if (moon != null)
            return moon + JeiText.Get("Waves15161718And19");
        if (condition is Conditions.MechdusaKill)
            return JeiText.Get("BothRemixAndForTheWorthyWorld");
        if (condition is Conditions.NoPortalGun)
            return JeiText.Get("NotInThePlayerSInventory") + Lang.GetItemNameValue(ItemID.PortalGun);
        return null;
    }

    // Translate members by declaring type. Identical field names in other assemblies
    // need not describe the same game state.
    internal static string Member(MemberInfo member)
    {
        Type type = member?.DeclaringType;
        if (type == null) return null;
        string name = member.Name.StartsWith("get_", StringComparison.Ordinal) ? member.Name[4..] : member.Name;
        if (name.StartsWith('<') && name.EndsWith(">k__BackingField", StringComparison.Ordinal))
            name = name.Substring(1, name.IndexOf('>') - 1);
        if (type == typeof(Main)) return name switch {
            "hardMode" => JeiText.Get("Hardmode"), "expertMode" => JeiText.Get("ExpertMode"), "masterMode" => JeiText.Get("MasterMode"),
            "remixWorld" => JeiText.Get("RemixWorld"), "zenithWorld" => JeiText.Get("GetFixedBoiWorld"), "getGoodWorld" => JeiText.Get("ForTheWorthyWorld"),
            "drunkWorld" => JeiText.Get("DrunkWorld"), "tenthAnniversaryWorld" => JeiText.Get("Celebrationmk10World"),
            "notTheBeesWorld" => JeiText.Get("NotTheBeesWorld"), "dontStarveWorld" => JeiText.Get("TheConstantWorld"), "noTrapsWorld" => JeiText.Get("NoTrapsWorld"),
            "dayTime" => JeiText.Get("Daytime"), "raining" => JeiText.Get("Rain"), "bloodMoon" => JeiText.Get("BloodMoon"), "eclipse" => JeiText.Get("SolarEclipse"),
            "snowMoon" => JeiText.Get("FrostMoon"), "pumpkinMoon" => JeiText.Get("PumpkinMoon"), "xMas" => JeiText.Get("Christmas"), "halloween" => JeiText.Get("Halloween"),
            "moonPhase" => JeiText.Get("MoonPhase"), "time" => JeiText.Get("InGameTime"), "netMode" => JeiText.Get("NetworkMode"),
            "npc" => JeiText.Get("WorldNPCs"), "player" => JeiText.Get("Players"),
            "LocalPlayer" => JeiText.Get("LocalPlayer"), "myPlayer" => JeiText.Get("LocalPlayerID"), "worldID" => JeiText.Get("WorldID"), _ => null
        };
        if (type == typeof(WorldGen)) return name switch {
            "crimson" => JeiText.Get("CrimsonWorld"), "drunkWorldGen" => JeiText.Get("DrunkWorld"), "getGoodWorldGen" => JeiText.Get("ForTheWorthyWorld"),
            "remixWorldGen" => JeiText.Get("RemixWorld"), "notTheBees" => JeiText.Get("NotTheBeesWorld"), "tenthAnniversaryWorldGen" => JeiText.Get("Celebrationmk10World"),
            _ => null
        };
        if (type == typeof(NPC)) return name switch {
            "SpawnedFromStatue" => JeiText.Get("SpawnedFromAStatue"), "boss" => JeiText.Get("TargetIsABoss"), "friendly" => JeiText.Get("TargetIsFriendly"),
            "townNPC" => JeiText.Get("TargetIsATownNPC"), "type" => JeiText.Get("TargetNPCType"), "netID" => JeiText.Get("TargetNPCNetID"),
            "GivenName" => JeiText.Get("NPCName"), "life" => JeiText.Get("NPCHealth"), "lifeMax" => JeiText.Get("NPCMaximumHealth"),
            "target" => JeiText.Get("NPCTargetPlayerID"), "realLife" => JeiText.Get("ParentNPCID"), "ai" => JeiText.Get("NPCAIState"),
            "waveNumber" => JeiText.Get("CurrentEventWave"), "LunarApocalypseIsUp" => JeiText.Get("LunarEventsActive"),
            "value" => JeiText.Get("NPCCoinValue"), "lastInteraction" => JeiText.Get("LastInteractingPlayerID"),
            "downedSlimeKing" => JeiText.Get("KingSlimeDefeated"), "downedBoss1" => JeiText.Get("EyeOfCthulhuDefeated"),
            "downedBoss2" => JeiText.Get("EaterOfWorldsOrBrainOfCthulhu"), "downedBoss3" => JeiText.Get("SkeletronDefeated"),
            "downedQueenBee" => JeiText.Get("QueenBeeDefeated"), "downedDeerclops" => JeiText.Get("DeerclopsDefeated"),
            "downedQueenSlime" => JeiText.Get("QueenSlimeDefeated"), "downedMechBossAny" => JeiText.Get("AtLeastOneMechanicalBossDefeated"),
            "downedMechBoss1" => JeiText.Get("TheDestroyerDefeated"), "downedMechBoss2" => JeiText.Get("TheTwinsDefeated"), "downedMechBoss3" => JeiText.Get("SkeletronPrimeDefeated"),
            "downedPlantBoss" => JeiText.Get("PlanteraDefeated"), "downedGolemBoss" => JeiText.Get("GolemDefeated"), "downedFishron" => JeiText.Get("DukeFishronDefeated"),
            "downedEmpressOfLight" => JeiText.Get("EmpressOfLightDefeated"), "downedAncientCultist" => JeiText.Get("LunaticCultistDefeated"), "downedMoonlord" => JeiText.Get("MoonLordDefeated"),
            "downedGoblins" => JeiText.Get("GoblinArmyDefeated"), "downedFrost" => JeiText.Get("FrostLegionDefeated"), "downedPirates" => JeiText.Get("PirateInvasionDefeated"),
            "downedMartians" => JeiText.Get("MartianMadnessDefeated"), _ => null
        };
        if (type == typeof(Entity)) return name switch {
            "active" => JeiText.Get("EntityIsActive"), "whoAmI" => JeiText.Get("EntityID"), "position" => JeiText.Get("EntityPosition"), _ => null
        };
        if (type == typeof(ModNPC) && name == "NPC") return JeiText.Get("TargetNPC");
        if (type == typeof(ModPlayer) && name == "Player") return JeiText.Get("Player");
        if (type == typeof(Terraria.GameContent.Events.DD2Event) && name == "DownedInvasionT3") return JeiText.Get("DefeatedTier3OfTheOldOne");
        if (type == typeof(Player)) return name switch {
            "name" => JeiText.Get("PlayerName"), "dead" => JeiText.Get("PlayerIsDead"), "difficulty" => JeiText.Get("CharacterDifficulty"), "team" => JeiText.Get("PlayerTeam"),
            "anglerQuestsFinished" => JeiText.Get("CompletedAnglerQuests"),
            "inventory" => JeiText.Get("PlayerInventory"), "armor" => JeiText.Get("PlayerEquipment"), "HeldItem" => JeiText.Get("HeldItem"), "buffType" => JeiText.Get("PlayerBuffType"),
            "statLife" => JeiText.Get("PlayerHealth"), "statLifeMax2" => JeiText.Get("MaximumPlayerHealth"), "extraAccessory" => JeiText.Get("DemonHeartConsumed"),
            "ZoneOverworldHeight" => JeiText.Get("OnTheSurface"), "ZoneDirtLayerHeight" => JeiText.Get("InTheUnderground"), "ZoneRockLayerHeight" => JeiText.Get("InTheCavernLayer"),
            "ZoneUnderworldHeight" => JeiText.Get("InTheUnderworld"), "ZoneSkyHeight" => JeiText.Get("InSpace"), "ZoneDungeon" => JeiText.Get("InTheDungeon"),
            "ZoneJungle" => JeiText.Get("InTheJungle"), "ZoneSnow" => JeiText.Get("InTheSnowBiome"), "ZoneDesert" => JeiText.Get("InTheDesert"), "ZoneBeach" => JeiText.Get("AtTheOcean"),
            "ZoneHallow" => JeiText.Get("InTheHallow"), "ZoneCorrupt" => JeiText.Get("InTheCorruption"), "ZoneCrimson" => JeiText.Get("InTheCrimson"),
            "ZoneGlowshroom" => JeiText.Get("InAGlowingMushroomBiome"), "ZoneGraveyard" => JeiText.Get("InAGraveyard"), "ZoneForest" => JeiText.Get("InTheForest"),
            _ => null
        };
        if (type == typeof(DropAttemptInfo)) return name switch {
            "npc" => JeiText.Get("SourceNPC"), "player" => JeiText.Get("Player"), "item" => JeiText.Get("SourceItemID"),
            "IsExpertMode" => JeiText.Get("ExpertMode"), "IsMasterMode" => JeiText.Get("MasterMode"), "IsInSimulation" => JeiText.Get("SimulatedDrop"), _ => null
        };
        if (type == typeof(Terraria.Item)) return name switch {
            "type" => JeiText.Get("ItemType"), "stack" => JeiText.Get("ItemCount"), "rare" => JeiText.Get("ItemRarity"), "value" => JeiText.Get("ItemValue"), _ => null
        };
        if (type.DeclaringType == typeof(WorldGen) && type.Name == "SavedOreTiers") return name switch {
            "Copper" => JeiText.Get("WorldSCopperTierOre"), "Iron" => JeiText.Get("WorldSIronTierOre"), "Silver" => JeiText.Get("WorldSSilverTierOre"),
            "Gold" => JeiText.Get("WorldSGoldTierOre"), "Cobalt" => JeiText.Get("WorldSCobaltTierOre"), "Mythril" => JeiText.Get("WorldSMythrilTierOre"),
            "Adamantite" => JeiText.Get("WorldSAdamantiteTierOre"), _ => null
        };
        if (typeof(ModConfig).IsAssignableFrom(type))
        {
            Mod owner = ModLoader.Mods.FirstOrDefault(mod => mod.Code == type.Assembly);
            if (owner == null) return null;
            MemberInfo declared = (MemberInfo)type.GetProperty(name) ?? member;
            string key = declared.GetCustomAttributesData()
                .FirstOrDefault(attr => attr.AttributeType == typeof(LabelKeyAttribute))?
                .ConstructorArguments.FirstOrDefault().Value as string;
            key = key?.TrimStart('$') ?? $"Mods.{owner.Name}.Configs.{type.Name}.{name}.Label";
            if (Language.Exists(key))
            {
                string title = Regex.Replace(Language.GetTextValue(key), @"\[(?:i|item)(?:/[^:\]]+)?:[^\]]+\]\s*", "").Trim();
                if (title.Length > 0) return JeiText.Get("Setting", title);
            }
        }
        // Many mods publish world progression as Downed{RegisteredNpc} or use a
        // corresponding property on the player. Resolve the target in that registry.
        Type valueType = member switch {
            FieldInfo f => f.FieldType, PropertyInfo p => p.PropertyType, MethodInfo m => m.ReturnType, _ => null
        };
        if (valueType == typeof(bool))
        {
            if (member is FieldInfo field && typeof(ModPlayer).IsAssignableFrom(type))
            {
                string effect = ItemEffects.GetValue(field, f => new Lazy<string>(() => FindItemEffect(f))).Value;
                if (effect != null) return effect;
            }
            if (member is FieldInfo hitFlag && typeof(GlobalNPC).IsAssignableFrom(type))
            {
                string hit = HitFlags.GetValue(hitFlag, f => new Lazy<string>(() => FindHitFlag(f))).Value;
                if (hit != null) return hit;
            }
            string flag = name.TrimStart('_');
            string prefix = new[] { "Downed", "Defeated" }.FirstOrDefault(s => flag.StartsWith(s, StringComparison.OrdinalIgnoreCase));
            if (prefix != null && prefix.Length < flag.Length)
            {
                string target = flag[prefix.Length..];
                Mod owner = ModLoader.Mods.FirstOrDefault(mod => mod.Code == type.Assembly);
                string boss = owner == null ? null : RegisteredBoss(owner, target);
                if (boss == null && NPCID.Search.TryGetId(target, out int vanilla) && vanilla > 0 && vanilla < NPCID.Count)
                    boss = Lang.GetNPCNameValue(vanilla);
                if (boss != null) return (typeof(ModPlayer).IsAssignableFrom(type) ? JeiText.Get("DefeatedByPlayer") : JeiText.Get("Defeated2")) + boss;
            }
            if (member is FieldInfo { IsStatic: true } || member is MethodInfo { IsStatic:true })
            {
                var owner = ModLoader.Mods.FirstOrDefault(mod => mod.Code == type.Assembly);
                if (owner != null)
                {
                    string mode = Registries.GetValue(owner, mod => new RegistryNames(mod)).ModeFlag(member);
                    if (mode != null) return mode;
                    if (member is FieldInfo state) return Origin(state);
                }
            }
        }
        if (member is FieldInfo other && (other.IsStatic && other.FieldType == typeof(int) ||
            !other.IsStatic && other.FieldType == typeof(bool) && (typeof(ModPlayer).IsAssignableFrom(type) ||
                typeof(ModNPC).IsAssignableFrom(type))))
            return Origin(other);
        return null;
    }

    internal static string Npc(Type type) => ModContent.GetContent<ModNPC>()
        .FirstOrDefault(npc => npc.GetType() == type)?.DisplayName.Value;

    internal static string Item(Type type) => ModContent.GetContent<ModItem>()
        .FirstOrDefault(item => item.GetType() == type)?.DisplayName.Value;

    // Cross-mod calls sometimes use an English display name and sometimes a registry
    // name. Only accept an unambiguous name from the mod addressed by the caller.
    internal static string Boss(string mod, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(mod) || !ModLoader.TryGetMod(mod, out var owner))
            return key;
        return RegisteredBoss(owner, key) ?? key;
    }

    private static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
