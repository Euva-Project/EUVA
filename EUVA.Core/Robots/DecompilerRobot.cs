// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EUVA.Core.Robots;

public sealed class DecompilerRobot : RobotBase
{
    private string _workspacePath = string.Empty;
    private int _annotationCount = 0;

    public DecompilerRobot(RobotRole role, IRobotNetwork network) : base(role, network) { }

    public override async Task<RobotResult> ExecuteAsync(MappedDumpContext ctx, string workspacePath, CancellationToken ct = default)
    {
        SetStatus(RobotStatus.Working);
        _workspacePath = workspacePath;
        _annotationCount = 0;

        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WORK]   {Role,-28} # analyzing dump via MMF...");
        Console.ForegroundColor = prev;

        try
        {
            await DispatchByRole(ctx, ct).ConfigureAwait(false);

            double confidence = _annotationCount > 0 ? Math.Min(1.0, _annotationCount * 0.3) : 1.0;
            string summaryText = $"{Role}: {_annotationCount} annotation(s)";

            byte[] verifKey = await _network.Admin.Verifier.RequestVerificationKeyAsync(Id, Role, _annotationCount, summaryText).ConfigureAwait(false);

            var result = new RobotResult
            {
                RobotId         = Id,
                Role            = Role,
                HasFindings     = _annotationCount > 0,
                Summary         = summaryText,
                AnnotationCount = _annotationCount,
                Confidence      = confidence,
                VerificationKey = verifKey
            };

            var prevLog = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[MSG] {Role,-28} waiting for peers at the finish line...");
            Console.ForegroundColor = prevLog;
            
            await WaitUntilAllPeersDoneAsync(ct).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            SetStatus(RobotStatus.Faulted);
            throw;
        }
        catch (Exception ex)
        {
            SetStatus(RobotStatus.Faulted);
            return new RobotResult
            {
                RobotId     = Id,
                Role        = Role,
                HasFindings = false,
                Summary     = $"[ERROR] {Role}: {ex.Message}",
                Confidence  = 0.0,
            };
        }
    }

    private void Emit(long offset, int line, string action, string context)
    {
        WorkspaceManager.WriteAnnotation(_workspacePath, Role, offset, line, action, context);
        Interlocked.Increment(ref _annotationCount);
    }

    private Task DispatchByRole(MappedDumpContext ctx, CancellationToken ct) =>
        Role switch
        {
            RobotRole.YaraScanner              => ScanYaraPatterns(ctx, ct),
            RobotRole.HexSignatureMatcher      => MatchHexSignatures(ctx, ct),
            RobotRole.BinaryPatternAnalyzer    => AnalyzeBinaryPatterns(ctx, ct),
            RobotRole.ApiChainTracer           => TraceApiChains(ctx, ct),
            RobotRole.MetadataExtractor        => ExtractMetadata(ctx, ct),
            RobotRole.IrLifterAgent            => AnnotateIrLifting(ctx, ct),
            RobotRole.ControlFlowAnalyzer      => AnalyzeControlFlow(ctx, ct),
            RobotRole.DataFlowAnalyzer         => AnalyzeDataFlow(ctx, ct),
            RobotRole.TypeInferenceAgent       => InferTypes(ctx, ct),
            RobotRole.CallingConventionAgent   => AnalyzeCallingConventions(ctx, ct),
            RobotRole.StringExtractor          => ExtractStrings(ctx, ct),
            RobotRole.EntropyAnalyzer          => AnalyzeEntropy(ctx, ct),
            RobotRole.ImportTracer             => TraceImports(ctx, ct),
            RobotRole.ExportTracer             => TraceExports(ctx, ct),
            RobotRole.SsaTransformer           => AnnotateSsa(ctx, ct),
            RobotRole.LoopDetectionAgent       => DetectLoops(ctx, ct),
            RobotRole.SwitchDetectionAgent     => DetectSwitches(ctx, ct),
            RobotRole.StructReconstructor      => ReconstructStructs(ctx, ct),
            RobotRole.VTableDetectionAgent     => DetectVTables(ctx, ct),
            RobotRole.IdiomRecognizer          => RecognizeIdioms(ctx, ct),
            RobotRole.DeadCodeAgent            => EliminateDeadCode(ctx, ct),
            RobotRole.ConstantPropagationAgent => PropagateConstants(ctx, ct),
            RobotRole.ExpressionSimplifier     => SimplifyExpressions(ctx, ct),
            RobotRole.SemanticGuesser          => GuessSemantics(ctx, ct),
            RobotRole.FingerprintAgent         => MatchFingerprints(ctx, ct),
            RobotRole.PseudocodeEmitter        => EnhancePseudocode(ctx, ct),
            RobotRole.NamingAgent              => ApplyNaming(ctx, ct),
            RobotRole.XrefAnalyzer             => AnalyzeXrefs(ctx, ct),
            RobotRole.WeightChainValidator     => ValidateWeightChain(ctx, ct),
            RobotRole.VerificationRelay        => RelayVerification(ctx, ct),
            _                                  => Task.CompletedTask,
        };

    private async Task ScanYaraPatterns(MappedDumpContext ctx, CancellationToken ct)
    {
        await Task.Yield();

        //debug
        string missingKey = "TestSig_01";
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ROBOT:ERR] {Role} missing YARA signature: '{missingKey}'. Requesting Admin help...");
        Console.ForegroundColor = prev;

        AdminResponse response = await RequestAdminHelpAsync(missingKey, ct);

        if (response.Decision == AdminDecision.InheritData && response.Payload != null)
        {
            prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[ROBOT:ACK] {Role} inherited payload of {response.Payload.Length} bytes. Processing...");
            Console.ForegroundColor = prev;
            Emit(0x0000, 0, "YARA_MATCH", $"Inherited KDB payload for {missingKey}");
        }
        else
        {
            prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[ROBOT:ACK] {Role} was instructed to Ignore missing '{missingKey}'. Skipping.");
            Console.ForegroundColor = prev;
        }
    }

    private async Task MatchHexSignatures(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task AnalyzeBinaryPatterns(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }

    private async Task TraceApiChains(MappedDumpContext ctx, CancellationToken ct)
    {
        await Task.Yield();

        ctx.RunScoped(span => 
        {
            if (KmpContainsAny(span, "CreateFile", "ReadFile", "WriteFile", "CloseHandle"))
                Emit(0x0000, 0, "API_CHAIN", "FileIO: CreateFile/ReadFile/WriteFile");

            if (KmpContainsAny(span, "VirtualAlloc", "VirtualProtect", "VirtualFree"))
                Emit(0x0000, 0, "API_CHAIN", "Memory: VirtualAlloc/VirtualProtect/VirtualFree");

            if (KmpContainsAny(span, "CreateThread", "OpenThread", "ResumeThread"))
                Emit(0x0000, 0, "API_CHAIN", "Thread: CreateThread/OpenThread/ResumeThread");

            if (KmpContainsAny(span, "RegOpenKey", "RegSetValue", "RegQueryValue"))
                Emit(0x0000, 0, "API_CHAIN", "Registry: RegOpenKey/RegSetValue/RegQueryValue");

            if (KmpContainsAny(span, "WSAStartup", "connect(", "send(", "recv("))
                Emit(0x0000, 0, "API_CHAIN", "Network: WSAStartup/connect/send/recv");
        });
    }

    private async Task ExtractMetadata(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task AnnotateIrLifting(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task AnalyzeControlFlow(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task AnalyzeDataFlow(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }

    private async Task InferTypes(MappedDumpContext ctx, CancellationToken ct)
    {
        await Task.Yield();

        ctx.RunScoped(span => 
        {
            if (KmpContainsAny(span, "int ", "unsigned int"))
                Emit(0x0000, 0, "TYPE_PROMOTE", "int/unsigned int -> KDB_LOOKUP_REQUIRED");
        });
    }

    private async Task AnalyzeCallingConventions(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }

    private async Task ExtractStrings(MappedDumpContext ctx, CancellationToken ct)
    {
        await Task.Yield();

        ctx.RunScoped(span => 
        {
            int stringCount = KmpCountOccurrences(span, "\"");
            if (stringCount > 0)
                Emit(0x0000, 0, "STRING_COUNT", $"{stringCount / 2} string literals detected");
        });
    }

    private async Task AnalyzeEntropy(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task TraceImports(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task TraceExports(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task AnnotateSsa(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }

    private async Task DetectLoops(MappedDumpContext ctx, CancellationToken ct)
    {
        await Task.Yield();
        
        ctx.RunScoped(span => 
        {
            int forCount   = KmpCountOccurrences(span, "for (");
            int whileCount = KmpCountOccurrences(span, "while (");
            int doCount    = KmpCountOccurrences(span, "do {");

            if (forCount + whileCount + doCount > 0)
                Emit(0x0000, 0, "LOOP_DETECT", $"for={forCount} while={whileCount} do={doCount}");
        });
    }

    private async Task DetectSwitches(MappedDumpContext ctx, CancellationToken ct)
    {
        await Task.Yield();
        
        ctx.RunScoped(span => 
        {
            int switchCount = KmpCountOccurrences(span, "switch (");
            if (switchCount > 0)
                Emit(0x0000, 0, "SWITCH_DETECT", $"count={switchCount}");
        });
    }

    private async Task ReconstructStructs(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task DetectVTables(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task RecognizeIdioms(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task EliminateDeadCode(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task PropagateConstants(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task SimplifyExpressions(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task GuessSemantics(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task MatchFingerprints(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task EnhancePseudocode(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task ApplyNaming(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task AnalyzeXrefs(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task ValidateWeightChain(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }
    private async Task RelayVerification(MappedDumpContext ctx, CancellationToken ct) { await Task.Yield(); }

    private static unsafe int KmpCountOccurrences(ReadOnlySpan<byte> text, string patternString)
    {
        int patLen = patternString.Length;
        if (patLen == 0 || text.Length < patLen) 
            return 0;
        
        byte* pat = stackalloc byte[patLen];
        for (int i = 0; i < patLen; i++) 
            pat[i] = (byte)patternString[i];

        int* lps = stackalloc int[patLen];
        lps[0] = 0;
        int len = 0, idx = 1;
        while (idx < patLen)
        {
            if (pat[idx] == pat[len]) 
                lps[idx++] = ++len;
            else if (len != 0) 
                len = lps[len - 1];
            else 
                lps[idx++] = 0;
        }

        int count = 0;
        int iTxt = 0, jPat = 0;
        
        while (iTxt < text.Length)
        {
            if (pat[jPat] == text[iTxt])
            {
                jPat++; 
                iTxt++;
            }
            if (jPat == patLen)
            {
                count++;
                jPat = lps[jPat - 1];
            }
            else if (iTxt < text.Length && pat[jPat] != text[iTxt])
            {
                if (jPat != 0) 
                    jPat = lps[jPat - 1];
                else 
                    iTxt++;
            }
        }
        return count;
    }

    private static bool KmpContainsAny(ReadOnlySpan<byte> text, params string[] patterns)
    {
        foreach (var p in patterns)
            if (KmpCountOccurrences(text, p) > 0) return true;
        return false;
    }
}
