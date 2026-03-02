// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EUVA.Core.Disassembly;

[StructLayout(LayoutKind.Sequential)]
public struct DisassembledLine
{
    
    public long Offset;
    public byte Length;

    public int TextLength;

    public unsafe fixed char TextBuffer[96];

    public unsafe fixed byte TextColorMap[96];

    public const int MaxTextLength = 96;

    
    public const byte ColMnemonic    = 0;
    public const byte ColRegister    = 1;
    public const byte ColNumber      = 2;
    public const byte ColKeyword     = 3;
    public const byte ColPunctuation = 4;
    public const byte ColText        = 5;

    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ReadOnlySpan<char> GetText()
    {
        fixed (char* ptr = TextBuffer)
            return new ReadOnlySpan<char>(ptr, TextLength);
    }

    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        Offset = 0;
        Length = 0;
        TextLength = 0;
    }
}
