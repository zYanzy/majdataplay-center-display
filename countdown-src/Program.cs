using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// MajdataPlay 2.0.3 -- Countdown (remaining chart time) IL patcher.
//
//   patcher <input.dll> --inspect
//       -> prints JSON: {"hasCountdown":bool}
//
//   patcher <input.dll> <output.dll>
//       -> injects BGInfoOption.Countdown = 17 and renders the remaining time
//          as "M:SS" in any BGInfo / SecondaryBGInfo / SubScreenBGInfo slot.
//          Idempotent: skips injection when already present.
//
// This engine is independent from the Fault patch. It only adds its own enum
// field and its own method prologues to BGInfoOption / ObjectCounter. Both
// engines use "jump to the current first instruction when the option does not
// match", so successive prologues form a chain automatically and work in any
// install order. It never touches DummyTouchPanelRenderer (sensor color).

internal static class Program
{
    // BGInfoOption originally has 0..15 (None = 15); Fault uses 16.
    private const int CountdownValue = 17;
    private const string HeaderText = "TIME LEFT";
    private const string TimeFormat = "m\\:ss";

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

    // Locate every IL anchor this patch depends on. Called by --inspect too,
    // so --inspect doubles as a version-compatibility check.
    private static (MethodDefinition output, MethodDefinition init,
                    MethodDefinition setActive, MethodReference setText,
                    FieldDefinition gpManager, MethodDefinition getAudioLength,
                    MethodDefinition getThisFrameSec, AssemblyNameReference corelib)
        ValidateAnchors(TypeDefinition counter, ModuleDefinition module)
    {
        MethodDefinition Find(string name, int paramCount) =>
            counter.Methods.FirstOrDefault(m => m.Name == name && m.Parameters.Count == paramCount)
            ?? throw new Exception($"ObjectCounter.{name}/{paramCount} not found (unsupported game version?)");

        var output = Find("UpdateInfoDisplayerOutput", 6);
        var init = Find("SetInfoDisplayerInitText", 4);
        var setActive = counter.Methods.FirstOrDefault(m => m.Name == "SetInfoDisplayerActive")
                        ?? throw new Exception("ObjectCounter.SetInfoDisplayerActive not found");

        // ObjectCounter already holds the GamePlayManager instance (assigned in
        // its initialization, before any info displayer output runs).
        var gpManager = counter.Fields.FirstOrDefault(f => f.Name == "_gpManager")
                        ?? throw new Exception("ObjectCounter._gpManager not found");

        var setText = counter.Methods
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Where(i => i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .FirstOrDefault(mr => mr is not null && mr.Name == "set_text" && mr.Parameters.Count == 1)
            ?? throw new Exception("TMP_Text::set_text reference not found");

        var gp = module.GetType("MajdataPlay.Scenes.Game.GamePlayManager")
                 ?? throw new Exception("GamePlayManager type not found (unsupported game version?)");
        var getAudioLength = gp.Methods.FirstOrDefault(m => m.Name == "get_AudioLength")
                             ?? throw new Exception("GamePlayManager.get_AudioLength not found");
        var getThisFrameSec = gp.Methods.FirstOrDefault(m => m.Name == "get_ThisFrameSec")
                              ?? throw new Exception("GamePlayManager.get_ThisFrameSec not found");

        // Core-library references must use the target module's mscorlib scope.
        var corelib = module.AssemblyReferences.FirstOrDefault(r => r.Name == "mscorlib")
                      ?? throw new Exception("mscorlib reference not found");

        return (output, init, setActive, setText, gpManager,
                getAudioLength, getThisFrameSec, corelib);
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
        bool hasCountdown = enumType.Fields.Any(f => f.Name == "Countdown");

        if (inspect)
        {
            Console.WriteLine("{\"hasCountdown\":" + (hasCountdown ? "true" : "false") + "}");
            return 0;
        }

        if (output is null) { Usage(); return 2; }

        if (!hasCountdown)
        {
            // ---- 1. BGInfoOption.Countdown = 17 ----
            var countdownEnumField = new FieldDefinition(
                "Countdown",
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
                enumType) { Constant = CountdownValue };
            enumType.Fields.Add(countdownEnumField);

            var corelib = anchors.corelib;
            var timeSpanRef = new TypeReference("System", "TimeSpan", module, corelib);
            var mathRef = new TypeReference("System", "Math", module, corelib);

            var fromSeconds = new MethodReference("FromSeconds", timeSpanRef, timeSpanRef)
            { HasThis = false };
            fromSeconds.Parameters.Add(new ParameterDefinition(module.TypeSystem.Double));

            var tsToString = new MethodReference("ToString", module.TypeSystem.String, timeSpanRef)
            { HasThis = true };
            tsToString.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

            var mathMax = new MethodReference("Max", module.TypeSystem.Single, mathRef)
            { HasThis = false };
            mathMax.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
            mathMax.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));

            // ---- 2. UpdateInfoDisplayerOutput(): render remaining time ----
            // All prologues branch to the instruction that was Instructions[0]
            // at insertion time. When the Fault patch is already present, that
            // instruction is the first instruction of the Fault prologue, so
            // the two prologues chain instead of short-circuiting each other.
            {
                var body = anchors.output.Body;
                var il = body.GetILProcessor();
                var first = body.Instructions[0];
                var option = anchors.output.Parameters[5];
                var text = anchors.output.Parameters[0];
                var textObject = anchors.output.Parameters[1];
                var headerObject = anchors.output.Parameters[3];

                var tmp = new VariableDefinition(timeSpanRef);
                body.Variables.Add(tmp);
                body.InitLocals = true;

                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, option));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldind_I4));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, CountdownValue));
                il.InsertBefore(first, Instruction.Create(OpCodes.Bne_Un, first));

                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, textObject));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, headerObject));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_1));
                il.InsertBefore(first, Instruction.Create(OpCodes.Call, anchors.setActive));

                // text.text = TimeSpan.FromSeconds(
                //     Math.Max(0, _gpManager.AudioLength - _gpManager.ThisFrameSec))
                //     .ToString("m\\:ss");
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, text));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldfld, anchors.gpManager));
                il.InsertBefore(first, Instruction.Create(OpCodes.Callvirt, anchors.getAudioLength));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldfld, anchors.gpManager));
                il.InsertBefore(first, Instruction.Create(OpCodes.Callvirt, anchors.getThisFrameSec));
                il.InsertBefore(first, Instruction.Create(OpCodes.Sub));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_R4, 0f));
                il.InsertBefore(first, Instruction.Create(OpCodes.Call, mathMax));
                il.InsertBefore(first, Instruction.Create(OpCodes.Conv_R8));
                il.InsertBefore(first, Instruction.Create(OpCodes.Call, fromSeconds));
                il.InsertBefore(first, Instruction.Create(OpCodes.Stloc, tmp));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldloca, tmp));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, TimeFormat));
                il.InsertBefore(first, Instruction.Create(OpCodes.Call, tsToString));
                il.InsertBefore(first, Instruction.Create(OpCodes.Callvirt, anchors.setText));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ret));

                body.MaxStackSize = Math.Max(body.MaxStackSize, 8);
            }

            // ---- 3. SetInfoDisplayerInitText(): header "TIME LEFT" ----
            {
                var body = anchors.init.Body;
                var il = body.GetILProcessor();
                var first = body.Instructions[0];
                var option = anchors.init.Parameters[3];
                var header = anchors.init.Parameters[1];
                var ret = Instruction.Create(OpCodes.Ret);

                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, option));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldind_I4));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, CountdownValue));
                il.InsertBefore(first, Instruction.Create(OpCodes.Bne_Un, first));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, header));
                il.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, ret));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_S, header));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, HeaderText));
                il.InsertBefore(first, Instruction.Create(OpCodes.Callvirt, anchors.setText));
                il.InsertBefore(first, ret);

                body.MaxStackSize = Math.Max(body.MaxStackSize, 4);
            }
        }

        asm.Write(output, new WriterParameters { WriteSymbols = false });
        Console.WriteLine("OK hasCountdown=" + (hasCountdown ? "existing" : "injected"));
        return 0;
    }
}
