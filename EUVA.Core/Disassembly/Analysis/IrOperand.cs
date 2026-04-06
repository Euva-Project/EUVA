// SPDX-License-Identifier: GPL-3.0-or-later

using Iced.Intel;

namespace EUVA.Core.Disassembly.Analysis;

public enum IrOperandKind : byte
{
    None = 0,
    Register,       
    Constant,       
    Memory,         
    StackSlot,      
    Flag,           
    Label,          
    Expression,     
}

public struct IrOperand
{
    public IrOperandKind Kind;
    public Register Register;      
    public int SsaVersion;         
    public long ConstantValue;
    public Register MemBase;
    public int MemBaseSsaVersion;
    public Register MemIndex;
    public int MemIndexSsaVersion;
    public string? MemBaseName;
    public string? MemIndexName;
    public int MemScale;
    public long MemDisplacement;
    public int StackOffset;        
    public int BlockIndex;         
    public IrInstruction? Expression;
    public byte BitSize;           
    public TypeInfo Type;
    public string? Name;

    public static IrOperand Reg(Register reg, byte bitSize = 64) => new()
    {
        Kind = IrOperandKind.Register,
        Register = reg,
        SsaVersion = -1,
        BitSize = bitSize,
    };

    public static IrOperand Const(long value, byte bitSize = 64) => new()
    {
        Kind = IrOperandKind.Constant,
        ConstantValue = value,
        BitSize = bitSize,
    };

    public static IrOperand Mem(Register @base, Register index, int scale, long disp, byte bitSize = 64) => new()
    {
        Kind = IrOperandKind.Memory,
        MemBase = @base,
        MemBaseSsaVersion = -1,
        MemIndex = index,
        MemIndexSsaVersion = -1,
        MemScale = scale,
        MemDisplacement = disp,
        BitSize = bitSize,
        SsaVersion = -1,
    };

    public static IrOperand Stack(int offset, byte bitSize = 64) => new()
    {
        Kind = IrOperandKind.StackSlot,
        StackOffset = offset,
        BitSize = bitSize,
        SsaVersion = -1,
    };

    public static IrOperand FlagReg() => new()
    {
        Kind = IrOperandKind.Flag,
        SsaVersion = -1,
        BitSize = 64,
    };

    public static IrOperand BlockLabel(int blockIndex) => new()
    {
        Kind = IrOperandKind.Label,
        BlockIndex = blockIndex,
    };

    public static IrOperand Expr(IrInstruction instr) => new()
    {
        Kind = IrOperandKind.Expression,
        Expression = instr,
        BitSize = instr.Destination.BitSize,
        Type = instr.Destination.Type,
    };

    
    public Register CanonicalRegister => Kind == IrOperandKind.Register
        ? GetCanonical(Register)
        : Register.None;

    
    public static Register GetCanonical(Register reg) => reg switch
    {
        
        Register.AL or Register.AH or Register.AX or Register.EAX or Register.RAX => Register.RAX,
        
        Register.BL or Register.BH or Register.BX or Register.EBX or Register.RBX => Register.RBX,
        
        Register.CL or Register.CH or Register.CX or Register.ECX or Register.RCX => Register.RCX,
        
        Register.DL or Register.DH or Register.DX or Register.EDX or Register.RDX => Register.RDX,
        
        Register.SIL or Register.SI or Register.ESI or Register.RSI => Register.RSI,
        
        Register.DIL or Register.DI or Register.EDI or Register.RDI => Register.RDI,
        
        Register.SPL or Register.SP or Register.ESP or Register.RSP => Register.RSP,
        
        Register.BPL or Register.BP or Register.EBP or Register.RBP => Register.RBP,
        
        Register.R8L or Register.R8W or Register.R8D or Register.R8 => Register.R8,
        Register.R9L or Register.R9W or Register.R9D or Register.R9 => Register.R9,
        Register.R10L or Register.R10W or Register.R10D or Register.R10 => Register.R10,
        Register.R11L or Register.R11W or Register.R11D or Register.R11 => Register.R11,
        Register.R12L or Register.R12W or Register.R12D or Register.R12 => Register.R12,
        Register.R13L or Register.R13W or Register.R13D or Register.R13 => Register.R13,
        Register.R14L or Register.R14W or Register.R14D or Register.R14 => Register.R14,
        Register.R15L or Register.R15W or Register.R15D or Register.R15 => Register.R15,
        
        _ => reg,
    };

    
    public bool SameLocation(in IrOperand other)
    {
        if (Kind != other.Kind) return false;
        return Kind switch
        {
            IrOperandKind.Register => CanonicalRegister == other.CanonicalRegister,
            IrOperandKind.StackSlot => StackOffset == other.StackOffset,
            IrOperandKind.Constant => ConstantValue == other.ConstantValue,
            IrOperandKind.Flag => true,
            _ => false,
        };
    }

    public override string ToString() => Kind switch
    {
        IrOperandKind.Register => SsaVersion >= 0 ? $"{Register}_v{SsaVersion}" : $"{Register}",
        IrOperandKind.Constant => $"0x{ConstantValue:X}",
        IrOperandKind.StackSlot => $"[rbp{(StackOffset >= 0 ? "+" : "")}{StackOffset}]",
        IrOperandKind.Memory => FormatMemory(),
        IrOperandKind.Flag => "FLAGS",
        IrOperandKind.Label => $"block_{BlockIndex}",
        IrOperandKind.Expression => $"({Expression})",
        _ => "?",
    };

    private string FormatMemory()
    {
        var parts = new List<string>();
        if (MemBase != Register.None) parts.Add(MemBase.ToString());
        if (MemIndex != Register.None)
            parts.Add(MemScale > 1 ? $"{MemIndex}*{MemScale}" : MemIndex.ToString());
        if (MemDisplacement != 0)
            parts.Add(MemDisplacement > 0 ? $"0x{MemDisplacement:X}" : $"-0x{-MemDisplacement:X}");
        return $"[{string.Join("+", parts)}]";
    }
}

public enum PrimitiveType : byte
{
    Unknown = 0,
    Int8,
    UInt8,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float32,
    Float64,
    Bool,
    Void,
    Struct,
}

public struct TypeInfo : IEquatable<TypeInfo>
{
    public PrimitiveType BaseType { get; set; }
    public byte PointerLevel { get; set; }
    public string? TypeName { get; set; }

    public static TypeInfo Unknown => new TypeInfo { BaseType = PrimitiveType.Unknown, PointerLevel = 0 };
    public static TypeInfo VoidPtr => new TypeInfo { BaseType = PrimitiveType.Void, PointerLevel = 1 };

    public bool Equals(TypeInfo other) => 
        BaseType == other.BaseType && 
        PointerLevel == other.PointerLevel && 
        TypeName == other.TypeName;

    public override bool Equals(object? obj) => obj is TypeInfo other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(BaseType, PointerLevel, TypeName);

    public static bool operator ==(TypeInfo left, TypeInfo right) => left.Equals(right);
    public static bool operator !=(TypeInfo left, TypeInfo right) => !(left == right);

    public override string ToString()
    {
        string name = TypeName ?? BaseType.ToString();
        if (PointerLevel > 0) name += new string('*', PointerLevel);
        return name;
    }
}
