// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EUVA.Core.Robots.Patterns;

public sealed class ExternDefineRobot
{
    private static readonly Regex RxGlobal  = new(@"\b(g_0x[0-9A-Fa-f]+)\b", RegexOptions.Compiled);
    private static readonly Regex RxSubCall = new(@"\b(sub_[0-9A-Fa-f]+)\s*\(", RegexOptions.Compiled);
    private readonly Dictionary<string, string> _constants = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] BlacklistedPrefixes = { "g_Data_" };

    public ExternDefineRobot()
    {
        LoadConstants();
    }

    private void LoadConstants()
    {
        string sigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "signatures.json");
        if (!File.Exists(sigPath)) return;

        try
        {
            var json = File.ReadAllText(sigPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

            if (doc.RootElement.TryGetProperty("ConstantPatterns", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in arr.EnumerateArray())
                {
                    if (entry.TryGetProperty("Value", out var val) && entry.TryGetProperty("NewName", out var name))
                    {
                        string hexVal = val.GetString() ?? "";
                        string symName = name.GetString() ?? "";
                        if (!string.IsNullOrEmpty(hexVal) && !string.IsNullOrEmpty(symName))
                        {
                            _constants.TryAdd(hexVal, symName);
                        }
                    }
                }
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"[ExternDefineRobot] Loaded {_constants.Count} constant patterns from signatures.json");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[ExternDefineRobot] Failed to load signatures.json: {ex.Message}");
            Console.ResetColor();
        }
    }

    public string[] Apply(IReadOnlyList<string> lines)
    {
        var globals  = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var subCalls = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedDefines = new Dictionary<string, string>();

        foreach (var line in lines)
        {
            string clean = StripComments(line);
            foreach (Match m in RxGlobal.Matches(clean))
            {
                string g = m.Groups[1].Value;
                if (!BlacklistedPrefixes.Any(p => g.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    globals.Add(g);
            }
            foreach (Match m in RxSubCall.Matches(clean))
            {
                subCalls.Add(m.Groups[1].Value);
            }
            foreach (var kvp in _constants)
            {
                if (clean.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    usedDefines.TryAdd(kvp.Value, kvp.Key);
                }
            }
        }

        if (globals.Count == 0 && subCalls.Count == 0 && usedDefines.Count == 0)
            return lines.ToArray();

        var header = new List<string>();

        if (usedDefines.Count > 0)
        {
            header.Add("// == Defines ==");
            foreach (var d in usedDefines.OrderBy(x => x.Key))
            {
                header.Add($"define {d.Key} {d.Value}");
            }
            header.Add("");
        }

        if (globals.Count > 0)
        {
            header.Add("// == Global Variables ==");
            foreach (var g in globals)
            {
                string addr = g.Replace("g_0x", "0x");
                header.Add($"extern DWORD {g}; // @ {addr}");
            }
            header.Add("");
        }

        if (subCalls.Count > 0)
        {
            header.Add("// == Forward Declarations ==");
            foreach (var s in subCalls)
            {
                header.Add($"extern uint64_t {s}(); // forward decl");
            }
            header.Add("");
        }

        var result = new List<string>(lines.Count + header.Count + 2);
        int insertAt = FindFunctionStart(lines);

        if (insertAt > 0)
        {
            for (int i = 0; i < insertAt; i++)
                result.Add(lines[i]);

            result.AddRange(header);
            result.Add("// ===================================");
            result.Add("");

            for (int i = insertAt; i < lines.Count; i++)
                result.Add(lines[i]);
        }
        else
        {
            result.AddRange(header);
            result.Add("// ===================================");
            result.Add("");
            result.AddRange(lines);
        }

        return result.ToArray();
    }

    private static int FindFunctionStart(IReadOnlyList<string> lines)
    {
        var rxFnSig = new Regex(@"^\s*(uint64_t|int|void|DWORD|BOOL|HRESULT|unsigned\s+int)\s+\w+\s*\(", RegexOptions.Compiled);

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("struct ") || trimmed.StartsWith("====") || trimmed.StartsWith("//"))
                continue;
            if (rxFnSig.IsMatch(lines[i]))
                return i;
        }
        return -1;
    }

    private static string StripComments(string line)
    {
        int idx = line.IndexOf("//");
        return idx >= 0 ? line.Substring(0, idx) : line;
    }
}
