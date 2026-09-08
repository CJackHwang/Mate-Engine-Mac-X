using System;
#if UNITY_EDITOR
using System.IO;
#endif
using UnityEngine;

namespace Unity.Pipeline.Config
{
    /// <summary>
    /// Configuration for Pipeline server functionality in Unity Player builds. Authored settings
    /// live in a JSON file under ProjectSettings/ (see <see cref="Save"/>/<see cref="Load"/>) —
    /// never as an asset under Assets/, so nothing about this package appears in the user's
    /// Project window. At real Player build time, PipelineRuntimeBuildProcessor bakes the current
    /// settings into a transient Resources asset (deleted again after the build) so the Player
    /// can find them via Resources.Load.
    /// </summary>
    public class RuntimePipelineConfig : ScriptableObject
    {
        /// <summary>Master switch. The server only starts when true. Off by default for safety.</summary>
        [Tooltip("Enable Pipeline HTTP server in Player builds. SECURITY WARNING: Only enable in development/QA builds, never production without proper security measures.")]
        public bool enableInBuilds = false;

        /// <summary>Listen port. 0 auto-assigns from 7900-7999.</summary>
        [Header("Server")]
        [Tooltip("HTTP port for Pipeline server. Use 0 for auto-assignment from range 7900-7999.")]
        public int port = 0;

        /// <summary>Per-request timeout, in milliseconds.</summary>
        [Tooltip("Request timeout in milliseconds. Higher values allow longer-running commands.")]
        [Range(1000, 60000)]
        public int requestTimeoutMs = 30000;

        /// <summary>Log remote requests for auditing.</summary>
        [Header("Runtime Behavior")]
        [Tooltip("Enable detailed logging of all remote requests for security auditing.")]
        public bool enableAuditLogging = true;

        /// <summary>Start the server automatically when the Player boots (or Play Mode is entered).</summary>
        [Tooltip("Start the server automatically when the Player boots (or Play Mode is entered).")]
        public bool autoStart = true;

        /// <summary>Dispatcher work items processed per frame.</summary>
        [Tooltip("Maximum work items the dispatcher processes per frame to maintain performance.")]
        [Range(1, 50)]
        public int maxWorkItemsPerFrame = 10;

        // Plain int field (not [Range]) so the Inspector shows a text field rather than an
        // unusable 0-65535 slider; the bound is still enforced here, same pattern as
        // EditorPipelineManager.OnValidate's m_WatchdogIntervalSeconds clamp.
        private void OnValidate()
        {
            port = Mathf.Clamp(port, 0, 65535);
        }

        /// <summary>Resource name the build-time-generated transient asset is saved under for <see cref="Load"/> to find in a Player build.</summary>
        public const string ResourceName = "RuntimePipelineConfig";

#if UNITY_EDITOR
        /// <summary>Project-relative path of the authored settings file (never under Assets/).</summary>
        private const string SettingsFilePath = "ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json";
#endif

        /// <summary>
        /// Load the current settings, or null if none exist yet. In the Editor (including Play
        /// Mode) this reads the authored ProjectSettings/ JSON file directly; in a real Player
        /// build it reads the build-time-generated transient Resources asset.
        /// </summary>
        /// <returns>The current settings, or null if none exist yet.</returns>
        public static RuntimePipelineConfig Load()
        {
#if UNITY_EDITOR
            var path = GetAbsoluteSettingsPath();
            if (!File.Exists(path))
                return null;

            var config = CreateInstance<RuntimePipelineConfig>();
            try
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), config);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: could not read runtime settings at '{path}' ({ex.Message}); using no config. Delete the file to reset.");
                DestroyImmediate(config);
                return null;
            }
            return config;
#else
            return Resources.Load<RuntimePipelineConfig>(ResourceName);
#endif
        }

