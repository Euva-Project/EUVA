// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using EUVA.Core.Scripting;
using EUVA.Core.Disassembly.Analysis;

public class PebTypeForcerPass : IDecompilerPass
{
    public PassStage Stage => PassStage.PreTypeInference;

    public void Execute(DecompilerContext context)
    {
        if (context.FunctionAddress != 0x140001000) return;
        
        foreach (var block in context.Blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.DefinesDest && 
                    instr.Destination.Kind == IrOperandKind.Register &&
                    instr.Destination.Name == "rcx")
                {
                    var pebType = new TypeInfo 
                    { 
                        BaseType = PrimitiveType.Struct,
                        PointerLevel = 1,
                        TypeName = "_PEB"
                    };
                    
                    var dest = instr.Destination; 
                    
                    dest.Type = pebType;
                    dest.Name = "pPeb";
                    
                    instr.Destination = dest; 
                    
                    //debug
                   // Console.WriteLine($"[PebTypeForcer] Injected _PEB* type at 0x{context.FunctionAddress:X}");
                    return; 
                }
            }
        }
    }
}

return new PebTypeForcerPass();