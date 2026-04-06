// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.InteropServices;

namespace EUVA.Core.Disassembly;

[StructLayout(LayoutKind.Sequential)]
public struct BasicBlock
{
    public long StartOffset;
    public int ByteLength;
    public int InstructionCount;
    public int[] Successors;
    public long[] SuccessorOffsets; 
    public bool IsConditional;
    public bool IsReturn;
    public bool IsFirstBlock;
    public bool IsData; 
}
