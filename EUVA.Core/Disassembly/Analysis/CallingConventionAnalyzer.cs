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
        public IrOperand? SourceOperand;

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

    public static FunctionArg[] RecoverCallArguments(IrBlock[] blocks, int blockIdx, int callInstrIndex, int bitness)
    {
        var args = new List<FunctionArg>();
        
        var instr = blocks[blockIdx].Instructions[callInstrIndex];
        bool isIat = instr.Sources.Length > 0 && instr.Sources[0].Kind == IrOperandKind.Memory && instr.Sources[0].MemDisplacement != 0 && instr.Sources[0].MemBase == Register.None;

        if (bitness == 64)
        {
            if (isIat)
            {
                var def = FindLastDefinition(blocks, blockIdx, callInstrIndex, Register.RCX);
                if (def == null) def = FindLastDefinition(blocks, blockIdx, callInstrIndex, Register.ECX);

                var arg = new FunctionArg(0, "a1", Register.RCX);
                if (def != null)
                {
                    if (def.Sources.Length > 0)
                    {
                        var src = def.Sources[0];
                        arg.SourceOperand = src;
                        arg.Type = src.Type != TypeInfo.Unknown ? src.Type : (src.Kind == IrOperandKind.Constant ? new TypeInfo { BaseType = PrimitiveType.UInt64 } : TypeInfo.Unknown);
                    }
                    def.IsDead = true;
                }
                else
                {
                    arg.SourceOperand = IrOperand.Reg(Register.RCX, 64);
                    arg.Type = new TypeInfo { BaseType = PrimitiveType.UInt64 };
                }
                args.Add(arg);
            }

            foreach (var reg in ArgRegs)
            {
                if (isIat && reg == Register.RCX) continue;

                var def = FindLastDefinition(blocks, blockIdx, callInstrIndex, reg);
                if (def != null)
                {
                    int argIdx = GetArgIndex(reg);
                    var arg = new FunctionArg(argIdx, $"a{argIdx + 1}", reg);
                    if (def.Sources.Length > 0)
                    {
                        var src = def.Sources[0];
                        arg.SourceOperand = src;
                        arg.Type = src.Type != TypeInfo.Unknown ? src.Type : (src.Kind == IrOperandKind.Constant ? new TypeInfo { BaseType = PrimitiveType.UInt64 } : TypeInfo.Unknown);
                    }
                    args.Add(arg);
                    def.IsDead = true; 
                }
            }
        }

        int step = bitness == 64 ? 8 : 4;
        int maxOffset = 128;

        for (int offset = 0; offset < maxOffset; offset += step) 
        {
            var def = FindLastStackDefinition(blocks, blockIdx, callInstrIndex, offset);
            if (def != null)
            {
                int argIdx = (bitness == 64) 
                    ? (offset < 32 ? (offset / 8) : (4 + (offset - 32) / 8))
                    : (offset / 4);

                var existing = args.FirstOrDefault(a => a.Index == argIdx);
                if (existing != null)
                {
                    if (existing.SourceOperand != null && 
                        existing.SourceOperand.Value.SameLocation(def.Sources[0]))
                        continue;
                    
                    argIdx = args.Max(a => a.Index) + 1;
                }

                var arg = new FunctionArg(argIdx, $"a{argIdx + 1}", Register.None) { StackOffset = offset };
                if (def.Sources.Length > 0)
                {
                    var src = def.Sources[0];
                    arg.SourceOperand = src;
                    arg.Type = src.Type != TypeInfo.Unknown ? src.Type : (src.Kind == IrOperandKind.Constant ? new TypeInfo { BaseType = (bitness == 64 ? PrimitiveType.UInt64 : PrimitiveType.UInt32) } : TypeInfo.Unknown);
                }
                args.Add(arg);
                def.IsDead = true; 
            }
        }

        args.Sort((a, b) => a.Index.CompareTo(b.Index));
        return args.ToArray();
    }

    private static IrInstruction? FindLastDefinition(IrBlock[] blocks, int blockIdx, int instrIdx, Register reg)
    {
        var visited = new HashSet<int>();
        var queue = new Queue<(int BlockIdx, int InstrIdx)>();
        queue.Enqueue((blockIdx, instrIdx - 1));

        while (queue.Count > 0)
        {
            var (currIdx, startInstr) = queue.Dequeue();
            if (currIdx < 0 || !visited.Add(currIdx)) continue;

            var block = blocks[currIdx];
            for (int i = startInstr; i >= 0; i--)
            {
                var instr = block.Instructions[i];
                if (instr.IsDead) continue;

                if (instr.DefinesDest && instr.Destination.Kind == IrOperandKind.Register &&
                    IrOperand.GetCanonical(instr.Destination.Register) == reg)
                {
                    return instr;
                }
            }

            foreach (var pred in block.Predecessors)
            {
                queue.Enqueue((pred, blocks[pred].Instructions.Count - 1));
            }
        }

        return null;
    }

    private static IrInstruction? FindLastStackDefinition(IrBlock[] blocks, int blockIdx, int instrIdx, int offset)
    {
        var visited = new HashSet<(int, int)>();
        var queue = new Queue<(int BlockIdx, int InstrIdx, int Delta)>();
        queue.Enqueue((blockIdx, instrIdx - 1, 0));

        while (queue.Count > 0)
        {
            var (currIdx, startInstr, delta) = queue.Dequeue();
            if (currIdx < 0 || !visited.Add((currIdx, delta))) continue;

            var block = blocks[currIdx];
            for (int i = startInstr; i >= 0; i--)
            {
                var instr = block.Instructions[i];
                if (instr.IsDead) continue;

                if (instr.DefinesDest && instr.Destination.Kind == IrOperandKind.Register &&
                    IrOperand.GetCanonical(instr.Destination.Register) == Register.RSP)
                {
                    if (instr.Opcode == IrOpcode.Sub && instr.Sources.Length == 2 && instr.Sources[1].Kind == IrOperandKind.Constant)
                        delta -= (int)instr.Sources[1].ConstantValue;
                    else if (instr.Opcode == IrOpcode.Add && instr.Sources.Length == 2 && instr.Sources[1].Kind == IrOperandKind.Constant)
                        delta += (int)instr.Sources[1].ConstantValue;
                }

                if (instr.Opcode == IrOpcode.Store && instr.Destination.Kind == IrOperandKind.Memory)
                {
                    var baseReg = IrOperand.GetCanonical(instr.Destination.MemBase);
                    var disp = instr.Destination.MemDisplacement;
                    if (baseReg == Register.RSP && disp == (offset + delta))
                    {
                        return instr;
                    }
                }
            }

            foreach (var pred in block.Predecessors)
            {
                queue.Enqueue((pred, blocks[pred].Instructions.Count - 1, delta));
            }
        }

        return null;
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
