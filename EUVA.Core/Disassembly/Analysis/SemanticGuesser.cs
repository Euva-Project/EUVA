// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Iced.Intel;

namespace EUVA.Core.Disassembly.Analysis;

public static class SemanticGuesser
{

    public static void GuessNames(
        IrBlock[] blocks,
        Dictionary<long, string> strings,
        Dictionary<ulong, string> imports,
        Dictionary<string, VariableSymbol> userRenames,
        Func<ulong, string>? stringExtractor)
    {
        foreach (var block in blocks)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var instr = block.Instructions[i];
                if (instr.IsDead) continue;

                string? newName     = null;
                ulong   triggerAddr = 0;

                foreach (var src in instr.Sources)
                {
                    ulong addr = 0;
                    if (src.Kind == IrOperandKind.Constant)
                        addr = (ulong)src.ConstantValue;
                    else if (src.Kind == IrOperandKind.Memory && src.MemBase == Register.None)
                        addr = (ulong)src.MemDisplacement;

                    if (addr == 0) continue;

                    string? str = strings.TryGetValue((long)addr, out var s)
                        ? s
                        : stringExtractor?.Invoke(addr);

                    if (!string.IsNullOrEmpty(str))
                    {
                        newName = SignatureCache.GetNameForString(str);
                        if (newName != null) { triggerAddr = addr; break; }
                    }
                }

                if (newName == null && instr.Opcode == IrOpcode.Call && instr.Sources.Length > 0)
                {
                    triggerAddr = ExtractApiAddress(instr.Sources[0]);
                    if (triggerAddr != 0
                        && imports.TryGetValue(triggerAddr, out var apiName)
                        && !string.IsNullOrEmpty(apiName))
                    {
                        int    sep      = apiName.IndexOf("::");
                        string cleanApi = sep >= 0 ? apiName.Substring(sep + 2) : apiName;

                        if (SignatureCache.Db.ApiSignatures.TryGetValue(cleanApi, out var sig)
                            && sig.ReturnName != null)
                        {
                            newName = sig.ReturnName;
                        }
                    }
                }

                if (newName != null)
                    ApplySemanticName(block, i, newName, triggerAddr, userRenames);
            }
        }
    }

    public static void GuessChainNames(
        IrBlock[] blocks,
        Dictionary<ulong, string> imports,
        ulong funcStartAddr,
        Dictionary<string, VariableSymbol> userRenames)
    {
        if (SignatureCache.Db.ApiChains == null || SignatureCache.Db.ApiChains.Count == 0)
            return;

        var calledApis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead || instr.Opcode != IrOpcode.Call || instr.Sources.Length == 0)
                    continue;

                ulong apiAddr = ExtractApiAddress(instr.Sources[0]);
                if (apiAddr == 0) continue;

                if (!imports.TryGetValue(apiAddr, out var apiName)
                    || string.IsNullOrEmpty(apiName)) continue;

                int sep = apiName.IndexOf("::");
                calledApis.Add(sep >= 0 ? apiName.Substring(sep + 2) : apiName);
            }
        }

        if (calledApis.Count == 0) return;

        string? chainFuncName = SignatureCache.GetChainName(calledApis);
        if (chainFuncName == null) return;

        
        string funcKey = $"sub_{funcStartAddr:X}";
        if (!userRenames.ContainsKey(funcKey))
            userRenames[funcKey] = new VariableSymbol(chainFuncName, chainFuncName, false);
    }

    private static void ApplySemanticName(
        IrBlock currentBlock, int instrIdx,
        string newName, ulong triggerAddr,
        Dictionary<string, VariableSymbol> userRenames)
    {
        var instr  = currentBlock.Instructions[instrIdx];
        var target = instr.Destination;

        
        if (target.Kind == IrOperandKind.None && instr.Opcode == IrOpcode.Store)
        {
            foreach (var src in instr.Sources)
            {
                ulong srcAddr = 0;
                if (src.Kind == IrOperandKind.Constant)
                    srcAddr = (ulong)src.ConstantValue;
                else if (src.Kind == IrOperandKind.Memory && src.MemBase == Register.None)
                    srcAddr = (ulong)src.MemDisplacement;

                if (srcAddr != 0 && srcAddr == triggerAddr) continue;
                target = src;
                break;
            }
        }

        if (target.Kind == IrOperandKind.None) return;

        
        if (target.Kind == IrOperandKind.StackSlot)
        {
            string key = NamingConventions.GetStackVariableName(target.StackOffset);
            userRenames[key] = new VariableSymbol(newName, newName, false);
        }
        
        else if (target.Kind == IrOperandKind.Constant)
        {
            userRenames[$"g_0x{target.ConstantValue:X}"] =
                new VariableSymbol(newName, newName, false);
        }
        else if (target.Kind == IrOperandKind.Memory && target.MemBase == Register.None)
        {
            userRenames[$"g_0x{target.MemDisplacement:X}"] =
                new VariableSymbol(newName, newName, false);
        }
        
        else if (target.Kind == IrOperandKind.Register && target.Register != Register.None)
        {
            Register canon  = IrOperand.GetCanonical(target.Register);
            string   regKey = $"reg_{canon}_{target.SsaVersion}";
            userRenames[regKey] = new VariableSymbol(newName, newName, false);
        }
    }

    internal static ulong ExtractApiAddress(IrOperand op)
    {
        if (op.Kind == IrOperandKind.Memory && op.MemBase == Register.None)
            return (ulong)op.MemDisplacement;
        if (op.Kind == IrOperandKind.Constant)
            return (ulong)op.ConstantValue;

        if (op.Kind == IrOperandKind.Expression
            && op.Expression != null
            && op.Expression.Opcode == IrOpcode.Load
            && op.Expression.Sources.Length == 1)
        {
            var s = op.Expression.Sources[0];
            if (s.Kind == IrOperandKind.Memory && s.MemBase == Register.None)
                return (ulong)s.MemDisplacement;
            if (s.Kind == IrOperandKind.Constant)
                return (ulong)s.ConstantValue;
        }
        return 0;
    }
}