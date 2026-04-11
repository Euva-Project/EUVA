// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EUVA.Core.Robots.Patterns.Types;

public static class AutoStructBuilder
{
    public static void DiscoverTypes(string[] lines, DataFlowTracker dataFlow)
    {
        var discovered = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        var rxField = new Regex(@"([\w_]+)->field_([0-9A-Fa-f]+)");
        var rxRawCast = new Regex(@"\*\s*\(\s*(?<type>[\w_:]+(?:\s*\*)?)\s*\*\s*\)\s*\(\s*(?<var>[\w_]+)\s*(?<sign>[\+\-])\s*(?:0x)?(?<offset>[0-9A-Fa-f]+)\s*\)");

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var matches = rxField.Matches(line);
            foreach (Match m in matches)
            {
                var varName = m.Groups[1].Value;
                var offset = m.Groups[2].Value;
                RegisterDiscovered(dataFlow, discovered, varName, offset, line, null);
            }

            var castMatches = rxRawCast.Matches(line);
            foreach (Match m in castMatches)
            {
                var type = m.Groups["type"].Value;
                var varName = m.Groups["var"].Value;
                var sign = m.Groups["sign"].Value;
                var offset = m.Groups["offset"].Value;
                
                if (sign == "-") offset = "minus_" + offset;
                RegisterDiscovered(dataFlow, discovered, varName, offset, line, type);
            }
        }

        foreach (var kvp in discovered)
        {
            var varName = kvp.Key;
            var fields = kvp.Value;
            
            var sortedOffsets = new List<string>(fields.Keys);
            sortedOffsets.Sort();
            string suffix = string.Join("_", sortedOffsets);
            var className = $"AutoStruct_{suffix}";

            var structDef = new StructDefinition
            {
                Name = className,
                Size = 0 
            };

            foreach (var offStr in sortedOffsets)
            {
                structDef.Fields[offStr] = new StructField
                {
                    Name = offStr.StartsWith("minus_") ? $"m_field_{offStr}" : $"m_field_{offStr}",
                    Type = fields[offStr] 
                };
            }

            TypeDatabase.RegisterDynamicStruct(structDef);
            dataFlow.SetKnownType(varName, className);
        }
    }

    private static void RegisterDiscovered(DataFlowTracker dataFlow, Dictionary<string, Dictionary<string, string>> discovered, string varName, string offset, string contextLine, string? explicitType)
    {
        var known = dataFlow.GetKnownType(varName);
        if (!string.IsNullOrEmpty(known) && !known.StartsWith("AutoStruct_"))
        {
           var existingDef = TypeDatabase.GetStruct(known);
           if (existingDef != null) return; 
        }

        if (!discovered.TryGetValue(varName, out var fields))
        {
            fields = new Dictionary<string, string>();
            discovered[varName] = fields;
        }

        string inferredType = "DWORD";
        if (!string.IsNullOrEmpty(explicitType))
        {
            inferredType = explicitType;
        }
        else 
        {
            if (contextLine.Contains("uint16_t") || contextLine.Contains("short")) inferredType = "uint16_t";
            else if (contextLine.Contains("uint8_t") || contextLine.Contains("byte")) inferredType = "uint8_t";
            else if (contextLine.Contains("void*") || contextLine.Contains("reinterpret_cast")) inferredType = "void*";
        }

        if (!fields.ContainsKey(offset) || fields[offset] == "DWORD")
        {
            fields[offset] = inferredType;
        }
    }
}
