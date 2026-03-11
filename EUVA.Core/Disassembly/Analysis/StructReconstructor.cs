// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;

public static class StructReconstructor
{
    
    public sealed class RecoveredStruct
    {
        public string Name;
        public SortedDictionary<long, RecoveredField> Fields = new();
        public int AccessCount;

        public RecoveredStruct(string name) => Name = name;
    }

    public sealed class RecoveredField
    {
        public long Offset;
        public string Name;
        public TypeInfo Type;
        public byte BitSize;

        public RecoveredField(long offset, string name, byte bitSize)
        {
            Offset = offset;
            Name = name;
            BitSize = bitSize;
            Type = TypeInfo.Unknown;
        }
    }

    public static List<RecoveredStruct> Reconstruct(IrBlock[] blocks)
    {
        
        var access = new Dictionary<string, Dictionary<long, (byte BitSize, int Count)>>();

        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr.IsDead) continue;

                
                ScanOperand(instr.Destination, access);
                foreach (ref var src in instr.Sources.AsSpan())
                    ScanOperand(src, access);
            }
        }

        var structs = new List<RecoveredStruct>();
        foreach (var (baseKey, offsets) in access)
        {
            if (offsets.Count < 2) continue; 

            var st = new RecoveredStruct($"struct_{baseKey.Replace("r_", "")}");
            foreach (var (offset, (bitSize, count)) in offsets)
            {
                string fieldName = $"field_{offset:X}";
                var field = new RecoveredField(offset, fieldName, bitSize);
                field.Type = bitSize switch
                {
                    8 => new TypeInfo { BaseType = PrimitiveType.UInt8 },
                    16 => new TypeInfo { BaseType = PrimitiveType.UInt16 },
                    32 => new TypeInfo { BaseType = PrimitiveType.UInt32 },
                    64 => new TypeInfo { BaseType = PrimitiveType.UInt64 },
                    _ => TypeInfo.Unknown,
                };
                st.Fields[offset] = field;
                st.AccessCount += count;
            }
            structs.Add(st);
        }

        return structs;
    }

    private static void ScanOperand(in IrOperand op,
        Dictionary<string, Dictionary<long, (byte BitSize, int Count)>> access)
    {
        if (op.Kind != IrOperandKind.Memory) return;
        if (op.MemBase == Iced.Intel.Register.None) return;
        if (op.MemIndex != Iced.Intel.Register.None) return; 
        if (op.MemDisplacement == 0) return; 

        
        var canonical = IrOperand.GetCanonical(op.MemBase);
        if (canonical == Iced.Intel.Register.RSP ||
            canonical == Iced.Intel.Register.RBP)
            return; 

        if (canonical == Iced.Intel.Register.RIP || canonical == Iced.Intel.Register.EIP)
            return;

        
        if (op.MemDisplacement > 0x10000 || op.MemDisplacement < -0x10000)
            return;

        var baseKey = op.SsaVersion >= 0 ? $"r_{canonical}_{op.SsaVersion}" : $"r_{canonical}";
        if (!access.TryGetValue(baseKey, out var offsets))
        {
            offsets = new();
            access[baseKey] = offsets;
        }

        if (offsets.TryGetValue(op.MemDisplacement, out var existing))
            offsets[op.MemDisplacement] = (existing.BitSize, existing.Count + 1);
        else
            offsets[op.MemDisplacement] = (op.BitSize, 1);
    }
}
