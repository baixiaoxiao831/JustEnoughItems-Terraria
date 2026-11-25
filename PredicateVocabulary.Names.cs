using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;

namespace JustEnoughItemsTerraria;

internal static partial class PredicateVocabulary
{
    private static readonly ConditionalWeakTable<Mod, RegistryNames> Registries = new();
    private static readonly ConditionalWeakTable<FieldInfo,Lazy<string>> ItemEffects = new();

    private static string RegisteredBoss(Mod owner, string key) =>
        Registries.GetValue(owner, mod => new RegistryNames(mod)).Find(key);

    internal static string Difficulty(Mod owner, string key)
    {
        if (owner == null || string.IsNullOrWhiteSpace(key)) return key;
        var names = Registries.GetValue(owner, mod => new RegistryNames(mod));
        string normalized = Normalize(key);
        string eventName = names.Direct(normalized);
        if (eventName != null) return eventName + "已开启";
        return (names.Mode(normalized) ?? key) + "难度已启用";
    }

    // Link the guard back to a registered item that writes this exact field. Static
    // metadata is sufficient; never simulate item use on a player during indexing.
    private static string FindItemEffect(FieldInfo field)
    {
        Mod owner = ModLoader.Mods.FirstOrDefault(mod => mod.Code == field.Module.Assembly);
        if (owner == null) return null;
        var matches = new HashSet<string>();
        byte[] token = BitConverter.GetBytes(field.MetadataToken);
        foreach (var item in owner.GetContent<ModItem>())
        foreach (string hook in new[] { "UseItem", "UpdateAccessory", "UpdateVanity" })
        {
            Type[] parameters = hook == "UpdateAccessory" ? [typeof(Terraria.Player),typeof(bool)] : [typeof(Terraria.Player)];
            MethodInfo method = item.GetType().GetMethod(hook, BindingFlags.Public | BindingFlags.Instance,null,parameters,null);
            if (method?.Module != field.Module) continue;
            var bytes = method.GetMethodBody()?.GetILAsByteArray();
            if (bytes == null) continue;
            bool candidate = false;
            for (int i=0; i+5<bytes.Length; i++)
                if (bytes[i] == 0x17 && bytes[i+1] == 0x7d && bytes.AsSpan(i+2,4).SequenceEqual(token)) { candidate=true; break; }
            if (!candidate) continue;
            using var copy = new DynamicMethodDefinition(method);
            bool writes = copy.Definition.Body.Instructions.Any(i => i.OpCode.Code == Code.Stfld &&
                i.Previous?.OpCode.Code == Code.Ldc_I4_1 && i.Operand is FieldReference f &&
                f.DeclaringType.FullName == field.DeclaringType.FullName.Replace('+','/') && f.Name == field.Name);
            if (writes)
            {
                string action = hook switch { "UseItem" => "已使用", "UpdateAccessory" => "装备栏已装备", _ => "时装栏已装备" };
                matches.Add(action + "「" + item.DisplayName.Value + "」");
            }
        }
        return matches.Count > 0 ? string.Join("或", matches.Order()) : null;
    }

    private sealed class RegistryNames
    {
        private readonly Dictionary<string,List<LocalizedText>> groups = new(), npcs = new(), modes = new();
        private readonly Dictionary<string,List<string>> modeFlags = new();
        private readonly Lazy<Dictionary<string,List<string>>> aliases;

        internal RegistryNames(Mod mod)
        {
            string prefix = "Mods." + mod.Name + ".";
            foreach (var entry in Language.FindAll((name, _) =>
                name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".EntryName", StringComparison.Ordinal)))
                Register(groups, entry.Key.Split('.')[^2], entry);
            foreach (var entry in Language.FindAll((name, _) => name.StartsWith(prefix + "UI.",StringComparison.Ordinal) ||
                     name.StartsWith(prefix + "Difficulties.",StringComparison.Ordinal)))
                Register(modes,entry.Key.Split('.')[^1],entry);
            foreach (var npc in mod.GetContent<ModNPC>())
            {
                Register(npcs, npc.Name, npc.DisplayName);
                foreach (string suffix in new[] { "Head", "Body", "Core" })
                    if (npc.Name.EndsWith(suffix, StringComparison.Ordinal))
                        Register(npcs, npc.Name[..^suffix.Length], npc.DisplayName);
            }
            aliases = new(() => ReadAliases(mod));
        }

        private static void Register(Dictionary<string,List<LocalizedText>> index, string name, LocalizedText text)
        {
            string key = Normalize(name);
            if (!index.TryGetValue(key, out var values)) index[key] = values = new();
            values.Add(text);
        }

