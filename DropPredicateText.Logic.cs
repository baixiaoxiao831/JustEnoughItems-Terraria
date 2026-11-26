using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace JustEnoughItemsTerraria;

internal static partial class DropPredicateText
{
    // The C# compiler emits jumps for both sides of a short-circuit operation. Minimize
    // the resulting small Boolean function, preserving its value for every assignment.
    private static Value Simplify(Value root)
    {
        if (root.Op is not ("and" or "or" or "!")) return root;
        var leaves = new List<Value>();
        void Collect(Value e) {
            if (e.Op is "and" or "or") { Collect(e.A); Collect(e.B); }
            else if (e.Op == "!") Collect(e.A);
            else if (!Is(e,0) && !Is(e,1)) {
                var atom = e.Op == "!=" ? Binary("=", e.A,e.B) : e;
                if (!leaves.Contains(atom)) leaves.Add(atom);
            }
        }
        Collect(root);
        if (leaves.Count > 7) return root;
        bool Eval(Value e,int mask) => e.Op switch {
            "and" => Eval(e.A,mask) && Eval(e.B,mask), "or" => Eval(e.A,mask) || Eval(e.B,mask),
            "!" => !Eval(e.A,mask), "!=" => !Eval(Binary("=",e.A,e.B),mask),
            _ when Is(e,0) => false, _ when Is(e,1) => true,
            _ => (mask & (1 << leaves.IndexOf(e))) != 0
        };
        var reduced = Reduce(leaves.ToArray(), Enumerable.Range(0,1 << leaves.Count).Select(mask => Eval(root,mask)).ToArray());
        return Print(reduced).Length <= Print(root).Length ? reduced : root;
    }

    private static Value Reduce(Value[] vars, bool[] table)
    {
        if (table.All(v => v)) return Number(1);
        if (table.All(v => !v)) return Number(0);
        if (vars.Length == 1) return table[1] ? vars[0] : Negate(vars[0]);

        for (int i = 0; i < vars.Length; i++) {
            int bit = 1 << i;
            var low = new bool[table.Length / 2];
            var high = new bool[low.Length];
            for (int j = 0; j < low.Length; j++) {
                int mask = (j % bit) + ((j / bit) * bit * 2);
                low[j] = table[mask]; high[j] = table[mask | bit];
            }
            var rest = vars.Where((_,j) => j != i).ToArray();
            if (low.SequenceEqual(high)) return Reduce(rest,low);
            if (low.All(v => !v)) return Binary("and",vars[i],Reduce(rest,high));
            if (high.All(v => !v)) return Binary("and",Negate(vars[i]),Reduce(rest,low));
            if (high.All(v => v)) return Binary("or",vars[i],Reduce(rest,low));
            if (low.All(v => v)) return Binary("or",Negate(vars[i]),Reduce(rest,high));
        }

        for (int k = 2; k < vars.Length; k++)
            if (table.Select((v,i) => v == (BitOperations.PopCount((uint)i) >= k)).All(v => v))
                return Atom(JeiText.Get("AtLeast2") + k + JeiText.Get("OfTheseConditions") + string.Join(JeiText.Get("ListSeparator"),vars.Select(v => Print(v))) + JeiText.Get("CloseParen"));

        // Test independent subexpressions before spelling out alternatives. For instance
        // (A or B) and (C or D) is much clearer than four repeated pairs.
        for (int mask = 1; mask < table.Length - 1; mask += 2) {
            int[] left = Enumerable.Range(0,vars.Length).Where(i => (mask & (1 << i)) != 0).ToArray();
            int[] right = Enumerable.Range(0,vars.Length).Except(left).ToArray();
            int Project(int value,int[] bits) {
                int projected = 0;
                for (int i = 0; i < bits.Length; i++) projected |= ((value >> bits[i]) & 1) << i;
                return projected;
            }
            foreach (bool conjunction in new[]{true,false}) {
                var a = new bool[1 << left.Length]; var b = new bool[1 << right.Length];
                for (int i = 0; i < table.Length; i++)
                    if (table[i] == conjunction) { a[Project(i,left)] = true; b[Project(i,right)] = true; }
                if (Enumerable.Range(0,table.Length).Any(i =>
                    (a[Project(i,left)] && b[Project(i,right)]) != (table[i] == conjunction))) continue;
                if (!conjunction) {
                    for (int i = 0; i < a.Length; i++) a[i] = !a[i];
                    for (int i = 0; i < b.Length; i++) b[i] = !b[i];
                }
                return Binary(conjunction ? "and" : "or",Reduce(left.Select(i => vars[i]).ToArray(),a),
                    Reduce(right.Select(i => vars[i]).ToArray(),b));
            }
        }

        // Pick largest valid cubes until all true cases are covered. Every cube is
        // independently checked against all false cases, including unvisited combinations.
        var remaining = Enumerable.Range(0,table.Length).Where(i => table[i]).ToHashSet();
        var cover = new List<(int Bits,int Values)>();
        while (remaining.Count > 0) {
            int bestBits = -1, bestValues = 0, bestCount = 0;
            foreach (int seed in remaining)
                for (int bits = 1; bits < table.Length; bits++) {
                    int values = seed & bits;
                    int covered = remaining.Count(v => (v & bits) == values);
                    if (covered < bestCount || covered == bestCount &&
                        BitOperations.PopCount((uint)bits) >= BitOperations.PopCount((uint)bestBits)) continue;
                    if (Enumerable.Range(0,table.Length).Any(v => (v & bits) == values && !table[v])) continue;
                    bestBits = bits; bestValues = values; bestCount = covered;
                }
            cover.Add((bestBits,bestValues));
            remaining.RemoveWhere(v => (v & bestBits) == bestValues);
        }
        for (int i = cover.Count - 1; i >= 0; --i) {
            var cube = cover[i];
            if (Enumerable.Range(0,table.Length).Where(v => (v & cube.Bits) == cube.Values)
                .All(v => cover.Any(other => cube != other && (v & other.Bits) == other.Values)))
                cover.RemoveAt(i);
        }
        return cover.Select(cube => vars.Select((v,i) => (v,bit:1 << i))
            .Where(t => (t.bit & cube.Bits) != 0)
            .Select(t => (t.bit & cube.Values) != 0 ? t.v : Negate(t.v))
            .Aggregate((a,b) => Binary("and",a,b))).Aggregate((a,b) => Binary("or",a,b));
    }
}
