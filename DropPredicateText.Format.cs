using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

internal static partial class DropPredicateText
{
    // Reuse translations published on actual registered Condition objects when another
    // mod wraps the same flag in an anonymous delegate. Never compare current booleans.
    private static Dictionary<MemberInfo,LocalizedText> registeredLabels;

    private static Dictionary<MemberInfo,LocalizedText> RegisteredLabels()
    {
        if (registeredLabels != null) return registeredLabels;
        var labels = new Dictionary<MemberInfo,LocalizedText>();
        var seen = new HashSet<Type>();
        void AddTable(Type holder) {
            if (holder == null || holder != typeof(Condition) && (!holder.IsAbstract || !holder.IsSealed) ||
                !seen.Add(holder)) return;
            foreach (var field in holder.GetFields(BindingFlags.Static | BindingFlags.Public))
                if (field.IsInitOnly && field.FieldType == typeof(Condition))
                    try { Add(field.GetValue(null) as Condition,false); } catch (Exception) { }
        }
        void Add(Condition c, bool includeTable) {
            var predicate = c?.Predicate;
            if (predicate == null || string.IsNullOrWhiteSpace(c.Description?.Value) || BrowserIndex.IsGenericConditionLabel(c.Description.Value) ||
                c.Description.Value == c.Description.Key) return;
            try {
                var method = predicate.Method;
                var il = method.GetMethodBody()?.GetILAsByteArray();
                // ldsfld flag; ret / call static getter; ret. Compound, inverted and
                // instance-dependent predicates cannot name an individual flag.
                if (il is {Length:6} && il[5] == 0x2a && il[0] is 0x7e or 0x28) {
                    var member = method.Module.ResolveMember(BitConverter.ToInt32(il,1));
                    if (member is FieldInfo {IsStatic:true, FieldType:var ft} && ft == typeof(bool) ||
                        member is MethodInfo {IsStatic:true, ReturnType:var rt} getter && rt == typeof(bool) && getter.GetParameters().Length == 0)
                        labels.TryAdd(member,c.Description);
                }
                if (includeTable) {
                    var holder = method.DeclaringType?.DeclaringType;
                    // This table has already supplied a registered condition. Its static
                    // initialization has happened in the mod's normal loading sequence.
                    if (holder != null && !seen.Contains(holder) && holder.GetFields(BindingFlags.Public | BindingFlags.Static)
                        .Any(f => f.FieldType == typeof(Condition) && f.IsInitOnly && ReferenceEquals(f.GetValue(null),c)))
                        AddTable(holder);
                }
            } catch (Exception) { }
        }
        AddTable(typeof(Condition));
        foreach (var recipe in Main.recipe ?? []) {
            if (recipe?.Conditions == null) continue;
            foreach (var c in recipe.Conditions) Add(c,true);
        }
        foreach (var shop in NPCShopDatabase.AllShops)
            foreach (var item in shop.ActiveEntries)
                foreach (var c in item.Conditions ?? []) Add(c,true);
        return registeredLabels = labels;
    }

    private static string Label(MemberInfo member)
    {
        var plain = PredicateVocabulary.Member(member);
        if (plain != null) return plain;
        if (!RegisteredLabels().TryGetValue(member,out var text)) return null;
        string label = text.Value;
        if (label.StartsWith("掉落：")) label = label[3..];
        return label.Trim();
    }

    private static string Format(Value v)
    {
        if (Is(v,0)) return "条件恒不成立";
        if (Is(v,1)) return "无额外限制";
        return Print(Simplify(v));
    }

    private static readonly Dictionary<Mod,Dictionary<FieldInfo,HashSet<string>>> trackedNpcs = new();

