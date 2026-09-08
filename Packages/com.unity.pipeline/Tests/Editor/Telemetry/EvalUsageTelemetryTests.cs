using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Pipeline.Telemetry;

namespace Unity.Pipeline.Tests.Editor.Telemetry
{
    /// <summary>
    /// Tests for <see cref="EvalUsageTelemetry"/>: the local JSONL store, the raw-source opt-in, and
    /// the size-cap rotation (AUTHAPI-29). All I/O goes to an isolated temp directory via
    /// <see cref="EvalUsageTelemetry.OverrideDirectory"/> / the directory seam, and the mutated statics
    /// are snapshotted and restored, so the real project telemetry file and global config are untouched.
    /// </summary>
    class EvalUsageTelemetryTests
    {
        private string m_Dir;
        private string m_JsonlPath;
        private string m_OldPath;

        private bool m_SavedEnabled;
        private bool m_SavedStoreSource;
        private string m_SavedOverrideDir;
        private long m_SavedMaxBytes;

        [SetUp]
        public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "EvalUsageTelemetryTest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(m_Dir);
            m_JsonlPath = Path.Combine(m_Dir, "eval-usage.jsonl");
            m_OldPath = Path.Combine(m_Dir, "eval-usage.old.jsonl");

            m_SavedEnabled = EvalUsageTelemetry.Enabled;
            m_SavedStoreSource = EvalUsageTelemetry.StoreSource;
            m_SavedOverrideDir = EvalUsageTelemetry.OverrideDirectory;
            m_SavedMaxBytes = EvalUsageTelemetry.MaxFileBytes;

            EvalUsageTelemetry.Enabled = true;
            EvalUsageTelemetry.StoreSource = false;
            EvalUsageTelemetry.OverrideDirectory = m_Dir;
        }

        [TearDown]
        public void TearDown()
        {
            EvalUsageTelemetry.Enabled = m_SavedEnabled;
            EvalUsageTelemetry.StoreSource = m_SavedStoreSource;
            EvalUsageTelemetry.OverrideDirectory = m_SavedOverrideDir;
            EvalUsageTelemetry.MaxFileBytes = m_SavedMaxBytes;

            if (Directory.Exists(m_Dir))
            {
                try { Directory.Delete(m_Dir, true); }
                catch { /* ignore cleanup failures */ }
            }
        }

        [Test]
        public void Record_WritesOneJsonlLine_WithExpectedFields()
        {
            EvalUsageTelemetry.Record("eval", "return MatchManager.Instance.State;", success: true, error: null, executionTimeMs: 12);

            var lines = File.ReadAllLines(m_JsonlPath);
            Assert.AreEqual(1, lines.Length, "One eval should append exactly one JSONL line");

            var obj = JObject.Parse(lines[0]);
            Assert.AreEqual("eval", (string)obj["command"]);
            Assert.IsTrue((bool)obj["success"]);
            Assert.AreEqual(EvalClassification.SingleExpression, (string)obj["classification"]);
            Assert.AreEqual(12, (long)obj["executionTimeMs"]);
            Assert.AreEqual("return MatchManager.Instance.State;".Length, (int)obj["payloadLength"]);
            Assert.AreEqual(1, (int)obj["lineCount"]);
            CollectionAssert.Contains(obj["fingerprints"].Select(t => (string)t).ToList(), "MatchManager.Instance.State");
            Assert.DoesNotThrow(() => DateTime.Parse((string)obj["time"],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind));
        }

        [Test]
        public void Record_ByDefault_DoesNotStoreRawSource()
        {
            EvalUsageTelemetry.StoreSource = false;
            EvalUsageTelemetry.Record("eval", "return SecretApi.Token;", success: true, error: null, executionTimeMs: 1);

            var line = File.ReadAllLines(m_JsonlPath).Single();
            var obj = JObject.Parse(line);
            Assert.IsFalse(obj.ContainsKey("source"), "Raw source must be absent by default");
            Assert.IsFalse(line.Contains("SecretApi.Token;"), "Raw source string must not appear in the record");
        }

        [Test]
        public void Record_WithStoreSourceOptIn_StoresRawSource()
        {
            EvalUsageTelemetry.StoreSource = true;
            EvalUsageTelemetry.Record("eval", "return 1 + 1;", success: true, error: null, executionTimeMs: 1);

            var obj = JObject.Parse(File.ReadAllLines(m_JsonlPath).Single());
            Assert.AreEqual("return 1 + 1;", (string)obj["source"]);
        }

        [Test]
        public void Record_WithStoreSourceOptIn_TruncatesOversizedSourceWithMarker()
        {
            EvalUsageTelemetry.StoreSource = true;
            var code = "return 1; // " + new string('x', EvalUsageTelemetry.MaxStoredSourceChars + 100);

            EvalUsageTelemetry.Record("eval", code, success: true, error: null, executionTimeMs: 1);

            var obj = JObject.Parse(File.ReadAllLines(m_JsonlPath).Single());
            var stored = (string)obj["source"];
            Assert.AreEqual(
                EvalUsageTelemetry.MaxStoredSourceChars + EvalUsageTelemetry.SourceTruncationMarker.Length,
                stored.Length,
                "Stored source is cut at the bound, plus the explicit truncation marker");
            StringAssert.EndsWith(EvalUsageTelemetry.SourceTruncationMarker, stored);
            Assert.AreEqual(code.Length, (int)obj["payloadLength"], "payloadLength still reports the full original length");
        }