        private static string Unique(Dictionary<string,List<LocalizedText>> index, string name)
        {
            if (!index.TryGetValue(name,out var entries)) return null;
            string[] labels = entries.Select(entry => entry.Value).Distinct().Take(2).ToArray();
            return labels.Length == 1 ? labels[0] : null;
        }

        internal string Direct(string normalized) => Unique(groups, normalized) ?? Unique(npcs, normalized);
        internal string Mode(string normalized) => Unique(modes,normalized);

        internal string ModeFlag(MemberInfo member)
        {
            _ = aliases.Value;
            string key = member.DeclaringType.FullName.Replace('+','/') + "." + member.Name;
            if (!modeFlags.TryGetValue(key,out var names)) return null;
            foreach (string alias in names) {
                string ev = Unique(groups,alias);
                if (ev != null) return ev + "已开启";
                string mode = Mode(alias);
                if (mode != null) return mode + "难度已启用";
            }
            return null;
        }

        internal string Find(string value)
        {
            string key = Normalize(value);
            string direct = Direct(key);
            if (direct != null) return direct;
            string[] joint = value.Split("And",StringSplitOptions.None);
            if (joint.Length == 2)
            {
                var first = Direct(Normalize(joint[0]));
                var second = Direct(Normalize(joint[1]));
                if (first != null && second != null) return first + "与" + second;
            }
            if (!aliases.Value.TryGetValue(key,out var alternatives)) return null;
            // Prefer the registered name of an encounter over the names of its constituent NPCs.
            foreach (var index in new[] { groups, npcs })
            {
                var found = alternatives.Select(name => Unique(index, name)).Where(s => s != null).Distinct().Take(2).ToArray();
                if (found.Length == 1) return found[0];
                if (found.Length > 1) return null;
            }
            return null;
        }

        // String switch cases with the same destination in a boss-progress query
        // are aliases declared by the mod. Inspect IL without running its method.
        private Dictionary<string,List<string>> ReadAliases(Mod mod)
        {
            var aliases = new Dictionary<string,List<string>>();
            foreach (Type type in AssemblyManager.GetLoadableTypes(mod.Code))
            foreach (MethodInfo query in type.GetMethods(BindingFlags.Static | BindingFlags.Public |
                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                bool progressQuery = query.Name.Contains("Downed",StringComparison.OrdinalIgnoreCase) &&
                    query.Name.Contains("Boss",StringComparison.OrdinalIgnoreCase);
                bool modeQuery = query.Name.Contains("Difficulty",StringComparison.OrdinalIgnoreCase) &&
                    query.Name.Contains("Active",StringComparison.OrdinalIgnoreCase);
                if (!progressQuery && !modeQuery || query.ReturnType != typeof(bool) ||
                     query.GetParameters() is not [{ ParameterType: var parameter }] || parameter != typeof(string)) continue;
                try
                {
                    using var copy = new DynamicMethodDefinition(query);
                    var labels = new Dictionary<Instruction,List<string>>();
                    foreach (var instruction in copy.Definition.Body.Instructions)
                    {
                        if (instruction.OpCode.Code != Code.Ldstr || instruction.Next?.Operand is not MethodReference equals ||
                            equals.DeclaringType.FullName != "System.String" || equals.Name != "op_Equality") continue;
                        var branch = instruction.Next.Next;
                        if (branch?.Operand is not Instruction target) continue;
                        if (branch.OpCode.Code is Code.Brfalse or Code.Brfalse_S) target = branch.Next;
                        else if (branch.OpCode.Code is not (Code.Brtrue or Code.Brtrue_S)) continue;
                        if (target?.OpCode.Code is Code.Br or Code.Br_S) target = target.Operand as Instruction;
                        if (target?.Next?.OpCode.Code != Code.Ret || target.OpCode.Code is not (Code.Call or Code.Ldsfld)) continue;
                        if (!labels.TryGetValue(target,out var list)) labels[target] = list = new();
                        list.Add(Normalize((string)instruction.Operand));
                        if (modeQuery && target.Operand is MemberReference state)
                            modeFlags[state.DeclaringType.FullName + "." + state.Name] = list;
                        string progress = target.Operand is MemberReference member ? member.Name : "";
                        if (progress.StartsWith("get_",StringComparison.Ordinal)) progress = progress[4..];
                        if (progress.StartsWith("downed",StringComparison.OrdinalIgnoreCase)) list.Add(Normalize(progress[6..]));
                    }
                    if (!progressQuery) continue;
                    foreach (var list in labels.Values)
                    foreach (string key in list.Distinct())
                    {
                        if (!aliases.TryGetValue(key, out var existing)) aliases[key] = existing = new();
                        existing.AddRange(list);
                    }
                }
                catch (Exception) { /* Names remain usable when a mod exposes native or unavailable metadata. */ }
            }
            return aliases;
        }
    }
}
