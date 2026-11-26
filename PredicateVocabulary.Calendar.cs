using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Terraria;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

internal static partial class PredicateVocabulary
{
    // Seasonal flags can retain their last value across updates. Describe their
    // initialization and the later assignments rather than evaluating today's date.
    private static string CalendarFlag(FieldInfo flag, Mod mod)
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var load = mod.GetType().GetMethod(nameof(Mod.Load), declared);
        if (!WritesField(load,flag)) return null;
        using var init = new DynamicMethodDefinition(load);
        var body = init.Definition.Body.Instructions;
        var sets = body.Where(i => i.OpCode.Code == Code.Stsfld && IsField(i,flag)).ToArray();
        if (sets.Length != 1) return null;
        var store = sets[0];
        var on = store.Previous;
        var compare = on?.Previous?.Previous;
        if (on?.OpCode.Code != Code.Ldc_I4_1 || on.Previous?.Operand != store || compare?.OpCode.Code != Code.Ceq ||
            compare.Previous?.Previous?.Operand is not MethodReference { DeclaringType.FullName:"System.DateTime" } part)
            return null;
        var number = compare.Previous;
        int date = number.OpCode.Code switch {
            >= Code.Ldc_I4_0 and <= Code.Ldc_I4_8 => number.OpCode.Code - Code.Ldc_I4_0,
            Code.Ldc_I4 or Code.Ldc_I4_S => Convert.ToInt32(number.Operand), _ => -1
        };
        string calendar = part.Name switch {
            "get_Month" when date is >= 1 and <= 12 => JeiText.Get("Month", date),
            "get_Day" when date is >= 1 and <= 31 => JeiText.Get("DayOfTheMonth", date), _ => null
        };
        if (calendar == null) return null;
        // The DateTime struct returned by Now is held in a local for its getter.
        var now = number.Previous?.Previous?.Previous?.Previous;
        if (now?.Operand is not MethodReference { DeclaringType.FullName:"System.DateTime", Name:"get_Now" } ||
            now.Previous?.Operand != on || now.Previous.OpCode.Code is not (Code.Brtrue or Code.Brtrue_S) ||
            now.Previous.Previous?.Operand is not FieldReference { FieldType.FullName:"Terraria.ModLoader.Mod" } optional) return null;
        var names = body.Where(i => i.Operand is MethodReference { Name:"TryGetMod", DeclaringType.FullName:"Terraria.ModLoader.ModLoader" } &&
                i.Previous?.Operand is FieldReference rf && rf.FullName == optional.FullName)
            .Select(i => (i.Previous.Previous?.OpCode.Code == Code.Ldarg_0 ? i.Previous.Previous.Previous : i.Previous.Previous)?.Operand as string)
            .Where(name => name != null).Distinct().ToArray();
        if (names.Length != 1) return null;
        string optionalName = ModLoader.TryGetMod(names[0],out var optionalMod) ? optionalMod.DisplayName : names[0];
        string setup = JeiText.Get("EnabledIfModsLoadDuringOrIs", calendar, optionalName);
        var updates = mod.GetContent<ModSystem>().SelectMany(system => system.GetType().GetMethods(declared))
            .Where(hook => (hook.Name.StartsWith("PreUpdate",StringComparison.Ordinal) ||
                            hook.Name.StartsWith("PostUpdate",StringComparison.Ordinal)) && WritesField(hook,flag)).ToArray();
        if (updates.Length == 0) return JeiText.Get("CalendarEventActive", setup);
        if (updates.Length != 1) return null;
        using var update = new DynamicMethodDefinition(updates[0]);
        var writes = update.Definition.Body.Instructions.Where(i => i.OpCode.Code == Code.Stsfld && IsField(i,flag)).ToArray();
        if (writes.Length != 2) return null;
        var yes = Guard(writes,Code.Ldc_I4_1);
        var no = Guard(writes,Code.Ldc_I4_0);
        if (yes?.OpCode.Code is not (Code.Brfalse or Code.Brfalse_S) || no?.OpCode.Code is not (Code.Brtrue or Code.Brtrue_S) ||
            yes.Previous?.Operand is not MethodReference setting || no.Previous?.Operand is not MethodReference same ||
            same.FullName != setting.FullName || no.Previous.Previous != yes.Operand) return null;
        var worldCheck = yes.Previous.Previous?.Previous;
        if (worldCheck?.Operand != yes.Operand || worldCheck.OpCode.Code is not (Code.Brfalse or Code.Brfalse_S) ||
            worldCheck.Previous?.Operand is not FieldReference { DeclaringType.FullName:"Terraria.Main" } world) return null;
        string worldName = Member(typeof(Main).GetField(world.Name,BindingFlags.Static | BindingFlags.Public));
        var config = mod.Code.GetType(setting.DeclaringType.FullName.Replace('/','+'));
        string option = config == null ? null : Member(config.GetMethod(setting.Name,BindingFlags.Instance | BindingFlags.Public));
        if (option == null || worldName == null) return null;
        return JeiText.Get("CalendarEventActiveTurnOffToStop", setup, option, worldName);
    }

    private static Instruction Guard(Instruction[] stores, Code value)
    {
        var entry = stores.FirstOrDefault(i => i.Previous?.OpCode.Code == value)?.Previous;
        if (entry == null) return null;
        while (entry.Previous?.OpCode.Code == Code.Stsfld && entry.Previous.Previous?.OpCode.Code == value)
            entry = entry.Previous.Previous;
        return entry.Previous;
    }
}
