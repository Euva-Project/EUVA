// SPDX-License-Identifier: GPL-3.0-or-later
namespace EUVA.Core.Disassembly.Analysis;

public static class DeadCodeElimination
{
    
    public static int Eliminate(IrBlock[] blocks)
    {
        int totalEliminated = 0;

        
        foreach (var block in blocks)
        {
            bool isDead = false;
            foreach (var instr in block.Instructions)
            {
                if (isDead)
                {
                    if (!instr.IsDead)
                    {
                        instr.IsDead = true;
                        totalEliminated++;
                    }
                    continue;
                }

                if (instr.Opcode is IrOpcode.Return or IrOpcode.Branch or IrOpcode.CondBranch)
                {
                    isDead = true;
                }
            }
        }

        
        foreach (var block in blocks)
        {
            var writtenTargets = new HashSet<string>();
            for (int i = block.Instructions.Count - 1; i >= 0; i--)
            {
                var instr = block.Instructions[i];
                if (instr.IsDead) continue;
                
                
                if (instr.HasSideEffects || instr.Opcode == IrOpcode.Call || instr.Condition != IrCondition.None)
                {
                    writtenTargets.Clear();
                    continue;
                }

                
                foreach (var src in instr.Sources)
                {
                    if (src.Kind == IrOperandKind.Memory)
                    {
                        if (src.MemIndex != Iced.Intel.Register.None)
                            writtenTargets.Clear(); 
                        else
                            writtenTargets.Remove(GetMemoryKey(src));
                    }
                }

                
                if (instr.Destination.Kind == IrOperandKind.Memory)
                {
                    if (instr.Destination.MemIndex != Iced.Intel.Register.None)
                    {
                        writtenTargets.Clear();
                    }
                    else
                    {
                        string key = GetMemoryKey(instr.Destination);
                        if (writtenTargets.Contains(key))
                        {
                            instr.IsDead = true;
                            totalEliminated++;
                        }
                        else
                        {
                            writtenTargets.Add(key);
                        }
                    }
                }
            }
        }

        
        var useCounts = new Dictionary<(string, int), int>();

        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;
                CountUsesRecursively(instr, useCounts);
            }
        }

        
        bool changed = true;

        while (changed)
        {
            changed = false;
            foreach (var block in blocks)
            {
                foreach (var instr in block.Instructions)
                {
                    if (instr.IsDead) continue;
                    if (instr.HasSideEffects) continue;
                    if (instr.Opcode == IrOpcode.Cmp || instr.Opcode == IrOpcode.Test) continue;
                    if (instr.Opcode == IrOpcode.Branch || instr.Opcode == IrOpcode.CondBranch) continue;
                    if (!instr.DefinesDest) continue;

                    var dstKey = SsaBuilder.GetVarKey(in instr.Destination);
                    if (dstKey == null) continue;

                    var k = (dstKey, instr.Destination.SsaVersion);
                    int uses = useCounts.GetValueOrDefault(k, 0);

                    if (uses == 0)
                    {
                        
                        instr.IsDead = true;
                        totalEliminated++;
                        changed = true;

                        foreach (ref var src in instr.Sources.AsSpan())
                        {
                            DecrementUsesRecursively(src, useCounts);
                        }
                    }
                }
            }
        }

        return totalEliminated;
    }

    public static void Compact(IrBlock[] blocks)
    {
        foreach (var block in blocks)
            block.Instructions.RemoveAll(i => i.IsDead);
    }

    private static void CountUsesRecursively(IrInstruction instr, Dictionary<(string, int), int> useCounts)
    {
        foreach (var src in instr.Sources)
        {
            if (src.Kind == IrOperandKind.Expression && src.Expression != null)
            {
                CountUsesRecursively(src.Expression, useCounts);
            }
            else if (src.Kind == IrOperandKind.Memory)
            {
                if (src.MemBase != Iced.Intel.Register.None)
                {
                    var k = ($"r_{IrOperand.GetCanonical(src.MemBase)}", src.SsaVersion);
                    useCounts[k] = useCounts.GetValueOrDefault(k, 0) + 1;
                }
                if (src.MemIndex != Iced.Intel.Register.None)
                {
                    var k = ($"r_{IrOperand.GetCanonical(src.MemIndex)}", src.SsaVersion); 
                    useCounts[k] = useCounts.GetValueOrDefault(k, 0) + 1;
                }
            }
            else
            {
                var key = SsaBuilder.GetVarKey(in src);
                if (key != null && src.SsaVersion >= 0)
                {
                    var k = (key, src.SsaVersion);
                    useCounts[k] = useCounts.GetValueOrDefault(k, 0) + 1;
                }
            }
        }
    }

    private static void DecrementUsesRecursively(IrOperand src, Dictionary<(string, int), int> useCounts)
    {
        if (src.Kind == IrOperandKind.Expression && src.Expression != null)
        {
            foreach (var inner in src.Expression.Sources)
                DecrementUsesRecursively(inner, useCounts);
        }
        else if (src.Kind == IrOperandKind.Memory)
        {
            if (src.MemBase != Iced.Intel.Register.None)
            {
                var k = ($"r_{IrOperand.GetCanonical(src.MemBase)}", src.SsaVersion);
                if (useCounts.TryGetValue(k, out int c)) useCounts[k] = c - 1;
            }
            if (src.MemIndex != Iced.Intel.Register.None)
            {
                var k = ($"r_{IrOperand.GetCanonical(src.MemIndex)}", src.SsaVersion);
                if (useCounts.TryGetValue(k, out int c)) useCounts[k] = c - 1;
            }
        }
        else
        {
            var key = SsaBuilder.GetVarKey(in src);
            if (key != null && src.SsaVersion >= 0)
            {
                var k = (key, src.SsaVersion);
                if (useCounts.TryGetValue(k, out int c)) useCounts[k] = c - 1;
            }
        }
    }

    private static string GetMemoryKey(IrOperand op)
    {
        string baseId = op.MemBase != Iced.Intel.Register.None ? $"{op.MemBase}_{op.SsaVersion}" : "none";
        return $"[{baseId}+{op.MemDisplacement}]";
    }
}
