using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Pipeline.Runtime.Commands;
using Unity.Pipeline.Telemetry;

namespace Unity.Pipeline.Tests.Editor.Telemetry
{
    /// <summary>
    /// End-to-end tests for the eval telemetry loop (AUTHAPI-29): running an <c>eval</c> writes a
    /// telemetry record, and the <c>report_evals</c> command (<see cref="EvalTelemetryCommand"/>) reads
    /// it back into an aggregated report. Telemetry is redirected to a temp directory so the real
    /// project's Library/Pipeline is never touched. Recording is fire-and-forget (a background task),
    /// so every eval is followed by <see cref="EvalUsageTelemetry.WaitForPendingRecords"/> before
    /// asserting on the log.
    /// </summary>
    class EvalTelemetryCommandTests
    {
        private string m_Dir;
        private bool m_SavedEnabled;
        private bool m_SavedStoreSource;
        private string m_SavedOverrideDir;

        [SetUp]
        public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "EvalTelemetryCmdTest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(m_Dir);

            m_SavedEnabled = EvalUsageTelemetry.Enabled;
            m_SavedStoreSource = EvalUsageTelemetry.StoreSource;
            m_SavedOverrideDir = EvalUsageTelemetry.OverrideDirectory;

            EvalUsageTelemetry.Enabled = true;
            EvalUsageTelemetry.StoreSource = false;
            EvalUsageTelemetry.OverrideDirectory = m_Dir;
        }

        [TearDown]
        public void TearDown()
        {
            // Drain any in-flight background record before the redirect target disappears.
            EvalUsageTelemetry.WaitForPendingRecords();

            EvalUsageTelemetry.Enabled = m_SavedEnabled;
            EvalUsageTelemetry.StoreSource = m_SavedStoreSource;
            EvalUsageTelemetry.OverrideDirectory = m_SavedOverrideDir;

            if (Directory.Exists(m_Dir))
            {
                try { Directory.Delete(m_Dir, true); }
                catch { /* ignore cleanup failures */ }
            }
        }

        [Test]
        public void RunningEval_WritesTelemetryRecord_WithExpectedShape()
        {
            var r = CodeEvalCommand.EvaluateCode("return Application.unityVersion;");
            Assert.IsTrue(r.Success, r.Error);
            Assert.IsTrue(EvalUsageTelemetry.WaitForPendingRecords(), "Background telemetry record should flush promptly");

            var records = EvalUsageTelemetry.ReadRecords(m_Dir);
            Assert.AreEqual(1, records.Count, "The eval should have produced exactly one telemetry record");

            var record = records.Single();
            Assert.AreEqual("eval", record.Command);
            Assert.IsTrue(record.Success);
            Assert.AreEqual(EvalClassification.SingleExpression, record.Classification);
            Assert.Greater(record.ExecutionTimeMs, 0);
            CollectionAssert.Contains(record.Fingerprints, "Application.unityVersion");
            Assert.IsNull(record.Source, "Raw source must not be stored by default");
        }

        [Test]
        public void EvalFile_RecordsWithEvalFileCommandName()
        {
            var path = Path.Combine(m_Dir, "snippet.cs");
            File.WriteAllText(path, "return Application.platform.ToString();");

            var r = CodeEvalCommand.EvaluateFile(path);
            Assert.IsTrue(r.Success, r.Error);
            Assert.IsTrue(EvalUsageTelemetry.WaitForPendingRecords(), "Background telemetry record should flush promptly");

            var record = EvalUsageTelemetry.ReadRecords(m_Dir).Single();
            Assert.AreEqual("eval_file", record.Command);
            CollectionAssert.Contains(record.Fingerprints, "Application.platform.ToString");
        }

        [Test]
        public void FailingEval_IsRecordedAsError()
        {
            var r = CodeEvalCommand.EvaluateCode("return 2 +;");
            Assert.IsFalse(r.Success);
            Assert.IsTrue(EvalUsageTelemetry.WaitForPendingRecords(), "Background telemetry record should flush promptly");

            var record = EvalUsageTelemetry.ReadRecords(m_Dir).Single();
            Assert.IsFalse(record.Success);
            Assert.AreEqual("Compilation Failed", record.Error);
        }

        [Test]
        public void ReportEvals_AggregatesRecordedEvals()
        {
            CodeEvalCommand.EvaluateCode("return Application.unityVersion;"); // one-liner, success
            CodeEvalCommand.EvaluateCode("Debug.Log(\"x\"); return 1;");      // statements, success
            CodeEvalCommand.EvaluateCode("return 2 +;");                       // error
            Assert.IsTrue(EvalUsageTelemetry.WaitForPendingRecords(), "Background telemetry records should flush promptly");

            var report = EvalTelemetryCommand.ReportEvals();

            Assert.AreEqual(3, report.TotalCount);
            Assert.AreEqual(1, report.ErrorCount);
            Assert.AreEqual(2, report.SuccessCount);
            Assert.AreEqual(1, report.StatementsCount);
            Assert.AreEqual(2, report.SingleExpressionCount);
            CollectionAssert.Contains(report.Fingerprints.Select(f => f.Member).ToList(), "Application.unityVersion");
            Assert.IsNotNull(report.CoverageSuggestions);
        }

        [Test]
        public void ReportEvals_NegativeTop_ClampsToDefault()
        {
            CodeEvalCommand.EvaluateCode("return Application.unityVersion;");
            CodeEvalCommand.EvaluateCode("AssetDatabase.Refresh();");
            Assert.IsTrue(EvalUsageTelemetry.WaitForPendingRecords(), "Background telemetry records should flush promptly");

            var report = EvalTelemetryCommand.ReportEvals(top: -5);

            // A negative cap falls back to the default (50) — it must neither empty the ranked lists
            // nor be treated as "unlimited".
            Assert.AreEqual(2, report.TotalCount);
            Assert.IsNotEmpty(report.Fingerprints);
            Assert.AreEqual(report.DistinctFingerprints, report.Fingerprints.Count,
                "Well under the default cap, nothing is truncated");
        }
    }
}
