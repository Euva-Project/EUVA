// SPDX-License-Identifier: GPL-3.0-or-later

using Iced.Intel;

namespace EUVA.Core.Disassembly.Analysis;


public static class SsaBuilder
{
    
    
    
    public static void Build(IrBlock[] blocks)
    {
        
        ComputeDefsUses(blocks);

        
        var allVars = new HashSet<string>();
        foreach (var block in blocks)
            allVars.UnionWith(block.Defs);

        
        InsertPhiNodes(blocks, allVars);

        
        Rename(blocks);
    }

    private static void ComputeDefsUses(IrBlock[] blocks)
    {
        foreach (var block in blocks)
        {
            block.Defs.Clear();
            block.Uses.Clear();

            foreach (var instr in block.Instructions)
            {
                
                foreach (ref var src in instr.Sources.AsSpan())
                {
                    string? key = GetVarKey(in src);
                    if (key != null && !block.Defs.Contains(key))
                        block.Uses.Add(key);
                }

                
                if (instr.DefinesDest)
                {
                    string? key = GetVarKey(in instr.Destination);
                    if (key != null)
                        block.Defs.Add(key);
                }
            }
        }
    }

    private static void InsertPhiNodes(IrBlock[] blocks, HashSet<string> allVars)
    {
        foreach (var varName in allVars)
        {
            
            var worklist = new Queue<int>();
            var defBlocks = new HashSet<int>();
            var phiInserted = new HashSet<int>();

            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].Defs.Contains(varName))
                {
                    worklist.Enqueue(i);
                    defBlocks.Add(i);
                }
            }

            while (worklist.Count > 0)
            {
                int b = worklist.Dequeue();
                foreach (int df in blocks[b].DominanceFrontier)
                {
                    if (phiInserted.Contains(df)) continue;
                    phiInserted.Add(df);

                    
                    var phiDst = ParseVarKey(varName);

                    
                    int predCount = blocks[df].Predecessors.Count;
                    var sources = new IrOperand[predCount];
                    var sourceBlocks = new int[predCount];
                    for (int p = 0; p < predCount; p++)
                    {
                        sources[p] = phiDst; 
                        sourceBlocks[p] = blocks[df].Predecessors[p];
                    }

                    var phi = IrInstruction.MakePhi(phiDst, sources, sourceBlocks);
                    blocks[df].Instructions.Insert(0, phi);

                    
                    if (!defBlocks.Contains(df))
                    {
                        defBlocks.Add(df);
                        worklist.Enqueue(df);
                    }
                }
            }
        }
    }

    private static void Rename(IrBlock[] blocks)
    {
        
        var counter = new Dictionary<string, int>();
        var stack = new Dictionary<string, Stack<int>>();

        
        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.DefinesDest || instr.Opcode == IrOpcode.Phi)
                {
                    string? key = GetVarKey(in instr.Destination);
                    if (key != null && !counter.ContainsKey(key))
                    {
                        counter[key] = 0;
                        stack[key] = new Stack<int>();
                        stack[key].Push(0); 
                    }
                }

                foreach (ref var src in instr.Sources.AsSpan())
                {
                    string? key = GetVarKey(in src);
                    if (key != null && !counter.ContainsKey(key))
                    {
                        counter[key] = 0;
                        stack[key] = new Stack<int>();
                        stack[key].Push(0);
                    }
                }
            }
        }

        RenameBlock(blocks, 0, counter, stack);
    }

    private static void RenameBlock(IrBlock[] blocks, int b,
        Dictionary<string, int> counter, Dictionary<string, Stack<int>> stack)
    {
        
        var pushedVars = new List<string>();

        foreach (var instr in blocks[b].Instructions)
        {
            
            if (instr.Opcode != IrOpcode.Phi)
            {
                for (int i = 0; i < instr.Sources.Length; i++)
                {
                    string? key = GetVarKey(in instr.Sources[i]);
                    if (key != null && stack.TryGetValue(key, out var s) && s.Count > 0)
                    {
                        instr.Sources[i].SsaVersion = s.Peek();
                    }
                }
            }

            if (instr.DefinesDest || instr.Opcode == IrOpcode.Phi)
            {
                string? key = GetVarKey(in instr.Destination);
                if (key != null && counter.ContainsKey(key))
                {
                    counter[key]++;
                    int newVersion = counter[key];
                    stack[key].Push(newVersion);
                    instr.Destination.SsaVersion = newVersion;
                    pushedVars.Add(key);
                }
            }
        }

        
        foreach (int s in blocks[b].Successors)
        {
            
            int predIdx = blocks[s].Predecessors.IndexOf(b);
            if (predIdx < 0) continue;

            foreach (var instr in blocks[s].Instructions)
            {
                if (instr.Opcode != IrOpcode.Phi) break; 

                string? key = GetVarKey(in instr.Destination);
                if (key != null && predIdx < instr.Sources.Length
                    && stack.TryGetValue(key, out var st) && st.Count > 0)
                {
                    instr.Sources[predIdx].SsaVersion = st.Peek();
                    if (instr.PhiSourceBlocks != null)
                        instr.PhiSourceBlocks[predIdx] = b;
                }
            }
        }

        
        foreach (int child in blocks[b].DomChildren)
            RenameBlock(blocks, child, counter, stack);

        
        foreach (var key in pushedVars)
            stack[key].Pop();
    }

    internal static string? GetVarKey(in IrOperand op) => op.Kind switch
    {
        IrOperandKind.Register when op.Register != Register.None => $"r_{op.CanonicalRegister}",
        IrOperandKind.StackSlot => $"s_{op.StackOffset}",
        IrOperandKind.Flag => "flags",
        _ => null,
    };

    private static IrOperand ParseVarKey(string key)
    {
        if (key.StartsWith("r_") && Enum.TryParse<Register>(key.Substring(2), out var reg))
            return IrOperand.Reg(reg, 64);
        if (key.StartsWith("s_") && int.TryParse(key.Substring(2), out int offset))
            return IrOperand.Stack(offset, 64);
        if (key == "flags")
            return IrOperand.FlagReg();
        return IrOperand.Reg(Register.None, 64);
    }
}
