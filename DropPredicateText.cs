using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

// Read the rule actually called by CanDrop. Visibility predicates and mutable world flags
// must never be sampled while building a description of all possible game states.
internal static partial class DropPredicateText
{
    private sealed record Value(string Op, string Text = null, Value A = null, Value B = null,
        object Captured = null, Type Type = null);

    private static Value Atom(string text, Type type = null) => new("atom", text, Type: type ?? typeof(bool));
    private static Value Number(int n) => new("number", n.ToString(CultureInfo.InvariantCulture), Type: typeof(int));
    private static Value Capture(object obj) => obj switch {
        null => new("null"), int n => Number(n), bool b => Number(b ? 1 : 0),
        string s => new("string", s), _ => new("captured", Captured: obj, Type: obj.GetType())
    };
    private static bool Is(Value v, int n) => v?.Op == "number" && v.Text == n.ToString(CultureInfo.InvariantCulture);
    private static bool IsBool(Value v) => v?.Type == typeof(bool);
    private static Value Binary(string op, Value a, Value b)
    {
        if (op is "and" or "or") {
            int absorb = op == "and" ? 0 : 1;
            if (Is(a,absorb) || Is(b,absorb)) return Number(absorb);
            if (Is(a,1 - absorb)) return b;
            if (Is(b,1 - absorb) || a == b) return a;
        }
        return new(op, A:a, B:b,Type: op is "+" or "-" or "*" or "/" or "%" ? typeof(int) : typeof(bool));
    }
    private static Value Negate(Value v) => v.Op switch {
        "!" => v.A, "=" => Binary("!=", v.A, v.B), "!=" => Binary("=", v.A, v.B),
        "<" or ">" or ">=" or "<=" when IsFloating(v.A) || IsFloating(v.B) => new("!",A:v,Type:typeof(bool)),
        ">" => Binary("<=", v.A, v.B), "<" => Binary(">=", v.A, v.B),
        ">=" => Binary("<", v.A, v.B), "<=" => Binary(">", v.A, v.B),
        _ when Is(v,0) => Number(1), _ when Is(v,1) => Number(0),
        _ => new("!", A:v, Type:typeof(bool))
    };
    private static bool IsFloating(Value v) => v?.Type == typeof(float) || v?.Type == typeof(double);

    private static Value Compare(string op, Value a, Value b)
    {
        if (a.Op == "number" && b.Op == "number" && int.TryParse(a.Text,out int n) && int.TryParse(b.Text,out int m)) {
            if (op == "+") return Number(n + m);
            if (op == "-") return Number(n - m);
            if (op is "=" or "!=" or "<" or ">" or "<=" or ">=")
                return Number((op switch {"=" => n == m,"!=" => n != m,"<" => n < m,">" => n > m,"<=" => n <= m,_ => n >= m}) ? 1 : 0);
        }
        if (b.Op == "null" && a.Op is "null" or "captured" && op is "=" or "!=")
            return Number(((a.Op == "null") == (op == "=")) ? 1 : 0);
        if (op == "=" && IsBool(a) && Is(b,0)) return Negate(a);
        if (op == "=" && IsBool(a) && Is(b,1)) return a;
        if (op == "=" && IsBool(b) && Is(a,0)) return Negate(b);
        if (op == "=" && IsBool(b) && Is(a,1)) return b;
        if (op == "!=") return Negate(Compare("=", a, b));
        return Binary(op,a,b);
    }

