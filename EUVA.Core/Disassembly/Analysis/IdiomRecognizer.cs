// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace EUVA.Core.Disassembly.Analysis;

public static class IdiomRecognizer
{
    public static void RecognizeIdioms(StructuredNode? root)
    {
        if (root == null) return;
        TransformNode(root);
    }

    private static void TransformNode(StructuredNode node)
    {
        if (node is SequenceNode seq)
        {
            for (int i = 0; i < seq.Children.Count; i++)
            {
                var child = seq.Children[i];
                if (child is DoWhileNode loop)
                {
                    if (TryCollapseMemset(loop, out var memsetCall))
                    {
                        seq.Children[i] = memsetCall;
                        continue;
                    }
                    if (TryCollapseMemcpy(loop, out var memcpyCall))
                    {
                        seq.Children[i] = memcpyCall;
                        continue;
                    }
                }
                TransformNode(seq.Children[i]);
            }
        }
        else if (node is IfNode ifn)
        {
            if (ifn.ThenBody != null) TransformNode(ifn.ThenBody);
            if (ifn.ElseBody != null) TransformNode(ifn.ElseBody);
        }
        else if (node is WhileNode wn) TransformNode(wn.Body);
        else if (node is DoWhileNode dwn) TransformNode(dwn.Body);
        else if (node is ForNode fn) TransformNode(fn.Body);
        else if (node is SwitchNode sn)
        {
            foreach (var c in sn.Cases) TransformNode(c.Body);
            if (sn.DefaultBody != null) TransformNode(sn.DefaultBody);
        }
    }

    private static void CollectInstructions(StructuredNode node, List<IrInstruction> instrs)
    {
        if (node is BlockNode bb)
        {
            foreach (var instr in bb.Block.Instructions)
            {
                if (!instr.IsDead) instrs.Add(instr);
            }
        }
        else if (node is SequenceNode seq)
        {
            foreach (var child in seq.Children) CollectInstructions(child, instrs);
        }
        else if (node is IfNode ifn)
        {
            if (ifn.ThenBody != null) CollectInstructions(ifn.ThenBody, instrs);
            if (ifn.ElseBody != null) CollectInstructions(ifn.ElseBody, instrs);
        }
    }

    private static bool TryCollapseMemset(DoWhileNode loop, out StructuredNode memsetCall)
    {
        memsetCall = null!;

        var bodyInstrs = new List<IrInstruction>();
        CollectInstructions(loop.Body, bodyInstrs);

        IrInstruction? storeInstr = null;

        foreach (var instr in bodyInstrs)
        {
            if (instr.Opcode == IrOpcode.Store && (instr.Sources[0].Kind == IrOperandKind.Constant && instr.Sources[0].ConstantValue == 0))
                storeInstr = instr;
        }

        if (storeInstr != null && loop.ConditionInstr != null)
        {
            var storeDest = storeInstr.Destination;
            if (storeDest.Kind != IrOperandKind.Memory) return false;

            var ptrReg = storeDest.MemBase;

            var cond = loop.ConditionInstr;
            IrOperand? countArg = null;

            if (cond.Sources.Length >= 2)
            {
                var left = cond.Sources[0];
                var right = cond.Sources[1];

                if (left.Kind == IrOperandKind.Register && IrOperand.GetCanonical(left.Register) == IrOperand.GetCanonical(ptrReg))
                {
                    if (right.Kind == IrOperandKind.Expression && right.Expression != null)
                    {
                        var expr = right.Expression;
                        if (expr.Opcode == IrOpcode.Add || expr.Opcode == IrOpcode.Sub)
                        {
                            var offset = expr.Sources.Length >= 2 && expr.Sources[1].Kind == IrOperandKind.Constant ? Math.Abs(expr.Sources[1].ConstantValue) : 0;
                            if (offset > 0) countArg = IrOperand.Const(offset, 32);
                        }
                    }
                    else if (right.Kind == IrOperandKind.Constant)
                    {

                        countArg = IrOperand.Const(Math.Abs(right.ConstantValue), 32);
                    }
                }
            }

            if (countArg != null)
            {
                var callInstr = IrInstruction.MakeCall(
                    IrOperand.Reg(Iced.Intel.Register.None, 64),
                    IrOperand.Reg(Iced.Intel.Register.None, 64),
                    new IrOperand[] { IrOperand.Reg(ptrReg, 64), storeInstr.Sources[0], countArg.Value }
                );
                callInstr.Sources[0].Name = "memset";
                callInstr.Comment = "idiom:memset";

                var newBlock = new IrBlock();
                newBlock.Instructions.Add(callInstr);
                memsetCall = new BlockNode(newBlock);
                return true;
            }
        }
        return false;
    }

    private static bool TryCollapseMemcpy(DoWhileNode loop, out StructuredNode memcpyCall)
    {
        memcpyCall = null!;
        var bodyInstrs = new List<IrInstruction>();
        CollectInstructions(loop.Body, bodyInstrs);

        IrInstruction? loadInstr = null;
        IrInstruction? storeInstr = null;
        IrInstruction? srcInc = null;
        IrInstruction? dstInc = null;

        foreach (var instr in bodyInstrs)
        {
            if (instr.Opcode == IrOpcode.Load) loadInstr = instr;
            else if (instr.Opcode == IrOpcode.Store) storeInstr = instr;
            else if (instr.Opcode == IrOpcode.Add && instr.Destination.Kind == IrOperandKind.Register)
            {
                if (srcInc == null) srcInc = instr;
                else dstInc = instr;
            }
        }

        if (loadInstr != null && storeInstr != null && srcInc != null && dstInc != null && loop.ConditionInstr != null)
        {
            if (loadInstr.Sources[0].Kind == IrOperandKind.Memory &&
                IrOperand.GetCanonical(loadInstr.Sources[0].MemBase) == IrOperand.GetCanonical(srcInc.Destination.Register) &&
                storeInstr.Destination.Kind == IrOperandKind.Memory &&
                IrOperand.GetCanonical(storeInstr.Destination.MemBase) == IrOperand.GetCanonical(dstInc.Destination.Register))
            {
                var countArg = loop.ConditionInstr.Sources.Length >= 2 ? loop.ConditionInstr.Sources[1] : IrOperand.Const(0, 32);

                var callInstr = IrInstruction.MakeCall(
                    IrOperand.Reg(Iced.Intel.Register.None, 64),
                    IrOperand.Reg(Iced.Intel.Register.None, 64),
                    new IrOperand[] { IrOperand.Reg(dstInc.Destination.Register, 64), IrOperand.Reg(srcInc.Destination.Register, 64), countArg }
                );
                callInstr.Sources[0].Name = "memcpy";
                callInstr.Comment = "idiom:memcpy";

                var newBlock = new IrBlock();
                newBlock.Instructions.Add(callInstr);
                memcpyCall = new BlockNode(newBlock);
                return true;
            }
        }
        return false;
    }
}
