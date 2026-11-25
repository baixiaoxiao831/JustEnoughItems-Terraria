using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace JustEnoughItemsTerraria;

// Discover localized descriptions from the loaded game's equipment comparisons.
// Read only direct slot comparisons leading to a set description; never execute equip effects.
internal static class ArmorBonusIndex
{
    private sealed record Guard(int Slot, int Value, Code Comparison, bool Taken, Instruction Start);
    private static Dictionary<(int Slot,int Value),HashSet<string>> keys;

    internal static void Clear() => keys = null;

    internal static IEnumerable<string> For(Item item)
    {
        keys ??= Build();
        int slot = item.headSlot >= 0 ? 0 : item.bodySlot >= 0 ? 1 : 2;
        int value = slot == 0 ? item.headSlot : slot == 1 ? item.bodySlot : item.legSlot;
        if (!keys.TryGetValue((slot,value),out var labels)) yield break;
        foreach (var label in labels)
            yield return Language.GetTextValue(label, Language.GetTextValue(Main.ReversedUpDownArmorSetBonuses ? "Key.UP" : "Key.DOWN"));
    }

    private static Dictionary<(int,int),HashSet<string>> Build()
    {
        var result = new Dictionary<(int,int),HashSet<string>>();
        using var method = new DynamicMethodDefinition(typeof(Player).GetMethod(nameof(Player.UpdateArmorSets)));
        var instructions = method.Definition.Body.Instructions;
        var incoming = new Dictionary<Instruction,List<Guard>>();
        void Edge(Instruction target, Guard guard)
        {
            if (target == null) return;
            if (!incoming.TryGetValue(target,out var edges)) incoming[target] = edges = new();
            edges.Add(guard);
        }
        foreach (var op in instructions)
        {
            Guard guard = null;
            if (op.OpCode.FlowControl == FlowControl.Cond_Branch && op.Operand is Instruction &&
                op.Previous.MatchLdcI4(out int value) && op.Previous.Previous.Operand is FieldReference field &&
                field.DeclaringType.FullName == typeof(Player).FullName && op.Previous.Previous.OpCode == OpCodes.Ldfld &&
                op.Previous.Previous.Previous.MatchLdarg(0))
            {
                int slot = field.Name switch { "head" => 0, "body" => 1, "legs" => 2, _ => -1 };
                if (slot >= 0) guard = new(slot, value, op.OpCode.Code, true, op.Previous.Previous.Previous);
            }
            if (op.Operand is Instruction jump) Edge(jump, guard);
            if (op.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw))
                Edge(op.Next, guard == null ? null : guard with { Taken = false });
        }

        var samples = ContentSamples.ItemsByType.Values;
        var values = new[]{samples.Select(i=>(int)i.headSlot), samples.Select(i=>(int)i.bodySlot), samples.Select(i=>(int)i.legSlot)}
            .Select(v=>v.Where(n=>n>=0).Distinct().ToArray()).ToArray();
        foreach (var op in instructions)
        {
            if (op.OpCode != OpCodes.Ldstr || op.Operand is not string key || !key.StartsWith("ArmorSetBonus.",StringComparison.Ordinal) ||
                !op.Previous.MatchLdarg(0)) continue;
            var path = new List<Guard>();
            void Walk(Instruction node)
            {
                if (incoming.TryGetValue(node,out var edges) && edges.Count > 0 && edges.All(g=>g != null))
                {
                    foreach (var edge in edges)
                    {
                        if (path.Contains(edge)) continue;
                        path.Add(edge);
                        Walk(edge.Start);
                        path.RemoveAt(path.Count-1);
                    }
                    return;
                }
                if (path.Count == 0) return;
                var choices = new int[3][];
                for (int slot=0;slot<3;slot++)
                {
                    var constraints = path.Where(g=>g.Slot==slot).ToArray();
                    if (constraints.Length == 0) continue;
                    choices[slot] = values[slot].Where(v=>constraints.All(g=>Matches(g,v))).ToArray();
                    if (choices[slot].Length == 0) return;
                }
                for (int slot=0;slot<3;slot++)
                foreach (int value in choices[slot] ?? Array.Empty<int>())
                {
                    if (!result.TryGetValue((slot,value),out var entries)) result[(slot,value)] = entries = new();
                    entries.Add(key);
                }
            }
            Walk(op.Previous);
        }
        return result;
    }

    private static bool Matches(Guard guard, int value) => guard.Comparison switch
    {
        Code.Beq or Code.Beq_S => (value == guard.Value) == guard.Taken,
        Code.Bne_Un or Code.Bne_Un_S => (value != guard.Value) == guard.Taken,
        Code.Blt or Code.Blt_S => (value < guard.Value) == guard.Taken,
        Code.Bgt or Code.Bgt_S => (value > guard.Value) == guard.Taken,
        Code.Ble or Code.Ble_S => (value <= guard.Value) == guard.Taken,
        Code.Bge or Code.Bge_S => (value >= guard.Value) == guard.Taken,
        _ => false
    };
}