    private static string TrackedNpc(Value absent, Value inactive)
    {
        if (absent.Op != "=" || !Is(absent.B,-1) || absent.A.Captured is not FieldInfo {IsStatic:true} slot ||
            inactive.Op != "!" || inactive.A.Captured is not FieldInfo {Name:nameof(Entity.active),DeclaringType:var activeType} ||
            activeType != typeof(Entity) || inactive.A.A is not {Op:"index"} index || index.B != absent.A ||
            index.A.Captured is not FieldInfo {Name:nameof(Main.npc), DeclaringType:var main} || main != typeof(Main)) return null;
        var mod = ModLoader.Mods.FirstOrDefault(m => m.Code == slot.DeclaringType.Assembly);
        if (mod == null) return null;
        if (!trackedNpcs.TryGetValue(mod,out var fields)) {
            fields = new();
            foreach (var npc in mod.GetContent<ModNPC>()) {
                try {
                    var ai = npc.GetType().GetMethod(nameof(ModNPC.AI),Type.EmptyTypes);
                    if (ai?.DeclaringType == typeof(ModNPC)) continue;
                    var ops = Tokens(ai).ToArray();
                    for (int i = 3; i < ops.Length; i++) {
                        if (ops[i].Code != OpCodes.Stsfld || ops[i-1].Code != OpCodes.Ldfld ||
                            ops[i-2].Code != OpCodes.Call || ops[i-3].Code != OpCodes.Ldarg_0) continue;
                        var getNpc = ai.Module.ResolveMethod(ops[i-2].Token);
                        var who = ai.Module.ResolveField(ops[i-1].Token);
                        if (getNpc.DeclaringType != typeof(ModNPC) || getNpc.Name != "get_NPC" ||
                            who.DeclaringType != typeof(Entity) || who.Name != nameof(Entity.whoAmI)) continue;
                        var field = ai.Module.ResolveField(ops[i].Token);
                        if (!fields.TryGetValue(field,out var names)) fields[field] = names = new();
                        names.Add(npc.DisplayName.Value);
                    }
                } catch (Exception) { }
            }
            trackedNpcs[mod] = fields;
        }
        return fields.TryGetValue(slot,out var candidates) && candidates.Count == 1 ?
            "未记录到在场的" + candidates.First() : null;
    }