    private static Value Choose(Value test, Value yes, Value no)
    {
        if (yes == no) return yes;
        if (Is(test,1)) return yes;
        if (Is(test,0)) return no;
        if (!(IsBool(yes) || Is(yes,0) || Is(yes,1)) || !(IsBool(no) || Is(no,0) || Is(no,1)))
            return new("choice",A:test,B:new("pair",A:yes,B:no),Type:yes.Type ?? no.Type);
        if (Is(yes,1) && Is(no,0)) return test;
        if (Is(no,1) && Is(yes,0)) return Negate(test);
        if (Is(no,0)) return Binary("and",test,yes);
        if (Is(yes,0)) return Binary("and",Negate(test),no);
        if (Is(yes,1)) return Binary("or",test,no);
        if (Is(no,1)) return Binary("or",Negate(test),yes);
        return Binary("or",Binary("and",test,yes),Binary("and",Negate(test),no));
    }

    public static string Describe(IItemDropRuleCondition condition) => Describe(condition,false);

    public static string Describe(IItemDropRuleCondition condition, bool negated)
    {
        if (condition == null) return null;
        try {
            var value = ReadCondition(condition);
            return Format(negated ? Negate(value) : value);
        } catch (Exception) { return null; }
    }

    internal static string KillDescription(IItemDropRuleCondition condition, int npc)
    {
        try
        {
            var root = Simplify(ReadCondition(condition));
            var terms = new List<Value>();
            void Split(Value value)
            {
                if (value.Op == "and") { Split(value.A); Split(value.B); }
                else terms.Add(value);
            }
            Split(root);
            for (int i = 0; i < terms.Count; i++)
            {
                var gate = terms[i];
                if (gate.Op != "<=" || !Is(gate.B,1)) continue;
                var names = new List<string>();
                bool Counts(Value v)
                {
                    if (v.Op == "+") return Counts(v.A) && Counts(v.B);
                    if (v.Op != "countNpcs" || v.A?.Op != "npcId") return false;
                    names.Add(v.A.Text);
                    return true;
                }
                if (!Counts(gate.A) || !names.Contains(Lang.GetNPCNameValue(npc))) continue;
                var parts = terms.Where((t,j) => j != i).Select(t => Print(t)).ToList();
                parts.Insert(0, names.Count == 1 ? "击败最后一只" + names[0] + "时掉落" :
                    string.Join("、",names) + "中最后一只被击败时掉落");
                return string.Join("\n",parts);
            }
        } catch (Exception) { }
        return null;
    }

    internal static bool OnlyEncounterGate(ModItem item)
    {
        if (item == null) return false;
        try
        {
            var method = item.GetType().GetMethod(nameof(ModItem.CanUseItem), [typeof(Player)]);
            var v = new Reader().Read(method,[Capture(item),new("player",Type:typeof(Player))]);
            bool Known(Value x) => x.Op switch {
                "!" => Known(x.A), "and" or "or" => Known(x.A) && Known(x.B),
                "anyNpc" => x.A?.Op == "npcId", "number" => Is(x,1),
                "member" when x.Captured is FieldInfo f =>
                    f.DeclaringType?.FullName == "CalamityMod.Events.BossRushEvent" && f.Name == "BossRushActive",
                _ => false
            };
            return Known(v);
        } catch (Exception) { return false; }
    }

    // Recognize a boss that uses the same NPC predicate to skip OnKill and to gate loot.
    // Match the complete early return, including its branch target, before naming the predicate.
    internal static string EncounterKillDescription(IItemDropRuleCondition condition, int npc)
    {
        try
        {
            var sample = Terraria.ID.ContentSamples.NpcsByNetId[npc];
            if (!sample.boss || sample.ModNPC == null) return null;
            var onKill = sample.ModNPC.GetType().GetMethod(nameof(ModNPC.OnKill), Type.EmptyTypes);
            var ops = Tokens(onKill).Take(5).ToArray();
            if (ops.Length != 5 || ops[0].Code != OpCodes.Ldarg_0 || ops[1].Code != OpCodes.Call ||
                onKill.Module.ResolveMethod(ops[1].Token) != typeof(ModNPC).GetProperty(nameof(ModNPC.NPC)).GetMethod ||
                ops[2].Code != OpCodes.Call || ops[4].Code != OpCodes.Ret ||
                (ops[3].Code != OpCodes.Brfalse_S && ops[3].Code != OpCodes.Brfalse) || ops[3].Token != 1)
                return null;
            var skip = onKill.Module.ResolveMethod(ops[2].Token) as MethodInfo;
            if (skip == null || !skip.IsStatic || skip.ReturnType != typeof(bool) ||
                skip.GetParameters() is not [{ParameterType: var target}] || target != typeof(NPC)) return null;
            return Format(ReadCondition(condition, killGate:skip));
        }
        catch (Exception) { return null; }
    }

