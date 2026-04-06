// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace EUVA.Core.Disassembly;

public sealed class ImportEntry
{
    public string Name { get; set; } = "";
    public ulong IatAddress { get; set; } 
    public long IatFileOffset { get; set; }
}

public sealed class DllImportInfo
{
    public string DllName { get; set; } = "";
    public List<ImportEntry> Entries { get; } = new();
}

public sealed class PeImportScanner
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private unsafe struct IMAGE_IMPORT_DESCRIPTOR
    {
        public uint OriginalFirstThunk;
        public uint TimeDateStamp;
        public uint ForwarderChain;
        public uint Name;
        public uint FirstThunk;
    }

    public unsafe List<DllImportInfo> Scan(byte* map, long fileLen, PeSectionInfo[] sections, int count, uint importDirectoryRva, int bitness)
    {
        var result = new List<DllImportInfo>();
        try
        {
            byte[] bytes = new byte[fileLen];
            Marshal.Copy((IntPtr)map, bytes, 0, (int)fileLen);
            var peFile = AsmResolver.PE.File.PEFile.FromBytes(bytes);
            var image = AsmResolver.PE.PEImage.FromFile(peFile);

            foreach (var module in image.Imports)
            {
                var dllInfo = new DllImportInfo { DllName = module.Name ?? "Unknown" };
                foreach (var symbol in module.Symbols)
                {
                    uint symRva = (uint)(symbol.AddressTableEntry?.Rva ?? 0);
                    dllInfo.Entries.Add(new ImportEntry
                    {
                        Name = symbol.Name ?? (symbol.Ordinal != 0 ? $"Ordinal_{symbol.Ordinal}" : "Unknown"),
                        IatAddress = (ulong)symRva,
                        IatFileOffset = RvaToFileOffset(symRva, sections, count)
                    });
                }
                result.Add(dllInfo);
            }
            if (result.Count > 0) return result;
        }
        catch { }

        if (importDirectoryRva == 0) return result;

        long descriptorOffset = RvaToFileOffset(importDirectoryRva, sections, count);
        if (descriptorOffset < 0 || descriptorOffset >= fileLen) return result;

        IMAGE_IMPORT_DESCRIPTOR* desc = (IMAGE_IMPORT_DESCRIPTOR*)(map + descriptorOffset);
        while (desc->Name != 0)
        {
            var dllInfo = new DllImportInfo();
            long nameOffset = RvaToFileOffset(desc->Name, sections, count);
            if (nameOffset >= 0 && nameOffset < fileLen)
                dllInfo.DllName = Marshal.PtrToStringAnsi((IntPtr)(map + nameOffset)) ?? "Unknown";

            uint lookupRva = desc->OriginalFirstThunk != 0 ? desc->OriginalFirstThunk : desc->FirstThunk;
            uint iatRva = desc->FirstThunk;
            long lookupOffset = RvaToFileOffset(lookupRva, sections, count);
            if (lookupOffset >= 0 && lookupOffset < fileLen)
            {
                int ptrSize = bitness / 8;
                byte* lookupPtr = map + lookupOffset;
                int i = 0;
                while (true)
                {
                    if (lookupOffset + (i + 1) * ptrSize > fileLen) break;
                    ulong entryValue = (bitness == 64) ? *(ulong*)(lookupPtr + i * 8) : *(uint*)(lookupPtr + i * 4);
                    if (entryValue == 0) break;

                    bool isOrdinal = (bitness == 64) ? (entryValue >> 63) != 0 : (entryValue >> 31) != 0;
                    var entry = new ImportEntry { 
                        IatAddress = iatRva + (ulong)(i * ptrSize),
                        IatFileOffset = RvaToFileOffset(iatRva + (uint)(i * ptrSize), sections, count)
                    };

                    if (isOrdinal) entry.Name = $"Ordinal_{entryValue & 0xFFFF}";
                    else
                    {
                        uint nameRva = (uint)(entryValue & 0xFFFFFFFF);
                        long nameFileOff = RvaToFileOffset(nameRva, sections, count);
                        if (nameFileOff >= 0 && nameFileOff + 2 < fileLen)
                            entry.Name = Marshal.PtrToStringAnsi((IntPtr)(map + nameFileOff + 2)) ?? "Unknown";
                        else entry.Name = $"Import_{i}";
                    }
                    dllInfo.Entries.Add(entry);
                    i++;
                }
            }
            result.Add(dllInfo);
            desc++;
            if ((byte*)desc + sizeof(IMAGE_IMPORT_DESCRIPTOR) > map + fileLen) break;
        }

        return result;
    }

    public static long RvaToFileOffset(uint rva, PeSectionInfo[] sections, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var sec = sections[i];
            if (rva >= sec.VirtualAddress && rva < sec.VirtualAddress + sec.Size)
                return (rva - sec.VirtualAddress) + sec.FileOffset;
        }
        return -1;
    }
}
