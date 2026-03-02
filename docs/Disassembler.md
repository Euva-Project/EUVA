## Disassembler

So here's an explanation of many implementation questions.
For high performance, direct pixel manipulation via a writeable bitmap and backbuffer is used. This is done specifically to eliminate heavy Windows rendering.
The zero-allocation pipeline means the program doesn't create new objects, but reuses memory. This approach results in the absence of a garbage collector. The program also folds zeros to avoid cluttering everything with zero bytes and to provide a clean interface.

To briefly describe the functions of **DisassemblyEngine.cs**:

**SetTarget**
binds the engine to a specific memory location on the stack or heap to write text and colors in a single click without unnecessary copying.

**Write**
manually maps token types from the Iced library to your internal colors and fills the array character by character, eliminating allocations on the managed heap.

**ReadByte**
implements the shortest possible path from a byte in a file to the decoder via pointer incrementation, which is critical when processing millions of instructions per second.

**DecodeVisible**
Cycles the decoder and formatter to fill the string structure, including automatic hex code generation for broken bytes so that the listing is never interrupted.

**CountInstructions**
Uses dry decoding without translating to text to quickly calculate how many instructions will fit in the buffer or on the screen.

**SkipInstructions**
Skip bytes, taking into account the variable length of x86 instructions, allowing you to accurately jump ten instructions ahead without knowing their size in advance.

**GetSyncOffset**
Uses heuristic analysis of instruction chains and enumeration of starting offsets to find the entry point where bytes begin to form meaningful code rather than random garbage.

---

To use the disassembler, press Ctrl+D. This module requires iced and the absence of allocations is necessary for high program performance.

Sample:
[DisassemblyEngine.cs](/EUVA.Core/Disassembly/DisassemblyEngine.cs)
[DisassemblerHexView.cs](/EUVA.UI/Controls/DisassemblerHexView.cs)