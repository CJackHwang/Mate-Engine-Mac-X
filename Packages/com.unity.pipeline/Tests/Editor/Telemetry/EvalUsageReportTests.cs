using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Telemetry;

namespace Unity.Pipeline.Tests.Editor.Telemetry
{
    /// <summary>
    /// Tests for <see cref="EvalUsageAggregator"/>: ranked fingerprint frequency, one-liner vs
    /// multi-statement percentages, error rate, and the command-coverage cross-reference (AUTHAPI-29).
    /// Records are built in-memory and the available-command set / coverage map are injected, so the
    /// tests are hermetic and independent of the live catalog.
    /// </summary>
    class EvalUsageReportTests
    {
        private static EvalUsageRecord Rec(string classification, bool success, params string[] fingerprints) =>
            new EvalUsageRecord
            {
                Classification = classification,
                Success = success,
                Fingerprints = fingerprints.ToList()
            };

        [Test]
        public void Build_RanksFingerprintsByDescendingCount()
        {
            var records = new List<EvalUsageRecord>
            {
                Rec(EvalClassification.SingleExpression, true, "AssetDatabase.Refresh"),
                Rec(EvalClassification.SingleExpression, true, "AssetDatabase.Refresh"),
                Rec(EvalClassification.SingleExpression, true, "AssetDatabase.Refresh"),
                Rec(EvalClassification.SingleExpression, true, "MatchManager.Instance.State"),
            };

            var report = EvalUsageAggregator.Build(records, new HashSet<string>());

            Assert.AreEqual("AssetDatabase.Refresh", report.Fingerprints[0].Member);
            Assert.AreEqual(3, report.Fingerprints[0].Count);
            Assert.AreEqual("MatchManager.Instance.State", report.Fingerprints[1].Member);
            Assert.AreEqual(1, report.Fingerprints[1].Count);
        }

        [Test]
        public void Build_ComputesOneLinerPercentageAndErrorRate()
        {
            var records = new List<EvalUsageRecord>
            {
                Rec(EvalClassification.SingleExpression, true, "A.B"),
                Rec(EvalClassification.SingleExpression, true, "A.B"),
                Rec(EvalClassification.SingleExpression, false, "A.B"),
                Rec(EvalClassification.Statements, true, "C.D"),
            };

            var report = EvalUsageAggregator.Build(records, new HashSet<string>());

            Assert.AreEqual(4, report.TotalCount);
            Assert.AreEqual(3, report.SingleExpressionCount);
            Assert.AreEqual(1, report.StatementsCount);
            Assert.AreEqual(75.0, report.OneLinerPercentage, 1e-9);
            Assert.AreEqual(1, report.ErrorCount);
            Assert.AreEqual(0.25, report.ErrorRate, 1e-9);
        }

        [Test]
        public void Build_CoverageSuggestions_FlagsFingerprintCoveredByExistingCommand()
        {
            // AUTHAPI-29 AC: AssetDatabase.Refresh maps to a hypothetical refresh_assets; when that
            // command exists in the catalog, the pattern is flagged as already coverable.
            var records = new List<EvalUsageRecord>
            {
                Rec(EvalClassification.SingleExpression, true, "AssetDatabase.Refresh"),
            };
            var catalog = new HashSet<string> { "refresh_assets" };

            var report = EvalUsageAggregator.Build(records, catalog);

            var hit = report.CoverageSuggestions.Covered.SingleOrDefault(c => c.Member == "AssetDatabase.Refresh");
            Assert.IsNotNull(hit, "AssetDatabase.Refresh should be flagged as covered by refresh_assets");
            Assert.AreEqual("refresh_assets", hit.Command);
            Assert.IsFalse(report.CoverageSuggestions.TopUncovered.Any(g => g.Member == "AssetDatabase.Refresh"));
        }

        [Test]
        public void Build_CoverageSuggestions_MappedButAbsentCommand_IsUncoveredWithSuggestion()
        {
            // Same fingerprint, but the mapped command does NOT exist yet -> concrete gap/proposal.
            var records = new List<EvalUsageRecord>
            {
                Rec(EvalClassification.SingleExpression, true, "AssetDatabase.Refresh"),
            };

            var report = EvalUsageAggregator.Build(records, new HashSet<string>());

            var gap = report.CoverageSuggestions.TopUncovered.SingleOrDefault(g => g.Member == "AssetDatabase.Refresh");
            Assert.IsNotNull(gap, "With no covering command, the pattern is an uncovered gap");
            Assert.AreEqual("refresh_assets", gap.SuggestedCommand, "The mapped-but-absent command is surfaced as a proposal");
            Assert.IsEmpty(report.CoverageSuggestions.Covered);
        }

