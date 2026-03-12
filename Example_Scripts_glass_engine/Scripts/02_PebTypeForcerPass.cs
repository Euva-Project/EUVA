// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using EUVA.Core.Scripting;
using EUVA.Core.Disassembly.Analysis;


public class PebTypeForcerPass : IDecompilerPass
{
    public PassStage Stage => PassStage.PreTypeInference;

    public void Execute(DecompilerContext context)
    {.
        bool isPebFunction = context.FunctionAddress == 0x140001000; 
        
        if (isPebFunction)
        {
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
                            Kind = TypeKind.Pointer,
                            BaseType = new TypeInfo { Kind = TypeKind.Struct, StructName = "_PEB" }
                        };
                        
                        context.ForceType(instr.Destination, pebType);
                        
                        instr.Destination.Name = "pPeb";
                        
                        Console.WriteLine($"[PebTypeForcer] Injected _PEB* type at 0x{context.FunctionAddress:X}");
                        return; 
                    }
                }
            }
        }
    }
}

return new PebTypeForcerPass();
