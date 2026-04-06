// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly;

public sealed class Function
{
    public string Name { get; set; } = string.Empty;
    public long StartRva { get; set; }
    public long EndRva { get; set; }
    public long FileOffset { get; set; }
    public int Size => (int)(EndRva - StartRva);

    public bool ContainsRva(long rva) => rva >= StartRva && rva < EndRva;
}