        [Test]
        public void Build_CoverageSuggestions_UnmappedFingerprint_IsUncoveredWithoutSuggestion()
        {
            var records = new List<EvalUsageRecord>
            {
                Rec(EvalClassification.SingleExpression, true, "SomeType.SomeMember"),
            };

            var report = EvalUsageAggregator.Build(records, new HashSet<string>());

            var gap = report.CoverageSuggestions.TopUncovered.SingleOrDefault(g => g.Member == "SomeType.SomeMember");
            Assert.IsNotNull(gap);
            Assert.IsNull(gap.SuggestedCommand, "An unmapped pattern has no proposed command name");
        }

        [Test]
        public void Build_TopUncovered_RespectsLimit()
        {
            var records = new List<EvalUsageRecord>();
            for (var i = 0; i < 10; i++)
                records.Add(Rec(EvalClassification.SingleExpression, true, $"Type{i}.Member"));

            var report = EvalUsageAggregator.Build(records, new HashSet<string>(), topUncoveredLimit: 3);

            Assert.AreEqual(3, report.CoverageSuggestions.TopUncovered.Count);
        }

        [Test]
        public void Build_CapsRankedFingerprints_AndReportsDistinctTotal()
        {
            // An accumulated log can hold thousands of distinct fingerprints; the ranked list is
            // capped and distinctFingerprints preserves the uncapped total so truncation is visible.
            var records = new List<EvalUsageRecord>();
            for (var i = 0; i < 10; i++)
                records.Add(Rec(EvalClassification.SingleExpression, true, $"Type{i}.Member"));
            records.Add(Rec(EvalClassification.SingleExpression, true, "Type0.Member")); // Type0 leads with 2.

            var report = EvalUsageAggregator.Build(records, new HashSet<string>(), fingerprintLimit: 3);

            Assert.AreEqual(3, report.Fingerprints.Count, "Ranked list is capped at fingerprintLimit");
            Assert.AreEqual(10, report.DistinctFingerprints, "Uncapped distinct total stays visible");
            Assert.AreEqual("Type0.Member", report.Fingerprints[0].Member, "Cap keeps the TOP of the ranking");
            Assert.AreEqual(10, report.CoverageSuggestions.TopUncovered.Count,
                "Coverage is computed from the full ranking, not the capped fingerprint list");
        }

        [Test]
        public void Build_NegativeLimits_FallBackToDefaults()
        {
            // Accepted range is 0.. — negatives clamp to the defaults instead of meaning "unlimited"
            // (or worse, emptying the lists).
            var records = new List<EvalUsageRecord>();
            for (var i = 0; i < 5; i++)
                records.Add(Rec(EvalClassification.SingleExpression, true, $"Type{i}.Member"));

            var report = EvalUsageAggregator.Build(
                records, new HashSet<string>(), topUncoveredLimit: -1, fingerprintLimit: -7);

            Assert.AreEqual(5, report.Fingerprints.Count, "5 distinct < default cap of 50, so nothing is cut");
            Assert.AreEqual(5, report.DistinctFingerprints);
            Assert.AreEqual(5, report.CoverageSuggestions.TopUncovered.Count);
        }

        [Test]
        public void Build_EmptyRecords_ProducesZeroedReport()
        {
            var report = EvalUsageAggregator.Build(new List<EvalUsageRecord>(), new HashSet<string>());

            Assert.AreEqual(0, report.TotalCount);
            Assert.AreEqual(0.0, report.ErrorRate);
            Assert.AreEqual(0.0, report.OneLinerPercentage);
            Assert.IsEmpty(report.Fingerprints);
        }
        [Test]
        public void CoverageMap_DoesNotClaimSaveAllCoversAssetDatabaseSaveAssets()
        {
            // Review regression: save_all saves open scenes (EditorSceneManager.SaveOpenScenes +
            // AssetDatabase.Refresh) and never calls AssetDatabase.SaveAssets — steering an agent
            // off that eval to save_all would silently stop persisting dirty non-scene assets.
            Assert.IsTrue(EvalCoverageMap.Default.TryGetValue("AssetDatabase.SaveAssets", out var mapped));
            Assert.AreNotEqual("save_all", mapped, "save_all is not a cover for AssetDatabase.SaveAssets");
            Assert.AreEqual("save_assets", mapped, "SaveAssets maps to a proposed gap command instead");
        }

        [Test]
        public void CoverageMap_HabitEntries_ResolveInTheLiveCatalog()
        {
            // The map's "already covered by command X" claims are only true while X exists: a
            // rename would silently reclassify the fingerprint as an uncovered gap with nothing
            // failing. Pin the habit entries against the live catalog so drift breaks a test.
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());
            var available = new HashSet<string>(CommandRegistry.DiscoverCommands().Select(c => c.Name));
            foreach (var name in new[] { "set_player_settings", "get_player_settings" })
                Assert.IsTrue(available.Contains(name),
                    $"coverage-map habit entry '{name}' no longer exists in the catalog — update EvalCoverageMap");
        }

    }
}
