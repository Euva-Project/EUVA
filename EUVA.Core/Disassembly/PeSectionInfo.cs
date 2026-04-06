// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly;

public readonly struct PeSectionInfo
{
    public readonly string Name;
    public readonly long FileOffset;
    public readonly long Size;
    public readonly uint VirtualAddress;
    public readonly uint Characteristics;

    public PeSectionInfo(string name, long fileOffset, long size, uint virtualAddress, uint characteristics)
    {
        Name = name;
        FileOffset = fileOffset;
        Size = size;
        VirtualAddress = virtualAddress;
        Characteristics = characteristics;
    }
}
