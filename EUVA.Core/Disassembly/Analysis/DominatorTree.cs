// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;

public static class DominatorTree
{
    
    public static void Build(IrBlock[] blocks)
    {
        int n = blocks.Length;
        if (n == 0) return;

        var idom = new int[n];
        for (int i = 0; i < n; i++) idom[i] = -1;
        idom[0] = 0; 

        
        var rpo = ComputeReversePostorder(blocks);
        var rpoIndex = new int[n];
        for (int i = 0; i < rpo.Length; i++)
            rpoIndex[rpo[i]] = i;

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 1; i < rpo.Length; i++) 
            {
                int b = rpo[i];
                int newIdom = -1;

                
                foreach (int p in blocks[b].Predecessors)
                {
                    if (idom[p] != -1)
                    {
                        newIdom = p;
                        break;
                    }
                }

                if (newIdom == -1) continue; 

                
                foreach (int p in blocks[b].Predecessors)
                {
                    if (p == newIdom) continue;
                    if (idom[p] != -1)
                        newIdom = Intersect(idom, rpoIndex, p, newIdom);
                }

                if (idom[b] != newIdom)
                {
                    idom[b] = newIdom;
                    changed = true;
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            blocks[i].Idom = (i == 0) ? -1 : idom[i];
            blocks[i].DomChildren.Clear();
        }

        
        for (int i = 1; i < n; i++)
        {
            if (idom[i] >= 0 && idom[i] < n)
                blocks[idom[i]].DomChildren.Add(i);
        }
    }
    
    public static bool Dominates(IrBlock[] blocks, int a, int b)
    {
        if (a == b) return true;
        int cur = b;
        while (cur != -1 && cur != 0)
        {
            cur = blocks[cur].Idom;
            if (cur == a) return true;
        }
        return a == 0; 
    }

    private static int Intersect(int[] idom, int[] rpoIndex, int b1, int b2)
    {
        int finger1 = b1;
        int finger2 = b2;
        while (finger1 != finger2)
        {
            while (rpoIndex[finger1] > rpoIndex[finger2])
                finger1 = idom[finger1];
            while (rpoIndex[finger2] > rpoIndex[finger1])
                finger2 = idom[finger2];
        }
        return finger1;
    }

    public static int[] ComputeReversePostorder(IrBlock[] blocks)
    {
        int n = blocks.Length;
        var visited = new bool[n];
        var postorder = new List<int>(n);

        void Dfs(int b)
        {
            visited[b] = true;
            foreach (int s in blocks[b].Successors)
            {
                if (!visited[s])
                    Dfs(s);
            }
            postorder.Add(b);
        }

        Dfs(0); 

        postorder.Reverse();
        return postorder.ToArray();
    }
}