    private static Value ReadCondition(IItemDropRuleCondition condition, bool forDifficulty = false, MethodInfo killGate = null)
    {
        var map = condition.GetType().GetInterfaceMap(typeof(IItemDropRuleCondition));
        int index = Array.FindIndex(map.InterfaceMethods, m => m.Name == nameof(IItemDropRuleCondition.CanDrop));
        return new Reader(forDifficulty,killGate).Read(map.TargetMethods[index], [Capture(condition), new("attempt",Type:typeof(DropAttemptInfo))]);
    }

    // Evaluate only difficulty leaves. An unknown player, biome or mod flag can still
    // satisfy an OR branch, including when the rule is reached through a failed gate.
    internal static LootDifficulty Difficulties(IItemDropRuleCondition condition)
    {
        try
        {
            bool inverted = condition is BrowserDropReport.Negated;
            var expr = ReadCondition(inverted ? ((BrowserDropReport.Negated)condition).Inner : condition, true);
            LootDifficulty allowed = 0;
            foreach (var tier in new[] { LootDifficulty.Classic, LootDifficulty.Expert, LootDifficulty.Master })
            {
                bool referenced = false;
                bool? Evaluate(Value v)
                {
                    if (v.Op == "member" && v.Captured is MemberInfo member)
                    {
                        if (member.DeclaringType == typeof(Main) || member.DeclaringType == typeof(DropAttemptInfo))
                        {
                            if (member.Name is "get_expertMode" or "IsExpertMode") { referenced = true; return tier != LootDifficulty.Classic; }
                            if (member.Name is "get_masterMode" or "IsMasterMode") { referenced = true; return tier == LootDifficulty.Master; }
                        }
                    }
                    return v.Op switch {
                        "and" => Evaluate(v.A) & Evaluate(v.B), "or" => Evaluate(v.A) | Evaluate(v.B),
                        "!" => !Evaluate(v.A), "number" when Is(v,0) => false, "number" when Is(v,1) => true,
                        _ => null
                    };
                }
                bool? result = Evaluate(expr);
                if (!referenced || result == null || result.Value != inverted) allowed |= tier;
            }
            return allowed;
        }
        catch (Exception) { return LootDifficulty.All; }
    }

    public static string Describe(Delegate predicate)
    {
        if (predicate == null) return null;
        try { return Format(new Reader().ReadDelegate(predicate, [])); }
        catch (Exception) { return null; }
    }

    private static readonly Dictionary<short,OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(op => op.Value);

    private static IEnumerable<(OpCode Code,int Token)> Tokens(MethodBase method) =>
        Instructions(method).Select(i => (i.Code,i.Token));

