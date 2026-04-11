// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace EUVA.Core.Robots;

public delegate void SpanScanner(ReadOnlySpan<byte> span);

public sealed class MappedDumpContext : IDisposable
{
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;

    private FileStream? _fs;

    public MappedDumpContext(string dumpPath)
    {
        var fileInfo = new FileInfo(dumpPath);
        if (fileInfo.Exists && fileInfo.Length > 0)
        {
            _fs = new FileStream(dumpPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _mmf = MemoryMappedFile.CreateFromFile(_fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        }
    }

    public unsafe void RunScoped(SpanScanner scanner)
    {
        if (_accessor == null)
            return;

        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        try
        {
            var span = new ReadOnlySpan<byte>(ptr, (int)_accessor.Capacity);
            scanner(span);
        }
        finally
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    public string[] ReadLines()
    {
        if (_fs == null) return Array.Empty<string>();
        _fs.Position = 0;
        using var reader = new StreamReader(_fs, System.Text.Encoding.UTF8, false, 4096, true);
        var content = reader.ReadToEnd();
        return content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
    }

    public void Dispose()
    {
        _accessor?.Dispose();
        _mmf?.Dispose();
        _fs?.Dispose();
    }
}
