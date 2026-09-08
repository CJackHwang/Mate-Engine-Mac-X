using Unity.Pipeline.Config;
using UnityEditor;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Draws a status HelpBox (feature description, or a security Warning/Error whenever
    /// enableInBuilds is on) above RuntimePipelineConfig's fields, grouped "Server" / "Runtime
    /// Behavior" via [Header] (enableInBuilds sits ungrouped above both, with its own note — it is
    /// checked both at build time and at runtime, unlike every other field which only matters once
    /// the driver is already running). Every field except enableInBuilds itself is disabled
    /// whenever enableInBuilds is off — there is nothing to configure for a server that will not
    /// run — and, separately, every field except maxWorkItemsPerFrame is also disabled during Play
    /// Mode: RuntimePipelineBootstrap already loaded its own (separate) config instance by the time
    /// this page could be edited, so changes here would silently do nothing until the next Play
    /// session or build — same as a built Player, whose config is frozen at build time.
    /// maxWorkItemsPerFrame is the one exception to the Play Mode rule — RuntimePipelineDriver.Update()
    /// polls it live (see RuntimePipelineConfig.GetLiveMaxWorkItemsPerFrame), so it stays editable
    /// and takes effect immediately, matching the pre-redesign component's behavior for this field;
    /// it is still disabled while enableInBuilds is off, since there is no driver polling it then.
    /// Never reached by clicking an asset in the Project window (the config is never a saved asset)
    /// — hosted directly by RuntimePipelineSettingsProvider's Project Settings page instead, via
    /// Editor.CreateEditor, which works fine on an in-memory instance.
    /// </summary>
    [CustomEditor(typeof(RuntimePipelineConfig))]
    class RuntimePipelineConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Default (150px) clips "Max Work Items Per Frame"; wide enough for every label here.
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 200f;

            var config = (RuntimePipelineConfig)target;
            var status = GetStatusMessage(config.enableInBuilds, EditorUserBuildSettings.development);
            EditorGUILayout.HelpBox(status.Message, status.Type);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableInBuilds"));
                EditorGUILayout.HelpBox(
                    "Checked in two places: at build time it decides whether the Pipeline server is " +
                    "baked into the Player at all; at runtime it decides whether the driver actually " +
                    "starts it — live from this file in the Editor (including Play Mode), but frozen " +
                    "to whatever it was at build time in a Player.",
                    MessageType.None);
            }

            // Nothing else here matters until Pipeline is actually enabled — grey it all out
            // rather than let you tune settings for a server that will not run.
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !config.enableInBuilds))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("port"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("requestTimeoutMs"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableAuditLogging"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("autoStart"));
            }

            // Polled live every frame by RuntimePipelineDriver.Update(), so it stays editable (and
            // useful to tune) even while playing, unlike everything else on this page — but only
            // when there's actually a driver that could be running to tune.
            using (new EditorGUI.DisabledScope(!config.enableInBuilds))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxWorkItemsPerFrame"));
            }

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Read-only while in Play Mode, except Max Work Items Per Frame: the running " +
                    "driver already loaded its own settings, so edits to the other fields would only " +
                    "take effect on the next Play session or build. Max Work Items Per Frame is " +
                    "re-read live every frame, so it can be tuned while playing.",
                    MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        /// <summary>
        /// Core status-message logic, factored out so it's unit-testable without rendering IMGUI.
        /// Always returns a message: an informative description of the feature when there is
        /// nothing to warn about, a Warning when enabled in a development build, or an Error when
        /// enabled without Development Build (about to ship remote code execution in a release build).
        /// </summary>
        public static (string Message, MessageType Type) GetStatusMessage(bool enableInBuilds, bool developmentBuildCurrentlyOn)
        {
            if (!enableInBuilds)
            {
                return ("Runtime Pipeline lets a Player build be remotely controlled over HTTP — " +
                    "hot-reload code, run commands, inspect state — without a domain reload. Enable " +
                    "'Enable In Builds' below to include it in Player builds.",
                    MessageType.Info);
            }

            if (!developmentBuildCurrentlyOn)
            {
                return ("SECURITY RISK: enableInBuilds is on but Development Build is currently off " +
                    "— the next build would ship the Pipeline HTTP server, including remote code " +
                    "execution, in a RELEASE build. Enable Development Build or turn this off before building.",
                    MessageType.Error);
            }

            return ("Pipeline server will be enabled in builds. Only ship this in development/QA " +
                "builds — it exposes code execution and hot reload over a local HTTP server.",
                MessageType.Warning);
        }
    }
}
