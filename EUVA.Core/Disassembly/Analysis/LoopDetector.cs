// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;


public static class LoopDetector
{
    
    public static List<LoopInfo> Detect(IrBlock[] blocks)
    {
        var loops = new List<LoopInfo>();

        
        for (int n = 0; n < blocks.Length; n++)
        {
            foreach (int h in blocks[n].Successors)
            {
                if (DominatorTree.Dominates(blocks, h, n))
                {
                    
                    var loop = BuildNaturalLoop(blocks, h, n);
                    loops.Add(loop);
                }
            }
        }

        loops.Sort((a, b) => b.Body.Count.CompareTo(a.Body.Count));

        
        for (int i = 0; i < loops.Count; i++)
        {
            for (int j = i + 1; j < loops.Count; j++)
            {
                if (loops[i].Body.IsSupersetOf(loops[j].Body))
                    loops[j].Depth = loops[i].Depth + 1;
            }
        }

        foreach (var block in blocks)
        {
            block.LoopHeader = null;
            block.InLoop = false;
        }

        foreach (var loop in loops)
        {
            blocks[loop.Header].LoopHeader = loop;
            foreach (int b in loop.Body)
                blocks[b].InLoop = true;

            
            if (blocks[loop.BackEdgeSource].EndsWithCondBranch)
                loop.IsDoWhile = true;
        }

        
        foreach (var loop in loops)
        {
            foreach (int b in loop.Body)
            {
                foreach (int s in blocks[b].Successors)
                {
                    if (!loop.Body.Contains(s))
                    {
                        if (!loop.ExitBlocks.Contains(b))
                            loop.ExitBlocks.Add(b);
                    }
                }
            }
        }

        return loops;
    }

    private static LoopInfo BuildNaturalLoop(IrBlock[] blocks, int header, int backEdgeSource)
    {
        var body = new HashSet<int> { header };
        var stack = new Stack<int>();

        if (backEdgeSource != header)
        {
            body.Add(backEdgeSource);
            stack.Push(backEdgeSource);
        }

        while (stack.Count > 0)
        {
            int m = stack.Pop();
            foreach (int pred in blocks[m].Predecessors)
            {
                if (!body.Contains(pred))
                {
                    body.Add(pred);
                    stack.Push(pred);
                }
            }
        }

        return new LoopInfo
        {
            Header = header,
            Body = body,
            BackEdgeSource = backEdgeSource,
        };
    }
}
