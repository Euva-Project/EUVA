// SPDX-License-Identifier: GPL-3.0-or-later

using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Iced.Intel;

namespace EUVA.Core.Disassembly;

public sealed class CfgScanner
{
    private const int MaxBlocks = 4096;
    private const int MaxBlockInstr = 512;

    
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe BasicBlock[] ScanFunction(byte* data, int length, long baseAddress, int bitness)
    {
        if (length <= 0) return Array.Empty<BasicBlock>();

        
        var leaders = ArrayPool<long>.Shared.Rent(MaxBlocks);
        var leaderCount = 0;

        try
        {
            leaders[leaderCount++] = baseAddress; 

            var reader = new UnsafePointerCodeReader();
            reader.Reset(data, length);
            var decoder = Decoder.Create(bitness, reader, (ulong)baseAddress);
            ulong endIP = (ulong)baseAddress + (ulong)length;
            Instruction instr = default;

            while (decoder.IP < endIP)
            {
                decoder.Decode(out instr);
                if (instr.IsInvalid)
                {
                    
                    continue;
                }

                ulong nextIP = instr.NextIP;

                switch (instr.FlowControl)
                {
                    case FlowControl.ConditionalBranch:
                        
                        AddLeader(leaders, ref leaderCount, (long)instr.NearBranchTarget, baseAddress, length);
                        AddLeader(leaders, ref leaderCount, (long)nextIP, baseAddress, length);
                        break;

                    case FlowControl.UnconditionalBranch:
                        AddLeader(leaders, ref leaderCount, (long)instr.NearBranchTarget, baseAddress, length);
                        
                        AddLeader(leaders, ref leaderCount, (long)nextIP, baseAddress, length);
                        break;

                    case FlowControl.Return:
                    case FlowControl.Exception:
                        
                        AddLeader(leaders, ref leaderCount, (long)nextIP, baseAddress, length);
                        break;

                    case FlowControl.Call:
                    case FlowControl.IndirectCall:
                        
                        break;

                    case FlowControl.IndirectBranch:
                        
                        AddLeader(leaders, ref leaderCount, (long)nextIP, baseAddress, length);
                        break;
                }
            }

            
            Array.Sort(leaders, 0, leaderCount);
            int uniqueCount = DeduplicateLeaders(leaders, leaderCount);

            
            var blocks = BuildBlocks(data, length, baseAddress, bitness, leaders, uniqueCount);

            
            return CollapsePaddingBlocks(blocks, data, length, baseAddress, bitness);
        }
        finally
        {
            ArrayPool<long>.Shared.Return(leaders);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddLeader(long[] leaders, ref int count, long addr, long baseAddr, int length)
    {
        if (addr < baseAddr || addr >= baseAddr + length) return;
        if (count >= leaders.Length) return;
        leaders[count++] = addr;
    }

    private static int DeduplicateLeaders(long[] arr, int count)
    {
        if (count <= 1) return count;
        int write = 1;
        for (int i = 1; i < count; i++)
        {
            if (arr[i] != arr[write - 1])
                arr[write++] = arr[i];
        }
        return write;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private unsafe BasicBlock[] BuildBlocks(byte* data, int length, long baseAddress,
        int bitness, long[] leaders, int leaderCount)
    {
        var blocks = new BasicBlock[leaderCount];
        var reader = new UnsafePointerCodeReader();

        for (int bi = 0; bi < leaderCount; bi++)
        {
            long blockStart = leaders[bi];
            long blockEnd = (bi + 1 < leaderCount) ? leaders[bi + 1] : baseAddress + length;
            int blockLen = (int)(blockEnd - blockStart);
            if (blockLen <= 0) { blocks[bi].StartOffset = blockStart; continue; }

            int dataOff = (int)(blockStart - baseAddress);
            if (dataOff < 0 || dataOff >= length) { blocks[bi].StartOffset = blockStart; continue; }

            reader.Reset(data + dataOff, Math.Min(blockLen, length - dataOff));
            var decoder = Decoder.Create(bitness, reader, (ulong)blockStart);
            ulong endIP = (ulong)blockEnd;

            int instrCount = 0;
            Instruction lastInstr = default;
            bool hasLast = false;

            while (decoder.IP < endIP && instrCount < MaxBlockInstr)
            {
                decoder.Decode(out var instr);
                if (instr.IsInvalid) { instrCount++; continue; }
                lastInstr = instr;
                hasLast = true;
                instrCount++;
            }

            blocks[bi].StartOffset = blockStart;
            blocks[bi].ByteLength = blockLen;
            blocks[bi].InstructionCount = instrCount;
            blocks[bi].IsFirstBlock = (bi == 0);

            if (!hasLast)
            {
                blocks[bi].IsReturn = true;
                blocks[bi].Successors = Array.Empty<int>();
                continue;
            }

            
            switch (lastInstr.FlowControl)
            {
                case FlowControl.ConditionalBranch:
                {
                    blocks[bi].IsConditional = true;
                    int targetIdx = FindBlock(leaders, leaderCount, (long)lastInstr.NearBranchTarget);
                    int fallthroughIdx = FindBlock(leaders, leaderCount, (long)lastInstr.NextIP);
                    if (targetIdx >= 0 && fallthroughIdx >= 0)
                        blocks[bi].Successors = new[] { fallthroughIdx, targetIdx };
                    else if (targetIdx >= 0)
                        blocks[bi].Successors = new[] { targetIdx };
                    else if (fallthroughIdx >= 0)
                        blocks[bi].Successors = new[] { fallthroughIdx };
                    else
                        blocks[bi].Successors = Array.Empty<int>();
                    break;
                }

                case FlowControl.UnconditionalBranch:
                {
                    int targetIdx = FindBlock(leaders, leaderCount, (long)lastInstr.NearBranchTarget);
                    blocks[bi].Successors = targetIdx >= 0 ? new[] { targetIdx } : Array.Empty<int>();
                    break;
                }

                case FlowControl.Return:
                case FlowControl.Exception:
                    blocks[bi].IsReturn = true;
                    blocks[bi].Successors = Array.Empty<int>();
                    break;

                case FlowControl.IndirectBranch:
                    blocks[bi].Successors = Array.Empty<int>();
                    break;

                default:
                {
                    
                    int nextIdx = bi + 1 < leaderCount ? bi + 1 : -1;
                    blocks[bi].Successors = nextIdx >= 0 ? new[] { nextIdx } : Array.Empty<int>();
                    break;
                }
            }
        }

        return blocks;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindBlock(long[] leaders, int count, long address)
    {
        int idx = Array.BinarySearch(leaders, 0, count, address);
        return idx >= 0 ? idx : -1;
    }

    private unsafe BasicBlock[] CollapsePaddingBlocks(BasicBlock[] blocks, byte* data, int length, long baseAddress, int bitness)
    {
        if (blocks.Length <= 1) return blocks;

        var remove = new bool[blocks.Length];
        var reader = new UnsafePointerCodeReader();

        for (int i = 1; i < blocks.Length; i++) 
        {
            if (blocks[i].ByteLength <= 0) continue;
            if (blocks[i].IsConditional || blocks[i].IsFirstBlock) continue;

            int dataOff = (int)(blocks[i].StartOffset - baseAddress);
            if (dataOff < 0 || dataOff >= length) continue;

            int blockLen = Math.Min(blocks[i].ByteLength, length - dataOff);
            if (blockLen <= 0) continue;

            reader.Reset(data + dataOff, blockLen);
            var decoder = Decoder.Create(bitness, reader, (ulong)blocks[i].StartOffset);
            ulong endIP = (ulong)blocks[i].StartOffset + (ulong)blockLen;

            bool allPadding = true;
            int count = 0;
            while (decoder.IP < endIP && count < MaxBlockInstr)
            {
                decoder.Decode(out var instr);
                if (instr.IsInvalid) { count++; continue; }
                if (instr.Mnemonic != Iced.Intel.Mnemonic.Nop &&
                    instr.Mnemonic != Iced.Intel.Mnemonic.Fnop &&
                    instr.Mnemonic != Iced.Intel.Mnemonic.Int3)
                {
                    allPadding = false;
                    break;
                }
                count++;
            }

            if (allPadding && count > 0)
            {
                
                var paddingSuccessors = blocks[i].Successors ?? Array.Empty<int>();

                
                for (int p = 0; p < blocks.Length; p++)
                {
                    if (remove[p] || blocks[p].Successors == null) continue;

                    bool pointsToRemoved = false;
                    for (int s = 0; s < blocks[p].Successors.Length; s++)
                    {
                        if (blocks[p].Successors[s] == i)
                        {
                            pointsToRemoved = true;
                            break;
                        }
                    }

                    if (pointsToRemoved)
                    {
                        
                        long predEnd = blocks[p].StartOffset + blocks[p].ByteLength;
                        long thisEnd = blocks[i].StartOffset + blocks[i].ByteLength;
                        if (thisEnd > predEnd)
                            blocks[p].ByteLength = (int)(thisEnd - blocks[p].StartOffset);

                        
                        var merged = new List<int>();
                        foreach (int s in blocks[p].Successors)
                        {
                            if (s == i)
                            {
                                
                                foreach (int ps in paddingSuccessors)
                                {
                                    if (!merged.Contains(ps))
                                        merged.Add(ps);
                                }
                            }
                            else
                            {
                                if (!merged.Contains(s))
                                    merged.Add(s);
                            }
                        }
                        blocks[p].Successors = merged.ToArray();
                    }
                }
                remove[i] = true;
            }
        }

        int kept = 0;
        for (int i = 0; i < blocks.Length; i++)
            if (!remove[i]) kept++;

        if (kept == blocks.Length) return blocks; 

        var remap = new int[blocks.Length];
        var result = new BasicBlock[kept];
        int wi = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            if (remove[i]) { remap[i] = -1; continue; }
            remap[i] = wi;
            result[wi++] = blocks[i];
        }

        for (int i = 0; i < result.Length; i++)
        {
            if (result[i].Successors == null) continue;
            var newSucc = new List<int>();
            foreach (int s in result[i].Successors)
            {
                if (s >= 0 && s < remap.Length && remap[s] >= 0)
                    newSucc.Add(remap[s]);
            }
            result[i].Successors = newSucc.ToArray();
        }

        return result;
    }
}
