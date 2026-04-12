// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EUVA.Core.Robots.Patterns;

public sealed class UniversalLabelRobot
{
    private readonly Regex _rxFnCall = new(@"\b([a-zA-Z_]\w*)\s*\(", RegexOptions.Compiled);
    private readonly Regex _rxString = new(@"""([^""]+)""", RegexOptions.Compiled);
    private readonly Regex _rxStruct = new(@"->([a-zA-Z_]\w*)", RegexOptions.Compiled);
    private readonly Regex _rxGlobal = new(@"\b(g_[a-zA-Z_]\w*|hr_[a-zA-Z_]\w*)\b", RegexOptions.Compiled);

    private readonly HashSet<string> _blacklistedFns = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "while", "for", "switch", "return", "catch", "sizeof", "alignof", "decltype",
        "reinterpret_cast", "static_cast", "dynamic_cast", "const_cast", "do", "else", "goto",
        "strlen", "wcslen", "memcpy", "memset", "memcmp", "_wcsicmp", "_wcscpy", "_stricmp", "std"
    };

    public string[] ApplyLabels(IReadOnlyList<string> lines)
    {
        var result = new List<string>();
        var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int depth = 0;
        bool expectTransition = false;
        bool inFunction = false;
        int linesSinceFunctionStart = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            string cleanLine = RemoveStringsAndComments(line);

            if (!inFunction && cleanLine.Contains("{"))
            {
                inFunction = true;
                depth++;
                result.Add(line);
                expectTransition = true;
                continue;
            }

            if (!inFunction)
            {
                result.Add(line);
                continue;
            }

            linesSinceFunctionStart++;

            int closes = CountChars(cleanLine, '}');
            depth -= closes;

            if (string.IsNullOrWhiteSpace(trimmed) && depth == 1)
            {
                expectTransition = true;
                result.Add(line);
                continue;
            }

            if (depth == 1 && (cleanLine.TrimStart().StartsWith("do ") || cleanLine.TrimStart() == "do" || cleanLine.TrimStart().StartsWith("while ")))
            {
                expectTransition = true;
            }

            if (expectTransition && depth == 1 && linesSinceFunctionStart > 5 && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("{") && !trimmed.StartsWith("}"))
            {
                if (!trimmed.StartsWith("else") && !trimmed.StartsWith("catch"))
                {
                    string labelName = ExtractSemanticName(lines, i, Math.Min(20, lines.Count - i));
                    if (!string.IsNullOrEmpty(labelName))
                    {
                        string finalLabel = labelName;
                        if (usedLabels.Contains(finalLabel))
                        {
                            finalLabel = $"{labelName}_L{i + 1}";
                        }
                        usedLabels.Add(finalLabel);
                        result.Add("");
                        result.Add($"{new string(' ', 0)}{finalLabel}:");
                    }
                }
                expectTransition = false;
            }

            result.Add(line);

            int opens = CountChars(cleanLine, '{');
            depth += opens;

            if (closes > 0 && depth == 1)
            {
                expectTransition = true;
            }
        }

        return result.ToArray();
    }

    private string ExtractSemanticName(IReadOnlyList<string> lines, int startIndex, int lookahead)
    {
        for (int i = 0; i < lookahead; i++)
        {
            string rawLine = lines[startIndex + i];
            string trimmed = rawLine.Trim();

            if (i > 0)
            {
                if (string.IsNullOrWhiteSpace(trimmed)) break;
                if (trimmed.StartsWith("do") || trimmed.StartsWith("while")) break;
                if (trimmed.StartsWith("loc_")) break;
                if (trimmed.StartsWith("}")) break; 
            }

            string cleanLine = RemoveStringsAndComments(rawLine);

        
            var callMatches = _rxFnCall.Matches(cleanLine);
            foreach (Match m in callMatches)
            {
                string fn = m.Groups[1].Value;
                if (_blacklistedFns.Contains(fn) || fn.StartsWith("sub_") || fn.StartsWith("byte_") || fn.StartsWith("loc_")) 
                    continue;


                if (fn.Length > 4 && (fn.EndsWith("W") || fn.EndsWith("A"))) 
                    fn = fn.Substring(0, fn.Length - 1);
                if (fn.EndsWith("Ex")) 
                    fn = fn.Substring(0, fn.Length - 2);

                return $"loc_{fn}";
            }

            var strMatch = _rxString.Match(lines[startIndex + i]);
            if (strMatch.Success)
            {
                string strVal = new string(strMatch.Groups[1].Value.Where(char.IsLetterOrDigit).ToArray());
                if (strVal.Length > 3)
                {
                    if (strVal.Length > 20) strVal = strVal.Substring(0, 20);
                    return $"loc_Str_{strVal}";
                }
            }

            var glMatch = _rxGlobal.Match(cleanLine);
            if (glMatch.Success && !glMatch.Value.StartsWith("g_Data_") && !glMatch.Value.StartsWith("g_0x"))
            {
                return $"loc_{glMatch.Value}";
            }

            var stMatch = _rxStruct.Match(cleanLine);
            if (stMatch.Success)
            {
                string field = stMatch.Groups[1].Value;
                if (!field.StartsWith("m_field_") && !field.StartsWith("minus_"))
                {
                    return $"loc_{field}";
                }
            }
        }

        return $"loc_Block_L{startIndex + 1}";
    }

    private string RemoveStringsAndComments(string line)
    {
        int cmt = line.IndexOf("//");
        if (cmt >= 0) line = line.Substring(0, cmt);
        
        bool inStr = false;
        var sb = new StringBuilder(line.Length);
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
                inStr = !inStr;
            else if (!inStr)
                sb.Append(line[i]);
        }
        return sb.ToString();
    }

    private int CountChars(string s, char c) => s.Count(x => x == c);
}
