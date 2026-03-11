// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;


public static class DominanceFrontier
{
    
    public static void Compute(IrBlock[] blocks)
    {
        
        foreach (var b in blocks)
            b.DominanceFrontier.Clear();

        for (int b = 0; b < blocks.Length; b++)
        {
            var preds = blocks[b].Predecessors;
            if (preds.Count < 2) continue; 

            foreach (int p in preds)
            {
                int runner = p;
                while (runner != -1 && runner != blocks[b].Idom)
                {
                    blocks[runner].DominanceFrontier.Add(b);
                    runner = blocks[runner].Idom;
                }
            }
        }
    }
}
