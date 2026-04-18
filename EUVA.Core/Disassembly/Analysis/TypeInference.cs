// SPDX-License-Identifier: GPL-3.0-or-later

using Iced.Intel;

namespace EUVA.Core.Disassembly.Analysis;

public static class TypeInference
{
    
    public static Dictionary<(Register, int), TypeInfo> Infer(IrBlock[] blocks, Dictionary<ulong, string>? imports = null)
    {
        int inferred = 0;
        var defMap = new Dictionary<(string, int), IrInstruction>();
        var useMap = new Dictionary<(string, int), List<IrInstruction>>();
        
        var varTypes = new Dictionary<(Register, int), TypeInfo>();

        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;
                if (instr.Opcode == IrOpcode.Call)
                    inferred += ApplyLibrarySignatures(instr, imports);
            }
        }


        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;

                if (instr.DefinesDest)
                {
                    var key = SsaBuilder.GetVarKey(in instr.Destination);
                    if (key != null && instr.Destination.SsaVersion >= 0)
                        defMap[(key, instr.Destination.SsaVersion)] = instr;
                }

                foreach (var src in instr.Sources)
                {
                    var key = SsaBuilder.GetVarKey(in src);
                    if (key != null && src.SsaVersion >= 0)
                    {
                        var tuple = (key, src.SsaVersion);
                        if (!useMap.TryGetValue(tuple, out var uses))
                        {
                            uses = new List<IrInstruction>();
                            useMap[tuple] = uses;
                        }
                        uses.Add(instr);
                    }
                }
            }
        }

        for (int pass = 0; pass < 3; pass++)
        {
            foreach (var block in blocks)
            {
                foreach (var instr in block.Instructions)
                {
                    if (instr.IsDead) continue;

                    foreach (var op in instr.Sources.Concat(new[] { instr.Destination }))
                    {
                        if (op.Kind == IrOperandKind.Memory && op.MemBase != Register.None)
                        {
                            var reg = IrOperand.GetCanonical(op.MemBase);
                            var key = (reg, op.MemBaseSsaVersion);
                            
                            bool isTypedPrt = varTypes.TryGetValue(key, out var current) && current != TypeInfo.Unknown && current.PointerLevel > 0;
                            
                            if (!isTypedPrt)
                            {
                                var pt = op.MemScale switch
                                {
                                    2 => PrimitiveType.UInt16,
                                    4 => PrimitiveType.UInt32,
                                    8 => PrimitiveType.UInt64,
                                    _ => PrimitiveType.UInt8
                                };
                                var newType = new TypeInfo { BaseType = pt, PointerLevel = 1 };
                                if (!varTypes.TryGetValue(key, out var existingType) || existingType == TypeInfo.Unknown || (existingType.PointerLevel == 1 && existingType.BaseType == PrimitiveType.UInt8 && pt != PrimitiveType.UInt8))
                                {
                                    varTypes[key] = newType;
                                    inferred++;
                                }
                            }
                        }
                    }

                    if (InferFromInstruction(instr) > 0)
                    {
                        if (instr.DefinesDest && instr.Destination.Kind == IrOperandKind.Register)
                        {
                            var key = (instr.Destination.CanonicalRegister, instr.Destination.SsaVersion);
                            varTypes[key] = instr.Destination.Type;
                        }
                        inferred++;
                    }
                }
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var block in blocks)
            {
                foreach (var instr in block.Instructions)
                {
                    if (instr.IsDead) continue;

                    if (instr.Opcode == IrOpcode.Assign && instr.Sources.Length == 1)
                    {
                        var dst = instr.Destination;
                        var src = instr.Sources[0];
                        if (dst.Kind == IrOperandKind.Register && src.Kind == IrOperandKind.Register)
                        {
                            var dKey = (dst.CanonicalRegister, dst.SsaVersion);
                            var sKey = (src.CanonicalRegister, src.SsaVersion);
                            
                            varTypes.TryGetValue(sKey, out var sType);
                            varTypes.TryGetValue(dKey, out var dType);

                            if (sType != TypeInfo.Unknown && dType == TypeInfo.Unknown)
                            {
                                varTypes[dKey] = sType;
                                changed = true;
                                inferred++;
                            }
                            else if (dType != TypeInfo.Unknown && sType == TypeInfo.Unknown)
                            {
                                varTypes[sKey] = dType;
                                changed = true;
                                inferred++;
                            }
                        }
                    }
                    else if (instr.Opcode == IrOpcode.Phi)
                    {
                        var dKey = (instr.Destination.CanonicalRegister, instr.Destination.SsaVersion);
                        varTypes.TryGetValue(dKey, out var dType);
                        
                        if (dType == TypeInfo.Unknown)
                        {
                            foreach (var src in instr.Sources)
                            {
                                if (src.Kind == IrOperandKind.Register)
                                {
                                    if (varTypes.TryGetValue((src.CanonicalRegister, src.SsaVersion), out var sType) && sType != TypeInfo.Unknown)
                                    {
                                        varTypes[dKey] = sType;
                                        changed = true;
                                        inferred++;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.DefinesDest && instr.Destination.Kind == IrOperandKind.Register)
                {
                    var key = (instr.Destination.CanonicalRegister, instr.Destination.SsaVersion);
                    if (varTypes.TryGetValue(key, out var t)) instr.Destination.Type = t;
                }
                for (int i = 0; i < instr.Sources.Length; i++)
                {
                    if (instr.Sources[i].Kind == IrOperandKind.Register)
                    {
                        var key = (instr.Sources[i].CanonicalRegister, instr.Sources[i].SsaVersion);
                        if (varTypes.TryGetValue(key, out var t)) instr.Sources[i].Type = t;
                    }
                }
            }
        }

        return varTypes;
    }

    private static void EnqueueUses(IrOperand operand, Dictionary<(string, int), List<IrInstruction>> useMap,
        Queue<IrInstruction> worklist, HashSet<IrInstruction> inWorklist)
    {
        var key = SsaBuilder.GetVarKey(in operand);
        if (key != null && operand.SsaVersion >= 0)
        {
            if (useMap.TryGetValue((key, operand.SsaVersion), out var uses))
            {
                foreach (var use in uses)
                {
                    if (inWorklist.Add(use))
                        worklist.Enqueue(use);
                }
            }
        }
    }

    private static int InferFromInstruction(IrInstruction instr)
    {
        int count = 0;

        switch (instr.Opcode)
        {
            
            case IrOpcode.Add or IrOpcode.Sub:
                if (instr.DefinesDest && instr.Destination.Type == TypeInfo.Unknown)
                {
                    bool isPointerMath = false;
                    for (int i = 0; i < instr.Sources.Length; i++)
                    {
                        if (instr.Sources[i].Type != TypeInfo.Unknown && instr.Sources[i].Type.PointerLevel > 0)
                        {
                            instr.Destination.Type = instr.Sources[i].Type;
                            isPointerMath = true;
                            
                            if (instr.Opcode == IrOpcode.Add && instr.Sources.Length >= 2 && 
                                instr.Sources[1].Kind == IrOperandKind.Constant && instr.Sources[1].ConstantValue != 0)
                            {
                                if (instr.Sources[i].Type.BaseType == PrimitiveType.Unknown || instr.Sources[i].Type.BaseType == PrimitiveType.Void)
                                {
                                    instr.Sources[i].Type = new TypeInfo { BaseType = PrimitiveType.Struct, PointerLevel = 1 };
                                    instr.Destination.Type = instr.Sources[i].Type;
                                }
                            }
                            count++;
                            break;
                        }
                    }

                    if (!isPointerMath)
                    {
                        instr.Destination.Type = InferNumericType(instr.BitSize, signed: false);
                        count++;
                    }
                }
                break;

            case IrOpcode.Mul or IrOpcode.Div or IrOpcode.Mod or IrOpcode.Neg:
                if (instr.DefinesDest && instr.Destination.Type == TypeInfo.Unknown)
                {
                    instr.Destination.Type = InferNumericType(instr.BitSize, signed: false);
                    count++;
                }
                break;

            case IrOpcode.IMul or IrOpcode.IDiv:
                if (instr.DefinesDest && instr.Destination.Type == TypeInfo.Unknown)
                {
                    instr.Destination.Type = InferNumericType(instr.BitSize, signed: true);
                    count++;
                }
                break;

            case IrOpcode.And or IrOpcode.Or or IrOpcode.Xor or IrOpcode.Not
                or IrOpcode.Shl or IrOpcode.Shr or IrOpcode.Sar:
                if (instr.DefinesDest && instr.Destination.Type == TypeInfo.Unknown)
                {
                    instr.Destination.Type = InferNumericType(instr.BitSize,
                        signed: instr.Opcode == IrOpcode.Sar);
                    count++;
                }
                break;

            case IrOpcode.Load:
                if (instr.Sources.Length > 0)
                {
                    if (instr.Sources[0].Type == TypeInfo.Unknown)
                    {
                        var destType = InferNumericType(instr.BitSize, signed: false);
                        instr.Sources[0].Type = new TypeInfo { BaseType = destType.BaseType != PrimitiveType.Unknown ? destType.BaseType : PrimitiveType.Void, PointerLevel = 1 };
                        count++;
                    }
                    
                    if (instr.Sources[0].Kind == IrOperandKind.Memory)
                    {
                        var mem = instr.Sources[0];
                        if (mem.MemIndex != Register.None && mem.MemScale >= 1)
                        {
                            if (mem.Type == TypeInfo.Unknown)
                            {
                                var baseType = mem.MemScale switch
                                {
                                    2 => PrimitiveType.UInt16,
                                    4 => PrimitiveType.UInt32,
                                    8 => PrimitiveType.UInt64,
                                    _ => PrimitiveType.UInt8
                                };
                                instr.Sources[0].Type = new TypeInfo { BaseType = baseType, PointerLevel = 1 };
                                count++;
                            }
                        }
                        else if (mem.MemDisplacement != 0 && mem.MemBase != Register.None)
                        {
                            if (mem.Type == TypeInfo.Unknown)
                            {
                                instr.Sources[0].Type = new TypeInfo { BaseType = PrimitiveType.Struct, PointerLevel = 1 };
                                count++;
                            }
                        }
                    }
                }
                break;

            case IrOpcode.Store:
                if (instr.Destination.Type == TypeInfo.Unknown && instr.Sources.Length > 0)
                {
                    var src = instr.Sources[0];
                    if (src.Type != TypeInfo.Unknown)
                    {
                        instr.Destination.Type = new TypeInfo { BaseType = src.Type.BaseType, PointerLevel = (byte)(src.Type.PointerLevel + 1) };
                        count++;
                    }
                    else
                    {
                        var srcType = InferNumericType(src.BitSize, signed: false);
                        instr.Destination.Type = new TypeInfo { BaseType = srcType.BaseType != PrimitiveType.Unknown ? srcType.BaseType : PrimitiveType.Void, PointerLevel = 1 };
                        count++;
                    }
                }
                break;

            case IrOpcode.Call:
                if (instr.DefinesDest && instr.Destination.Type == TypeInfo.Unknown)
                {
                    instr.Destination.Type = instr.BitSize >= 64
                        ? new TypeInfo { BaseType = PrimitiveType.Void, PointerLevel = 1 } 
                        : new TypeInfo { BaseType = PrimitiveType.Int32, PointerLevel = 0 };
                    count++;
                }
                break;
        }

        return count;
    }

    private static int ApplyLibrarySignatures(IrInstruction instr, Dictionary<ulong, string>? imports)
    {
        if (imports == null || instr.Opcode != IrOpcode.Call || instr.Sources.Length == 0) return 0;

        var target = instr.Sources[0];
        if (target.Kind != IrOperandKind.Constant) return 0;

        if (imports.TryGetValue((ulong)target.ConstantValue, out var name))
        {
            var cleanName = name.Replace("__imp_", "").Split('@')[0].TrimStart('_');
            
            if (_signatures.TryGetValue(cleanName, out var sig))
            {
                int inferred = 0;
                if (instr.DefinesDest && instr.Destination.Type == TypeInfo.Unknown)
                {
                    instr.Destination.Type = sig.ReturnType;
                    inferred++;
                }

                for (int i = 0; i < sig.ArgTypes.Length && i + 1 < instr.Sources.Length; i++)
                {
                    if (instr.Sources[i + 1].Type == TypeInfo.Unknown)
                    {
                        instr.Sources[i + 1].Type = sig.ArgTypes[i];
                        inferred++;
                    }
                }
                return inferred;
            }
        }
        return 0;
    }

    private static readonly Dictionary<string, (TypeInfo ReturnType, TypeInfo[] ArgTypes)> _signatures = new()
    {
        { "memcpy", (TypeInfo.VoidPtr, new[] { TypeInfo.VoidPtr, TypeInfo.VoidPtr, new TypeInfo { BaseType = PrimitiveType.UInt64 } }) },
        { "memset", (TypeInfo.VoidPtr, new[] { TypeInfo.VoidPtr, new TypeInfo { BaseType = PrimitiveType.Int32 }, new TypeInfo { BaseType = PrimitiveType.UInt64 } }) },
        { "malloc", (TypeInfo.VoidPtr, new[] { new TypeInfo { BaseType = PrimitiveType.UInt64 } }) },
        { "free", (new TypeInfo { BaseType = PrimitiveType.Void }, new[] { TypeInfo.VoidPtr }) },
        { "strcpy", (TypeInfo.VoidPtr, new[] { TypeInfo.VoidPtr, TypeInfo.VoidPtr }) },
        { "strlen", (new TypeInfo { BaseType = PrimitiveType.UInt64 }, new[] { TypeInfo.VoidPtr }) },
        { "printf", (new TypeInfo { BaseType = PrimitiveType.Int32 }, new[] { TypeInfo.VoidPtr }) },
    };

    private static TypeInfo InferFromSources(IrInstruction instr,
        Dictionary<(string, int), IrInstruction> defMap)
    {
        if (instr.Opcode == IrOpcode.Assign && instr.Sources.Length == 1)
        {
            
            var src = instr.Sources[0];
            if (src.Type != TypeInfo.Unknown) return src.Type;

            var key = SsaBuilder.GetVarKey(in src);
            if (key != null && src.SsaVersion >= 0 && defMap.TryGetValue((key, src.SsaVersion), out var def))
            {
                if (def.Destination.Type != TypeInfo.Unknown)
                    return def.Destination.Type;
            }
        }

        if (instr.Opcode == IrOpcode.Phi)
        {
            
            TypeInfo common = TypeInfo.Unknown;
            foreach (var src in instr.Sources)
            {
                var srcType = src.Type;
                if (srcType == TypeInfo.Unknown)
                {
                    var key = SsaBuilder.GetVarKey(in src);
                    if (key != null && src.SsaVersion >= 0 && defMap.TryGetValue((key, src.SsaVersion), out var def))
                        srcType = def.Destination.Type;
                }
                if (srcType == TypeInfo.Unknown) continue;

                if (common == TypeInfo.Unknown)
                {
                    common = srcType;
                }
                else if (common != srcType)
                {
                    
                    if (common.PointerLevel == srcType.PointerLevel)
                    {
                        
                        if (common.BaseType == PrimitiveType.Void && srcType.BaseType != PrimitiveType.Void)
                            common = srcType;
                        else if (common.BaseType != PrimitiveType.Void && srcType.BaseType == PrimitiveType.Void)
                        {
                            
                        }
                        else
                        {
                            return TypeInfo.Unknown; 
                        }
                    }
                    else
                    {
                        return TypeInfo.Unknown; 
                    }
                }
            }
            return common;
        }

        return TypeInfo.Unknown;
    }

    private static TypeInfo InferNumericType(byte bitSize, bool signed) => (bitSize, signed) switch
    {
        (8, false) => new TypeInfo { BaseType = PrimitiveType.UInt8 },
        (8, true)  => new TypeInfo { BaseType = PrimitiveType.Int8 },
        (16, false) => new TypeInfo { BaseType = PrimitiveType.UInt16 },
        (16, true)  => new TypeInfo { BaseType = PrimitiveType.Int16 },
        (32, false) => new TypeInfo { BaseType = PrimitiveType.UInt32 },
        (32, true)  => new TypeInfo { BaseType = PrimitiveType.Int32 },
        (64, false) => new TypeInfo { BaseType = PrimitiveType.UInt64 },
        (64, true)  => new TypeInfo { BaseType = PrimitiveType.Int64 },
        _ => TypeInfo.Unknown
    };
}
