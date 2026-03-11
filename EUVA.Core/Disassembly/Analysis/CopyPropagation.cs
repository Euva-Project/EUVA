// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;

public static class CopyPropagation
{
    
    public static int Propagate(IrBlock[] blocks)
    {
        
        var copySource = new Dictionary<(string, int), IrOperand>();

        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;
                if (instr.Opcode != IrOpcode.Assign) continue;
                if (instr.Sources.Length != 1) continue;
                if (instr.Condition != IrCondition.None) continue; 

                var dstKey = SsaBuilder.GetVarKey(in instr.Destination);
                if (dstKey == null || instr.Destination.SsaVersion < 0) continue;

                var src = instr.Sources[0];
                
                src = ResolveCopy(src, copySource);
                copySource[(dstKey, instr.Destination.SsaVersion)] = src;
            }
        }

        
        int replaced = 0;
        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;

                for (int i = 0; i < instr.Sources.Length; i++)
                {
                    ref var src = ref instr.Sources[i];
                    var resolved = ResolveCopy(src, copySource);
                    if (!resolved.SameLocation(in src) || resolved.SsaVersion != src.SsaVersion)
                    {
                        byte bs = src.BitSize;
                        instr.Sources[i] = resolved;
                        if (instr.Sources[i].BitSize == 0)
                            instr.Sources[i].BitSize = bs;
                        replaced++;
                    }
                }
            }
        }

        return replaced;
    }

    private static IrOperand ResolveCopy(IrOperand op,
        Dictionary<(string, int), IrOperand> copySource)
    {
        var key = SsaBuilder.GetVarKey(in op);
        if (key == null || op.SsaVersion < 0) return op;

        int depth = 0;
        while (depth < 100) 
        {
            var k = (key, op.SsaVersion);
            if (!copySource.TryGetValue(k, out var next)) break;
            op = next;
            key = SsaBuilder.GetVarKey(in op);
            if (key == null || op.SsaVersion < 0) break;
            depth++;
        }

        return op;
    }
}
