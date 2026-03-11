// SPDX-License-Identifier: GPL-3.0-or-later

using Iced.Intel;

namespace EUVA.Core.Disassembly.Analysis;

public static class ExpressionInliner
{
    private sealed class DefUseInfo
    {
        public IrInstruction? DefInstr;
        public int UseCount;
        public List<IrInstruction> Uses = new();
    }

    public static int Inline(IrBlock[] blocks)
    {
        int changes = 0;
        var info = new Dictionary<string, DefUseInfo>();

        
        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;

                
                if (instr.DefinesDest && (instr.Destination.Kind == IrOperandKind.Register || instr.Destination.Kind == IrOperandKind.StackSlot))
                {
                    string key = GetSsaKey(instr.Destination);
                    if (!info.TryGetValue(key, out var dt))
                    {
                        dt = new DefUseInfo();
                        info[key] = dt;
                    }
                    dt.DefInstr = instr;
                }

                
                TrackUsesRecursively(instr, instr, info);

                if (instr.Destination.Kind == IrOperandKind.Memory)
                {
                    var dst = instr.Destination;
                    if (dst.MemBase != Register.None) AddUse(dst.MemBase, dst.SsaVersion, instr, info);
                    if (dst.MemIndex != Register.None) AddUse(dst.MemIndex, dst.SsaVersion, instr, info);
                }
            }
        }

        
        foreach (var block in blocks)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var instr = block.Instructions[i];
                if (instr.IsDead) continue;

                
                if (!instr.DefinesDest || (instr.Destination.Kind != IrOperandKind.Register && instr.Destination.Kind != IrOperandKind.StackSlot))
                    continue;

                
                if (instr.Opcode == IrOpcode.Call || instr.HasSideEffects)
                    continue;
                    
                
                if (instr.Opcode == IrOpcode.Phi)
                    continue;

                string key = GetSsaKey(instr.Destination);
                if (info.TryGetValue(key, out var dt) && dt.UseCount == 1)
                {
                    var consumer = dt.Uses[0];

                    
                    if (consumer.Opcode == IrOpcode.Phi)
                        continue;

                    bool readsMemory = instr.Opcode == IrOpcode.Load;
                    bool safeToInline = !readsMemory || IsInSameBlockAndSafe(block, instr, consumer);

                    if (safeToInline)
                    {
                        
                        ReplaceUseWithExpression(consumer, instr);
                        instr.IsDead = true; 
                        changes++;
                    }
                }
            }
        }

        return changes;
    }

    private static void TrackUsesRecursively(IrInstruction node, IrInstruction rootConsumer, Dictionary<string, DefUseInfo> info)
    {
        foreach (var src in node.Sources)
        {
            if (src.Kind == IrOperandKind.Expression && src.Expression != null)
            {
                TrackUsesRecursively(src.Expression, rootConsumer, info);
            }
            else if (src.Kind == IrOperandKind.Register || src.Kind == IrOperandKind.StackSlot)
            {
                string key = GetSsaKey(src);
                if (!info.TryGetValue(key, out var dt))
                {
                    dt = new DefUseInfo();
                    info[key] = dt;
                }
                dt.UseCount++;
                dt.Uses.Add(rootConsumer);
            }
            else if (src.Kind == IrOperandKind.Memory)
            {
                if (src.MemBase != Register.None) AddUse(src.MemBase, src.SsaVersion, rootConsumer, info);
                if (src.MemIndex != Register.None) AddUse(src.MemIndex, src.SsaVersion, rootConsumer, info);
            }
        }
    }

    private static void AddUse(Register reg, int ssa, IrInstruction consumer, Dictionary<string, DefUseInfo> info)
    {
        var op = IrOperand.Reg(reg, 64);
        op.SsaVersion = ssa;
        string key = GetSsaKey(op);
        if (!info.TryGetValue(key, out var dt))
        {
            dt = new DefUseInfo();
            info[key] = dt;
        }
        dt.UseCount++;
        dt.Uses.Add(consumer);
    }

    private static void ReplaceUseWithExpression(IrInstruction consumer, IrInstruction exprInstr)
    {
        for (int i = 0; i < consumer.Sources.Length; i++)
        {
            var src = consumer.Sources[i];

            if (src.Kind == IrOperandKind.Expression && src.Expression != null)
            {
                ReplaceUseWithExpression(src.Expression, exprInstr);
                continue;
            }

            bool isMatch = false;
            
            if (src.Kind == IrOperandKind.Register && exprInstr.Destination.Kind == IrOperandKind.Register)
                isMatch = src.CanonicalRegister == exprInstr.Destination.CanonicalRegister;
            else if (src.Kind == IrOperandKind.StackSlot && exprInstr.Destination.Kind == IrOperandKind.StackSlot)
                isMatch = src.StackOffset == exprInstr.Destination.StackOffset;

            if (isMatch && src.SsaVersion == exprInstr.Destination.SsaVersion)
            {
                consumer.Sources[i] = IrOperand.Expr(exprInstr);
            }
        }
    }

    private static bool IsInSameBlockAndSafe(IrBlock block, IrInstruction def, IrInstruction use)
    {
        int defIdx = block.Instructions.IndexOf(def);
        int useIdx = block.Instructions.IndexOf(use);
        
        if (defIdx == -1 || useIdx == -1) return false; 
        if (defIdx >= useIdx) return false; 

        
        for (int i = defIdx + 1; i < useIdx; i++)
        {
            var instr = block.Instructions[i];
            if (instr.IsDead) continue;
            if (instr.HasSideEffects) return false;
        }

        return true;
    }

    private static string GetSsaKey(IrOperand op)
    {
        if (op.Kind == IrOperandKind.StackSlot)
            return $"stack_{op.StackOffset}_{op.SsaVersion}";
        return $"{op.CanonicalRegister}_{op.SsaVersion}";
    }
}