    internal static IEnumerable<(int Offset,int End,OpCode Code,int Token)> Instructions(MethodBase method)
    {
        var bytes = method?.GetMethodBody()?.GetILAsByteArray();
        if (bytes == null) yield break;
        int pos = 0;
        while (pos < bytes.Length) {
            int offset = pos;
            short id = bytes[pos++];
            if (id == 0xfe) id = (short)(0xfe00 | bytes[pos++]);
            var op = Codes[id];
            int count = op.OperandType switch {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2, OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes,pos), _ => 4
            };
            int token = count == 4 ? BitConverter.ToInt32(bytes,pos) : count == 1 ? (sbyte)bytes[pos] : 0;
            pos += count;
            if (op.OperandType == OperandType.InlineVar) token = BitConverter.ToUInt16(bytes,pos-count);
            if (op.OperandType == OperandType.ShortInlineVar) token = bytes[pos-count];
            yield return (offset,pos,op,token);
        }
    }

    private static Dictionary<FieldInfo,string> integrationFields;
    private static Dictionary<FieldInfo,string> IntegrationFields()
    {
        if (integrationFields != null) return integrationFields;
        var found = new Dictionary<FieldInfo,string>();
        foreach (var mod in ModLoader.Mods) {
            var method = mod.GetType().GetMethod(nameof(Mod.Load),Type.EmptyTypes);
            try {
                var history = new Queue<(OpCode Code,int Token)>();
                foreach (var (op,token) in Tokens(method)) {
                    history.Enqueue((op,token));
                    if (history.Count > 5) history.Dequeue();
                    if (op != OpCodes.Stfld || history.Count != 5) continue;
                    var seq = history.ToArray();
                    if (seq[0].Code != OpCodes.Ldstr || seq[1].Code != OpCodes.Ldarg_0 ||
                        seq[2].Code != OpCodes.Ldflda || seq[3].Code != OpCodes.Call) continue;
                    var call = method.Module.ResolveMethod(seq[3].Token);
                    if (call.DeclaringType == typeof(ModLoader) && call.Name == nameof(ModLoader.TryGetMod))
                        found[method.Module.ResolveField(token)] = method.Module.ResolveString(seq[0].Token);
                }
            } catch (Exception) { }
        }
        return integrationFields = found;
    }

    private sealed class Reader(bool forDifficulty = false, MethodInfo killGate = null)
    {
        private int steps;
        private int depth;

        public Value ReadDelegate(Delegate del, Value[] args)
        {
            if (del.GetInvocationList().Length != 1) throw new NotSupportedException();
            return Read(del.Method, del.Method.IsStatic ? args : [Capture(del.Target), ..args]);
        }

        public Value Read(MethodInfo method, Value[] args)
        {
            if (++depth > 12) throw new NotSupportedException();
            try {
                var body = method.GetMethodBody();
                var il = body?.GetILAsByteArray();
                if (il == null || il.Length > 900 || body.ExceptionHandlingClauses.Count != 0)
                    throw new NotSupportedException();
                return Walk(0, [], new Value[body.LocalVariables.Count]);

                Value Walk(int pc, List<Value> stack, Value[] locals, int splits = 0)
                {
                    if (splits > 48) throw new NotSupportedException();
                    Value Pop() { var last = stack[^1]; stack.RemoveAt(stack.Count - 1); return last; }
                    Value[] PopArgs(int n) {
                        var list = stack.GetRange(stack.Count - n,n).ToArray();
                        stack.RemoveRange(stack.Count - n,n);
                        return list;
                    }
                    while (++steps <= 2000)
                    {
                        short id = il[pc++];
                        if (id == 0xfe) id = (short)(0xfe00 | il[pc++]);
                        var op = Codes[id];
                        string name = op.Name;
                        int data = 0, target = -1;
                        Type[] owners = method.DeclaringType?.GetGenericArguments(), types = method.GetGenericArguments();
                        switch (op.OperandType) {
                            case OperandType.InlineNone: break;
                            case OperandType.ShortInlineVar: data = il[pc++]; break;
                            case OperandType.ShortInlineI: data = (sbyte)il[pc++]; break;
                            case OperandType.InlineVar: data = BitConverter.ToUInt16(il,pc); pc += 2; break;
                            case OperandType.ShortInlineBrTarget: data = (sbyte)il[pc++]; target = pc + data; break;
                            case OperandType.InlineBrTarget: data = BitConverter.ToInt32(il,pc); pc += 4; target = pc + data; break;
                            case OperandType.ShortInlineR:
                                stack.Add(new("number",BitConverter.ToSingle(il,pc).ToString("R",CultureInfo.InvariantCulture),Type:typeof(float)));
                                pc += 4; continue;
                            case OperandType.InlineR:
                                stack.Add(new("number",BitConverter.ToDouble(il,pc).ToString("R",CultureInfo.InvariantCulture),Type:typeof(double)));
                                pc += 8; continue;
                            case OperandType.InlineI: case OperandType.InlineField: case OperandType.InlineMethod:
                            case OperandType.InlineType: case OperandType.InlineString:
                                data = BitConverter.ToInt32(il,pc); pc += 4; break;
                            default: throw new NotSupportedException();
                        }
                        if (target >= 0) {
                            var branch = name.Replace(".s","");
                            if (branch == "br") { pc = target; continue; }
                            Value condition;
                            if (branch is "brtrue" or "brfalse") {
                                condition = Pop();
                                if (condition.Op == "null") condition = Number(0);
                                else if (condition.Op is "captured" or "string" or "array") condition = Number(1);
                                else if (condition.Type != null && !condition.Type.IsValueType)
                                    condition = Compare("!=",condition,Capture(null));
                                else if (!IsBool(condition) && !Is(condition,0) && !Is(condition,1))
                                    condition = Compare("!=",condition,Number(0));
                                if (branch == "brfalse") condition = Negate(condition);
                            } else {
                                var b = Pop(); var a = Pop();
                                condition = Compare(branch switch {
                                    "beq" => "=", "bne.un" => "!=", "bge" => ">=", "bgt" => ">",
                                    "ble" => "<=", "blt" => "<", _ => throw new NotSupportedException()
                                },a,b);
                            }
                            if (Is(condition,0)) continue;
                            if (Is(condition,1)) { pc = target; continue; }
                            if (stack.Concat(locals).Any(v => v?.Op is "array" or "localRef"))
                                throw new NotSupportedException();
                            return Choose(condition, Walk(target,[..stack],(Value[])locals.Clone(),splits + 1), Walk(pc,stack,locals,splits + 1));
                        }
                        if (name.StartsWith("ldarg")) { stack.Add(args[Index(name,data)]); continue; }
                        if (name.StartsWith("ldloca")) { stack.Add(new("localRef",Captured:locals,Text:data.ToString())); continue; }
                        if (name.StartsWith("ldloc")) { stack.Add(locals[Index(name,data)] ?? throw new NotSupportedException()); continue; }
                        if (name.StartsWith("stloc")) { locals[Index(name,data)] = Pop(); continue; }
                        if (name.StartsWith("ldc.i4")) { stack.Add(Number(name == "ldc.i4.m1" ? -1 : Index(name,data))); continue; }
                        switch (name) {
                            case "nop": break;
                            case "ret": return stack.Count == 1 ? Pop() : throw new NotSupportedException();
                            case "ldnull": stack.Add(Capture(null)); break;
                            case "ldstr": stack.Add(Capture(method.Module.ResolveString(data))); break;
                            case "initobj":
                                var address = Pop();
                                if (address.Op != "localRef" || address.Captured is not Value[] localValues)
                                    throw new NotSupportedException();
                                localValues[int.Parse(address.Text,CultureInfo.InvariantCulture)] = Capture(null); break;
                            case "dup": stack.Add(stack[^1]); break;
                            case "pop": Pop(); break;
                            case "conv.i4": case "conv.u4":
                                if (stack[^1].Type != typeof(int)) throw new NotSupportedException();
                                break;
                            case "ldlen":
                                if (Pop().Captured is not Array sized) throw new NotSupportedException();
                                stack.Add(Number(sized.Length)); break;
                            case "ldfld": case "ldsfld": case "ldflda": case "ldsflda":
                                var field = method.Module.ResolveField(data,owners,types);
                                stack.Add(Field(field,field.IsStatic ? null : Pop())); break;
                            case "call": case "callvirt":
                                var called = (MethodInfo)method.Module.ResolveMethod(data,owners,types);
                                var parms = PopArgs(called.GetParameters().Length);
                                var instance = called.IsStatic ? null : Pop();
                                stack.Add(Call(called,instance,parms)); break;
                            case "ceq": case "clt": case "clt.un": case "cgt": case "cgt.un": case "add": case "sub":
                            case "mul": case "div": case "rem": case "and": case "or":
                                var right = Pop(); var left = Pop();
                                if (name == "cgt.un" && right.Op == "null") { stack.Add(Compare("!=", left, right)); break; }
                                if (name is "clt.un" or "cgt.un" && (left.Type == typeof(float) || right.Type == typeof(float) ||
                                    left.Type == typeof(double) || right.Type == typeof(double))) {
                                    stack.Add(new("!",A:Compare(name == "clt.un" ? ">=" : "<=",left,right),Type:typeof(bool))); break;
                                }
                                if (name is "and" or "or" && !IsBool(left) && !IsBool(right)) throw new NotSupportedException();
                                stack.Add(Compare(name switch {
                                    "ceq" => "=", "clt" => "<", "cgt" => ">", "add" => "+", "sub" => "-",
                                    "mul" => "*", "div" => "/", "rem" => "%", "or" => "or", "and" => "and",
                                    _ => throw new NotSupportedException()
                                },left,right)); break;
                            case "newarr":
                                var length = Pop();
                                if (length.Op != "number" || !int.TryParse(length.Text,out int size) || size < 0 || size > 64)
                                    throw new NotSupportedException();
                                stack.Add(new("array",Captured:new Value[size])); break;
                            case "stelem.ref":
                                var value = Pop(); var offset = Pop(); var array = Pop();
                                if (array.Captured is not Value[] values || offset.Op != "number") throw new NotSupportedException();
                                values[int.Parse(offset.Text,CultureInfo.InvariantCulture)] = value; break;
                            case "ldelem.i4": case "ldelem.r4": case "ldelem.ref": case "ldelem.u1":
                                var index = Pop(); var items = Pop();
                                if (items.Captured is string[] names && index.Op == "number")
                                    stack.Add(Capture(names[int.Parse(index.Text,CultureInfo.InvariantCulture)]));
                                else stack.Add(new("index",A:items,B:index,Type:items.Type?.GetElementType()));
                                break;
                            case "unbox.any": case "castclass": case "box":
                                var cast = method.Module.ResolveType(data,owners,types);
                                stack.Add(Pop() with {Type = cast}); break;
                            case "isinst":
                                var wanted = method.Module.ResolveType(data,owners,types);
                                var subject = Pop();
                                stack.Add(new("typed",wanted.Name,A:subject,Type:wanted)); break;
                            default: throw new NotSupportedException();
                        }
                    }
                    throw new NotSupportedException();
                }
            } finally { --depth; }
        }

        private static int Index(string opcode, int operand) =>
            opcode.Length >= 2 && opcode[^2] == '.' && opcode[^1] is >= '0' and <= '8' ? opcode[^1] - '0' : operand;

        private static Value Field(FieldInfo field, Value owner)
        {
            var type = field.FieldType;
            // Only traverse fields on the registered condition/closure. Leaves on the player,
            // NPC, world, configuration or any other live object retain symbolic meaning.
            if (owner?.Captured is { } root && (root is IItemDropRuleCondition ||
                root.GetType().IsDefined(typeof(CompilerGeneratedAttribute),false)))
                return Capture(field.GetValue(root));
            if (typeof(Mod).IsAssignableFrom(field.DeclaringType)) {
                if (typeof(Mod).IsAssignableFrom(type) && (field.IsStatic || owner?.Captured is Mod))
                    return Capture(field.GetValue(field.IsStatic ? null : owner.Captured));
                if (typeof(ModBiome).IsAssignableFrom(type) && (field.IsStatic || owner?.Captured is Mod))
                    return Capture(field.GetValue(field.IsStatic ? null : owner.Captured));
                if (owner?.Captured is Mod && type == typeof(bool) && IntegrationFields().TryGetValue(field,out var modName))
                    return Number(ModLoader.HasMod(modName) ? 1 : 0);
            }
            if (type == typeof(NPC)) return new("npc",Type:type);
            if (type == typeof(Player)) return new("player",Type:type);
            var label = PredicateVocabulary.Member(field);
            if (label != null) return new("member", label, owner, Captured:field, Type:type);
            if (type == typeof(Condition) && field.IsStatic && field.DeclaringType == typeof(Condition))
                return Capture(field.GetValue(null));
            return new("member",field.Name,owner,Captured:field,Type:type);
        }

        private Value Call(MethodInfo method, Value owner, Value[] args)
        {
            if (method == killGate && args is [{Op:"npc"}]) return Negate(Atom("击败首领并完成战斗时掉落"));
            if (owner?.Captured is Delegate del && method.Name == "Invoke") return ReadDelegate(del,args);
            if (method.DeclaringType == typeof(string)) {
                if (method.Name is "op_Equality" or "op_Inequality" && args.Length == 2)
                    return Compare(method.Name == "op_Equality" ? "=" : "!=",args[0],args[1]);
                if (method.Name is "ToLower" or "ToLowerInvariant" && args.Length == 0)
                    return new("lower",A:owner,Type:typeof(string));
                if (method.Name == "Contains" && args is [{Op:"string"} fragment])
                    return new("contains",A:owner,B:fragment,Type:typeof(bool));
            }
            if (owner?.Captured is Condition condition && method.Name == nameof(Condition.IsMet)) {
                string text = condition.Description?.Value;
                return !forDifficulty && !string.IsNullOrWhiteSpace(text) ? Atom(text) : ReadDelegate(condition.Predicate,[]);
            }
            var type = method.DeclaringType;
            if (type == typeof(NPC) && method.Name == "get_ModNPC") return new("instance","目标 NPC",A:owner,Type:method.ReturnType);
            if (type == typeof(ModNPC) && method.Name == "get_NPC") return new("npc",Type:typeof(NPC));
            if (type == typeof(ModPlayer) && method.Name == "get_Player") return new("player",Type:typeof(Player));
            if (method.IsGenericMethod && (type == typeof(Player) && method.Name == "GetModPlayer" ||
                type == typeof(NPC) && method.Name == "GetGlobalNPC"))
                return new("instance",method.GetGenericArguments()[0].Name,A:owner,Type:method.ReturnType);
            if (method.IsGenericMethod && type == typeof(Player) && method.Name == "InModBiome") {
                var biome = ModContent.GetContent<ModBiome>().FirstOrDefault(b => b.GetType() == method.GetGenericArguments()[0]);
                if (biome != null) return Atom("位于" + biome.DisplayName.Value);
            }
            if (type == typeof(Player) && method.Name == "InModBiome" && args is [{Captured:ModBiome registered}])
                return Atom("位于" + registered.DisplayName.Value);
            if (type == typeof(ModContent) && method.IsGenericMethod) {
                var entity = method.GetGenericArguments()[0];
                if (method.Name == nameof(ModContent.NPCType)) return new("npcId",PredicateVocabulary.Npc(entity),Type:typeof(int));
                if (method.Name == nameof(ModContent.ItemType)) return new("itemId",PredicateVocabulary.Item(entity),Type:typeof(int));
                if (method.Name == nameof(ModContent.GetInstance)) return new("instance",entity.Name,Type:entity);
            }
            if (type == typeof(NPC) && args.Length == 1 && method.Name is nameof(NPC.CountNPCS) or nameof(NPC.AnyNPCs)) {
                var npc = args[0];
                if (npc.Op == "number" && int.TryParse(npc.Text,out int id) && ContentSamples.NpcsByNetId.ContainsKey(id))
                    npc = new("npcId",Lang.GetNPCNameValue(id),Type:typeof(int));
                return new(method.Name == nameof(NPC.CountNPCS) ? "countNpcs" : "anyNpc",A:npc,Type:method.ReturnType);
            }
            if (type == typeof(ModLoader) && method.Name == nameof(ModLoader.HasMod) && args is [{Op:"string"}])
                return Number(ModLoader.HasMod(args[0].Text) ? 1 : 0);
            if (type == typeof(ModLoader) && method.Name == nameof(ModLoader.TryGetMod) &&
                args is [{Op:"string"} modName, {Op:"localRef", Captured:Value[] slots} addr]) {
                bool found = ModLoader.TryGetMod(modName.Text,out var foundMod);
                slots[int.Parse(addr.Text,CultureInfo.InvariantCulture)] = Capture(foundMod);
                return Number(found ? 1 : 0);
            }
            if (owner?.Captured is Mod source && method.Name == nameof(Mod.TryFind) && method.IsGenericMethod &&
                args is [{Op:"string"} contentKey, {Op:"localRef",Captured:Value[] contentSlots} contentAddr]) {
                object content;
                var kind = method.GetGenericArguments()[0];
                if (kind == typeof(ModNPC)) { source.TryFind<ModNPC>(contentKey.Text,out var npc); content = npc; }
                else if (kind == typeof(ModItem)) { source.TryFind<ModItem>(contentKey.Text,out var item); content = item; }
                else throw new NotSupportedException();
                contentSlots[int.Parse(contentAddr.Text,CultureInfo.InvariantCulture)] = Capture(content);
                return Number(content != null ? 1 : 0);
            }
            if (method.Name == "get_Type" && args.Length == 0) {
                if (owner?.Captured is ModNPC npc) return new("npcId",npc.DisplayName.Value,Type:typeof(int));
                if (owner?.Captured is ModItem item) return new("itemId",item.DisplayName.Value,Type:typeof(int));
            }
            if (owner?.Captured is Mod mod && method.Name == nameof(Mod.Call) &&
                args is [{Captured: Value[] parameters}] && parameters is [{Op:"string"} request, {Op:"string"} key] &&
                request.Text is "Downed" or "GetDownedBoss" or "GetBossDowned" or "GetDifficultyActive")
                return request.Text == "GetDifficultyActive" ? Atom(PredicateVocabulary.Difficulty(mod,key.Text)) :
                    Atom("已击败" + PredicateVocabulary.Boss(mod.Name,key.Text));
            if (typeof(Mod).IsAssignableFrom(type) && method.Name == nameof(Mod.Call)) {
                if (owner?.Captured is Mod api && args is [{Captured:Value[] command}] && command is [{Op:"string"} verb, ..]) {
                    if (command is [_, {Op:"npc"}] && verb.Text.StartsWith("Get",StringComparison.Ordinal) &&
                        verb.Text.EndsWith("AI",StringComparison.OrdinalIgnoreCase))
                        return new("atom", "目标的模组 AI 状态",Type:typeof(float[]));
                    var details = string.Join("、",command.Skip(1).Select(arg => Print(arg)));
                    return Atom(api.DisplayName + " 的 " + verb.Text + (details.Length == 0 ? "" : "（" + details + "）"));
                }
                throw new NotSupportedException();
            }
            if (method.IsSpecialName && method.Name.StartsWith("get_")) {
                var label = Label(method);
                if (label != null) return new("member",label,Captured:method,Type:method.ReturnType);
            }
            if (method.ReturnType == typeof(void)) throw new NotSupportedException();
            if (method.IsVirtual && !method.IsFinal && !type.IsSealed) throw new NotSupportedException();
            return Read(method,owner == null ? args : [owner,..args]);
        }
    }
}
