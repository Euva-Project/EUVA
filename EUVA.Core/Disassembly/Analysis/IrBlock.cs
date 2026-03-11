// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;


public sealed class IrBlock
{
    
    public int Index;
    public long StartAddress;
    public int ByteLength;
    public List<IrInstruction> Instructions = new();
    public List<int> Successors = new();
    public List<int> Predecessors = new();
    public bool IsEntry;
    public bool IsReturn;
    public int Idom = -1;
    public List<int> DomChildren = new();
    public HashSet<int> DominanceFrontier = new();
    public HashSet<string> Defs = new();
    public HashSet<string> Uses = new();
    public IrInstruction? LastCmpInstr;
    public HashSet<string> LiveIn = new();
    public HashSet<string> LiveOut = new();
    public LoopInfo? LoopHeader;
    public bool InLoop;

    public IrInstruction? Terminator =>
        Instructions.Count > 0 ? Instructions[^1] : null;

    
    public bool EndsWithCondBranch =>
        Terminator?.Opcode == IrOpcode.CondBranch;

    
    public bool HasTerminator =>
        Terminator?.Opcode is IrOpcode.Branch or IrOpcode.CondBranch or IrOpcode.Return;

    public override string ToString() =>
        $"Block_{Index} [{Instructions.Count} instrs, {Successors.Count} succs, {Predecessors.Count} preds]";
}


public sealed class LoopInfo
{
    public int Header;
    public HashSet<int> Body = new();
    public List<int> ExitBlocks = new();
    public int BackEdgeSource;
    public bool IsDoWhile;
    public int Depth;
}
