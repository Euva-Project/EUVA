// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EUVA.Core.Robots.Patterns;


public static class PatternLoader
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

   
    public static List<TransformRule> LoadAll(string rulesDir)
    {
        var rules = new List<TransformRule>();

        if (!Directory.Exists(rulesDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[PatternLoader] Rules directory not found: {rulesDir}");
            Console.ResetColor();
            return rules;
        }

        foreach (var file in Directory.GetFiles(rulesDir, "*.jsonl"))
        {
            rules.AddRange(LoadFile(file));
        }

        rules.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"[PatternLoader] Loaded {rules.Count} rules from {rulesDir}");
        Console.ResetColor();

        return rules;
    }

    public static List<TransformRule> LoadByCategory(string rulesDir, string category)
    {
        string filePath = Path.Combine(rulesDir, $"{category}.jsonl");
        if (!File.Exists(filePath)) return new List<TransformRule>();

        var rules = LoadFile(filePath);
        rules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        return rules;
    }

    public static List<TransformRule> LoadByCategories(string rulesDir, params string[] categories)
    {
        var rules = new List<TransformRule>();
        foreach (var cat in categories)
        {
            rules.AddRange(LoadByCategory(rulesDir, cat));
        }
        rules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        return rules;
    }

    private static List<TransformRule> LoadFile(string filePath)
    {
        var rules = new List<TransformRule>();

        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) continue;

            try
            {
                var rule = JsonSerializer.Deserialize<TransformRule>(trimmed, _jsonOpts);
                if (rule != null && !string.IsNullOrEmpty(rule.Pattern))
                {
                    rules.Add(rule);
                }
            }
            catch (JsonException ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[PatternLoader] Bad JSONL in {Path.GetFileName(filePath)}: {ex.Message}");
                Console.ResetColor();
            }
        }

        return rules;
    }

    public static string GetDefaultRulesDir()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Robots", "Patterns", "Rules");
    }
}
