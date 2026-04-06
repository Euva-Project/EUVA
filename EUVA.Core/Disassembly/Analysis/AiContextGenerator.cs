// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text;

namespace EUVA.Core.Disassembly.Analysis;

public sealed class AiContextGenerator
{
    public static string Generate(
        IrBlock[] blocks, 
        Dictionary<ulong, string> imports, 
        Dictionary<long, string> strings,
        CallingConventionAnalyzer.FunctionSignature? signature = null,
        IReadOnlyDictionary<string, VariableSymbol>? userRenames = null)
    {
        var sb = new StringBuilder();
        
      
        if (signature != null)
        {
            var args = string.Join(", ", signature.Arguments.Select(a => $"{a.Type} {a.Name}"));
            sb.AppendLine($"// Function: {signature.ReturnType} {signature.Name}({args})");
        }
        
        
        var relevantImports = new HashSet<string>();
        var relevantStrings = new HashSet<string>();
        
        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;
                foreach (var op in instr.Sources)
                {
                    if (op.Kind == IrOperandKind.Constant)
                    {
                        if (imports.TryGetValue((ulong)op.ConstantValue, out var imp)) relevantImports.Add($"0x{op.ConstantValue:X} -> {imp}");
                        if (strings.TryGetValue(op.ConstantValue, out var str)) relevantStrings.Add($"0x{op.ConstantValue:X} -> \"{str}\"");
                    }
                }
            }
        }

        if (relevantImports.Count > 0)
        {
            sb.AppendLine("// External Calls:");
            foreach (var imp in relevantImports) sb.AppendLine($"//   {imp}");
        }
        if (relevantStrings.Count > 0)
        {
            sb.AppendLine("// String Literals:");
            foreach (var str in relevantStrings) sb.AppendLine($"//   {str}");
        }
        sb.AppendLine();

       
        foreach (var block in blocks)
        {
            sb.AppendLine($"block_{block.Index}:");
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;
                sb.AppendLine("  " + FormatInstruction(instr, imports, strings, userRenames));
            }
        }
        return sb.ToString();
    }

    private static string FormatInstruction(IrInstruction instr, Dictionary<ulong, string> imports, Dictionary<long, string> strings, IReadOnlyDictionary<string, VariableSymbol>? userRenames = null)
    {
        var dst = instr.DefinesDest ? $"{FormatOperand(instr.Destination, imports, strings, userRenames)} = " : "";
        var srcs = string.Join(", ", instr.Sources.Select(s => FormatOperand(s, imports, strings, userRenames)));
        var op = instr.Opcode.ToString().ToLower();
        var cond = instr.Condition != IrCondition.None ? $" [{instr.Condition}]" : "";
        
        return $"{dst}{op}{cond}({srcs})";
    }

    private static string FormatOperand(IrOperand op, Dictionary<ulong, string> imports, Dictionary<long, string> strings, IReadOnlyDictionary<string, VariableSymbol>? userRenames = null)
    {
        string typePrefix = op.Type.BaseType != PrimitiveType.Unknown ? $"({op.Type}) " : "";
        
        string baseStr = op.Kind switch
        {
            IrOperandKind.Register or IrOperandKind.StackSlot => GetRenamedName(op, userRenames),
            IrOperandKind.Constant => FormatConstant(op.ConstantValue, imports, strings),
            IrOperandKind.Memory => FormatMemory(op, userRenames),
            IrOperandKind.Flag => "flags",
            IrOperandKind.Label => $"block_{op.BlockIndex}",
            IrOperandKind.Expression => $"({FormatInstruction(op.Expression!, imports, strings, userRenames)})",
            _ => "?"
        };

        return typePrefix + baseStr;
    }

    private static string FormatConstant(long value, Dictionary<ulong, string> imports, Dictionary<long, string> strings)
    {
        if (imports.TryGetValue((ulong)value, out var imp)) return $"&{imp}";
        if (strings.TryGetValue(value, out var str)) return $"\"{str.Replace("\n", "\\n").Replace("\r", "")}\"";
        return $"0x{value:X}";
    }

    private static string FormatMemory(IrOperand op, IReadOnlyDictionary<string, VariableSymbol>? userRenames = null)
    {
        var parts = new List<string>();
        if (op.MemBase != Iced.Intel.Register.None) 
            parts.Add(GetRenamedName(new IrOperand { Kind = IrOperandKind.Register, Register = op.MemBase, BitSize = 64 }, userRenames));
        
        if (op.MemIndex != Iced.Intel.Register.None)
        {
            string idx = GetRenamedName(new IrOperand { Kind = IrOperandKind.Register, Register = op.MemIndex, BitSize = 64 }, userRenames);
            parts.Add(op.MemScale > 1 ? $"{idx}*{op.MemScale}" : idx);
        }

        if (op.MemDisplacement != 0)
            parts.Add(op.MemDisplacement > 0 ? $"0x{op.MemDisplacement:X}" : $"-0x{-op.MemDisplacement:X}");
        
        return $"ptr_at({string.Join("+", parts)})";
    }

    private static string GetRenamedName(IrOperand op, IReadOnlyDictionary<string, VariableSymbol>? userRenames)
    {
        string defaultName = NamingConventions.GetVariableName(op);
        if (userRenames != null && userRenames.TryGetValue(defaultName, out var sym))
            return sym.Name + (sym.IsAiGenerated ? " /* AI */" : "");
        return defaultName;
    }
}

