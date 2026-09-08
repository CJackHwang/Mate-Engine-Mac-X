using System;
using System.IO;
using System.Diagnostics;
using Newtonsoft.Json;
using UnityEngine;
using Unity.Pipeline.Security;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Manages instance descriptor files for CLI discovery.
    /// Written to Library/Pipeline/.unity-pipeline-port (under the project's git-ignored Library
    /// folder) for CLI tools to find running Editor instances.
    /// </summary>
    [Serializable]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    class InstanceDescriptor
    {
        private const string DescriptorFileName = ".unity-pipeline-port";

        /// <summary>
        /// Corrective guidance surfaced via <see cref="Info"/> when the Editor is interactive and
        /// wasn't launched with -automated (UUM-149977). Read by every client before it can even
        /// connect (see <see cref="Info"/>), unlike a console warning which was logged once per
        /// server instance and nobody watches anyway.
        /// </summary>
        private const string AutomationInfoMessage =
            "This editor has not been opened in automated mode and therefore could get stuck on modal dialogs. Human intervention may be required.";

        /// <summary>
        /// Process ID of the Unity Editor
        /// </summary>
        [JsonProperty("pid")]
        public int Pid { get; set; }

        /// <summary>
        /// HTTP port the pipeline server is listening on
        /// </summary>
        [JsonProperty("port")]
        public int Port { get; set; }

        /// <summary>
        /// Full path to the Unity project
        /// </summary>
        [JsonProperty("projectPath")]
        public string ProjectPath { get; set; }

        /// <summary>
        /// Name of the Unity project
        /// </summary>
        [JsonProperty("projectName")]
        public string ProjectName { get; set; }

        /// <summary>
        /// Unity Editor version string
        /// </summary>
        [JsonProperty("unityVersion")]
        public string UnityVersion { get; set; }

        /// <summary>
        /// Editor mode: "editor", "batchmode"
        /// </summary>
        [JsonProperty("mode")]
        public string Mode { get; set; }

        /// <summary>
        /// When the Editor instance was started
        /// </summary>
        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// Last heartbeat timestamp
        /// </summary>
        [JsonProperty("lastHeartbeat")]
        public DateTime LastHeartbeat { get; set; }

        /// <summary>
        /// Security token for code evaluation commands
        /// </summary>
        [JsonProperty("evalToken")]
        public string EvalToken { get; set; }

        /// <summary>
        /// Corrective guidance for the client, e.g. warning that this instance could get stuck on
        /// a modal dialog. Null when there is nothing to say (launched with -automated, or
        /// batchmode — which can't show modals regardless, see <see cref="Mode"/>) — omitted from
        /// the JSON entirely, same convention as <see cref="BaseResponse.Warnings"/>. Read this
        /// before issuing commands: it's the one piece of state every client sees, since reading
        /// the descriptor is a mandatory first step just to connect (UUM-149977). There is no
        /// separate "automated" flag: it would only ever disagree with a null/non-null `Info` for
        /// batchmode instances, and that distinction carries no information — batchmode can't show
        /// modal dialogs either way.
        /// </summary>
        [JsonProperty("info", NullValueHandling = NullValueHandling.Ignore)]
        public string Info { get; set; }

        /// <summary>
        /// Optional wire features this server understands (e.g. <c>exec.argv</c>), mirrored from
        /// <see cref="BasePipelineServer.Capabilities"/> so the two carriers cannot drift.
        ///
        /// Carried in the descriptor because every client already reads this file locally to
        /// obtain the eval token, so negotiation costs no extra request. A descriptor with NO
        /// capabilities key at all comes from a server too old to have the field, and a client
        /// must then assume it supports none of these features.
        /// </summary>
        [JsonProperty("capabilities", NullValueHandling = NullValueHandling.Ignore)]
        public string[] Capabilities { get; set; }

        /// <summary>
        /// Create instance descriptor for current Editor session
        /// </summary>
        /// <param name="port">Port the server is listening on.</param>
        /// <param name="automated">Whether the Editor was launched with -automated.</param>
        /// <returns>A populated descriptor for this Editor session.</returns>
        public static InstanceDescriptor CreateCurrent(int port, bool automated)
        {
            try
            {
                var projectPath = Path.GetDirectoryName(Application.dataPath);
                var projectName = Path.GetFileName(projectPath);
                var pid = Process.GetCurrentProcess().Id;
                var unityVersion = Application.unityVersion;
                var isBatchMode = Application.isBatchMode;

                // Generate eval token at server startup for CLI auto-discovery
                var evalToken = SecurityTokenManager.GetOrCreateToken();

                return new InstanceDescriptor
                {
                    Capabilities = BasePipelineServer.Capabilities,
                    Pid = pid,
                    Port = port,
                    ProjectPath = projectPath,
                    ProjectName = projectName,
                    UnityVersion = unityVersion,
                    Mode = isBatchMode ? "batchmode" : "editor",
                    StartedAt = DateTime.UtcNow,
                    LastHeartbeat = DateTime.UtcNow,
                    EvalToken = evalToken,
                    // Only in an interactive Editor: batchmode (CI, upm-pvp, UTR) can't show modal
                    // popups, so the guidance would be pure noise there.
                    Info = (!automated && !isBatchMode) ? AutomationInfoMessage : null
                };
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"CreateCurrent failed: {ex.Message}");
                UnityEngine.Debug.LogError($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Serializes descriptor writes. Heartbeat rewrites can now run concurrently (requests
        /// are processed in parallel since the /api/progress work); a torn concurrent write
        /// would leave clients reading half a descriptor.
        /// </summary>
        private static readonly object m_WriteGate = new object();

        /// <summary>
        /// Write instance descriptor to project root
        /// </summary>
        /// <param name="descriptor">The descriptor to write.</param>
        public static void WriteToProjectRoot(InstanceDescriptor descriptor)
        {
            lock (m_WriteGate)
            {
                WriteToProjectRootLocked(descriptor);
            }
        }

        private static void WriteToProjectRootLocked(InstanceDescriptor descriptor)
        {
            try
            {
                var filePath = GetDescriptorFilePath(descriptor.ProjectPath);
                var isNewFile = !File.Exists(filePath);
                // The descriptor lives under Library/Pipeline, which may not exist yet.
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                var json = JsonConvert.SerializeObject(descriptor, Formatting.Indented);
                File.WriteAllText(filePath, json);

                // The descriptor carries the auth token; keep it readable only by the current user.
                // Applied once on creation (heartbeat rewrites preserve the existing permissions).
                if (isNewFile)
                    FilePermissions.RestrictToCurrentUser(filePath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"WriteToProjectRoot failed: {ex.Message}");
                UnityEngine.Debug.LogError($"Descriptor: {descriptor?.ProjectPath}, PID: {descriptor?.Pid}");
                throw;
            }
        }

        /// <summary>
        /// Read instance descriptor from project root
        /// </summary>
        /// <param name="projectPath">Absolute path to the Unity project.</param>
        /// <returns>The descriptor, or null if missing/corrupted.</returns>
        public static InstanceDescriptor ReadFromProjectRoot(string projectPath)
        {
            var filePath = GetDescriptorFilePath(projectPath);

            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<InstanceDescriptor>(json);
            }
            catch
            {
                // Invalid or corrupted file
                return null;
            }
        }

        /// <summary>
        /// Remove instance descriptor from project root
        /// </summary>
        /// <param name="projectPath">Absolute path to the Unity project.</param>
        public static void RemoveFromProjectRoot(string projectPath)
        {
            var filePath = GetDescriptorFilePath(projectPath);

            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        /// <summary>
        /// Update heartbeat timestamp in existing descriptor file
        /// </summary>
        /// <param name="projectPath">Absolute path to the Unity project.</param>
        public static void UpdateHeartbeat(string projectPath)
        {
            var descriptor = ReadFromProjectRoot(projectPath);
            if (descriptor != null)
            {
                descriptor.LastHeartbeat = DateTime.UtcNow;
                WriteToProjectRoot(descriptor);
            }
        }

        /// <summary>
        /// Absolute path to the editor descriptor file for a project: the git-ignored
        /// Library/Pipeline folder under the project root. Kept in one place so discovery code
        /// and tests can't drift from where this class itself reads/writes.
        /// </summary>
        /// <param name="projectPath">Absolute path to the Unity project.</param>
        /// <returns>The descriptor file's absolute path.</returns>
        public static string GetDescriptorFilePath(string projectPath)
        {
            return Path.Combine(projectPath, "Library", "Pipeline", DescriptorFileName);
        }
    }
}