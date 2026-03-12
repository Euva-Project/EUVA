// SPDX-License-Identifier: GPL-3.0-or-later

using Iced.Intel;

namespace EUVA.Core.Disassembly.Analysis;

public static class NamingConventions
{
    public static string GetRegisterName(Register reg)
    {
        var canonical = IrOperand.GetCanonical(reg);
        return canonical switch
        {
            Register.RAX => "rax",
            Register.RCX => "a1",
            Register.RDX => "a2",
            Register.R8 => "a3",
            Register.R9 => "a4",
            Register.RBX => "v1",
            Register.RSI => "v2",
            Register.RDI => "v3",
            Register.RBP => "rbp",
            Register.RSP => "rsp",
            Register.R10 => "t1",
            Register.R11 => "t2",
            Register.R12 => "v4",
            Register.R13 => "v5",
            Register.R14 => "v6",
            Register.R15 => "v7",
            _ => reg.ToString().ToLowerInvariant(),
        };
    }

    public static string GetStackVariableName(int offset)
    {
        if (offset < 0)
            return $"var_{-offset:X}";
        if (offset >= 0 && offset < 0x28)
            return $"spill_{offset:X}";
        return $"arg_{offset:X}";
    }

    public static string GetVariableName(IrOperand op)
    {
        if (op.Name != null) return op.Name;

        return op.Kind switch
        {
            IrOperandKind.Register => GetRegisterName(op.Register),
            IrOperandKind.StackSlot => GetStackVariableName(op.StackOffset),
            _ => op.Register == Register.None && op.SsaVersion != 0 ? $"tmp_{Math.Abs(op.SsaVersion)}" : "tmp"
        };
    }
}
