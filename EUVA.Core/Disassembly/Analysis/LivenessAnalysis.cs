// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;

public static class LivenessAnalysis
{

    public static void Compute(IrBlock[] blocks)
    {
        foreach (var b in blocks)
        {
            b.LiveIn.Clear();
            b.LiveOut.Clear();
        }

        
        var gen = new HashSet<string>[blocks.Length];
        var kill = new HashSet<string>[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            gen[i] = new HashSet<string>();
            kill[i] = new HashSet<string>();
            ComputeGenKill(blocks[i], gen[i], kill[i]);
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = blocks.Length - 1; i >= 0; i--)
            {
                var block = blocks[i];

                
                var newLiveOut = new HashSet<string>();
                foreach (int s in block.Successors)
                    newLiveOut.UnionWith(blocks[s].LiveIn);

                
                var newLiveIn = new HashSet<string>(gen[i]);
                foreach (var v in newLiveOut)
                {
                    if (!kill[i].Contains(v))
                        newLiveIn.Add(v);
                }

                if (!newLiveIn.SetEquals(block.LiveIn) || !newLiveOut.SetEquals(block.LiveOut))
                {
                    block.LiveIn = newLiveIn;
                    block.LiveOut = newLiveOut;
                    changed = true;
                }
            }
        }
    }

    private static void ComputeGenKill(IrBlock block, HashSet<string> gen, HashSet<string> kill)
    {
        foreach (var instr in block.Instructions)
        {
            if (instr.IsDead) continue;

            
            foreach (ref var src in instr.Sources.AsSpan())
            {
                var key = SsaBuilder.GetVarKey(in src);
                if (key != null && !kill.Contains(key))
                    gen.Add(key);
            }

            
            if (instr.DefinesDest)
            {
                var key = SsaBuilder.GetVarKey(in instr.Destination);
                if (key != null)
                    kill.Add(key);
            }
        }
    }
}
