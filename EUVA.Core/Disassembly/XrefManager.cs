// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using Iced.Intel;

namespace EUVA.Core.Disassembly;

public sealed class XrefManager
{
    private readonly Dictionary<long, List<long>> _targetToSources = new();

    public void BuildXrefs(IEnumerable<Instruction> instructions)
    {
        _targetToSources.Clear();
        foreach (var instr in instructions)
        {
            if (instr.FlowControl is FlowControl.Call or FlowControl.ConditionalBranch or FlowControl.UnconditionalBranch)
            {
                if (instr.NearBranchTarget != 0)
                {
                    AddXref((long)instr.NearBranchTarget, (long)instr.IP);
                }
            }

            bool isRipRel = instr.MemoryBase == Register.RIP || instr.MemoryBase == Register.EIP;
            if (isRipRel)
            {
                long target = (long)instr.NextIP + (long)instr.MemoryDisplacement64;
                AddXref(target, (long)instr.IP);
            }
            else
            {
                for (int i = 0; i < instr.OpCount; i++)
                {
                    if (instr.GetOpKind(i) == OpKind.Memory)
                    {
                        if (instr.MemoryBase == Register.None && instr.MemoryIndex == Register.None)
                        {
                            long target = (long)instr.MemoryDisplacement64;
                            if (target != 0)
                                AddXref(target, (long)instr.IP);
                        }
                        break;
                    }
                }
            }
        }
    }

    private void AddXref(long targetRva, long sourceRva)
    {
        if (!_targetToSources.TryGetValue(targetRva, out var sources))
        {
            sources = new List<long>();
            _targetToSources[targetRva] = sources;
        }
        sources.Add(sourceRva);
    }

    public IReadOnlyList<long> GetXrefs(long targetRva)
    {
        if (_targetToSources.TryGetValue(targetRva, out var sources))
            return sources;
        return Array.Empty<long>();
    }
}
