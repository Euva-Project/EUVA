// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;

public static class SwitchDetector
{
    
    public sealed class SwitchInfo
    {
        public int BlockIndex;
        public IrOperand IndexVariable;
        public long TableBase;
        public int CaseCount;
        public int[] CaseTargets = Array.Empty<int>();
        public int DefaultTarget = -1;
    }

    public static List<SwitchInfo> Detect(IrBlock[] blocks)
    {
        var switches = new List<SwitchInfo>();

        for (int i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            var term = block.Terminator;
            if (term == null) continue;

            
            if (term.Opcode == IrOpcode.Branch && term.Sources.Length > 0)
            {
                var src = term.Sources[0];
                if (src.Kind == IrOperandKind.Memory && src.MemScale > 1)
                {
                    var sw = new SwitchInfo
                    {
                        BlockIndex = i,
                        IndexVariable = IrOperand.Reg(src.MemIndex, src.BitSize),
                        TableBase = src.MemDisplacement,
                    };
                    switches.Add(sw);
                }
            }

            
            if (i > 0 && block.Predecessors.Count > 0)
            {
                foreach (int p in block.Predecessors)
                {
                    if (p >= blocks.Length) continue;
                    var pred = blocks[p];
                    if (pred.EndsWithCondBranch)
                    {
                        
                        var condTerm = pred.Terminator!;
                        if (condTerm.Condition == IrCondition.UnsignedAbove ||
                            condTerm.Condition == IrCondition.UnsignedBelowEq)
                        {
                            for (int j = pred.Instructions.Count - 1; j >= 0; j--)
                            {
                                if (pred.Instructions[j].Opcode == IrOpcode.Cmp &&
                                    pred.Instructions[j].Sources.Length == 2)
                                {
                                    var cmpSrc = pred.Instructions[j].Sources[1];
                                    if (cmpSrc.Kind == IrOperandKind.Constant)
                                    {
                                        
                                        var existing = switches.Find(s => s.BlockIndex == i);
                                        if (existing != null)
                                        {
                                            existing.CaseCount = (int)cmpSrc.ConstantValue + 1;
                                            existing.IndexVariable = pred.Instructions[j].Sources[0];
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        return switches;
    }
}
