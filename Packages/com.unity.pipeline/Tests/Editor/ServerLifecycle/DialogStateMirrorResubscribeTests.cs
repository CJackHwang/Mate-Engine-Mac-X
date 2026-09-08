#if UNITY_6000_7_OR_NEWER
using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Pipeline.Editor;
using UnityEditor;

namespace Unity.Pipeline.Tests.Editor.ServerLifecycle
{
    /// <summary>
    /// Tests that <see cref="EditorDialogStateMirror"/> resubscribes to
    /// <see cref="EditorDialogEvents"/> after its static subscription is lost (e.g. domain
    /// reload), by manually stripping and rearming the event delegates.
    ///
    /// Marked [Explicit]: raises a "live" dialog-will-show event directly on the actual live
    /// pipeline server (<see cref="PipelineServerStartup.Server"/>), not an isolated test server - the
    /// mirror only targets the live server by design. Conflicts with the live editor server. Run
    /// deliberately.
    /// </summary>
    [Explicit("Raises a dialog on the live pipeline server's dialog gate; conflicts with the live editor server. Run deliberately.")]
    [Category("ServerLifecycle")]
    public class DialogStateMirrorResubscribeTests
    {
        [TearDown]
        public void TearDown()
        {
            Rearm();
        }

        [Test]
        public void MirrorSurvivesLostSubscription_WhenRearmed()
        {
            Strip();

            RaiseDialogWillShow("dead");
            var server = PipelineServerStartup.Server;
            Assume.That(server, Is.Not.Null, "PRECONDITION: the live pipeline server must be running for this test");
            Assert.IsFalse(server.Dialogs.IsDialogOpen,
                "Stripping the subscription should stop the mirror from observing new dialogs (sanity check on the strip step)");

            Rearm();

            var liveId = RaiseDialogWillShow("live");
            try
            {
                Assert.IsTrue(server.Dialogs.IsDialogOpen,
                    "Re-arming the subscription must restore mirroring");
            }
            finally
            {
                // Always dismiss the live dialog, even if the assertion above fails - otherwise
                // the live server's dialog busy-gate stays tripped for the rest of the Editor
                // session (every MainThreadRequired command 503s forever), including poisoning
                // the next run of this same test.
                RaiseDialogDismissed(liveId);
            }
        }

        static void Strip()
        {
            var willShowField = typeof(EditorDialogEvents).GetField("dialogWillShow", BindingFlags.NonPublic | BindingFlags.Static);
            var dismissedField = typeof(EditorDialogEvents).GetField("dialogDismissed", BindingFlags.NonPublic | BindingFlags.Static);
            willShowField.SetValue(null, null);
            dismissedField.SetValue(null, null);
        }

        static void Rearm()
        {
            var ctor = typeof(EditorDialogStateMirror).TypeInitializer;
            ctor.Invoke(null, null);
        }

        static int RaiseDialogWillShow(string title)
        {
            var method = typeof(EditorDialogEvents).GetMethod("Internal_DialogWillShow", BindingFlags.NonPublic | BindingFlags.Static);
            return (int)method.Invoke(null, new object[] { title, title, new[] { "OK" }, (int)DialogIconType.Info });
        }

        static void RaiseDialogDismissed(int id)
        {
            var method = typeof(EditorDialogEvents).GetMethod("Internal_DialogDismissed", BindingFlags.NonPublic | BindingFlags.Static);
            method.Invoke(null, new object[] { id });
        }
    }
}
#endif
