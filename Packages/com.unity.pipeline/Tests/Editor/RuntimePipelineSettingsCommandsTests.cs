using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Pipeline.Config;
using Unity.Pipeline.Editor.Commands.ProjectSettings;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for get_runtime_pipeline_settings/set_runtime_pipeline_settings, the structured
    /// command equivalent of the Project Settings > Pipeline > Runtime page. Play Mode refusal
    /// is not covered here — forcing a real Play Mode transition is out of scope for an EditMode
    /// unit test; the guard itself is a one-line check mirroring the Editor's own DisabledScope.
    /// </summary>
    class RuntimePipelineSettingsCommandsTests
    {
        [SetUp]
        public void SetUp()
        {
            RuntimePipelineResourceTestGuard.AssertNoConfigAssetExists();
        }

        [TearDown]
        public void TearDown()
        {
            RuntimePipelineResourceTestGuard.DeleteConfigIfExists();
        }

        [Test]
        public void Get_NoConfigYet_ReturnsDefaultsWithoutAuthoringFile()
        {
            var response = RuntimePipelineSettingsCommands.Get();

            Assert.IsTrue(response.Success);
            Assert.AreEqual("runtime_pipeline", response.Group);
            Assert.AreEqual(false, response.Values["enableInBuilds"]);
            Assert.AreEqual(0, response.Values["port"]);
            Assert.AreEqual(30000, response.Values["requestTimeoutMs"]);
            Assert.AreEqual(true, response.Values["enableAuditLogging"]);
            Assert.AreEqual(true, response.Values["autoStart"]);
            Assert.AreEqual(10, response.Values["maxWorkItemsPerFrame"]);
            Assert.IsNull(RuntimePipelineConfig.Load(),
                "Reading must not author the settings file — its existence is the 'configured' marker migration keys off");
        }

        [Test]
        public void Set_NullSettings_Fails()
        {
            var response = RuntimePipelineSettingsCommands.Set(settings: null, confirm: true);

            Assert.IsFalse(response.Success);
        }

        [Test]
        public void Set_NoChanges_ReturnsMessageWithoutApplying()
        {
            var response = RuntimePipelineSettingsCommands.Set(new RuntimePipelineSettingsInput(), confirm: true);

            Assert.IsTrue(response.Success);
            Assert.IsFalse(response.Applied);
            StringAssert.Contains("No changes specified", response.Message);
            Assert.IsNull(RuntimePipelineConfig.Load(), "A no-op set must not author the settings file");
        }

        // Neither a refused set (no confirm/dry_run) nor a dry-run preview may persist anything —
        // including the settings file itself. Before the fix, LoadOrCreateConfig() ran before the
        // confirm/dry-run gate and saved a default file even on these non-applying paths.
        [TestCase(false, false, TestName = "Set_WithoutConfirmOrDryRun_RefusesAndAuthorsNothing")]
        [TestCase(true, true, TestName = "Set_DryRun_PreviewsAndAuthorsNothing")]
        public void Set_NotApplied_AuthorsNothing(bool dryRun, bool expectedSuccess)
        {
            var response = RuntimePipelineSettingsCommands.Set(new RuntimePipelineSettingsInput { Port = 7950 }, dryRun: dryRun);

            Assert.AreEqual(expectedSuccess, response.Success);
            Assert.IsFalse(response.Applied);
            Assert.IsNull(RuntimePipelineConfig.Load(), "Neither a refused nor a dry-run set may author the settings file");
        }

        [Test]
        public void Set_ConfirmedWithValuesEqualToDefaults_AuthorsNothing()
        {
            // Port's default is already 0, so this is a confirmed set that changes nothing. Accepted
            // as a no-op by design: asking for the value you already have is indistinguishable from
            // NoChanges(), and special-casing "confirmed but no-op" to force-persist would make Set
            // behave differently depending on whether a file already exists.
            var response = RuntimePipelineSettingsCommands.Set(new RuntimePipelineSettingsInput { Port = 0 }, confirm: true);

            Assert.IsFalse(response.Applied);
            Assert.IsNull(RuntimePipelineConfig.Load());
        }

        // Each field's out-of-range value must be rejected outright (not clamped), matching the
        // same bounds RuntimePipelineConfig's Inspector enforces (OnValidate's port clamp,
        // requestTimeoutMs/maxWorkItemsPerFrame's [Range] attributes) — none of which run on a
        // direct field set like Apply() performs. maxWorkItemsPerFrame=0 in particular used to
        // apply successfully and then permanently starve Dispatcher.ProcessWorkQueue's main-thread
        // work loop.
        private static readonly TestCaseData[] OutOfRangeSettings =
        {
            new TestCaseData(new RuntimePipelineSettingsInput { Port = -1 }).SetName("Set_PortBelowRange_RejectedAndNotApplied"),
            new TestCaseData(new RuntimePipelineSettingsInput { Port = 65536 }).SetName("Set_PortAboveRange_RejectedAndNotApplied"),
            new TestCaseData(new RuntimePipelineSettingsInput { RequestTimeoutMs = 999 }).SetName("Set_RequestTimeoutMsBelowRange_RejectedAndNotApplied"),
            new TestCaseData(new RuntimePipelineSettingsInput { RequestTimeoutMs = 60001 }).SetName("Set_RequestTimeoutMsAboveRange_RejectedAndNotApplied"),
            new TestCaseData(new RuntimePipelineSettingsInput { MaxWorkItemsPerFrame = 0 }).SetName("Set_MaxWorkItemsPerFrameBelowRange_RejectedAndNotApplied"),
            new TestCaseData(new RuntimePipelineSettingsInput { MaxWorkItemsPerFrame = 51 }).SetName("Set_MaxWorkItemsPerFrameAboveRange_RejectedAndNotApplied"),
        };

        [TestCaseSource(nameof(OutOfRangeSettings))]
        public void Set_OutOfRangeValue_RejectedAndNotApplied(RuntimePipelineSettingsInput settings)
        {
            var response = RuntimePipelineSettingsCommands.Set(settings, confirm: true);

            Assert.IsFalse(response.Success);
            Assert.IsFalse(response.Applied);
            Assert.IsNull(RuntimePipelineConfig.Load(), "A rejected set must not persist anything");
        }

        [Test]
        public void Set_Confirmed_AppliesAndPersists()
        {
            var response = RuntimePipelineSettingsCommands.Set(
                new RuntimePipelineSettingsInput { EnableInBuilds = true, Port = 7950, MaxWorkItemsPerFrame = 20 },
                confirm: true);

            Assert.IsTrue(response.Success);
            Assert.IsTrue(response.Applied);
            Assert.AreEqual(true, response.Values["enableInBuilds"]);
            Assert.AreEqual(7950, response.Values["port"]);
            Assert.AreEqual(20, response.Values["maxWorkItemsPerFrame"]);

            var persisted = RuntimePipelineConfig.Load();
            Assert.IsNotNull(persisted);
            Assert.IsTrue(persisted.enableInBuilds);
            Assert.AreEqual(7950, persisted.port);
            Assert.AreEqual(20, persisted.maxWorkItemsPerFrame);
        }

        [Test]
        public void Set_OmittedFields_LeftUnchanged()
        {
            RuntimePipelineSettingsCommands.Set(new RuntimePipelineSettingsInput { RequestTimeoutMs = 45000 }, confirm: true);

            var response = RuntimePipelineSettingsCommands.Set(new RuntimePipelineSettingsInput { Port = 7910 }, confirm: true);

            Assert.AreEqual(45000, response.Values["requestTimeoutMs"], "A field omitted from this call must keep its prior value");
            Assert.AreEqual(7910, response.Values["port"]);
        }

        /// <summary>
        /// Guards against schema drift: RuntimePipelineSettingsInput's properties and Read()'s
        /// dictionary keys are hand-maintained mirrors of RuntimePipelineConfig's fields, with
        /// nothing enforcing they stay in sync. Add/rename/remove a config field without updating
        /// both, and get_/set_runtime_pipeline_settings would silently keep the old schema — these
        /// fail loudly instead.
        /// </summary>
        [Test]
        public void SettingsInput_HasOnePropertyPerConfigField()
        {
            var configFieldNames = typeof(RuntimePipelineConfig)
                .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(f => f.Name)
                .ToArray();

            var expectedPropertyNames = configFieldNames
                .Select(name => char.ToUpperInvariant(name[0]) + name.Substring(1))
                .ToArray();

            var inputPropertyNames = typeof(RuntimePipelineSettingsInput)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(p => p.Name)
                .ToArray();

            CollectionAssert.AreEquivalent(expectedPropertyNames, inputPropertyNames,
                "RuntimePipelineSettingsInput must expose exactly one nullable property per " +
                "RuntimePipelineConfig field (PascalCase of the field name), or " +
                "set_runtime_pipeline_settings will silently ignore new fields. Update " +
                "RuntimePipelineSettingsCommands.cs (RuntimePipelineSettingsInput, Read(), Apply()).");
        }

        [Test]
        public void Get_ValuesHaveOneEntryPerConfigField()
        {
            var configFieldNames = typeof(RuntimePipelineConfig)
                .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(f => f.Name)
                .ToArray();

            var response = RuntimePipelineSettingsCommands.Get();

            CollectionAssert.AreEquivalent(configFieldNames, response.Values.Keys,
                "get_runtime_pipeline_settings must report exactly one value per " +
                "RuntimePipelineConfig field, or Read() in RuntimePipelineSettingsCommands.cs has " +
                "fallen out of sync with the config schema.");
        }

        /// <summary>
        /// Guards against drift between Set()'s hard-coded validation bounds and
        /// RuntimePipelineConfig's own [Range] attributes (the ones the Inspector actually
        /// enforces): if either changes without the other, this fails loudly instead of the two
        /// silently disagreeing about what's a valid value.
        /// </summary>
        [TestCase("MinRequestTimeoutMs", "MaxRequestTimeoutMs", nameof(RuntimePipelineConfig.requestTimeoutMs))]
        [TestCase("MinMaxWorkItemsPerFrame", "MaxMaxWorkItemsPerFrame", nameof(RuntimePipelineConfig.maxWorkItemsPerFrame))]
        public void ValidationBounds_MatchRuntimePipelineConfigRangeAttributes(string minConstName, string maxConstName, string configFieldName)
        {
            var min = GetPrivateConst(minConstName);
            var max = GetPrivateConst(maxConstName);

            var field = typeof(RuntimePipelineConfig).GetField(configFieldName, BindingFlags.Public | BindingFlags.Instance);
            var range = (UnityEngine.RangeAttribute)Attribute.GetCustomAttribute(field, typeof(UnityEngine.RangeAttribute));

            Assert.IsNotNull(range, $"{configFieldName} must carry a [Range] attribute for this drift guard to check against");
            Assert.AreEqual((int)range.min, min, $"RuntimePipelineSettingsCommands.{minConstName} has drifted from {configFieldName}'s [Range] attribute");
            Assert.AreEqual((int)range.max, max, $"RuntimePipelineSettingsCommands.{maxConstName} has drifted from {configFieldName}'s [Range] attribute");
        }

        private static int GetPrivateConst(string name)
        {
            var field = typeof(RuntimePipelineSettingsCommands).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"Expected a private const named '{name}' on RuntimePipelineSettingsCommands");
            return (int)field.GetValue(null);
        }
    }
}