    private static string Print(Value v, int parent = 0)
    {
        string Part(Value a, int p = 0) => Print(a,p);
        switch (v.Op) {
            case "atom": case "npcId": case "itemId": case "number": case "string": return v.Text;
            case "instance": return v.Text;
            case "typed": return Part(v.A) + "（" + v.Text + "）";
            case "null": return "空";
            case "npc": return "掉落目标";
            case "player": return "玩家";
            case "captured" when v.Captured is NPC: return "掉落目标";
            case "captured" when v.Captured is Player: return "玩家";
            case "captured" when v.Captured is Mod mod: return mod.DisplayName;
            case "member": {
                var member = (MemberInfo)v.Captured;
                string label = Label(member);
                if (label != null && v.A?.Op == "index") return Part(v.A) + "的" + label;
                if (label != null) return label;
                if (v.A?.Op is "npc" or "player") return Part(v.A) + "的 " + v.Text;
                return member.DeclaringType.Name + "." + v.Text;
            }
            case "index": {
                bool ai = v.A.Captured is FieldInfo field && field.FieldType == typeof(float[]) &&
                    (field.DeclaringType == typeof(NPC) || typeof(GlobalNPC).IsAssignableFrom(field.DeclaringType)) &&
                    field.Name.EndsWith("AI",StringComparison.OrdinalIgnoreCase);
                if ((ai || v.A.Text == "目标的模组 AI 状态") && v.B.Op == "number" && int.TryParse(v.B.Text,out int slot))
                    return (v.A.Captured is FieldInfo {DeclaringType:var t} && t == typeof(NPC) ? "目标 AI" : "目标的模组 AI") +
                        " 状态第 " + (slot + 1) + " 项";
                return Part(v.A) + "[" + Part(v.B) + "]";
            }
            case "lower": return Part(v.A) + "（转为小写）";
            case "contains": return Part(v.A) + "含有「" + Part(v.B) + "」";
            case "choice": return "（若" + Part(v.A) + "，取" + Part(v.B.A) + "，否则取" + Part(v.B.B) + "）";
            case "countNpcs": return "在场的" + Part(v.A) + "数量";
            case "anyNpc": return Part(v.A) + "在场";
            case "!": {
                var a = Part(v.A,4);
                if (a == "雕像生成") return "非雕像生成";
                if (a is "困难模式" or "已进入困难模式") return "困难模式前";
                if (a == "颠倒世界" || a == "大师模式" || a == "专家模式") return "非" + a;
                if (a.StartsWith("已击败")) return "尚未击败" + a[3..];
                if (a.StartsWith("日历活动已开启（",StringComparison.Ordinal))
                    return "日历活动未开启" + a["日历活动已开启".Length..];
                if (a.StartsWith("击败") && a.EndsWith("后")) return a[..^1] + "前";
                if (a.EndsWith("在场")) return a[..^2] + "不在场";
                if (a.StartsWith("设置「") && a.EndsWith("」")) return "关闭" + a;
                return "不满足「" + Part(v.A) + "」";
            }
            case "and": case "or": {
                bool and = v.Op == "and";
                if (!and) {
                    var tracked = TrackedNpc(v.A,v.B) ?? TrackedNpc(v.B,v.A);
                    if (tracked != null) return tracked;
                    var tests = new List<Value>();
                    void CollectChoices(Value x) {
                        if (x.Op == "or") { CollectChoices(x.A); CollectChoices(x.B); }
                        else tests.Add(x);
                    }
                    CollectChoices(v);
                    if (tests.Count >= 2 && tests.All(x => x.Op == "=" && x.A == tests[0].A && x.B.Op == "string"))
                        return Part(tests[0].A) + "为" + string.Join("、",tests.Select(x => "「" + x.B.Text + "」")) + "之一";
                }
                int p = and ? 2 : 1;
                string text = Part(v.A,p) + (and ? "且" : "或") + Part(v.B,p);
                return p < parent ? "（" + text + "）" : text;
            }
            case "=": case "!=": case "<": case ">": case ">=": case "<=": {
                if (v.A.Captured is FieldInfo {DeclaringType:var t,Name:nameof(Main.netMode)} && t == typeof(Main) &&
                    v.Op is "=" or "!=" && v.B.Op == "number") {
                    bool equal = v.Op == "=";
                    var mode = (v.B.Text,equal) switch {
                        ("0",true) => "单人模式", ("0",false) => "多人模式",
                        ("1",true) => "多人客户端", ("1",false) => "单人或多人服务器端",
                        ("2",true) => "多人服务器端", ("2",false) => "单人或多人客户端", _ => null
                    };
                    if (mode != null) return mode;
                }
                if (v.A.Op == "+" && v.A.A.Op == "countNpcs" && v.A.B.Op == "countNpcs" &&
                    v.Op == "<=" && Is(v.B,1))
                    return Part(v.A.A.A) + "与" + Part(v.A.B.A) + "的在场总数不超过 1";
                string right = v.B.Op == "string" ? "「" + Part(v.B) + "」" : Part(v.B);
                var mi = v.A.Captured as MemberInfo;
                if (mi?.DeclaringType == typeof(WorldGen.SavedOreTiers) && v.B.Op == "number" &&
                    int.TryParse(v.B.Text,out int tile) && tile >= 0 && tile < TileLoader.TileCount) {
                    string ore = BrowserIndex.NaturalStationName(tile);
                    if (!string.IsNullOrWhiteSpace(ore)) right = ore;
                }
                if (mi?.DeclaringType == typeof(NPC) && mi.Name == "type" && v.B.Op == "number" &&
                    int.TryParse(v.B.Text,out int npc) && ContentSamples.NpcsByNetId.ContainsKey(npc))
                    right = Lang.GetNPCNameValue(npc);
                string comparison = v.Op switch {"=" => "为","!=" => "不为","<" => "小于",">" => "大于",
                    "<=" => "不超过",">=" => "至少为",_ => throw new NotSupportedException()};
                return Part(v.A,3) + comparison + right;
            }
            case "+": case "-": case "*": case "/": case "%": return "（" + Part(v.A) + " " + v.Op + " " + Part(v.B) + "）";
            default: throw new NotSupportedException();
        }
    }
}
