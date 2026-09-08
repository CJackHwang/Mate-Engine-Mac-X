using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Pipeline.HotReload;
using Unity.Pipeline.Runtime.Commands;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Probe for the baseline-aware in-place reload tests: two independent [HotReload] methods so
    /// a reload of one can be asserted to leave the other running compiled.
    /// </summary>
    public class BaselineReloadProbe
    {
        public int first;
        public int second;

        [HotReload]
        public void First()
        {
            first = 1; // original body
        }

        [HotReload]
        public void Second()
        {
            second = 1; // original body
        }
    }

    /// <summary>
    /// Tests for <see cref="HotReloadBaseline"/> — the per-method snapshot that lets a reload skip
    /// methods still matching the compiled code — and for its use by InPlaceReloadProcessor
    /// (skip unchanged, unregister reverted).
    /// </summary>
    class HotReloadBaselineTests
    {
        private const string FakePath = "Assets/FakeBaselineProbe.cs";

        private const string BaselineSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class BaselineReloadProbe
    {
        public int first;
        public int second;

        [HotReload]
        public void First()
        {
            first = 1; // original body
        }

        [HotReload]
        public void Second()
        {
            second = 1; // original body
        }
    }
}";

        [TearDown]
        public void TearDown()
        {
            HotReloadBaseline.Clear();
            HotReloadRegistry.UnregisterMethodOverride("BaselineReloadProbe.First");
            HotReloadRegistry.UnregisterMethodOverride("BaselineReloadProbe.Second");
        }

        // -------- classification --------

        [Test]
        public void NoBaseline_ClassifiesNothing()
        {
            Assert.IsNull(HotReloadBaseline.GetUnchangedMethods(FakePath, BaselineSource),
                "Without a captured baseline every method must be treated as changed.");
            Assert.IsFalse(HotReloadBaseline.IsFileUpToDate(FakePath, BaselineSource));
        }

        [Test]
        public void IdenticalSource_AllMethodsUnchanged()
        {
            Assert.IsTrue(HotReloadBaseline.CaptureFromSource(FakePath, BaselineSource));

            var unchanged = HotReloadBaseline.GetUnchangedMethods(FakePath, BaselineSource);
            Assert.IsNotNull(unchanged);
            CollectionAssert.AreEquivalent(new[] { "First", "Second" }, unchanged);
            Assert.IsTrue(HotReloadBaseline.IsFileUpToDate(FakePath, BaselineSource));
        }

        [Test]
        public void WhitespaceAndCommentEdits_DoNotCountAsChanges()
        {
            HotReloadBaseline.CaptureFromSource(FakePath, BaselineSource);

            var reformatted = BaselineSource
                .Replace("first = 1; // original body", "first =\n                1; /* reflowed, comment changed */")
                .Replace("public void Second()", "public   void   Second()");

            var unchanged = HotReloadBaseline.GetUnchangedMethods(FakePath, reformatted);
            CollectionAssert.AreEquivalent(new[] { "First", "Second" }, unchanged);
            Assert.IsTrue(HotReloadBaseline.IsFileUpToDate(FakePath, reformatted));
        }

        [Test]
        public void BodyEdit_MarksOnlyThatMethodChanged()
        {
            HotReloadBaseline.CaptureFromSource(FakePath, BaselineSource);

            var edited = BaselineSource.Replace("second = 1;", "second = 99;");
            var unchanged = HotReloadBaseline.GetUnchangedMethods(FakePath, edited);

            CollectionAssert.AreEquivalent(new[] { "First" }, unchanged);
            Assert.IsFalse(HotReloadBaseline.IsFileUpToDate(FakePath, edited));
        }

        [Test]
        public void SignatureEdit_MarksMethodChanged()
        {
            HotReloadBaseline.CaptureFromSource(FakePath, BaselineSource);

            // Renaming a parameterless method's parameter list is not possible; add a default arg.
            var edited = BaselineSource.Replace("public void Second()", "public void Second(int times = 1)");
            var unchanged = HotReloadBaseline.GetUnchangedMethods(FakePath, edited);

            // The signature change also changes the class context (the signature is context), so
            // conservatively nothing is skippable.
            CollectionAssert.DoesNotContain(unchanged, "Second");
        }

        [Test]
        public void FieldEdit_ChangesContext_NothingSkippable()
        {
            HotReloadBaseline.CaptureFromSource(FakePath, BaselineSource);

            // An untouched body can read this field; the override compiles against the current
            // file, so a declaration change conservatively invalidates every method.
            var edited = BaselineSource.Replace("public int second;", "public int second = 5;");
            var unchanged = HotReloadBaseline.GetUnchangedMethods(FakePath, edited);

            Assert.IsNotNull(unchanged);
            Assert.AreEqual(0, unchanged.Count);
        }

        [Test]
        public void HelperBodyEdit_DoesNotChangeContext()
        {
            var withHelper = BaselineSource.Replace(
                "public int first;",
                "public int first;\n        private int Helper() { return 3; }\n        private int Arrow => 4;");
            HotReloadBaseline.CaptureFromSource(FakePath, withHelper);

            // Overrides call helpers through the compiled host either way, so a helper body edit
            // can't change what an override does — it must not mark reloadable methods changed.
            var edited = withHelper
                .Replace("return 3;", "return 30;")
                .Replace("Arrow => 4;", "Arrow => 40;");
            var unchanged = HotReloadBaseline.GetUnchangedMethods(FakePath, edited);

            CollectionAssert.AreEquivalent(new[] { "First", "Second" }, unchanged);
        }

        [Test]
        public void MustReloadMethods_BlockUpToDateShortCircuit()
        {
            HotReloadBaseline.CaptureFromSource(FakePath, BaselineSource);

            // A device already running an override for Second can't unregister it remotely — the
            // file must not be reported up to date even though the source matches the baseline.
            var pushed = new HashSet<string> { "Second" };
            Assert.IsFalse(HotReloadBaseline.IsFileUpToDate(FakePath, BaselineSource, null, pushed));
        }

        [Test]
        public void SerializeRestore_RoundTripsClassification()
        {
            HotReloadBaseline.CaptureFromSource(FakePath, BaselineSource);
            var serialized = HotReloadBaseline.Serialize();

            HotReloadBaseline.Clear();
            Assert.IsNull(HotReloadBaseline.GetUnchangedMethods(FakePath, BaselineSource));

            HotReloadBaseline.Restore(serialized);
            var edited = BaselineSource.Replace("second = 1;", "second = 99;");
            CollectionAssert.AreEquivalent(new[] { "First" },
                HotReloadBaseline.GetUnchangedMethods(FakePath, edited));
        }

        // -------- processor integration --------

        [Test]
        public void Reload_SkipsUnchangedMethods_AndUnregistersReverted()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(BaselineReloadProbe).GetMethod(nameof(BaselineReloadProbe.First)),
                new HotReloadWithOverridesAttribute());
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(BaselineReloadProbe).GetMethod(nameof(BaselineReloadProbe.Second)),
                new HotReloadWithOverridesAttribute());

            var path = Path.Combine(Application.temporaryCachePath, "BaselineReloadProbe_edit.cs");
            try
            {
                // Watch start: disk matches the compiled probe.
                HotReloadBaseline.CaptureFromSource(path, BaselineSource);

                // Save 1: untouched file → nothing compiles, nothing registers.
                File.WriteAllText(path, BaselineSource);
                var result = InPlaceReloadProcessor.ProcessSourceFileOnMainThread(path);
                Assert.IsTrue(result.Success, result.ErrorMessage);
                Assert.IsTrue(result.AllUpToDate);
                CollectionAssert.IsEmpty(result.RegisteredMethods);
                CollectionAssert.AreEquivalent(new[] { "First", "Second" }, result.UpToDateMethods);
                Assert.IsFalse(HotReloadRegistry.HasOverride("BaselineReloadProbe.Second"));

                // Save 2: only Second edited → First stays compiled, Second gets an override.
                File.WriteAllText(path, BaselineSource.Replace("second = 1;", "second = 99;"));
                result = InPlaceReloadProcessor.ProcessSourceFileOnMainThread(path);
                Assert.IsTrue(result.Success, result.ErrorMessage);
                CollectionAssert.AreEquivalent(new[] { "BaselineReloadProbe.Second" }, result.RegisteredMethods);
                CollectionAssert.AreEquivalent(new[] { "First" }, result.UpToDateMethods);
                Assert.IsFalse(HotReloadRegistry.HasOverride("BaselineReloadProbe.First"));

                var probe = new BaselineReloadProbe();
                probe.Second();
                Assert.AreEqual(99, probe.second, "Edited body should run via the woven dispatch");
                probe.First();
                Assert.AreEqual(1, probe.first, "Unchanged body should run compiled");

                // Save 3: the edit is undone → the stale override is removed, compiled body runs.
                File.WriteAllText(path, BaselineSource);
                result = InPlaceReloadProcessor.ProcessSourceFileOnMainThread(path);
                Assert.IsTrue(result.Success, result.ErrorMessage);
                Assert.IsTrue(result.AllUpToDate);
                CollectionAssert.AreEquivalent(new[] { "BaselineReloadProbe.Second" }, result.RevertedMethods);
                Assert.IsFalse(HotReloadRegistry.HasOverride("BaselineReloadProbe.Second"));

                var reverted = new BaselineReloadProbe();
                reverted.Second();
                Assert.AreEqual(1, reverted.second, "Reverted method should run the original compiled body");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test] // The reload_file command's contract for the baseline paths: an up-to-date file is
               // an explicit "up to date" success (never "successful with 0 methods"), and stale
               // overrides it reverts are reported, not silently unregistered.
        public void ReloadFileCommand_UpToDate_ReportsUpToDateAndReverted()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(BaselineReloadProbe).GetMethod(nameof(BaselineReloadProbe.First)),
                new HotReloadWithOverridesAttribute());
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(BaselineReloadProbe).GetMethod(nameof(BaselineReloadProbe.Second)),
                new HotReloadWithOverridesAttribute());

            var path = Path.Combine(Application.temporaryCachePath, "BaselineReloadProbe_cmd.cs");
            try
            {
                HotReloadBaseline.CaptureFromSource(path, BaselineSource);

                // Edit Second → a normal reload that registers one override.
                File.WriteAllText(path, BaselineSource.Replace("second = 1;", "second = 99;"));
                var edited = HotReloadCommands.ReloadFile(path);
                Assert.IsTrue(edited.Success, edited.Message);
                StringAssert.Contains("1 methods", edited.Message);

                // Undo the edit → explicit up-to-date success that names the reverted override.
                File.WriteAllText(path, BaselineSource);
                var upToDate = HotReloadCommands.ReloadFile(path);
                Assert.IsTrue(upToDate.Success, upToDate.Message);
                StringAssert.Contains("Up to date", upToDate.Message);
                StringAssert.Contains("reverted 1 stale override(s): BaselineReloadProbe.Second", upToDate.Message);
                StringAssert.DoesNotContain("0 methods", upToDate.Message);
                Assert.IsFalse(HotReloadRegistry.HasOverride("BaselineReloadProbe.Second"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void Reload_WithoutBaseline_ReloadsEverything()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(BaselineReloadProbe).GetMethod(nameof(BaselineReloadProbe.First)),
                new HotReloadWithOverridesAttribute());
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(BaselineReloadProbe).GetMethod(nameof(BaselineReloadProbe.Second)),
                new HotReloadWithOverridesAttribute());

            var path = Path.Combine(Application.temporaryCachePath, "BaselineReloadProbe_nobaseline.cs");
            try
            {
                // No baseline captured (no watch): pre-baseline behavior — every method reloads.
                File.WriteAllText(path, BaselineSource);
                var result = InPlaceReloadProcessor.ProcessSourceFileOnMainThread(path);
                Assert.IsTrue(result.Success, result.ErrorMessage);
                Assert.IsFalse(result.AllUpToDate);
                CollectionAssert.AreEquivalent(
                    new[] { "BaselineReloadProbe.First", "BaselineReloadProbe.Second" },
                    result.RegisteredMethods);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
