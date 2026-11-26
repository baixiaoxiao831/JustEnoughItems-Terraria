using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

internal static partial class PredicateVocabulary
{
    private static readonly ConditionalWeakTable<FieldInfo,Lazy<string>> HitFlags = new();
    private static readonly ConditionalWeakTable<FieldInfo,Lazy<string>> StateOrigins = new();

    private static string Origin(FieldInfo field) =>
        StateOrigins.GetValue(field, f => new Lazy<string>(() => FindOrigin(f))).Value;

    private static bool IsField(Instruction instruction, FieldInfo field) =>
        instruction?.Operand is FieldReference source && source.DeclaringType.FullName == field.DeclaringType.FullName.Replace('+','/') &&
        field.Name == source.Name;

    private static bool WritesField(MethodInfo method, FieldInfo field)
    {
        if (method?.Module != field.Module) return false;
        byte[] il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) return false;
        byte[] token = BitConverter.GetBytes(field.MetadataToken);
        for (int i = 0; i + 4 < il.Length; i++)
            if ((il[i] == 0x80 || il[i] == 0x7d) && il.AsSpan(i+1,4).SequenceEqual(token)) return true;
        return false;
    }

    private static string FindOrigin(FieldInfo field)
    {
        Mod owner = ModLoader.Mods.FirstOrDefault(mod => mod.Code == field.Module.Assembly);
        if (owner == null) return null;
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        if (field.FieldType == typeof(bool) && typeof(ModNPC).IsAssignableFrom(field.DeclaringType))
        {
            // Some encounter variants reveal their canonical localized name directly
            // in the NPC's name hook behind exactly this flag.
            var rename = field.DeclaringType.GetMethod(nameof(ModNPC.ModifyTypeName),declared);
            if (rename != null) {
                using var copy = new DynamicMethodDefinition(rename);
                var ops = copy.Definition.Body.Instructions.Where(i => i.OpCode.Code != Code.Nop).ToArray();
                if (ops.Length == 8 && IsField(ops[1],field) && ops[2].OpCode.Code is Code.Brfalse or Code.Brfalse_S &&
                    ops[4].OpCode.Code == Code.Ldstr && ops[5].Operand is MethodReference { ReturnType.FullName:"System.String" } &&
                    ops[6].OpCode.Code == Code.Stind_Ref && ops[2].Operand == ops[7]) {
                    string key = (string)ops[4].Operand;
                    if (!Language.Exists(key)) key = "Mods." + owner.Name + "." + key;
                    if (Language.Exists(key)) return JeiText.Get("TargetIsInThe") + Language.GetTextValue(key) + JeiText.Get("Form");
                }
            }
            foreach (var item in owner.GetContent<ModItem>()) {
                var use = item.GetType().GetMethod(nameof(ModItem.UseItem),declared,null,[typeof(Player)],null);
                if (!WritesField(use,field)) continue;
                using var copy = new DynamicMethodDefinition(use);
                var ops = copy.Definition.Body.Instructions;
                foreach (var set in ops.Where(i => i.OpCode.Code == Code.Stfld && IsField(i,field))) {
                    if (set.Previous?.OpCode.Code != Code.Ldc_I4_1 || set.Previous.Previous?.OpCode.Code != Code.Isinst ||
                        set.Previous.Previous.Previous?.Operand is not MethodReference { Name:"get_ModNPC", DeclaringType.FullName:"Terraria.NPC" }) continue;
                    var branch = set.Previous.Previous.Previous.Previous?.Previous;
                    if (branch?.OpCode.Code is not (Code.Brfalse or Code.Brfalse_S) ||
                        branch.Previous?.Operand is not FieldReference guard || guard.DeclaringType.FullName != "Terraria.Main" ||
                        branch.Operand is not Instruction end || end.Offset <= set.Offset ||
                        !ops.Any(i => i.Operand is GenericInstanceMethod npc && npc.Name == "NPCType" &&
                            npc.GenericArguments[0].FullName == field.DeclaringType.FullName.Replace('+','/'))) continue;
                    string world = Member(typeof(Main).GetField(guard.Name,BindingFlags.Public | BindingFlags.Static));
                    if (world != null) return JeiText.Get("In") + world + JeiText.Get("SummonedUsing") + item.DisplayName.Value + JeiText.Get("Separator2");
                }
            }
            return null;
        }
        if (field.FieldType == typeof(int) && field.IsStatic)
        {
            var load = owner.GetType().GetMethod(nameof(Mod.Load),declared);
            if (WritesField(load,field))
            {
                using var copy = new DynamicMethodDefinition(load);
                foreach (var store in copy.Definition.Body.Instructions.Where(i => i.OpCode.Code == Code.Stsfld && IsField(i,field)))
                    if (store.Previous?.Operand is MethodReference date && date.DeclaringType.FullName == typeof(DateTime).FullName &&
                        date.Name is "get_Month" or "get_Day") return date.Name == "get_Month" ? JeiText.Get("MonthWhenModsLoaded") : JeiText.Get("DayOfMonthWhenModsLoaded");
            }
            var species = new HashSet<string>();
            foreach (var npc in owner.GetContent<ModNPC>()) {
                var hook = npc.GetType().GetMethod(nameof(ModNPC.AI),declared);
                if (!WritesField(hook,field)) continue;
                using var copy = new DynamicMethodDefinition(hook);
                if (copy.Definition.Body.Instructions.Any(i => i.OpCode.Code == Code.Stsfld && IsField(i,field) &&
                    i.Previous?.Operand is FieldReference { Name: "whoAmI", DeclaringType.FullName:"Terraria.Entity" } &&
                    i.Previous.Previous?.Operand is MethodReference { Name:"get_NPC", DeclaringType.FullName:"Terraria.ModLoader.ModNPC" }))
                    species.Add(npc.DisplayName.Value);
            }
            return species.Count == 1 ? species.Single() + JeiText.Get("ActiveEntityID") : null;
        }
        if (field.FieldType == typeof(bool) && !field.IsStatic && typeof(ModPlayer).IsAssignableFrom(field.DeclaringType))
        {
            var buffs = new HashSet<string>();
            foreach (var buff in owner.GetContent<ModBuff>())
            foreach (var hook in buff.GetType().GetMethods(declared).Where(m => m.Name == nameof(ModBuff.Update)))
            {
                if (!WritesField(hook,field)) continue;
                using var copy = new DynamicMethodDefinition(hook);
                if (copy.Definition.Body.Instructions.Any(i => i.OpCode.Code == Code.Stfld && IsField(i,field) &&
                    i.Previous?.OpCode.Code == Code.Ldc_I4_1)) buffs.Add(buff.DisplayName.Value);
            }
            return buffs.Count == 0 ? null : JeiText.Get("PlayerHasBuff") + string.Join(JeiText.Get("Or2"), buffs) + JeiText.Get("Separator3");
        }
        if (field.FieldType != typeof(bool) || !field.IsStatic) return null;
        string calendar = CalendarFlag(field,owner);
        if (calendar != null) return calendar;
        foreach (var global in owner.GetContent<GlobalNPC>())
        {
            var hook = global.GetType().GetMethod(nameof(GlobalNPC.OnKill),declared);
            if (!WritesField(hook,field)) continue;
            using var copy = new DynamicMethodDefinition(hook);
            var il = copy.Definition.Body.Instructions;
            var mods = il.Where(i => i.Operand is MethodReference { Name:"TryGetMod", DeclaringType.FullName:"Terraria.ModLoader.ModLoader" })
                .Select(i => i.Previous?.Previous?.Operand as string).Distinct().ToArray();
            if (mods.Length != 1 || mods[0] == null) continue;
            foreach (var store in il.Where(i => i.OpCode.Code == Code.Stsfld && IsField(i,field)))
            {
                var already = store.Previous?.Previous;
                if (store.Previous?.OpCode.Code != Code.Ldc_I4_1 || already?.OpCode.Code is not (Code.Brtrue or Code.Brtrue_S) ||
                    !IsField(already.Previous,field)) continue;
                var typed = already.Previous.Previous;
                if (typed?.Operand != already.Operand || typed.OpCode.Code is not (Code.Bne_Un or Code.Bne_Un_S) ||
                    typed.Previous?.Operand is not int id || id <= 0 || id >= Terraria.ID.NPCID.Count ||
                    typed.Previous.Previous?.Operand is not FieldReference { Name:"type",DeclaringType.FullName:"Terraria.NPC" }) continue;
                var prerequisite = typed.Previous.Previous.Previous?.Previous;
                if (prerequisite?.Operand != already.Operand || prerequisite.OpCode.Code is not (Code.Brfalse or Code.Brfalse_S) ||
                    prerequisite.Previous?.Previous?.Operand is not MethodReference { Name:"Call",DeclaringType.FullName:"Terraria.ModLoader.Mod" }) continue;
                int call = il.IndexOf(prerequisite.Previous.Previous);
                if (call < 11 || il[call-2].OpCode.Code != Code.Ldstr || il[call-6].OpCode.Code != Code.Ldstr ||
                    (string)il[call-6].Operand is not ("Downed" or "GetDownedBoss" or "GetBossDowned")) continue;
                return JeiText.Get("Defeated") + Boss(mods[0],(string)il[call-2].Operand) + JeiText.Get("ThenDefeated") + Lang.GetNPCNameValue(id);
            }
        }
        return null;
    }

    // A common loot condition is a boolean latched on a GlobalNPC in the projectile
    // hit hook. Recognize complementary true/false writes behind direct projectile
    // comparisons. Unknown control flow stays outside this reader.
    private static string FindHitFlag(FieldInfo field)
    {
        var type = field.DeclaringType;
        const BindingFlags hooks = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
        foreach (var callback in type.GetMethods(hooks).Where(m => m.Name is "ModifyHitByProjectile" or "OnHitByProjectile"))
        {
            using var copy = new DynamicMethodDefinition(callback);
            var il = copy.Definition.Body.Instructions;
            var stores = il.Where(i => i.OpCode.Code == Code.Stfld && i.Operand is FieldReference f &&
                f.DeclaringType.FullName == type.FullName.Replace('+','/') && f.Name == field.Name).ToArray();
            if (stores.Length != 2 || !stores.Any(i => i.Previous.OpCode.Code == Code.Ldc_I4_0) ||
                !stores.Any(i => i.Previous.OpCode.Code == Code.Ldc_I4_1)) continue;
            var yes = Block(stores.Single(i => i.Previous.OpCode.Code == Code.Ldc_I4_1));
            var no = Block(stores.Single(i => i.Previous.OpCode.Code == Code.Ldc_I4_0));
            if (yes == null || no == null) continue;
            var terms = new List<(string text, bool jumpsToSuccess)>();
            var branch = yes.Previous;
            while (branch?.Operand is Instruction target && (target == yes || target == no))
            {
                bool equal = branch.OpCode.Code is Code.Beq or Code.Beq_S;
                if (!equal && branch.OpCode.Code is not (Code.Bne_Un or Code.Bne_Un_S)) { terms.Clear(); break; }
                var constant = branch.Previous;
                if (constant?.Previous?.Operand is not MemberReference source || source.DeclaringType.FullName != "Terraria.Projectile") break;
                string name;
                if (source.Name == "type" && constant.Operand is FieldReference projectileId)
                {
                    // These ID registries are already initialized during mod loading.
                    // For arbitrary types with lazy initializers, require a direct
                    // registered projectile match instead of initializing mod code.
                    var registry = type.Assembly.GetType(projectileId.DeclaringType.FullName.Replace('/','+'));
                    var id = registry?.GetField(projectileId.Name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    int number = registry?.TypeInitializer == null && id?.FieldType == typeof(int) ? (int)id.GetValue(null) : 0;
                    if (number > 0 && number < ProjectileLoader.ProjectileCount) name = Lang.GetProjectileName(number).Value;
                    else {
                        var matches = ModContent.GetContent<ModProjectile>().Where(p => p.Name == projectileId.Name)
                            .Select(p => p.DisplayName.Value).Distinct().Take(2).ToArray();
                        if (matches.Length != 1) { terms.Clear(); break; }
                        name = matches[0];
                    }
                    name = JeiText.Get("Projectile") + (equal == (target == yes) ? JeiText.Get("Is") : JeiText.Get("IsNot2")) + "「" + name + "」";
                }
                else if (source.Name == "get_DamageType" && constant.Operand is MethodReference damage &&
                         damage.DeclaringType.FullName == typeof(DamageClass).FullName)
                {
                    string category = damage.Name switch { "get_Ranged" => JeiText.Get("Ranged"), "get_Melee" => JeiText.Get("Melee"),
                        "get_Magic" => JeiText.Get("Magic"), "get_Summon" => JeiText.Get("Summon"), _ => null };
                    if (category == null) { terms.Clear(); break; }
                    name = JeiText.Get("DamageType") + (equal == (target == yes) ? JeiText.Get("Is") : JeiText.Get("IsNot2")) + category;
                }
                else { terms.Clear(); break; }
                terms.Insert(0,(name,target == yes));
                branch = constant.Previous.Previous?.Previous;
            }
            if (terms.Count == 1 || terms.Count == 2)
                return JeiText.Get("OnTheLastProjectileHit") + terms[0].text + (terms.Count == 1 ? "" : (terms[0].jumpsToSuccess ? JeiText.Get("Or") : JeiText.Get("And")) + terms[1].text);
        }
        return null;
    }

    private static Instruction Block(Instruction store)
    {
        var receiver = store.Previous?.Previous;
        return receiver?.Operand is MethodReference { Name: "GetGlobalNPC" } ? receiver.Previous : null;
    }
}
