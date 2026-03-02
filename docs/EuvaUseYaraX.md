## Euva Use Yarax rules

So, the important technical part of this project is the YaraX rules engine. It's needed to expand the range of tasks and maintain compatibility with thousands of ready-made rules.
Here, the MMF file isn't loaded into RAM, but the OS allocates virtual addresses that reference the data on disk. The data is loaded by the OS kernel at the time of access.
This is implemented specifically to save RAM.
For small files up to 256 MB, mapping is done in one piece. Large files in this case are scanned in chunks or windows of 16 MB each. Also, to avoid situations where half of the signature is in another window, there is an overlap of 64 kilobytes. This is enough for each chunk or window to overlap another.

If thousands of rules have the same name, the engine doesn't create thousands of strings; it stores them by reusing the same reference.
In the hex context generation and hashing methods, memory is allocated on the stack. This code also works with unsafe pointers to remove array bounds checks, providing some speed gain.
Security features have been implemented. If the engine detects bad rules, such as finding any byte, the engine will simply abort scanning either the entire file or a chunk.
The interface has a limit on the number of records. If the limit is exceeded, the scanner will pause and wait. This is done to avoid cluttering the memory or channel with an endless number of new records. 

You can also change the ruleset on the fly if there's a new rules file.
And there's a shutdown system that doesn't just throw an exception but tries to terminate gracefully via a flag. However, you need to be careful with this, as it's an experimental feature and its behavior may be ambiguous at the moment. When a match is found, the program needs to show it in the byte grid; a pointer is taken; chunk boundaries are checked, or the entire file is not checked; if the file is small, the bytes are converted to a string using bitwise shifts.

---

Sample:
[YaraIntegration.cs](/EUVA.UI/YaraIntegration.cs)