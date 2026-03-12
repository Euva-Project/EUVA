// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using EUVA.Core.Scripting;
using EUVA.Core.Disassembly.Analysis;
using System.Linq;


public class AntiDebugTrapBypasser : IDecompilerPass
{
    public PassStage Stage => PassStage.PreSsa;

    public void Execute(DecompilerContext context)
    {
        for (int i = 0; i < context.Blocks.Length; i++)
        {
            var block = context.Blocks[i];
            
            bool callsIsDebugger = block.Instructions.Any(instr => 
                instr.Opcode == IrOpcode.Call && 
                instr.Sources.Length > 0 &&
                instr.Sources[0].Kind == IrOperandKind.Constant &&
                IsKnownDebuggerApi(instr.Sources[0].ConstantValue, context)
            );

            if (callsIsDebugger)
            {

                var condBranch = block.Instructions.FirstOrDefault(ins => ins.Opcode == IrOpcode.CondBranch);
                if (condBranch != null && block.Successors.Count == 2)
                {
                    Console.WriteLine($"[AntiDebugBypasser] Found IsDebuggerPresent at block {i}");

                    int safeBlockIdx = block.Successors[0];

                    condBranch.Opcode = IrOpcode.Branch;
                    condBranch.Condition = IrCondition.None;
                    condBranch.Sources = new[] { IrOperand.BlockLabel(safeBlockIdx) };
                    
                    block.Successors.RemoveAt(1); 
                }
            }
        }
    }

    private bool IsKnownDebuggerApi(long address, DecompilerContext ctx)
    {
        if (ctx.GlobalRenames.TryGetValue($"sub_{address:X}", out string name))
        {
            return name.Contains("IsDebuggerPresent");
        }
        return false;
    }
}

return new AntiDebugTrapBypasser();
