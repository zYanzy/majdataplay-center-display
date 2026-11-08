using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// MajdataPlay 2.0.3 -- Fault counter IL patcher.
//
//   patcher <input.dll> --inspect
//       -> prints JSON: {"hasFault":bool}
//
//   patcher <input.dll> <output.dll>
//       -> injects the Fault counter (idempotent: skips if already present)
//
// This engine touches ONLY BGInfoOption and ObjectCounter. It never reads or
// writes DummyTouchPanelRenderer, so it is completely independent from the
// sensor-color patch: either one can be installed alone, or both in any order.

internal static class Program
{
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static void Usage()
    {
        Console.Error.WriteLine("usage:");
        Console.Error.WriteLine("  patcher <input.dll> --inspect");
        Console.Error.WriteLine("  patcher <input.dll> <output.dll>");
    }

    // Locate every IL anchor this patch depends on. Called both by --inspect
    // and by the real injection, so --inspect doubles as a version check.
    private static (MethodDefinition clear, MethodDefinition combo,
                    MethodDefinition output, MethodDefinition init,
                    MethodDefinition setActive, MethodReference setText,
                    AssemblyNameReference corelib)
        ValidateAnchors(TypeDefinition counter, ModuleDefinition module)
    {
        MethodDefinition Find(string name, int paramCount) =>
            counter.Methods.FirstOrDefault(m => m.Name == name && m.Parameters.Count == paramCount)
            ?? throw new Exception($"ObjectCounter.{name}/{paramCount} not found (unsupported game version?)");

        var clear = Find("Clear", 0);
        var combo = Find("UpdateComboCount", 2);
        var output = Find("UpdateInfoDisplayerOutput", 6);
        var init = Find("SetInfoDisplayerInitText", 4);
        var setActive = counter.Methods.FirstOrDefault(m => m.Name == "SetInfoDisplayerActive")
                        ?? throw new Exception("ObjectCounter.SetInfoDisplayerActive not found");

        // Reuse an existing callvirt to TMP_Text::set_text(string) so the
        // reference keeps its original scope (Unity.TextMeshPro).
        var setText = counter.Methods
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Where(i => i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .FirstOrDefault(mr => mr is not null && mr.Name == "set_text" && mr.Parameters.Count == 1)
            ?? throw new Exception("TMP_Text::set_text reference not found");

        // Core-library references must use the target module's mscorlib scope.
        var corelib = module.AssemblyReferences.FirstOrDefault(r => r.Name == "mscorlib")
                      ?? throw new Exception("mscorlib reference not found");

        return (clear, combo, output, init, setActive, setText, corelib);
    }

    private static int Run(string[] args)
    {
        if (args.Length < 1) { Usage(); return 2; }

        string input = args[0];
        bool inspect = args.Any(a => string.Equals(a, "--inspect", StringComparison.OrdinalIgnoreCase));
        string? output = (args.Length >= 2 && !args[1].StartsWith("--", StringComparison.Ordinal))
            ? args[1] : null;

        var asm = AssemblyDefinition.ReadAssembly(input, new ReaderParameters
        {
            ReadSymbols = false,
            InMemory = true
        });
        var module = asm.MainModule;

        var enumType = module.GetType("MajdataPlay.Settings.BGInfoOption")
                       ?? throw new Exception("BGInfoOption type not found (unsupported game version?)");
        var counter = module.GetType("MajdataPlay.Scenes.Game.ObjectCounter")
                      ?? throw new Exception("ObjectCounter type not found (unsupported game version?)");

        var anchors = ValidateAnchors(counter, module);
        bool hasFault = enumType.Fields.Any(f => f.Name == "Fault")
                        && counter.Fields.Any(f => f.Name == "_fault");

        if (inspect)
        {
            Console.WriteLine("{\"hasFault\":" + (hasFault ? "true" : "false") + "}");
            return 0;
        }

        if (output is null) { Usage(); return 2; }

        if (!hasFault)
        {
            // ---- 1. BGInfoOption.Fault = 16 (after None = 15) ----
            var faultEnumField = new FieldDefinition(
                "Fault",
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
                enumType) { Constant = 16 };
            enumType.Fields.Add(faultEnumField);

            // ---- 2. ObjectCounter._fault ----
            var faultField = new FieldDefinition("_fault", FieldAttributes.Private, module.TypeSystem.Int64);
            counter.Fields.Add(faultField);

            var setText = anchors.setText;
            var corelib = anchors.corelib;
            var objectRef = new TypeReference("System", "Object", module, corelib);
            var int64Ref = new TypeReference("System", "Int64", module, corelib);
            var objectToString = new MethodReference("ToString", module.TypeSystem.String, objectRef) { HasThis = true };

            // ---- 3. Clear(): _fault = 0 ----
            {
                var il = anchors.clear.Body.GetILProcessor();
                var first = anchors.clear.Body.Instructions[0];
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Conv_I8));
                il.InsertBefore(first, Instruction.Create(OpCodes.Stfld, faultField));
            }

            // ---- 4. UpdateComboCount(): decrement on Miss / Good / Great, skip Perfect + TooFast ----
            // JudgeGrade: Miss=0, LateGood=1, LateGreat*=2..4, Perfect*=5..9,
            //             FastGreat*=10..12, FastGood=13, TooFast=14
            {
                var body = anchors.combo.Body;
                var il = body.GetILProcessor();
                var first = body.Instructions[0];
                var grade = anchors.combo.Parameters[0];
                var dec = Instruction.Create(OpCodes.Ldarg_0);

                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, grade));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, 5));
                il.InsertBefore(first, Instruction.Create(OpCodes.Blt, dec));       // 0..4 -> decrement
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, grade));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, 10));
                il.InsertBefore(first, Instruction.Create(OpCodes.Blt, first));     // 5..9 -> skip
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, grade));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, 14));
                il.InsertBefore(first, Instruction.Create(OpCodes.Bge, first));     // >=14 -> skip
                il.InsertBefore(first, dec);                                       // 10..13 -> decrement
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldfld, faultField));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_1));
                il.InsertBefore(first, Instruction.Create(OpCodes.Conv_I8));
                il.InsertBefore(first, Instruction.Create(OpCodes.Sub));
                il.InsertBefore(first, Instruction.Create(OpCodes.Stfld, faultField));
            }

            // ---- 5. UpdateInfoDisplayerOutput(): render the Fault value ----
            {
                var body = anchors.output.Body;
                var il = body.GetILProcessor();
                var first = body.Instructions[0];
                var option = anchors.output.Parameters[5];
                var text = anchors.output.Parameters[0];
                var textObject = anchors.output.Parameters[1];
                var headerObject = anchors.output.Parameters[3];

                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, option));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldind_I4));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, 16));
                il.InsertBefore(first, Instruction.Create(OpCodes.Bne_Un, first));

                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, textObject));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, headerObject));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_1));
                il.InsertBefore(first, Instruction.Create(OpCodes.Call, anchors.setActive));

                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, text));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldfld, faultField));
                il.InsertBefore(first, Instruction.Create(OpCodes.Box, int64Ref));
                il.InsertBefore(first, Instruction.Create(OpCodes.Callvirt, objectToString));
                il.InsertBefore(first, Instruction.Create(OpCodes.Callvirt, setText));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ret));
            }

            // ---- 6. SetInfoDisplayerInitText(): header "Fault" ----
            {
                var body = anchors.init.Body;
                var il = body.GetILProcessor();
                var first = body.Instructions[0];
                var option = anchors.init.Parameters[3];
                var header = anchors.init.Parameters[1];
                var ret = Instruction.Create(OpCodes.Ret);

                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, option));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldind_I4));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, 16));
                il.InsertBefore(first, Instruction.Create(OpCodes.Bne_Un, first));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, header));
                il.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, ret));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, header));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, "Fault"));
                il.InsertBefore(first, Instruction.Create(OpCodes.Callvirt, setText));
                il.InsertBefore(first, ret);
            }

            anchors.clear.Body.MaxStackSize = Math.Max(anchors.clear.Body.MaxStackSize, 4);
            anchors.combo.Body.MaxStackSize = Math.Max(anchors.combo.Body.MaxStackSize, 4);
            anchors.output.Body.MaxStackSize = Math.Max(anchors.output.Body.MaxStackSize, 8);
            anchors.init.Body.MaxStackSize = Math.Max(anchors.init.Body.MaxStackSize, 4);
        }

        asm.Write(output, new WriterParameters { WriteSymbols = false });
        Console.WriteLine("OK hasFault=" + (hasFault ? "existing" : "injected"));
        return 0;
    }
}
