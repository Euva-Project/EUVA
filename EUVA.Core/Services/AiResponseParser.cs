// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace EUVA.Core.Services;

public static class AiResponseParser
{
    public static void Parse(string response, Dictionary<string, string> results)
    {
        if (string.IsNullOrEmpty(response)) return;

        ReadOnlySpan<char> span = response.AsSpan();
        int start = 0;

        while (start < span.Length)
        {
            int end = span[start..].IndexOf('\n');
            if (end == -1) end = span.Length - start;

            ReadOnlySpan<char> line = span.Slice(start, end).Trim();
            start += end + 1;

            if (line.IsEmpty) continue;

            
            if (line.StartsWith("```")) continue;

            int sep = line.IndexOf('=');
            if (sep == -1) continue;

            ReadOnlySpan<char> oldName = line[..sep].Trim();
            ReadOnlySpan<char> newName = line[(sep + 1)..].Trim();

            if (!oldName.IsEmpty && !newName.IsEmpty)
            {
                results[oldName.ToString()] = newName.ToString();
            }
        }
    }
}
