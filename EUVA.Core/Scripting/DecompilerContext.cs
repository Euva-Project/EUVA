// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using EUVA.Core.Disassembly;
using EUVA.Core.Disassembly.Analysis;

namespace EUVA.Core.Scripting;

public sealed class DecompilerContext
{
    public IrBlock[] Blocks { get; set; }
    public StructuredNode? AstRoot { get; set; }
    public Dictionary<string, string> GlobalRenames { get; }
    public Dictionary<string, HashSet<ulong>> GlobalStructs { get; }
    public PseudocodeEmitter? Emitter { get; }
    public long FunctionAddress { get; }
    
    public DecompilerContext(
        IrBlock[] blocks,
        Dictionary<string, string> globalRenames,
        Dictionary<string, HashSet<ulong>> globalStructs,
        long functionAddress,
        PseudocodeEmitter? emitter = null,
        StructuredNode? astRoot = null)
    {
        Blocks = blocks;
        GlobalRenames = globalRenames;
        GlobalStructs = globalStructs;
        FunctionAddress = functionAddress;
        Emitter = emitter;
        AstRoot = astRoot;
    }

    /// <summary>
    /// Helper to force a type on an SSA-versioned operand.
    /// </summary>
    public void ForceType(IrOperand operand, TypeInfo type)
    {
        operand.Type = type;
    }
}
