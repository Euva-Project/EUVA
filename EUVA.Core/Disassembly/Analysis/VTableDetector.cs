// SPDX-License-Identifier: GPL-3.0-or-later

using Iced.Intel;

namespace EUVA.Core.Disassembly.Analysis;

public static class VTableDetector
{
    public sealed class VTableCall
    {
        public int BlockIndex;
        public int InstructionIndex;
        public IrOperand ObjectPointer;     
        public long VTableOffset;           
        public string MethodName;           

        public VTableCall(int blockIdx, int instrIdx, IrOperand objPtr, long offset)
        {
            BlockIndex = blockIdx;
            InstructionIndex = instrIdx;
            ObjectPointer = objPtr;
            VTableOffset = offset;
            MethodName = $"vfunc_{offset / 8}";
        }
    }


    public static List<VTableCall> Detect(IrBlock[] blocks)
    {
        var vtableCalls = new List<VTableCall>();

        foreach (var block in blocks)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var instr = block.Instructions[i];
                if (instr.IsDead || instr.Opcode != IrOpcode.Call) continue;
                if (instr.Sources.Length == 0) continue;

                var target = instr.Sources[0];

                
                if (target.Kind == IrOperandKind.Memory &&
                    target.MemBase != Register.None &&
                    target.MemIndex == Register.None)
                {
                    
                    
                    var vtableLoad = FindVTableLoad(block, i, target.MemBase);
                    if (vtableLoad != null)
                    {
                        var call = new VTableCall(
                            block.Index, i,
                            vtableLoad.Value, target.MemDisplacement);
                        vtableCalls.Add(call);
                    }
                }
            }
        }

        return vtableCalls;
    }

    private static IrOperand? FindVTableLoad(IrBlock block, int beforeIndex, Register vtableReg)
    {
        var canonical = IrOperand.GetCanonical(vtableReg);

        for (int i = beforeIndex - 1; i >= 0; i--)
        {
            var instr = block.Instructions[i];
            if (instr.IsDead) continue;

            
            if (instr.Opcode == IrOpcode.Load && instr.DefinesDest)
            {
                if (instr.Destination.Kind == IrOperandKind.Register &&
                    IrOperand.GetCanonical(instr.Destination.Register) == canonical)
                {
                    var mem = instr.Sources[0];
                    if (mem.Kind == IrOperandKind.Memory &&
                        mem.MemDisplacement == 0 &&
                        mem.MemIndex == Register.None &&
                        mem.MemBase != Register.None)
                    {
                        
                        return IrOperand.Reg(mem.MemBase, 64);
                    }
                }
            }

            if (instr.DefinesDest &&
                instr.Destination.Kind == IrOperandKind.Register &&
                IrOperand.GetCanonical(instr.Destination.Register) == canonical)
                break;
        }

        return null;
    }
}
