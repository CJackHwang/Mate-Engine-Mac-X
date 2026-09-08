using System;
using System.IO;
using NUnit.Framework;
using Unity.Pipeline.Telemetry;

// No namespace on purpose: an NUnit [SetUpFixture] outside any namespace runs its OneTimeSetUp /
// OneTimeTearDown once for the WHOLE assembly (same pattern as PipelineWatchdogTestGuard), so this
// covers every editor test regardless of sub-namespace.

/// <summary>
/// Redirects <see cref="EvalUsageTelemetry.OverrideDirectory"/> to a throwaway temp directory for the
/// entire EditMode run. Many suites run evals through the live server without going near the telemetry
/// fixtures (JobEndpointTests, ApiEndpointsTests, hot-reload suites, …); without this suite-level
/// redirect each of those evals would append a record to the REAL project's
/// <c>Library/Pipeline/eval-usage.jsonl</c>, polluting the dogfood telemetry with test noise. The
/// telemetry-specific fixtures keep their own per-fixture redirects — they save this fixture's
/// directory in SetUp and restore it in TearDown, so the two layers compose.
///
/// The original override (normally null → the real Library/Pipeline) is restored and the temp
/// directory deleted in OneTimeTearDown, after draining any in-flight background records. Note: a
/// mid-run domain reload would reset the static override; like the other suite guards, this accepts
/// that narrow window rather than trying to survive it.
/// </summary>
[SetUpFixture]
sealed class EvalTelemetryTestGuard
{
    private string m_TempDir;
    private string m_SavedOverrideDir;

    [OneTimeSetUp]
    public void RedirectTelemetryForRun()
    {
        m_SavedOverrideDir = EvalUsageTelemetry.OverrideDirectory;
        m_TempDir = Path.Combine(Path.GetTempPath(), "PipelineEvalTelemetryRun_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(m_TempDir);
        EvalUsageTelemetry.OverrideDirectory = m_TempDir;
    }

    [OneTimeTearDown]
    public void RestoreTelemetryTarget()
    {
        // Let stragglers flush before the directory disappears (recording is fire-and-forget).
        EvalUsageTelemetry.WaitForPendingRecords();

        EvalUsageTelemetry.OverrideDirectory = m_SavedOverrideDir;

        if (!string.IsNullOrEmpty(m_TempDir) && Directory.Exists(m_TempDir))
        {
            try { Directory.Delete(m_TempDir, true); }
            catch { /* ignore cleanup failures */ }
        }
    }
}
