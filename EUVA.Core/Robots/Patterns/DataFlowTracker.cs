// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EUVA.Core.Robots.Patterns;

public sealed class DataFlowTracker
{
    public sealed class SymbolInfo
    {
        public int AssignedAtLine { get; set; }
        public string SourceExpression { get; set; } = string.Empty;
        public string? SourceCall { get; set; }
        public string? KnownType { get; set; }
        public string? SemanticTag { get; set; }
    }

    private readonly Dictionary<string, SymbolInfo> _symbols = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (string Type, string Tag)> _knownApis = new(StringComparer.OrdinalIgnoreCase)
    {
       
        { "GetVersion",             ("DWORD",   "os_version") },
        { "GetVersionExW",          ("BOOL",    "os_version_ex") },
        { "GetVersionExA",          ("BOOL",    "os_version_ex") },
        { "CreateFileW",            ("HANDLE",  "file_handle") },
        { "CreateFileA",            ("HANDLE",  "file_handle") },
        { "OpenFile",               ("HFILE",   "file_handle") },
        { "ReadFile",               ("BOOL",    "file_read_result") },
        { "WriteFile",              ("BOOL",    "file_write_result") },
        { "CloseHandle",            ("BOOL",    "close_result") },
        { "DeleteFileW",            ("BOOL",    "delete_result") },
        { "DeleteFileA",            ("BOOL",    "delete_result") },
        { "VirtualAlloc",           ("LPVOID",  "allocated_mem") },
        { "VirtualFree",            ("BOOL",    "free_result") },
        { "HeapAlloc",              ("LPVOID",  "allocated_mem") },
        { "GlobalAlloc",            ("HGLOBAL", "allocated_mem") },
        { "LocalAlloc",             ("HLOCAL",  "allocated_mem") },
        { "malloc",                 ("void*",   "allocated_mem") },
        { "GetCurrentProcess",      ("HANDLE",  "process_handle") },
        { "GetCurrentProcessId",    ("DWORD",   "process_id") },
        { "GetCurrentThreadId",     ("DWORD",   "thread_id") },
        { "CreateProcessW",         ("BOOL",    "create_proc_result") },
        { "CreateProcessA",         ("BOOL",    "create_proc_result") },
        { "GetModuleHandleW",       ("HMODULE", "module_handle") },
        { "GetModuleHandleA",       ("HMODULE", "module_handle") },
        { "LoadLibraryW",           ("HMODULE", "module_handle") },
        { "LoadLibraryA",           ("HMODULE", "module_handle") },
        { "GetProcAddress",         ("FARPROC", "func_pointer") },
        { "FindWindowW",            ("HWND",    "window_handle") },
        { "FindWindowA",            ("HWND",    "window_handle") },
        { "CreateWindowExW",        ("HWND",    "window_handle") },
        { "CreateWindowExA",        ("HWND",    "window_handle") },
        { "GetDesktopWindow",       ("HWND",    "window_handle") },
        { "RegOpenKeyExW",          ("LONG",    "reg_status") },
        { "RegOpenKeyExA",          ("LONG",    "reg_status") },
        { "RegQueryValueExW",       ("LONG",    "reg_status") },
        { "RegQueryValueExA",       ("LONG",    "reg_status") },
        { "lstrlenA",               ("int",     "string_length") },
        { "lstrlenW",               ("int",     "string_length") },
        { "strlen",                 ("size_t",  "string_length") },
        { "wcslen",                 ("size_t",  "string_length") },
        { "GetLastError",           ("DWORD",   "error_code") },
        { "SetErrorMode",           ("UINT",    "prev_error_mode") },
        { "GetTickCount",           ("DWORD",   "tick_count") },
        { "GetSystemTime",          ("void",    "system_time") },
    };

    private static readonly Dictionary<string, Dictionary<string, string>> _knownConstantsForTag = new()
    {
        { "os_version", new Dictionary<string, string>
            {
                { "5",  "_WIN32_WINNT_WIN2K" },
                { "6",  "_WIN32_WINNT_VISTA" },
                { "10", "_WIN32_WINNT_WIN10" },
            }
        }
    };

    public void AnalyzePass(string[] lines)
    {
        _symbols.Clear();

        var assignCallRx = new Regex(
            @"^\s*(?<var>\w+)\s*=\s*(?:.*::)?(?<func>\w+)\s*\(",
            RegexOptions.Compiled);

        var assignStringRx = new Regex(
            @"^\s*(?<var>\w+)\s*=\s*""(?<str>[^""]*)""\s*;",
            RegexOptions.Compiled);

        var assignNumRx = new Regex(
            @"^\s*(?<var>\w+)\s*=\s*(?<val>0x[0-9A-Fa-f]+|\d+)\s*;",
            RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var callMatch = assignCallRx.Match(line);
            if (callMatch.Success)
            {
                string varName = callMatch.Groups["var"].Value;
                string funcName = callMatch.Groups["func"].Value;

                var sym = new SymbolInfo
                {
                    AssignedAtLine = i,
                    SourceExpression = line.Trim(),
                    SourceCall = funcName
                };

                if (_knownApis.TryGetValue(funcName, out var apiInfo))
                {
                    sym.KnownType = apiInfo.Type;
                    sym.SemanticTag = apiInfo.Tag;
                }

                _symbols[varName] = sym;
                continue;
            }

            var strMatch = assignStringRx.Match(line);
            if (strMatch.Success)
            {
                string varName = strMatch.Groups["var"].Value;
                _symbols[varName] = new SymbolInfo
                {
                    AssignedAtLine = i,
                    SourceExpression = line.Trim(),
                    KnownType = "const char*",
                    SemanticTag = "string_literal"
                };
                continue;
            }

            var numMatch = assignNumRx.Match(line);
            if (numMatch.Success)
            {
                string varName = numMatch.Groups["var"].Value;
             
                if (!_symbols.ContainsKey(varName))
                {
                    _symbols[varName] = new SymbolInfo
                    {
                        AssignedAtLine = i,
                        SourceExpression = line.Trim(),
                        KnownType = "unsigned int"
                    };
                }
            }
        }
    }

    public SymbolInfo? GetSymbol(string varName)
    {
        return _symbols.TryGetValue(varName, out var info) ? info : null;
    }

    public bool IsCallResult(string varName, string apiName)
    {
        if (!_symbols.TryGetValue(varName, out var info)) return false;
        return string.Equals(info.SourceCall, apiName, StringComparison.OrdinalIgnoreCase);
    }

    public string? GetSemanticTag(string varName)
    {
        return _symbols.TryGetValue(varName, out var info) ? info.SemanticTag : null;
    }

    public string? GetKnownType(string varName)
    {
        return _symbols.TryGetValue(varName, out var info) ? info.KnownType : null;
    }

    public string? TryResolveConstant(string varName, string value)
    {
        if (!_symbols.TryGetValue(varName, out var info)) return null;
        if (info.SemanticTag == null) return null;

        if (_knownConstantsForTag.TryGetValue(info.SemanticTag, out var constMap))
        {
            if (constMap.TryGetValue(value, out var constName))
                return constName;
        }

        return null;
    }

    public IReadOnlyDictionary<string, SymbolInfo> AllSymbols => _symbols;
}
