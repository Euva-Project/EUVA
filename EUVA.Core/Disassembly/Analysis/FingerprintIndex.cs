// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EUVA.Core.Disassembly.Analysis;

public sealed class FunctionRecord
{
    [JsonPropertyName("n")] public string n { get; set; } = string.Empty;
    [JsonPropertyName("l")] public string l { get; set; } = string.Empty;
    [JsonPropertyName("f")] public FunctionFingerprint? f { get; set; }
    [JsonPropertyName("Name")] public string NameProp { get; set; } = string.Empty;
    [JsonPropertyName("Lib")] public string LibProp { get; set; } = string.Empty;
    [JsonPropertyName("Fingerprint")] public FunctionFingerprint? FingerprintProp { get; set; }

    
    [JsonIgnore]
    public string Name 
    { 
        get => !string.IsNullOrEmpty(n) ? n : NameProp;
        set => n = value; 
    }

    [JsonIgnore]
    public string Lib 
    { 
        get => !string.IsNullOrEmpty(l) ? l : LibProp;
        set => l = value; 
    }

    [JsonIgnore]
    public FunctionFingerprint Fingerprint 
    { 
        get => f ?? FingerprintProp ?? new FunctionFingerprint();
        set => f = value; 
    }
}

public sealed class MatchResult
{
    public FunctionRecord Record  { get; init; } = null!;
    public float          Score   { get; init; }
    public bool           IsHigh   => Score >= FingerprintMatcher.ThresholdHigh;
    public bool           IsMedium => Score >= FingerprintMatcher.ThresholdMedium;
    public bool           IsLow    => Score >= FingerprintMatcher.ThresholdLow;

    public string FormatComment()
        => FingerprintMatcher.FormatResult(Score, Record.Name, Record.Lib);
}


public sealed class FingerprintIndex
{
    private static FingerprintIndex? _instance;
    public  static FingerprintIndex  Instance => _instance ??= new FingerprintIndex();

    private readonly List<FunctionRecord> _records = new();
    private readonly Dictionary<uint, List<int>> _topoIndex = new();
    private readonly Dictionary<int, List<int>> _blockCountIndex = new();

    public int RecordCount => _records.Count;

    public async Task LoadAsync(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            System.Diagnostics.Debug.WriteLine($"[*] Loading big base: {filePath}...");
            
            using var fs = File.OpenRead(filePath);
            using Stream sourceStream = filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) 
                ? new GZipStream(fs, CompressionMode.Decompress) 
                : fs;

            var opts = new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                
                NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            var records = JsonSerializer.DeserializeAsyncEnumerable<FunctionRecord>(sourceStream, opts);
            int loadedCount = 0;
            
            await foreach (var rec in records)
            {
                
                if (rec != null && !string.IsNullOrEmpty(rec.Name))
                {
                    AddRecord(rec);
                    loadedCount++;
                    
                    if (loadedCount % 100000 == 0)
                        System.Diagnostics.Debug.WriteLine($"    ... loaded {loadedCount} fingerprints");
                }
            }

            RebuildIndex();
            System.Diagnostics.Debug.WriteLine($"[+] Base successfully built! {GetStats()}");
        }
        catch (JsonException jex)
        {
            System.Diagnostics.Debug.WriteLine($"[!] JSON ERROR. Line {jex.LineNumber}, Byte {jex.BytePositionInLine}. Path: {jex.Path}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[!] FATAL BASE ERROR: {ex.Message}");
        }
    }

    public void Save(string filePath)
    {
        try
        {
            if (!filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) filePath += ".gz";
            using var fs = File.Create(filePath);
            using var gz = new GZipStream(fs, CompressionLevel.Optimal);
            var opts = new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
            JsonSerializer.Serialize(gz, _records, opts);
        }
        catch { }
    }

    public void AddRecord(FunctionRecord record)
    {
        int idx = _records.Count;
        _records.Add(record);

        uint topoHash = record.Fingerprint.TopoHash;
        if (!_topoIndex.TryGetValue(topoHash, out var topoList))
        {
            topoList = new List<int>(4);
            _topoIndex[topoHash] = topoList;
        }
        topoList.Add(idx);

        int bc = record.Fingerprint.BlockCount;
        if (!_blockCountIndex.TryGetValue(bc, out var bcList))
        {
            bcList = new List<int>(4);
            _blockCountIndex[bc] = bcList;
        }
        bcList.Add(idx);
    }

    public void RebuildIndex()
    {
        _topoIndex.Clear();
        _blockCountIndex.Clear();

        for (int i = 0; i < _records.Count; i++)
        {
            var fp = _records[i].Fingerprint;
            if (!_topoIndex.ContainsKey(fp.TopoHash)) _topoIndex[fp.TopoHash] = new List<int>(4);
            _topoIndex[fp.TopoHash].Add(i);

            if (!_blockCountIndex.ContainsKey(fp.BlockCount)) _blockCountIndex[fp.BlockCount] = new List<int>(4);
            _blockCountIndex[fp.BlockCount].Add(i);
        }
    }

    private const int   MinCandidates = 15;
    private const float MinScore      = FingerprintMatcher.ThresholdLow;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public MatchResult? FindBest(FunctionFingerprint query)
    {
        if (_records.Count == 0) return null;

        var candidates = GetCandidates(query);
        if (candidates.Count == 0) return null;

        MatchResult? best = null;

        foreach (int idx in candidates)
        {
            var rec   = _records[idx];
            float score = FingerprintMatcher.Compare(query, rec.Fingerprint);

            if (score < MinScore) continue;
            if (best == null || score > best.Score)
                best = new MatchResult { Record = rec, Score = score };
        }

        return best;
    }

    private HashSet<int> GetCandidates(FunctionFingerprint query)
    {
        var candidates = new HashSet<int>();

        if (_topoIndex.TryGetValue(query.TopoHash, out var exactList))
            foreach (int i in exactList) candidates.Add(i);

        
        if (candidates.Count < MinCandidates)
        {
            for (int delta = -5; delta <= 5; delta++)
            {
                int bc = query.BlockCount + delta;
                if (_blockCountIndex.TryGetValue(bc, out var bcList))
                    foreach (int i in bcList) candidates.Add(i);
            }
        }
        return candidates;
    }

    public void Clear()
    {
        _records.Clear();
        _topoIndex.Clear();
        _blockCountIndex.Clear();
    }
    
    public string GetStats()
    {
        int topoGroups  = _topoIndex.Count;
        int avgPerGroup = topoGroups > 0 ? _records.Count / topoGroups : 0;
        return $"Records={_records.Count} TopoGroups={topoGroups} AvgPerGroup={avgPerGroup}";
    }
}