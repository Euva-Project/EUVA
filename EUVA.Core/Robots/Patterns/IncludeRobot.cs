// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EUVA.Core.Robots.Patterns;

public sealed class IncludeRobot
{
    private class IncludeRule
    {
        public string Token { get; set; }
        public string Header { get; set; }
    }

    private readonly List<IncludeRule> _rules = new();
    private static readonly Regex RxStdToken = new(@"\bstd::([a-zA-Z_]\w*)\b", RegexOptions.Compiled);

    public IncludeRobot()
    {
        LoadRules();
    }

    private void LoadRules()
    {
        string rulesDir = PatternLoader.GetDefaultRulesDir();
        string filePath = Path.Combine(rulesDir, "includes.jsonl");

        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[IncludeRobot] Missing rules file: {filePath}");
            Console.ResetColor();
            return;
        }

        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) continue;

            try
            {
                var rule = JsonSerializer.Deserialize<IncludeRule>(trimmed, jsonOpts);
                if (rule != null && !string.IsNullOrEmpty(rule.Token))
                {
                    _rules.Add(rule);
                }
            }
            catch (JsonException ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[IncludeRobot] Bad JSONL in includes.jsonl: {ex.Message}");
                Console.ResetColor();
            }
        }
        
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"[IncludeRobot] Loaded {_rules.Count} include rules from includes.jsonl");
        Console.ResetColor();
    }

    public string[] Apply(IReadOnlyList<string> lines)
    {
        var includes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var usings = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("//"))
                continue;
            var match = RxStdToken.Matches(line);
            foreach (Match m in match)
            {
                usings.Add($"using std::{m.Groups[1].Value};");
            }

            foreach (var rule in _rules)
            {
                if (Regex.IsMatch(line, $@"\b{Regex.Escape(rule.Token)}\b") || 
                    (rule.Token.Contains("::") && line.Contains(rule.Token)))
                {
                    if (!string.IsNullOrEmpty(rule.Header))
                        includes.Add($"include {rule.Header}");
                }
            }
        }

        if (includes.Count == 0 && usings.Count == 0)
            return lines.ToArray();

        var header = new List<string>();
        
        header.Add("// == Required Headers & Namespaces ==");
        
        if (includes.Count > 0)
        {
            foreach (var inc in includes)
            {
                header.Add(inc);
            }
            header.Add("");
        }

        if (usings.Count > 0)
        {
            foreach (var use in usings)
            {
                header.Add(use);
            }
            header.Add("");
        }

        var result = new List<string>(lines.Count + header.Count);
        
        result.AddRange(header);
        result.AddRange(lines);

        return result.ToArray();
    }
}
