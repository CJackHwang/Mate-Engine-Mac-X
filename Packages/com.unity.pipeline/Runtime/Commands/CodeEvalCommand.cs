using System;
using Unity.Pipeline.Models;
using Unity.Pipeline.Compilation;
using Unity.Pipeline.Telemetry;
using UnityEngine;
using Unity.Pipeline.Commands;

namespace Unity.Pipeline.Runtime.Commands
{
    /// <summary>
    /// Command for evaluating C# code dynamically using Roslyn compilation.
    /// Requires security token for authorization.
    /// </summary>
    static class CodeEvalCommand
    {
        [CliCommand("eval", "Evaluate C# code dynamically using Roslyn compiler", MainThreadRequired = true, Tags = new[] { "scripts/eval" })]
        public static EvalResponse EvaluateCode(
            [CliArg("code", "C# code to evaluate", Required = true)] string code,
            [CliArg("timeout", "Timeout in milliseconds")] int timeout = 5000)
        {
            if (string.IsNullOrWhiteSpace(code))
                return RejectedEval("eval", code, "Code parameter is required and cannot be empty");

            return EvaluateSource(code, timeout, "eval");
        }

        [CliCommand("eval_file", "Evaluate C# code read from a .cs file on disk", MainThreadRequired = true, Tags = new[] { "scripts/eval" })]
        public static EvalResponse EvaluateFile(
            [CliArg("file", "Path to a .cs file to evaluate", Required = true)] string file,
            [CliArg("timeout", "Timeout in milliseconds")] int timeout = 5000)
        {
            if (string.IsNullOrWhiteSpace(file))
                return RejectedEval("eval_file", null, "File parameter is required and cannot be empty");

            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return RejectedEval("eval_file", null, "File must have a .cs extension");

            if (!System.IO.File.Exists(file))
                return RejectedEval("eval_file", null, $"File not found: {file}");

            string code;
            try
            {
                code = System.IO.File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                return RejectedEval("eval_file", null, $"Failed to read file: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(code))
                return RejectedEval("eval_file", code, $"File is empty: {file}");

            return EvaluateSource(code, timeout, "eval_file");
        }

        /// <summary>
        /// Reject an invocation at validation time AND record it: a rejected eval is still an eval
        /// invocation, and skipping telemetry here would undercount report_evals' errorCount /
        /// errorRate for exactly the mistakes an agent makes most (empty code, wrong path).
        /// </summary>
        private static EvalResponse RejectedEval(string command, string code, string errorDetails)
        {
            // Guarded like the RecordInBackground call in EvaluateSource: telemetry must never
            // affect eval behavior, so a failure in its synchronous prelude (ResolveDirectory,
            // the write-chain lock) can't turn a clean Bad Request into an unhandled exception.
            try
            {
                EvalUsageTelemetry.RecordInBackground(command, code ?? string.Empty, success: false,
                    error: "Bad Request", executionTimeMs: 0);
            }
            catch
            {
                // Telemetry must never affect eval behavior.
            }

            return EvalResponse.EvalFailure("Bad Request", errorDetails);
        }

        /// <summary>Upper bound on a single eval's timeout — 24 hours (CLI-335; was 30s).</summary>
        private const int MaxTimeoutMs = 86_400_000;

        /// <summary>
        /// Shared evaluation path: compiles and executes the given C# source and returns the
        /// response. Both the <c>eval</c> and <c>eval_file</c> commands funnel their resolved
        /// source string into here. Each invocation is recorded to local eval-usage telemetry
        /// (AUTHAPI-29) on a best-effort basis before returning.
        /// </summary>
        private static EvalResponse EvaluateSource(string code, int timeout, string commandName)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            EvalResponse response;

            try
            {
                // Validate timeout
                // Cap raised from 30000ms (CLI-335): long evals are now legitimate — clients
                // set custom timeouts or run detached jobs and reattach. 24h bounds abuse.
                // (Supersedes the interim 300000ms raise from UUM-148641.)
                if (timeout <= 0 || timeout > MaxTimeoutMs)
                {
                    stopwatch.Stop();
                    response = EvalResponse.EvalFailure(
                        "Bad Request",
                        $"Timeout must be between 1ms and {MaxTimeoutMs}ms",
                        stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    // Execute compilation and evaluation. We're already on the main thread
                    // (MainThreadRequired = true), so call the synchronous path directly.
                    var result = EvalCodeCompiler.CompileAndExecuteOnMainThread(code, timeout, null);

                    stopwatch.Stop();

                    // Update execution time
                    if (result != null)
                    {
                        result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                    }

                    response = result ?? EvalResponse.EvalFailure(
                        "Unknown Error",
                        "Compilation returned null result",
                        stopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Debug.LogError($"Pipeline: Eval command failed: {ex.Message}");
                Debug.LogError($"Pipeline: Stack trace: {ex.StackTrace}");

                response = EvalResponse.EvalFailure(
                    "Execution Failed",
                    ex.ToString(),
                    stopwatch.ElapsedMilliseconds);
            }

            // Best-effort local telemetry (editor-only; a no-op in players): fingerprint + shape only
            // by default (no raw source unless the explicit opt-in is set). The eval primitives and
            // the current settings are snapshotted here on the main thread, then the Roslyn parse and
            // file append run on a background task — fire-and-forget, zero latency added to the
            // response. RecordInBackground swallows its own errors, but guard here too so an
            // unexpected failure can never turn a good eval into a failed command.
            try
            {
                EvalUsageTelemetry.RecordInBackground(
                    commandName,
                    code,
                    response.Success,
                    response.Error,
                    response.ExecutionTimeMs ?? stopwatch.ElapsedMilliseconds);
            }
            catch
            {
                // Telemetry must never affect eval behavior.
            }

            return response;
        }
    }
}