        [Test]
        public void RecordInBackground_WritesTheSameRecord_OffTheCallingThread()
        {
            EvalUsageTelemetry.RecordInBackground("eval", "AssetDatabase.Refresh();", success: true, error: null, executionTimeMs: 7);

            Assert.IsTrue(EvalUsageTelemetry.WaitForPendingRecords(), "Background record should flush promptly");

            var record = EvalUsageTelemetry.ReadRecords(m_Dir).Single();
            Assert.AreEqual("eval", record.Command);
            Assert.AreEqual(7, record.ExecutionTimeMs);
            CollectionAssert.Contains(record.Fingerprints, "AssetDatabase.Refresh");
        }

        [Test]
        public void RecordInBackground_UsesConfigSnapshotFromCallTime()
        {
            // StoreSource is captured when the record is queued; a later flip must not affect the
            // in-flight record (the writer works from the snapshot, not the live statics).
            EvalUsageTelemetry.StoreSource = false;
            EvalUsageTelemetry.RecordInBackground("eval", "return SecretApi.Token;", success: true, error: null, executionTimeMs: 1);
            EvalUsageTelemetry.StoreSource = true;

            Assert.IsTrue(EvalUsageTelemetry.WaitForPendingRecords());
            Assert.IsNull(EvalUsageTelemetry.ReadRecords(m_Dir).Single().Source,
                "Raw source must follow the opt-in value captured at record time");
        }

        [Test]
        public void Record_Failure_StoresErrorCategory()
        {
            EvalUsageTelemetry.Record("eval", "return 2 +;", success: false, error: "Compilation Failed", executionTimeMs: 3);

            var obj = JObject.Parse(File.ReadAllLines(m_JsonlPath).Single());
            Assert.IsFalse((bool)obj["success"]);
            Assert.AreEqual("Compilation Failed", (string)obj["error"]);
        }

        [Test]
        public void Record_WhenDisabled_WritesNothing()
        {
            EvalUsageTelemetry.Enabled = false;
            EvalUsageTelemetry.Record("eval", "return 1;", success: true, error: null, executionTimeMs: 1);

            Assert.IsFalse(File.Exists(m_JsonlPath), "Disabled telemetry must not write locally (and never transmits)");
        }

        [Test]
        public void ReadRecords_RoundTripsAppendedRecords_InOrder()
        {
            EvalUsageTelemetry.Record("eval", "A.B();", true, null, 1);
            EvalUsageTelemetry.Record("eval_file", "C.D();", false, "Runtime Error", 2);

            var records = EvalUsageTelemetry.ReadRecords(m_Dir);
            Assert.AreEqual(2, records.Count);
            Assert.AreEqual("eval", records[0].Command);
            Assert.AreEqual("eval_file", records[1].Command);
            CollectionAssert.Contains(records[0].Fingerprints, "A.B");
            CollectionAssert.Contains(records[1].Fingerprints, "C.D");
        }

        [Test]
        public void ReadRecords_SkipsMalformedLines()
        {
            EvalUsageTelemetry.Record("eval", "A.B();", true, null, 1);
            File.AppendAllText(m_JsonlPath, "this is not json\n");
            EvalUsageTelemetry.Record("eval", "C.D();", true, null, 1);

            var records = EvalUsageTelemetry.ReadRecords(m_Dir);
            Assert.AreEqual(2, records.Count, "Malformed lines are skipped; valid records survive");
        }

        [Test]
        public void Append_OverSizeCap_RotatesToOldFile_ThenStartsFresh()
        {
            // Force the cap so a couple of records trip rotation without writing megabytes.
            EvalUsageTelemetry.MaxFileBytes = 1;

            EvalUsageTelemetry.Record("eval", "First.Call();", true, null, 1);
            Assert.IsTrue(File.Exists(m_JsonlPath));

            // Next append sees the file already at/over the cap, rotates it away, then writes fresh.
            EvalUsageTelemetry.Record("eval", "Second.Call();", true, null, 1);

            Assert.IsTrue(File.Exists(m_OldPath), "Previous log should be rotated to eval-usage.old.jsonl");
            Assert.AreEqual(1, File.ReadAllLines(m_JsonlPath).Length, "Active file starts fresh after rotation");
            StringAssert.Contains("Second.Call", File.ReadAllText(m_JsonlPath));
        }

