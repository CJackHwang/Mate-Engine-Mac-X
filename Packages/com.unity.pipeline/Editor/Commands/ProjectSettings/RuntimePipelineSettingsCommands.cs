using System.Collections.Generic;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Config;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Get/set the Pipeline Runtime settings (Project Settings > Pipeline > Runtime), i.e.
    /// RuntimePipelineConfig: whether the runtime Pipeline server is enabled in builds, its port,
    /// timeouts, audit logging, auto-start, and per-frame work item budget. This is the structured
    /// equivalent of what a human does through that Project Settings page — the config has no
    /// scene GameObject or asset to script against directly (see RuntimePipelineConfig.Load/Save).
    /// </summary>
    static class RuntimePipelineSettingsCommands
    {
        const string Group = "runtime_pipeline";

        // Must stay in sync with RuntimePipelineConfig's own enforcement: OnValidate() clamps port
        // to this same range, and requestTimeoutMs/maxWorkItemsPerFrame carry matching [Range]
        // attributes — but neither is enforced on a direct field set like Apply() below performs,
        // so Set() must check them itself. The 7900-7999 "recommended" port range is deliberately
        // NOT enforced here: that's Validate()'s job as a build-time warning, not a hard rejection.
        const int MinPort = 0, MaxPort = 65535;
        const int MinRequestTimeoutMs = 1000, MaxRequestTimeoutMs = 60000;
        const int MinMaxWorkItemsPerFrame = 1, MaxMaxWorkItemsPerFrame = 50;

        [CliCommand("get_runtime_pipeline_settings", "Read Pipeline Runtime settings (enableInBuilds, port, requestTimeoutMs, enableAuditLogging, autoStart, maxWorkItemsPerFrame). Reports the settings in effect, falling back to built-in defaults when none have been authored; never creates the settings file.", MainThreadRequired = true, Tags = new[] { "settings/runtime_pipeline" })]
        public static ProjectSettingsResponse Get() => ProjectSettingsCommand.Get(Group, Read);

        [CliCommand("set_runtime_pipeline_settings", "Change Pipeline Runtime settings. Requires confirm=true; use dry_run to preview. Not undoable via Ctrl+Z. Refused while in Play Mode: the running driver already loaded its own settings, so a change would silently not apply until the next Play session or build. port, requestTimeoutMs, and maxWorkItemsPerFrame are rejected outright if outside the same bounds the Project Settings page enforces.", MainThreadRequired = true, Tags = new[] { "settings/runtime_pipeline" })]
        public static ProjectSettingsResponse Set(
            [CliArg("settings", "Fields to change; omitted fields are left unchanged.")] RuntimePipelineSettingsInput settings = null,
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry_run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return ProjectSettingsResponse.Fail(Group, "No 'settings' object provided.");

            if (EditorApplication.isPlaying)
            {
                return ProjectSettingsResponse.Fail(Group,
                    "Refused: Pipeline Runtime settings are read-only while in Play Mode — the running " +
                    "driver already loaded its own settings, so a change here would silently not apply " +
                    "until the next Play session or build.");
            }

            var violations = new List<string>();
            if (settings.Port.HasValue && (settings.Port.Value < MinPort || settings.Port.Value > MaxPort))
                violations.Add($"port {settings.Port.Value} out of range ({MinPort}..{MaxPort}).");
            if (settings.RequestTimeoutMs.HasValue && (settings.RequestTimeoutMs.Value < MinRequestTimeoutMs || settings.RequestTimeoutMs.Value > MaxRequestTimeoutMs))
                violations.Add($"requestTimeoutMs {settings.RequestTimeoutMs.Value} out of range ({MinRequestTimeoutMs}..{MaxRequestTimeoutMs}).");
            if (settings.MaxWorkItemsPerFrame.HasValue && (settings.MaxWorkItemsPerFrame.Value < MinMaxWorkItemsPerFrame || settings.MaxWorkItemsPerFrame.Value > MaxMaxWorkItemsPerFrame))
                violations.Add($"maxWorkItemsPerFrame {settings.MaxWorkItemsPerFrame.Value} out of range ({MinMaxWorkItemsPerFrame}..{MaxMaxWorkItemsPerFrame}).");
            if (violations.Count > 0)
                return ProjectSettingsResponse.Fail(Group, string.Join("; ", violations));

            var config = RuntimePipelineSettingsProvider.LoadOrCreateConfig();
            try
            {
                var changes = new List<string>();

                if (settings.EnableInBuilds.HasValue && settings.EnableInBuilds.Value != config.enableInBuilds)
                    changes.Add($"enableInBuilds {config.enableInBuilds} -> {settings.EnableInBuilds.Value}");
                if (settings.Port.HasValue && settings.Port.Value != config.port)
                    changes.Add($"port {config.port} -> {settings.Port.Value}");
                if (settings.RequestTimeoutMs.HasValue && settings.RequestTimeoutMs.Value != config.requestTimeoutMs)
                    changes.Add($"requestTimeoutMs {config.requestTimeoutMs} -> {settings.RequestTimeoutMs.Value}");
                if (settings.EnableAuditLogging.HasValue && settings.EnableAuditLogging.Value != config.enableAuditLogging)
                    changes.Add($"enableAuditLogging {config.enableAuditLogging} -> {settings.EnableAuditLogging.Value}");
                if (settings.AutoStart.HasValue && settings.AutoStart.Value != config.autoStart)
                    changes.Add($"autoStart {config.autoStart} -> {settings.AutoStart.Value}");
                if (settings.MaxWorkItemsPerFrame.HasValue && settings.MaxWorkItemsPerFrame.Value != config.maxWorkItemsPerFrame)
                    changes.Add($"maxWorkItemsPerFrame {config.maxWorkItemsPerFrame} -> {settings.MaxWorkItemsPerFrame.Value}");

                if (changes.Count == 0)
                    return NoChanges();

                var planText = "Set Pipeline Runtime settings: " + string.Join("; ", changes);

                void Apply()
                {
                    if (settings.EnableInBuilds.HasValue) config.enableInBuilds = settings.EnableInBuilds.Value;
                    if (settings.Port.HasValue) config.port = settings.Port.Value;
                    if (settings.RequestTimeoutMs.HasValue) config.requestTimeoutMs = settings.RequestTimeoutMs.Value;
                    if (settings.EnableAuditLogging.HasValue) config.enableAuditLogging = settings.EnableAuditLogging.Value;
                    if (settings.AutoStart.HasValue) config.autoStart = settings.AutoStart.Value;
                    if (settings.MaxWorkItemsPerFrame.HasValue) config.maxWorkItemsPerFrame = settings.MaxWorkItemsPerFrame.Value;
                    config.Save();
                }

                return ProjectSettingsCommand.Apply(Group, confirm, dryRun, settings, () => planText, Apply, Read);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        static ProjectSettingsResponse NoChanges()
        {
            var response = ProjectSettingsCommand.Get(Group, Read);
            response.Message = "No changes specified; nothing to apply.";
            return response;
        }

        static Dictionary<string, object> Read()
        {
            var config = RuntimePipelineSettingsProvider.LoadOrCreateConfig();
            try
            {
                return new Dictionary<string, object>
                {
                    ["enableInBuilds"] = config.enableInBuilds,
                    ["port"] = config.port,
                    ["requestTimeoutMs"] = config.requestTimeoutMs,
                    ["enableAuditLogging"] = config.enableAuditLogging,
                    ["autoStart"] = config.autoStart,
                    ["maxWorkItemsPerFrame"] = config.maxWorkItemsPerFrame
                };
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }

    /// <summary>Pipeline Runtime settings to change. Null/omitted fields are left unchanged.</summary>
    class RuntimePipelineSettingsInput : IStructuredCommandInput
    {
        [CliArg("enableInBuilds", "Enable the Pipeline HTTP server in Player builds. SECURITY WARNING: only enable in development/QA builds.")]
        public bool? EnableInBuilds { get; set; }

        [CliArg("port", "HTTP port for the Pipeline server. 0 = auto-assign from range 7900-7999.")]
        public int? Port { get; set; }

        [CliArg("requestTimeoutMs", "Request timeout in milliseconds.")]
        public int? RequestTimeoutMs { get; set; }

        [CliArg("enableAuditLogging", "Log every remote request for security auditing.")]
        public bool? EnableAuditLogging { get; set; }

        [CliArg("autoStart", "Start the server automatically when the Player boots (or Play Mode is entered).")]
        public bool? AutoStart { get; set; }

        [CliArg("maxWorkItemsPerFrame", "Maximum work items the dispatcher processes per frame.")]
        public int? MaxWorkItemsPerFrame { get; set; }
    }
}
