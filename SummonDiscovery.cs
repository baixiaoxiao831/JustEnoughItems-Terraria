using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

// Follow known integer arguments from item-use hooks into actual spawn calls.
// Unknown stack values are null. Never run another mod's item or helper methods.
internal sealed class SummonDiscovery
{
    private readonly Dictionary<Type,int> npcTypes = ModContent.GetContent<ModNPC>()
        .GroupBy(n => n.GetType()).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().Type);
    private readonly HashSet<(int Boss,int Item)> found = new();

    internal static HashSet<(int Boss,int Item)> Build()
    {
        var scan = new SummonDiscovery();
        foreach (var item in ModContent.GetContent<ModItem>())
        {
            int budget = 64;
            foreach (var name in new[]{nameof(ModItem.UseItem), nameof(ModItem.Shoot)})
            {
                var signature = typeof(ModItem).GetMethod(name).GetParameters().Select(p => p.ParameterType).ToArray();
                var method = item.GetType().GetMethod(name, signature);
                if (method != null && method.DeclaringType != typeof(ModItem))
                    scan.Read(method, item.Type, new int?[signature.Length + 1], 0, ref budget);
            }
        }
        return scan.found;
    }

    private int? Read(MethodInfo method, int item, int?[] args, int depth, ref int budget)
    {
        if (depth > 5 || budget-- <= 0) return null;
        try
        {
            var body = method.GetMethodBody();
            if (body == null) return null;
            var ops = DropPredicateText.Instructions(method).ToArray();
            if (body.ExceptionHandlingClauses.Count != 0) return null;
            var positions = ops.Select((instruction, index) => (instruction.Offset, index))
                .ToDictionary(pair => pair.Offset, pair => pair.index);
            var bytes = body.GetILAsByteArray();
            var paths = new Stack<(int Pc, int?[] Locals, int?[] Args, Stack<int?> Stack)>();
            paths.Push((0, new int?[body.LocalVariables.Count], args, new Stack<int?>()));
            int? returned = null;
            bool seenReturn = false;
            // Limit work even if an item delegates to a large dispatcher or loops.
            int remaining = 4096;
            while (paths.TryPop(out var path))
            {
                var (pc, locals, arguments, stack) = path;
                int? Pop() => stack.TryPop(out var v) ? v : null;
                while (pc < ops.Length)
                {
                    if (--remaining < 0 || paths.Count > 64) return null;
                    var (offset,end,op,token) = ops[pc++];
                    int n = op.Value;
                    void Fork(int destination) => paths.Push((positions[destination], (int?[])locals.Clone(),
                        (int?[])arguments.Clone(), new Stack<int?>(stack.Reverse())));
                    if (op == OpCodes.Switch)
                    {
                        int? selector = Pop();
                        int count = BitConverter.ToInt32(bytes, offset + 1);
                        int table = offset + 5;
                        if (selector is int choice)
                        {
                            if ((uint)choice < (uint)count) pc = positions[end + BitConverter.ToInt32(bytes, table + 4*choice)];
                        }
                        else
                        {
                            if (count > 64) return null;
                            for (int i=0;i<count;i++) Fork(end + BitConverter.ToInt32(bytes,table + 4*i));
                        }
                        continue;
                    }
                    if (op.FlowControl == FlowControl.Branch)
                    {
                        if (op != OpCodes.Br && op != OpCodes.Br_S) return null;
                        pc = positions[end + token];
                        continue;
                    }
                    if (op.FlowControl == FlowControl.Cond_Branch)
                    {
                        int? right = Pop();
                        bool? take = op == OpCodes.Brfalse || op == OpCodes.Brfalse_S ? (right.HasValue ? right == 0 : null)
                            : op == OpCodes.Brtrue || op == OpCodes.Brtrue_S ? (right.HasValue ? right != 0 : null)
                            : Compare(op, Pop(), right);
                        if (take == true) pc = positions[end + token];
                        if (!take.HasValue) Fork(end + token);
                        continue;
                    }
                    if (op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un ||
                        op == OpCodes.Clt || op == OpCodes.Clt_Un)
                    {
                        int? right = Pop();
                        var comparison = Compare(op,Pop(),right);
                        stack.Push(comparison.HasValue ? comparison.Value ? 1 : 0 : null);
                        continue;
                    }
                    if (op == OpCodes.Add || op == OpCodes.Sub || op == OpCodes.And ||
                        op == OpCodes.Or || op == OpCodes.Xor || op == OpCodes.Mul)
                    {
                        int? right = Pop(), left = Pop();
                        stack.Push(op == OpCodes.Sub ? left - right : op == OpCodes.Add ? left + right :
                            op == OpCodes.And ? left & right : op == OpCodes.Or ? left | right :
                            op == OpCodes.Xor ? left ^ right : left * right);
                        continue;
                    }
                    if (op == OpCodes.Starg || op == OpCodes.Starg_S) { arguments[token]=Pop(); continue; }
                    if (op == OpCodes.Ldarga || op == OpCodes.Ldarga_S) arguments[token]=null;
                    if (op == OpCodes.Ldloca || op == OpCodes.Ldloca_S) locals[token]=null;
                    if (op == OpCodes.Nop || op == OpCodes.Conv_R4 || op == OpCodes.Conv_I4) continue;
                    if (n >= OpCodes.Ldc_I4_M1.Value && n <= OpCodes.Ldc_I4_8.Value) stack.Push(n - OpCodes.Ldc_I4_0.Value);
                    else if (op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S) stack.Push(token);
                    else if (n >= OpCodes.Ldarg_0.Value && n <= OpCodes.Ldarg_3.Value) stack.Push(arguments[n-OpCodes.Ldarg_0.Value]);
                    else if (op == OpCodes.Ldarg_S || op == OpCodes.Ldarg) stack.Push(arguments[token]);
                    else if (n >= OpCodes.Stloc_0.Value && n <= OpCodes.Stloc_3.Value) locals[n-OpCodes.Stloc_0.Value] = Pop();
                    else if (op == OpCodes.Stloc_S || op == OpCodes.Stloc) locals[token] = Pop();
                    else if (n >= OpCodes.Ldloc_0.Value && n <= OpCodes.Ldloc_3.Value) stack.Push(locals[n-OpCodes.Ldloc_0.Value]);
                    else if (op == OpCodes.Ldloc_S || op == OpCodes.Ldloc) stack.Push(locals[token]);
                    else if (op == OpCodes.Dup) stack.Push(stack.TryPeek(out var v) ? v : null);
                    else if (op == OpCodes.Ret)
                    {
                        var value = Pop();
                        returned = !seenReturn || returned == value ? value : null;
                        seenReturn = true;
                        break;
                    }
                    else if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
                    {
                        var target = method.Module.ResolveMethod(token, method.DeclaringType.GetGenericArguments(), method.GetGenericArguments());
                        var parameters = target.GetParameters();
                        int self = target.IsStatic || op == OpCodes.Newobj ? 0 : 1;
                        var values = new int?[parameters.Length + self];
                        for (int i=values.Length-1; i>=0; i--) values[i]=Pop();
                        int? value = null;
                        if (target is MethodInfo called)
                        {
                            if (called.DeclaringType == typeof(ModContent) && called.Name == nameof(ModContent.NPCType)
                                && npcTypes.TryGetValue(called.GetGenericArguments()[0], out int npc)) value=npc;
                            else if (called.DeclaringType == typeof(NPC) && called.Name == nameof(NPC.SpawnOnPlayer))
                                Add(values[1],item);
                            else if (called.DeclaringType == typeof(NPC) && (called.Name == nameof(NPC.NewNPC) || called.Name == nameof(NPC.NewNPCDirect)))
                            {
                                int type=Array.FindIndex(parameters,p=>string.Equals(p.Name,"type",StringComparison.OrdinalIgnoreCase));
                                if(type>=0) Add(values[type],item);
                            }
                            else if (called.DeclaringType == typeof(NetMessage) && called.Name == nameof(NetMessage.SendData)
                                && values[0] == MessageID.SpawnBossUseLicenseStartEvent) Add(values[5],item);
                            else if (called.Module.Assembly == method.Module.Assembly && (!called.IsVirtual || called.IsFinal || op == OpCodes.Call))
                                value=Read(called,item,values,depth+1,ref budget);
                            if (called.ReturnType != typeof(void)) stack.Push(value);
                        }
                        else if (op == OpCodes.Newobj) stack.Push(null);
                    }
                    else
                    {
                        // Fixed-arity arithmetic, field reads and conversions consume their
                        // operands but do not prove an NPC ID. Preserve unrelated lower slots.
                        string pop = op.StackBehaviourPop.ToString();
                        if(pop=="Varpop" || op == OpCodes.Calli) return null;
                        if(pop!="Pop0") for(int i=0;i<pop.Split('_').Length;i++) Pop();
                        if(op.StackBehaviourPush != StackBehaviour.Push0) stack.Push(null);
                        if(op.FlowControl == FlowControl.Throw) break;
                    }
                }
            }
            return returned;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException
            or TypeLoadException or MissingMemberException or BadImageFormatException or System.IO.FileNotFoundException
            or IndexOutOfRangeException) { return null; }
    }

    private static bool? Compare(OpCode op, int? left, int? right)
    {
        if (left is not int a || right is not int b) return null;
        // Short and long branches encode the same comparison.
        string name = op.Name.Replace(".s", "");
        return name switch {
            "ceq" or "beq" => a == b, "bne.un" => a != b,
            "cgt" or "bgt" => a > b, "clt" or "blt" => a < b,
            "ble" => a <= b, "bge" => a >= b,
            "cgt.un" or "bgt.un" => (uint)a > (uint)b, "clt.un" or "blt.un" => (uint)a < (uint)b,
            "ble.un" => (uint)a <= (uint)b, "bge.un" => (uint)a >= (uint)b,
            _ => null
        };
    }

    private void Add(int? npc,int item)
    {
        if(npc is int type && ContentSamples.NpcsByNetId.TryGetValue(type,out var sample) && sample.boss)
            found.Add((type,item));
    }
}