        [Test]
        public void ReadRecords_MergesRotatedHistory_OldestFirst()
        {
            // The report must not lose pre-rotation history: ReadRecords merges the rotated .old file
            // first, then the active file, so readable history is bounded to ~2x the size cap and stays
            // in chronological order.
            EvalUsageTelemetry.Record("eval", "First.Call();", true, null, 1);
            EvalUsageTelemetry.Rotate(m_Dir);
            EvalUsageTelemetry.Record("eval", "Second.Call();", true, null, 1);

            var records = EvalUsageTelemetry.ReadRecords(m_Dir);

            Assert.AreEqual(2, records.Count, "Rotated history merges with the active file");
            CollectionAssert.Contains(records[0].Fingerprints, "First.Call");
            CollectionAssert.Contains(records[1].Fingerprints, "Second.Call");
        }

        [Test]
        public void ReadRecords_MissingDirectory_ReturnsEmpty()
        {
            var missing = Path.Combine(m_Dir, "does-not-exist");

            IReadOnlyList<EvalUsageRecord> records = null;
            Assert.DoesNotThrow(() => records = EvalUsageTelemetry.ReadRecords(missing));
            Assert.IsEmpty(records);
        }

        [Test]
        public void ReadRecords_ActiveFileLockedByExternalWriter_ReturnsWhatParsed()
        {
            // A reader racing an exclusive out-of-process writer (or a rotation TOCTOU) must degrade
            // to "return what parsed" rather than throw. Hold the ACTIVE file with no sharing; the
            // rotated history must still come back.
            EvalUsageTelemetry.Record("eval", "First.Call();", true, null, 1);
            EvalUsageTelemetry.Rotate(m_Dir);
            EvalUsageTelemetry.Record("eval", "Second.Call();", true, null, 1);

            using (new FileStream(m_JsonlPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                IReadOnlyList<EvalUsageRecord> records = null;
                Assert.DoesNotThrow(() => records = EvalUsageTelemetry.ReadRecords(m_Dir));
                // FileShare enforcement varies by platform: where the OS honours it (Windows) the
                // locked active file is skipped (1 record); where it does not, both parse. Either way
                // the contract holds: no throw, and the rotated history is returned.
                Assert.GreaterOrEqual(records.Count, 1);
                CollectionAssert.Contains(records[0].Fingerprints, "First.Call");
            }
        }

        [Test]
        public void Rotate_ReplacesPreviousOldFile()
        {
            EvalUsageTelemetry.Append(m_Dir, new EvalUsageRecord { Command = "eval", Fingerprints = new List<string> { "A.X" } });
            EvalUsageTelemetry.Rotate(m_Dir);

            EvalUsageTelemetry.Append(m_Dir, new EvalUsageRecord { Command = "eval", Fingerprints = new List<string> { "B.Y" } });
            EvalUsageTelemetry.Rotate(m_Dir);

            Assert.IsFalse(File.Exists(m_JsonlPath), "Active file moved away by rotation");
            var line = File.ReadAllLines(m_OldPath).Single();
            Assert.IsTrue(line.Contains("B.Y"), "Old file should hold the most recent pre-rotation content");
        }

        [Test]
        public void Rotate_NoActiveFile_IsNoOp()
        {
            Assert.DoesNotThrow(() => EvalUsageTelemetry.Rotate(m_Dir));
            Assert.IsFalse(File.Exists(m_JsonlPath));
            Assert.IsFalse(File.Exists(m_OldPath));
        }
        [Test]
        public void Record_CountsLoneCarriageReturnLineEndings()
        {
            // Review regression: only \n was counted, so classic-Mac (\r) bodies reported
            // lineCount 1. All three ending styles must agree.
            EvalUsageTelemetry.Record("eval", "a\rb\rc", true, null, 1);
            EvalUsageTelemetry.Record("eval", "a\r\nb\r\nc", true, null, 1);
            EvalUsageTelemetry.Record("eval", "a\nb\nc", true, null, 1);

            var records = EvalUsageTelemetry.ReadRecords(m_Dir);
            var lastThree = new List<int>();
            for (var i = records.Count - 3; i < records.Count; i++)
                lastThree.Add(records[i].LineCount);
            CollectionAssert.AreEqual(new[] { 3, 3, 3 }, lastThree,
                "\r, \r\n and \n bodies of three lines must all report lineCount 3");
        }

        [Test]
        public void RecordInBackground_BackToBackCalls_LandInSubmissionOrder()
        {
            // Review regression: independent Task.Run per record let back-to-back writes land
            // out of order; the chain must preserve submission order (ReadRecords documents the
            // log as chronological).
            for (var i = 1; i <= 10; i++)
                EvalUsageTelemetry.RecordInBackground("eval", new string('a', i), success: true, error: null, executionTimeMs: 1);
            Assert.IsTrue(EvalUsageTelemetry.WaitForPendingRecords(10000), "background records must drain");

            var records = EvalUsageTelemetry.ReadRecords(m_Dir);
            Assert.GreaterOrEqual(records.Count, 10);
            var lastTen = new List<int>();
            for (var i = records.Count - 10; i < records.Count; i++)
                lastTen.Add(records[i].PayloadLength);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, lastTen,
                "writes must land in submission order");
        }

    }
}
