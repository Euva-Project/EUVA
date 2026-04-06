// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using EUVA.Core.Scripting;
using EUVA.Core.Disassembly.Analysis;


public class XorDeobfuscatorPass : IDecompilerPass
{
    public PassStage Stage => PassStage.PostSsa;

    public void Execute(DecompilerContext context)
    {
        int optimizations = 0;

        foreach (var block in context.Blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;

            
                if (instr.Opcode == IrOpcode.Xor && 
                    instr.Sources.Length == 2 && 
                    instr.Sources[1].Kind == IrOperandKind.Constant)
                {
                    
                    if (instr.Sources[0].Kind == IrOperandKind.Constant)
                    {
                        long val1 = instr.Sources[0].ConstantValue;
                        long val2 = instr.Sources[1].ConstantValue;
                        
                       
                        long realValue = val1 ^ val2;

                       
                        instr.Opcode = IrOpcode.Assign;
                        instr.Sources = new[] { IrOperand.Const(realValue, instr.Sources[0].BitSize) };
                        
                        optimizations++;
                    }
                }
            }
        }

        if (optimizations > 0)
        {
            Console.WriteLine($"[XorDeobfuscator] Simplified {optimizations} obfuscated XOR expressions at 0x{context.FunctionAddress:X}");
        }
    }
}

return new XorDeobfuscatorPass();
