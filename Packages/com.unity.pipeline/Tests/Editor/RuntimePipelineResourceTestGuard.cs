using NUnit.Framework;
using Unity.Pipeline.Config;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Shared [SetUp]/[TearDown] helpers for EditMode tests that touch RuntimePipelineConfig's
    /// authored settings file: a stray leftover from an earlier failed test run would silently
    /// corrupt these tests' results (Load() would find it instead of "no config"). Fail loudly
    /// instead, and give every such test one place to clean up after itself.
    ///
    /// CI note: these tests read/write the real project's
    /// ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json — there is no
    /// per-test sandbox or temp-path override. That's safe for a normal CI run (a fresh checkout
    /// has no such file, and AssertNoConfigAssetExists()/DeleteConfigIfExists() fail loudly rather
    /// than silently corrupting results if one is somehow already present) but means these tests
    /// must not run concurrently with anything else that touches the same project's
    /// ProjectSettings/ — including a second parallel test run against the same checkout, or a
    /// live Editor instance with the settings page open.
    /// </summary>
    internal static class RuntimePipelineResourceTestGuard
    {
        public static void AssertNoConfigAssetExists()
        {
            Assert.IsNull(RuntimePipelineConfig.Load(),
                "A RuntimePipelineConfig settings file already exists for this project; these " +
                "tests require a clean slate. Remove it before running.");
        }

        public static void DeleteConfigIfExists()
        {
            RuntimePipelineConfig.DeleteSettingsFileForTesting();
        }
    }
}