#if UNITY_EDITOR
        private static int s_LiveMaxWorkItemsPerFrameCache = 10;
        private static DateTime s_LiveMaxWorkItemsPerFrameCacheFileTime;

        /// <summary>
        /// Cheap, per-frame-callable read of just maxWorkItemsPerFrame from the authored settings
        /// file, for RuntimePipelineDriver.Update() to poll without paying Load()'s full
        /// CreateInstance+JSON-parse cost every frame. Re-parses only when the file's last-write
        /// time changes, so a Project Settings edit (including while in Play Mode) takes effect on
        /// the very next frame, while an idle frame costs only a single file-timestamp check.
        /// Editor-only: a Player's config is frozen at build time, so there is nothing to poll for.
        /// </summary>
        public static int GetLiveMaxWorkItemsPerFrame(int fallback)
        {
            var path = GetAbsoluteSettingsPath();
            if (!File.Exists(path))
                return fallback;

            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime != s_LiveMaxWorkItemsPerFrameCacheFileTime)
            {
                var config = Load();
                if (config != null)
                {
                    s_LiveMaxWorkItemsPerFrameCache = config.maxWorkItemsPerFrame;
                    DestroyImmediate(config);
                }
                s_LiveMaxWorkItemsPerFrameCacheFileTime = writeTime;
            }

            return s_LiveMaxWorkItemsPerFrameCache;
        }

        /// <summary>Persist this instance's current field values to the ProjectSettings/ JSON file.</summary>
        public void Save()
        {
            var path = GetAbsoluteSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
        }

        /// <summary>
        /// Last-write-time of the authored settings file (UTC), or DateTime.MinValue if it doesn't
        /// exist yet. Lets a long-lived caller (the Project Settings page) detect that the file
        /// changed underneath it — e.g. a build's "Disable Pipeline" security-dialog choice, or a
        /// set_runtime_pipeline_settings CLI call — without reloading from disk on every frame.
        /// </summary>
        public static DateTime GetSettingsFileWriteTimeUtc()
        {
            var path = GetAbsoluteSettingsPath();
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }

        /// <summary>Delete the authored settings file, if any. For test cleanup only.</summary>
        public static void DeleteSettingsFileForTesting()
        {
            var path = GetAbsoluteSettingsPath();
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string GetAbsoluteSettingsPath()
        {
            // Application.dataPath is ".../Assets"; ProjectSettings is a sibling directory.
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, SettingsFilePath);
        }
#endif

        /// <summary>
        /// Validate the configuration for correctness.
        /// Called by build processor to ensure safe deployment.
        /// </summary>
        /// <returns>The validation result.</returns>
        public ValidationResult Validate()
        {
            if (!enableInBuilds)
                return ValidationResult.Success("Runtime Pipeline disabled");

            // Port validation
            if (port != 0 && (port < 7900 || port > 7999))
            {
                return ValidationResult.Warning(
                    $"Port {port} is outside recommended runtime range 7900-7999. May conflict with Editor instances.");
            }

            return ValidationResult.Success("Configuration is valid");
        }
    }

    /// <summary>
    /// Result from configuration validation.
    /// </summary>
    [Serializable]
    public class ValidationResult
    {
        /// <summary>False only for <see cref="Error"/> results.</summary>
        public bool IsValid { get; set; }
        /// <summary>"success", "warning", or "error".</summary>
        public string Level { get; set; } // "success", "warning", "error"
        /// <summary>Human-readable validation message.</summary>
        public string Message { get; set; }

        /// <summary>Create a successful validation result.</summary>
        /// <param name="message">Human-readable message.</param>
        /// <returns>A valid, "success"-level result.</returns>
        public static ValidationResult Success(string message = "Valid") =>
            new ValidationResult { IsValid = true, Level = "success", Message = message };

        /// <summary>Create a valid-but-noteworthy validation result.</summary>
        /// <param name="message">Human-readable message.</param>
        /// <returns>A valid, "warning"-level result.</returns>
        public static ValidationResult Warning(string message) =>
            new ValidationResult { IsValid = true, Level = "warning", Message = message };

        /// <summary>Create a failed validation result.</summary>
        /// <param name="message">Human-readable message.</param>
        /// <returns>An invalid, "error"-level result.</returns>
        public static ValidationResult Error(string message) =>
            new ValidationResult { IsValid = false, Level = "error", Message = message };
    }
}
