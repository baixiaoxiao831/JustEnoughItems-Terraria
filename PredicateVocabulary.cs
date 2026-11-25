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
            if (names.Length == 1) return "已击败" + names[0];
        }
        var zone = Regex.Match(text, @"^While in (?:the )?(.+?)(?: biome)?$", RegexOptions.IgnoreCase);
        if (zone.Success)
        {
            string area = zone.Groups[1].Value;
            string native = area.ToLowerInvariant() switch { "jungle" => "丛林", "underworld" => "地狱",
                "jungle lab" => "丛林实验室", "underworld lab" => "地狱实验室", _ => null };
            if (native != null) return "位于" + native;
            string[] biomes = ModContent.GetContent<ModBiome>().Where(b => Normalize(b.Name) == Normalize(area) ||
                 Normalize(b.Name) == Normalize(area) + "BIOME").Select(b => b.DisplayName.Value).Distinct().Take(2).ToArray();
            if (biomes.Length == 1) return "位于" + biomes[0];
        }
        return null;
    }

    internal static string Vanilla(IItemDropRuleCondition condition)
    {
        string moon = condition switch {
            Conditions.FrostMoonDropGateForTrophies => "霜月",
            Conditions.PumpkinMoonDropGateForTrophies => "南瓜月", _ => null
        };
        if (moon != null)
            return moon + "第15–16、17–18、19波起的额外通过率分别为1/4、1/3、1/2；专家及以上分别为5/18、7/18、2/3";
        if (condition is Conditions.MechdusaKill)
            return "世界同时启用颠倒世界与受苦世界特性，且其余种类的机械首领均已离场";
        if (condition is Conditions.NoPortalGun)
            return "玩家物品栏中没有" + Lang.GetItemNameValue(ItemID.PortalGun);
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
            "hardMode" => "困难模式", "expertMode" => "专家模式", "masterMode" => "大师模式",
            "remixWorld" => "颠倒世界", "zenithWorld" => "天顶世界", "getGoodWorld" => "受苦世界",
            "drunkWorld" => "醉酒世界", "tenthAnniversaryWorld" => "十周年世界",
            "notTheBeesWorld" => "蜂蜜世界", "dontStarveWorld" => "饥荒联动世界", "noTrapsWorld" => "无陷阱世界",
            "dayTime" => "白天", "raining" => "下雨", "bloodMoon" => "血月", "eclipse" => "日食",
            "snowMoon" => "霜月", "pumpkinMoon" => "南瓜月", "xMas" => "圣诞节", "halloween" => "万圣节",
            "moonPhase" => "月相", "time" => "游戏时间", "netMode" => "联网模式",
            "npc" => "场上 NPC", "player" => "玩家列表",
            "LocalPlayer" => "本地玩家", "myPlayer" => "本地玩家编号", "worldID" => "世界编号", _ => null
        };
        if (type == typeof(WorldGen)) return name switch {
            "crimson" => "猩红世界", "drunkWorldGen" => "醉酒世界", "getGoodWorldGen" => "受苦世界",
            "remixWorldGen" => "颠倒世界", "notTheBees" => "蜂蜜世界", "tenthAnniversaryWorldGen" => "十周年世界",
            _ => null
        };
        if (type == typeof(NPC)) return name switch {
            "SpawnedFromStatue" => "雕像生成", "boss" => "目标为首领", "friendly" => "目标为友好 NPC",
            "townNPC" => "目标为城镇 NPC", "type" => "目标 NPC 类型", "netID" => "目标 NPC 注册编号",
            "GivenName" => "NPC 名字", "life" => "NPC 当前生命", "lifeMax" => "NPC 生命上限",
            "target" => "NPC 的目标玩家编号", "realLife" => "NPC 所属主体编号", "ai" => "NPC AI 状态",
            "waveNumber" => "当前事件波数", "LunarApocalypseIsUp" => "月亮事件进行中",
            "value" => "NPC 钱币价值", "lastInteraction" => "上次与 NPC 交互的玩家编号",
            "downedSlimeKing" => "已击败史莱姆王", "downedBoss1" => "已击败克苏鲁之眼",
            "downedBoss2" => "已击败世界吞噬怪或克苏鲁之脑", "downedBoss3" => "已击败骷髅王",
            "downedQueenBee" => "已击败蜂王", "downedDeerclops" => "已击败独眼巨鹿",
            "downedQueenSlime" => "已击败史莱姆皇后", "downedMechBossAny" => "已击败至少一个机械首领",
            "downedMechBoss1" => "已击败毁灭者", "downedMechBoss2" => "已击败双子魔眼", "downedMechBoss3" => "已击败机械骷髅王",
            "downedPlantBoss" => "已击败世纪之花", "downedGolemBoss" => "已击败石巨人", "downedFishron" => "已击败猪龙鱼公爵",
            "downedEmpressOfLight" => "已击败光之女皇", "downedAncientCultist" => "已击败拜月教邪教徒", "downedMoonlord" => "已击败月亮领主",
            "downedGoblins" => "已击退哥布林军队", "downedFrost" => "已击退雪人军团", "downedPirates" => "已击退海盗入侵",
            "downedMartians" => "已击退火星暴乱", _ => null
        };
        if (type == typeof(Entity)) return name switch {
            "active" => "实体处于活动状态", "whoAmI" => "实体编号", "position" => "实体位置", _ => null
        };
        if (type == typeof(ModNPC) && name == "NPC") return "目标 NPC";
        if (type == typeof(ModPlayer) && name == "Player") return "玩家";
        if (type == typeof(Terraria.GameContent.Events.DD2Event) && name == "DownedInvasionT3") return "已抵御第三级撒旦军队";
        if (type == typeof(Player)) return name switch {
            "name" => "玩家名称", "dead" => "玩家已死亡", "difficulty" => "角色难度", "team" => "玩家队伍",
            "anglerQuestsFinished" => "玩家已完成的渔夫任务数",
            "inventory" => "玩家物品栏", "armor" => "玩家装备栏", "HeldItem" => "手持物品", "buffType" => "玩家增益类型",
            "statLife" => "玩家当前生命", "statLifeMax2" => "玩家生命上限", "extraAccessory" => "已使用恶魔之心",
            "ZoneOverworldHeight" => "位于地表", "ZoneDirtLayerHeight" => "位于地下层", "ZoneRockLayerHeight" => "位于洞穴层",
            "ZoneUnderworldHeight" => "位于地狱", "ZoneSkyHeight" => "位于太空", "ZoneDungeon" => "位于地牢",
            "ZoneJungle" => "位于丛林", "ZoneSnow" => "位于雪原", "ZoneDesert" => "位于沙漠", "ZoneBeach" => "位于海洋",
            "ZoneHallow" => "位于神圣之地", "ZoneCorrupt" => "位于腐化之地", "ZoneCrimson" => "位于猩红之地",
            "ZoneGlowshroom" => "位于发光蘑菇生物群落", "ZoneGraveyard" => "位于墓地", "ZoneForest" => "位于森林",
            _ => null
        };
        if (type == typeof(DropAttemptInfo)) return name switch {
            "npc" => "掉落来源 NPC", "player" => "玩家", "item" => "来源物品编号",
            "IsExpertMode" => "专家模式", "IsMasterMode" => "大师模式", "IsInSimulation" => "模拟掉落", _ => null
        };
        if (type == typeof(Terraria.Item)) return name switch {
            "type" => "物品类型", "stack" => "物品数量", "rare" => "物品稀有度", "value" => "物品价值", _ => null
        };
        if (type.DeclaringType == typeof(WorldGen) && type.Name == "SavedOreTiers") return name switch {
            "Copper" => "世界的铜级矿石", "Iron" => "世界的铁级矿石", "Silver" => "世界的银级矿石",
            "Gold" => "世界的金级矿石", "Cobalt" => "世界的钴级矿石", "Mythril" => "世界的秘银级矿石",
            "Adamantite" => "世界的精金级矿石", _ => null
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
                if (title.Length > 0) return $"设置「{title}」";
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
                if (boss != null) return (typeof(ModPlayer).IsAssignableFrom(type) ? "玩家已击败" : "已击败") + boss;
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
