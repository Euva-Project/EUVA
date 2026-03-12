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
        _compiledRunners.Clear();
        foreach (var list in _activePasses.Values)
            list.Clear();

        if (!Directory.Exists(ScriptsDirectory))
        {
            try
            {
                Directory.CreateDirectory(ScriptsDirectory);
            }
            catch
            {
                return;
            }
        }

        var scriptFiles = Directory.GetFiles(ScriptsDirectory, "*.cs", SearchOption.AllDirectories);
        if (scriptFiles.Length == 0)
            return;

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
                Console.WriteLine($"[ScriptLoader] Failed to compile script '{Path.GetFileName(file)}': {ex.Message}");
            }
        }
    }

    public async Task PrepareFunctionPassesAsync()
    {
        foreach (var list in _activePasses.Values)
            list.Clear();

        foreach (var runner in _compiledRunners)
        {
            try
            {
                var result = await runner.Invoke();
                if (result != null)
                {
                    _activePasses[result.Stage].Add(result);
                }
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"[ScriptLoader] Error instantiating pass: {ex.Message}");
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
                Console.WriteLine($"[ScriptLoader] Error executing pass '{pass.GetType().Name}': {ex.Message}");
            }
        }
    }
}
