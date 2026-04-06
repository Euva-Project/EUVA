// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EUVA.Core.Disassembly.Analysis;

namespace EUVA.Core.DatabaseParser;

public static class DatabaseParser
{
    public static async Task LoadFingerprintsGzAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"[!] Base not found: {filePath}");

        Console.WriteLine($"[*] Loading base signatures: {filePath}...");


        var opts = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        int loadedCount = 0;

        try
        {
            using var fs = File.OpenRead(filePath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);

            var records = JsonSerializer.DeserializeAsyncEnumerable<FunctionRecord>(gz, opts);

            await foreach (var rec in records)
            {

                if (rec != null && !string.IsNullOrEmpty(rec.Name))
                {
                    FingerprintIndex.Instance.AddRecord(rec);
                    loadedCount++;
                }
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[!] JSON parsing error (line {ex.LineNumber}): {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            Console.WriteLine($"[!] GZip decompression error: {ex.Message}");
        }

        Console.WriteLine($"[+] Loaded signatures: {loadedCount}");

        if (loadedCount > 0)
        {
            Console.WriteLine("[*] Building search indexes...");
            FingerprintIndex.Instance.RebuildIndex();
            Console.WriteLine($"[✓] Base loaded: {FingerprintIndex.Instance.GetStats()}");
        }
        else
        {
            Console.WriteLine("[!] WARNING: Base loaded with 0 records. Please check the file format.");
        }
    }
}