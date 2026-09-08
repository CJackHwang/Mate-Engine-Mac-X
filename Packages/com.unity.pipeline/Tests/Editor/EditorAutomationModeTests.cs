using NUnit.Framework;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Models;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for the "-automated" detection (UUM-149977) and the corrective guidance it drives on
    /// the instance descriptor's <see cref="InstanceDescriptor.Info"/> field.
    /// </summary>
    class EditorAutomationModeTests
    {
        private static readonly object[] DetectAutomatedModeCases =
        {
            new object[] { new string[0], false },
            new object[] { new[] { "-batchmode" }, false },
            new object[] { new[] { "-automated" }, true },
            new object[] { new[] { "-batchmode", "-automated", "-quit" }, true },
            // Exact token match only — a value-bearing arg must not false-positive.
            new object[] { new[] { "-projectPath", "-automated-ish" }, false }
        };

        [TestCaseSource(nameof(DetectAutomatedModeCases))]
        public void DetectAutomatedMode_MatchesExactFlag(string[] args, bool expected)
        {
            Assert.AreEqual(expected, EditorPipelineServer.DetectAutomatedMode(args));
        }

        [Test]
        public void CreateCurrent_Automated_HasNoInfo()
        {
            var descriptor = InstanceDescriptor.CreateCurrent(port: 7800, automated: true);

            Assert.IsNull(descriptor.Info, "An automated instance has nothing to warn about");
        }

        [Test]
        public void CreateCurrent_NotAutomated_InteractiveEditor_HasInfo()
        {
            // Info only applies to an interactive Editor (batchmode can't show modals regardless —
            // see InstanceDescriptor.CreateCurrent). CI runs this suite via UTR in -batchmode, so
            // self-ignore there rather than assert on the wrong branch of that condition.
            if (Application.isBatchMode)
            {
                Assert.Ignore("Info is only set for an interactive Editor (skipped in batchmode).");
                return;
            }

            var descriptor = InstanceDescriptor.CreateCurrent(port: 7800, automated: false);

            Assert.IsNotNull(descriptor.Info, "A non-automated, interactive instance should carry corrective guidance");
            StringAssert.Contains("modal dialogs", descriptor.Info);
        }
    }
}
