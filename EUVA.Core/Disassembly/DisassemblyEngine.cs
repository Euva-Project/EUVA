// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.CompilerServices;
using Iced.Intel;

namespace EUVA.Core.Disassembly;


public sealed unsafe class StructOutput : FormatterOutput
{
    private char* _buffer;
    private byte* _colorMap;
    private int _pos;
    private int _capacity;

    
    public int Length => _pos;


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTarget(char* buffer, byte* colorMap, int capacity)
    {
        _buffer = buffer;
        _colorMap = colorMap;
        _pos = 0;
        _capacity = capacity;
    }

    public override void Write(string text, FormatterTextKind kind)
    {
        int len = text.Length;
        int avail = _capacity - _pos;
        if (avail <= 0) return;
        if (len > avail) len = avail;

        byte col = KindToColor(kind);
        for (int i = 0; i < len; i++)
        {
            _buffer[_pos] = text[i];
            _colorMap[_pos] = col;
            _pos++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte KindToColor(FormatterTextKind kind) => kind switch
    {
        FormatterTextKind.Mnemonic => DisassembledLine.ColMnemonic,
        FormatterTextKind.Register => DisassembledLine.ColRegister,
        FormatterTextKind.Number => DisassembledLine.ColNumber,
        FormatterTextKind.LabelAddress => DisassembledLine.ColNumber,
        FormatterTextKind.FunctionAddress => DisassembledLine.ColNumber,
        FormatterTextKind.Keyword => DisassembledLine.ColKeyword,
        FormatterTextKind.Prefix => DisassembledLine.ColKeyword,
        FormatterTextKind.Directive => DisassembledLine.ColKeyword,
        FormatterTextKind.Operator => DisassembledLine.ColPunctuation,
        FormatterTextKind.Punctuation => DisassembledLine.ColPunctuation,
        FormatterTextKind.Decorator => DisassembledLine.ColPunctuation,
        _ => DisassembledLine.ColText,
    };
}


public sealed unsafe class UnsafePointerCodeReader : CodeReader
{
    private byte* _ptr;
    private int _length;
    private int _pos;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(byte* ptr, int length)
    {
        _ptr = ptr;
        _length = length;
        _pos = 0;
    }

    public override int ReadByte()
    {
        if (_pos >= _length) return -1;
        return _ptr[_pos++];
    }
}


public sealed class DisassemblyEngine
{
    private readonly MasmFormatter _formatter;
    private readonly StructOutput _output;
    private readonly UnsafePointerCodeReader _codeReader;
    private int _bitness;

    public int Bitness
    {
        get => _bitness;
        set
        {
            if (value != 16 && value != 32 && value != 64)
                throw new ArgumentException("Bitness must be 16, 32, or 64.");
            _bitness = value;
        }
    }

    public DisassemblyEngine(int bitness = 32)
    {
        _bitness = bitness;
        _formatter = new MasmFormatter();
        _formatter.Options.SpaceAfterOperandSeparator = true;
        _formatter.Options.UppercaseMnemonics = false;
        _formatter.Options.UppercaseRegisters = false;
        _formatter.Options.HexPrefix = "0";
        _formatter.Options.HexSuffix = "h";
        _formatter.Options.FirstOperandCharIndex = 8;
        _output = new StructOutput();
        _codeReader = new UnsafePointerCodeReader();
    }


    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe int DecodeVisible(byte* data, int dataLength, long baseOffset,
        DisassembledLine[] output, int maxLines)
    {
        if (dataLength <= 0 || maxLines <= 0) return 0;

        _codeReader.Reset(data, dataLength);
        var decoder = Decoder.Create(_bitness, _codeReader, (ulong)baseOffset);
        ulong endIP = (ulong)baseOffset + (ulong)dataLength;
        int count = 0;
        Instruction instr = default;

        while (count < maxLines && decoder.IP < endIP)
        {
            ulong ipBefore = decoder.IP;
            decoder.Decode(out instr);

            if (instr.IsInvalid)
            {
                
                ref var badLine = ref output[count];
                badLine.Offset = (long)ipBefore;
                badLine.Length = 1;
                fixed (char* buf = badLine.TextBuffer)
                {
                    byte val = data[ipBefore - (ulong)baseOffset];
                    buf[0] = 'd'; buf[1] = 'b'; buf[2] = ' ';
                    buf[3] = HexChar(val >> 4);
                    buf[4] = HexChar(val & 0xF);
                    buf[5] = 'h';
                    badLine.TextLength = 6;
                }
                count++;
                continue;
            }

            ref var line = ref output[count];
            line.Offset = (long)instr.IP;
            line.Length = (byte)instr.Length;

            fixed (char* buf = line.TextBuffer)
            fixed (byte* cmap = line.TextColorMap)
            {
                _output.SetTarget(buf, cmap, DisassembledLine.MaxTextLength);
                _formatter.Format(in instr, _output);
                line.TextLength = _output.Length;
            }

            count++;
        }

        return count;
    }

 
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe int CountInstructions(byte* data, int dataLength, long baseOffset, int byteCount)
    {
        if (dataLength <= 0 || byteCount <= 0) return 0;

        int usable = Math.Min(dataLength, byteCount);
        _codeReader.Reset(data, usable);
        var decoder = Decoder.Create(_bitness, _codeReader, (ulong)baseOffset);
        ulong endIP = (ulong)baseOffset + (ulong)usable;
        int count = 0;
        Instruction instr = default;

        while (decoder.IP < endIP)
        {
            decoder.Decode(out instr);
            
            count++;
        }

        return count;
    }


    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe int SkipInstructions(byte* data, int dataLength, long baseOffset, int instructionCount)
    {
        if (dataLength <= 0 || instructionCount <= 0) return 0;

        _codeReader.Reset(data, dataLength);
        var decoder = Decoder.Create(_bitness, _codeReader, (ulong)baseOffset);
        ulong endIP = (ulong)baseOffset + (ulong)dataLength;
        int totalBytes = 0;
        Instruction instr = default;

        for (int i = 0; i < instructionCount && decoder.IP < endIP; i++)
        {
            decoder.Decode(out instr);
            
            totalBytes += instr.IsInvalid ? 1 : instr.Length;
        }

        return totalBytes;
    }


    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe long GetSyncOffset(byte* data, int dataLength, long baseOffset, long targetOffset, int lookbackBytes = 128)
    {
        if (targetOffset <= 0) return 0;
        
        
        long limitStart = Math.Max(0, targetOffset - lookbackBytes);
        int availableLookback = (int)(targetOffset - Math.Max(limitStart, baseOffset));
        
        if (availableLookback <= 0) return targetOffset;

        
        int offsetIntoData = (int)(targetOffset - availableLookback - baseOffset);
        byte* scanStartPtr = data + offsetIntoData;
        int scanLength = availableLookback + 32; 

        if (offsetIntoData < 0 || offsetIntoData + scanLength > dataLength)
        {
            
            return targetOffset; 
        }

        
        long bestSync = targetOffset;
        int maxValidChain = 0;

        for (int startShift = 0; startShift < 8 && startShift < availableLookback; startShift++)
        {
            _codeReader.Reset(scanStartPtr + startShift, scanLength - startShift);
            var decoder = Decoder.Create(_bitness, _codeReader, (ulong)(baseOffset + offsetIntoData + startShift));
            
            int validChain = 0;
            Instruction instr = default;
            long lastValidBeforeTarget = -1;

            while (decoder.IP < (ulong)(targetOffset + 16))
            {
                decoder.Decode(out instr);
                if (instr.IsInvalid) break;
                
                validChain++;
                if ((long)instr.IP <= targetOffset)
                    lastValidBeforeTarget = (long)instr.IP;
            }

            if (validChain > maxValidChain && lastValidBeforeTarget != -1)
            {
                maxValidChain = validChain;
                bestSync = lastValidBeforeTarget;
            }
        }

        return bestSync;
    }


    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe void FindInstructionEnclosing(byte* data, int dataLength, long baseOffset, long startSync, long targetOffset, out long foundOffset, out int foundLen)
    {
        foundOffset = targetOffset;
        foundLen = 1;

        if (startSync < baseOffset || startSync > targetOffset) startSync = targetOffset;

        _codeReader.Reset(data + (startSync - baseOffset), (int)(baseOffset + dataLength - startSync));
        var decoder = Decoder.Create(_bitness, _codeReader, (ulong)startSync);
        Instruction instr = default;

        while (decoder.IP <= (ulong)targetOffset)
        {
            decoder.Decode(out instr);
            if (instr.IsInvalid)
            {
                if ((long)decoder.IP > targetOffset)
                {
                    foundOffset = (long)decoder.IP - 1;
                    foundLen = 1;
                    return;
                }
                continue;
            }

            if ((long)instr.IP <= targetOffset && targetOffset < (long)instr.IP + instr.Length)
            {
                foundOffset = (long)instr.IP;
                foundLen = instr.Length;
                return;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char HexChar(int nibble)
        => (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
}
