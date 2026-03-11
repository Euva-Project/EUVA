// SPDX-License-Identifier: GPL-3.0-or-later

using Iced.Intel;

namespace EUVA.Core.Disassembly.Analysis;


public static class CallingConventionAnalyzer
{
     
    public sealed class FunctionSignature
    {
        public string Name = "sub_unknown";
        public TypeInfo ReturnType = TypeInfo.Unknown;
        public List<FunctionArg> Arguments = new();
        public bool HasReturnValue;
        public bool IsVarArgs;
    }

    public sealed class FunctionArg
    {
        public int Index;
        public string Name;
        public TypeInfo Type;
        public Register SourceRegister;
        public int StackOffset; 

        public FunctionArg(int index, string name, Register reg)
        {
            Index = index;
            Name = name;
            SourceRegister = reg;
        }
    }

    private static readonly Register[] ArgRegs = { Register.RCX, Register.RDX, Register.R8, Register.R9 };
    private static readonly Register[] ArgRegs32 = { Register.ECX, Register.EDX, Register.R8D, Register.R9D };
    public static readonly HashSet<Register> CalleeSaved = new()
    {
        Register.RBX, Register.RBP, Register.RDI, Register.RSI,
        Register.R12, Register.R13, Register.R14, Register.R15,
    };

    
    public static readonly HashSet<Register> Volatile = new()
    {
        Register.RAX, Register.RCX, Register.RDX, Register.R8, Register.R9,
        Register.R10, Register.R11,
    };

    public static FunctionSignature AnalyzeFunction(IrBlock[] blocks, string funcName = "sub_unknown")
    {
        var sig = new FunctionSignature { Name = funcName };

        if (blocks.Length == 0) return sig;

        
        var usedArgRegs = new HashSet<Register>();
        var definedRegs = new HashSet<Register>();

        
        int maxScan = Math.Min(blocks.Length, 3);
        for (int bi = 0; bi < maxScan; bi++)
        {
            foreach (var instr in blocks[bi].Instructions)
            {
                if (instr.IsDead) continue;

                
                foreach (ref var src in instr.Sources.AsSpan())
                {
                    if (src.Kind == IrOperandKind.Register)
                    {
                        var canonical = IrOperand.GetCanonical(src.Register);
                        if (IsArgRegister(canonical) && !definedRegs.Contains(canonical))
                            usedArgRegs.Add(canonical);
                    }
                }

                
                if (instr.DefinesDest && instr.Destination.Kind == IrOperandKind.Register)
                {
                    definedRegs.Add(IrOperand.GetCanonical(instr.Destination.Register));
                }
            }
        }

        
        for (int i = 0; i < ArgRegs.Length; i++)
        {
            if (usedArgRegs.Contains(ArgRegs[i]))
            {
                var arg = new FunctionArg(i, $"a{i + 1}", ArgRegs[i])
                {
                    Type = new TypeInfo { BaseType = PrimitiveType.UInt64 },
                };
                sig.Arguments.Add(arg);
            }
        }

        
        sig.HasReturnValue = DetectReturnValue(blocks);
        if (sig.HasReturnValue)
            sig.ReturnType = new TypeInfo { BaseType = PrimitiveType.UInt64 };

        return sig;
    }

    public static FunctionArg[] RecoverCallArguments(IrBlock block, int callInstrIndex)
    {
        var args = new List<FunctionArg>();
        var found = new HashSet<Register>();

        
        for (int i = callInstrIndex - 1; i >= 0 && found.Count < 4; i--)
        {
            var instr = block.Instructions[i];
            if (instr.IsDead) continue;

            
            if (instr.Opcode == IrOpcode.Call) break;

            if (instr.DefinesDest && instr.Destination.Kind == IrOperandKind.Register)
            {
                var canonical = IrOperand.GetCanonical(instr.Destination.Register);
                if (IsArgRegister(canonical) && !found.Contains(canonical))
                {
                    found.Add(canonical);
                    int argIdx = GetArgIndex(canonical);
                    if (argIdx >= 0)
                    {
                        var arg = new FunctionArg(argIdx, $"a{argIdx + 1}", canonical);

                        if (instr.Sources.Length > 0)
                        {
                            var src = instr.Sources[0];
                            if (src.Kind == IrOperandKind.Constant)
                                arg.Type = new TypeInfo { BaseType = PrimitiveType.UInt64 };
                            else if (src.Type != TypeInfo.Unknown)
                                arg.Type = src.Type;
                        }

                        args.Add(arg);
                    }
                }
            }
        }

        args.Sort((a, b) => a.Index.CompareTo(b.Index));
        return args.ToArray();
    }

    private static bool DetectReturnValue(IrBlock[] blocks)
    {
        foreach (var block in blocks)
        {
            if (!block.IsReturn) continue;

            
            for (int i = block.Instructions.Count - 1; i >= 0; i--)
            {
                var instr = block.Instructions[i];
                if (instr.Opcode == IrOpcode.Return) continue;
                if (instr.IsDead) continue;

                if (instr.DefinesDest && instr.Destination.Kind == IrOperandKind.Register)
                {
                    var canonical = IrOperand.GetCanonical(instr.Destination.Register);
                    if (canonical == Register.RAX)
                        return true;
                }
                break; 
            }
        }
        return false;
    }

    private static bool IsArgRegister(Register canonical) =>
        canonical == Register.RCX || canonical == Register.RDX ||
        canonical == Register.R8 || canonical == Register.R9;

    private static int GetArgIndex(Register canonical) => canonical switch
    {
        Register.RCX => 0,
        Register.RDX => 1,
        Register.R8 => 2,
        Register.R9 => 3,
        _ => -1,
    };
}
