using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Utility methods for Unity Editor testing scenarios.
    /// Provides async/await support for Editor state changes and transitions.
    /// </summary>
    static class EditorTestUtilities
    {
        public static async Task WaitFor(Func<bool> condition, int timeoutMs = 1000)
        {
            var tcs = new TaskCompletionSource<bool>();
            var timeoutCts = new CancellationTokenSource(timeoutMs);
            timeoutCts.Token.Register(() =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.SetResult(false); // Indicate timeout, but don't throw
                }
            });

            while (!condition())
            {
                await Task.Delay(10); // Small delay to avoid busy waiting
            }
        }

        /// <summary>
        /// Manual test for the modal-stall risk the instance descriptor's "info" field warns about
        /// (see <see cref="Models.InstanceDescriptor.Info"/>): a blocking modal dialog can stall the
        /// Editor's main-thread update loop, which stalls <c>Dispatcher.ProcessWorkQueue</c> and
        /// therefore all pipeline command processing until the dialog is dismissed.
        ///
        /// Steps:
        /// 1. Launch the Editor WITHOUT the -automated command-line arg and start the pipeline
        ///    server. Confirm the descriptor's "info" field carries the "not in automated mode..."
        ///    guidance.
        /// 2. Send a command through the pipeline client (e.g. a status/heartbeat request) to
        ///    confirm it currently responds normally.
        /// 3. Invoke this menu item to pop the "Hello / world" dialog. While it is open, send
        ///    another command and confirm it does NOT complete (the dispatcher is stalled).
        /// 4. Dismiss the dialog (Okay or Nope) and confirm the pending command completes
        ///    immediately afterward.
        /// 5. Repeat with the Editor launched WITH -automated: the descriptor's "info" field should
        ///    be absent, but the modal will still stall processing the same way — -automated only
        ///    silences the guidance, it doesn't prevent the stall.
        /// </summary>
        [MenuItem("Window/Pipeline/Tests/Invoke Modal!")]
        public static void InvokeModal()
        {
            EditorUtility.DisplayDialog("Hello", "world", "okay", "nope");
        }
    }
}