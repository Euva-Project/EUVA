// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;

public static class ConstantPropagation
{
    public static int Propagate(IrBlock[] blocks)
    {
        var defs = new Dictionary<(string, int), IrInstruction>();

        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;
                if (instr.DefinesDest || instr.Opcode == IrOpcode.Phi)
                {
                    var key = SsaBuilder.GetVarKey(in instr.Destination);
                    if (key != null && instr.Destination.SsaVersion >= 0)
                        defs[(key, instr.Destination.SsaVersion)] = instr;
                }
            }
        }

        int simplified = 0;

        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;

                bool allConst = true;
                var constValues = new long[instr.Sources.Length];

                for (int i = 0; i < instr.Sources.Length; i++)
                {
                    ref var src = ref instr.Sources[i];
                    long? val = TryResolveConstant(in src, defs);
                    if (val.HasValue)
                    {
                        constValues[i] = val.Value;
                    }
                    else
                    {
                        allConst = false;
                    }
                }

                if (!allConst || !instr.DefinesDest) continue;
                if (instr.Opcode == IrOpcode.Phi) continue; 

                long? result = TryFold(instr.Opcode, constValues, instr.Sources.Length);
                if (result.HasValue)
                {
                    instr.Opcode = IrOpcode.Assign;
                    instr.Sources = new[] { IrOperand.Const(result.Value, instr.BitSize) };
                    simplified++;
                }
            }
        }
 
        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;

                for (int i = 0; i < instr.Sources.Length; i++)
                {
                    ref var src = ref instr.Sources[i];
                    if (src.Kind == IrOperandKind.Register || src.Kind == IrOperandKind.StackSlot)
                    {
                        long? val = TryResolveConstant(in src, defs);
                        if (val.HasValue)
                        {
                            byte bs = src.BitSize;
                            instr.Sources[i] = IrOperand.Const(val.Value, bs);
                            simplified++;
                        }
                    }
                }
            }
        }

        return simplified;
    }

    private static long? TryResolveConstant(in IrOperand op,
        Dictionary<(string, int), IrInstruction> defs)
    {
        if (op.Kind == IrOperandKind.Constant)
            return op.ConstantValue;

        var key = SsaBuilder.GetVarKey(in op);
        if (key == null || op.SsaVersion < 0) return null;

        if (!defs.TryGetValue((key, op.SsaVersion), out var defInstr)) return null;
        if (defInstr.Opcode != IrOpcode.Assign) return null;
        if (defInstr.Sources.Length != 1) return null;
        if (defInstr.Sources[0].Kind == IrOperandKind.Constant)
            return defInstr.Sources[0].ConstantValue;

        return null;
    }

    private static long? TryFold(IrOpcode op, long[] values, int count)
    {
        if (count == 1)
        {
            return op switch
            {
                IrOpcode.Assign => values[0],
                IrOpcode.Neg => -values[0],
                IrOpcode.Not => ~values[0],
                _ => null,
            };
        }

        if (count == 2)
        {
            long a = values[0], b = values[1];
            return op switch
            {
                IrOpcode.Add => a + b,
                IrOpcode.Sub => a - b,
                IrOpcode.Mul or IrOpcode.IMul => a * b,
                IrOpcode.And => a & b,
                IrOpcode.Or => a | b,
                IrOpcode.Xor => a ^ b,
                IrOpcode.Shl => a << (int)b,
                IrOpcode.Shr => (long)((ulong)a >> (int)b),
                IrOpcode.Sar => a >> (int)b,
                IrOpcode.Div when b != 0 => (long)((ulong)a / (ulong)b),
                IrOpcode.IDiv when b != 0 => a / b,
                IrOpcode.Mod when b != 0 => a % b,
                _ => null,
            };
        }

        return null;
    }
}
