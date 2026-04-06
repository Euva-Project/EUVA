// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Iced.Intel;

namespace EUVA.Core.Disassembly;

public sealed class FunctionDiscoverer
{
    private const int MaxInstrPerFunc = 5000;

    public unsafe List<Function> Discover(byte* map, long fullLen, long entryPoint, ExecutableRange[]? execRanges, int bitness)
    {
        var functions = new HashSet<long>();
        var visitedBlocks = new HashSet<long>();
        var queue = new Queue<long>();

        if (entryPoint >= 0 && entryPoint < fullLen)
            queue.Enqueue(entryPoint);

        var reader = new UnsafePointerCodeReader();

        while (queue.Count > 0)
        {
            long currentIP = queue.Dequeue();
            if (visitedBlocks.Contains(currentIP)) continue;
            visitedBlocks.Add(currentIP);
            functions.Add(currentIP);

            if (currentIP < 0 || currentIP >= fullLen) continue;
            if (execRanges != null && !IsExecutable(currentIP, execRanges)) continue;

            reader.Reset(map + currentIP, (int)(fullLen - currentIP));
            var decoder = Decoder.Create(bitness, reader, (ulong)currentIP);

            int instrCount = 0;
            while (decoder.IP < (ulong)fullLen && instrCount < MaxInstrPerFunc)
            {
                decoder.Decode(out var instr);
                if (instr.IsInvalid) break;
                instrCount++;

                if (instr.Mnemonic == Mnemonic.Int3 || instr.Mnemonic == Mnemonic.Nop) break;

                bool isTerminal = false;
                switch (instr.FlowControl)
                {
                    case FlowControl.Call:
                        if (instr.NearBranchTarget != 0)
                        {
                            long target = (long)instr.NearBranchTarget;
                            if (target >= 0 && target < fullLen && (execRanges == null || IsExecutable(target, execRanges)))
                            {
                                if (!functions.Contains(target))
                                    queue.Enqueue(target);
                            }
                        }
                        break;

                    case FlowControl.ConditionalBranch:
                        long t1 = (long)instr.NearBranchTarget;
                        if (!visitedBlocks.Contains(t1)) queue.Enqueue(t1);
                        break;

                    case FlowControl.UnconditionalBranch:
                        long t2 = (long)instr.NearBranchTarget;
                        if (!visitedBlocks.Contains(t2)) queue.Enqueue(t2);
                        isTerminal = true;
                        break;

                    case FlowControl.Return:
                    case FlowControl.Exception:
                        isTerminal = true;
                        break;
                }

                if (isTerminal) break;
            }
        }

        var sorted = functions.OrderBy(x => x).ToList();
        var result = new List<Function>();
        for (int i = 0; i < sorted.Count; i++)
        {
            long start = sorted[i];
            long nextStart = (i + 1 < sorted.Count) ? sorted[i + 1] : fullLen;
            
            long end = Math.Min(start + 0x8000, nextStart);

            result.Add(new Function 
            { 
                Name = $"sub_{start:X}",
                StartRva = start, 
                EndRva = end,
                FileOffset = start
            });
        }

        return result;
    }

    public Function? GetParentFunction(IEnumerable<Function> functions, long rva)
    {
        return functions.FirstOrDefault(f => rva >= f.StartRva && rva < f.EndRva);
    }

    private static bool IsExecutable(long addr, ExecutableRange[] sections)
    {
        foreach (var sec in sections)
            if (addr >= sec.Start && addr < sec.End) return true;
        return false;
    }
}
