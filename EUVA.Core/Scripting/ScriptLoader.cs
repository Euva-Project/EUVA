// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;


namespace EUVA.Core.Scripting;

public sealed class ScriptLoader
{
    public static readonly ScriptLoader Instance = new();
    public Action<string>? OnLogMessage { get; set; }

    private readonly List<ScriptRunner<IDecompilerPass>> _compiledRunners = new();
    private readonly Dictionary<PassStage, List<IDecompilerPass>> _activePasses = new();

    public string ScriptsDirectory { get; }

    private ScriptLoader()
    {   
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        ScriptsDirectory = Path.Combine(baseDir, "Scripts");
        
        foreach (PassStage stage in Enum.GetValues<PassStage>())
        {
            _activePasses[stage] = new List<IDecompilerPass>();
        }
    }

   public async Task InitializeAsync()
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [INIT] Glass Engine Initialization Started...");

        _compiledRunners.Clear();
        foreach (var list in _activePasses.Values) list.Clear();

        if (!Directory.Exists(ScriptsDirectory))
        {
            try { Directory.CreateDirectory(ScriptsDirectory); }
            catch { return; }
        }

        var scriptFiles = Directory.GetFiles(ScriptsDirectory, "*.cs", SearchOption.AllDirectories);
        if (scriptFiles.Length == 0)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [INIT] No scripts found. Engine is idle.");
            return;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [INIT] Found {scriptFiles.Length} scripts. Compiling...");

        var options = ScriptOptions.Default
            .WithReferences(
                typeof(IDecompilerPass).Assembly,        
                typeof(System.Linq.Enumerable).Assembly,  
                typeof(System.Collections.Generic.List<>).Assembly 
            )
            .WithImports("System", "System.Collections.Generic", "System.Linq", "EUVA.Core.Scripting", "EUVA.Core.Disassembly", "EUVA.Core.Disassembly.Analysis");

        foreach (var file in scriptFiles)
        {
            try
            {
                string code = await File.ReadAllTextAsync(file);
                var script = CSharpScript.Create<IDecompilerPass>(code, options);
                script.Compile();
                var runner = script.CreateDelegate();
                _compiledRunners.Add(runner);
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[Glass Engine] Compile Error! File: {Path.GetFileName(file)}\n{ex.Message}\n");
            }
        }
        
        OnLogMessage?.Invoke($"[Glass Engine] Compilation phase finished.");
    }

    public async Task PrepareFunctionPassesAsync()
    {
        foreach (var list in _activePasses.Values) list.Clear();

        foreach (var runner in _compiledRunners)
        {
            try
            {
                var result = await runner.Invoke();
                if (result != null) _activePasses[result.Stage].Add(result);
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [PR ERROR] Failed to instantiate pass:\n{ex.Message}\n");
            }
        }
    }

    public void RunScripts(PassStage stage, DecompilerContext context)
    {
        var passes = _activePasses[stage];
        if (passes.Count == 0) return;

        foreach (var pass in passes)
        {
            try 
            {
                pass.Execute(context);
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[Glass Engine] Func: 0x{context.FunctionAddress:X8} | Stage: {stage} | Pass: {pass.GetType().Name}\nException: {ex.Message}\n");
            }
        }
    }
}