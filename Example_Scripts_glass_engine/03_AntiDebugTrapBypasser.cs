using System;
using System.Linq;
using EUVA.Core.Scripting;
using EUVA.Core.Disassembly.Analysis;

public class UniversalTestLoggerPass : IDecompilerPass
{
    public PassStage Stage => PassStage.PreSsa;

    public void Execute(DecompilerContext context)
    {
        if (context.Blocks == null) 
        {
            Console.WriteLine($"[Glass Engine] WARNING: Function at 0x{context.FunctionAddress:X8} has no blocks!");
            return;
        }

        int totalInstructions = 0;
        foreach (var block in context.Blocks)
        {
            if (block.Instructions != null)
            {
                totalInstructions += block.Instructions.Count;
            }
        }

        Console.WriteLine("[Glass Engine] -> analyzed Func: 0x{context.FunctionAddress:X8} | Blocks: {context.Blocks.Length}\n");
    }
}


return new UniversalTestLoggerPass();