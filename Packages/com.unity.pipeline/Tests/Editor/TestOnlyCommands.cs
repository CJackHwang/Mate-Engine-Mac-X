using Unity.Pipeline.Commands;
using UnityEditor;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Test-only command that shows a real modal dialog on demand, for manually validating that a
    /// connected agent recognizes the busy/dialog state instead of retrying blindly (see the
    /// Validation plan in the design doc linked from the companion trunk PR that adds
    /// UnityEditor.EditorDialogEvents — not committed to this repo).
    /// Only discoverable when this test assembly is loaded — never present in a consumer's Editor.
    /// </summary>
    static class TestOnlyCommands
    {
        [CliCommand("trigger_test_dialog", "Show a real modal dialog for manual agent-validation testing", MainThreadRequired = true, Tags = new[] { "test_only" })]
        public static bool TriggerTestDialog(
            [CliArg("title", "Dialog title")] string title = "Pipeline Manual Test",
            [CliArg("message", "Dialog body")] string message = "Triggered by trigger_test_dialog for manual agent-validation testing.")
        {
            return EditorUtility.DisplayDialog(title, message, "OK", "Cancel");
        }
    }
}
