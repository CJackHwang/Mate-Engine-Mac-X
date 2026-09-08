using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Telemetry;
using UnityEditor;
using UnityEngine;
using UnityEngine.Analytics;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Reports pipeline usage to Unity's editor analytics: one session-start event on the first
    /// command a client executes, one event per command executed after that, and one session-stop
    /// event when the Editor quits.
    ///
    /// Everything here is main-thread only — SendAnalytic, SessionState and the editor state the
    /// events describe all require it. Commands finish on the background HTTP thread, so
    /// <see cref="EditorPipelineServer"/> posts each one to the server dispatcher, which runs it
    /// here on the next main-thread pump.
    ///
    /// A session spans a Unity PROCESS, not a domain: the latch lives in SessionState, which
    /// survives domain reloads and dies with the editor, so a recompile does not start a new
    /// session.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class PipelineAnalytics
    {
        private const string VendorKey = "unity.pipeline";

        private const string SessionStartedEventName = "Pipeline_SessionStarted";
        private const string CommandExecutedEventName = "Pipeline_CommandExecuted";
        private const string SessionStoppedEventName = "Pipeline_SessionStopped";

        // A session emits at most one start and one stop; commands are the volume, and an
        // agent-driven session issues them continuously.
        private const int SessionEventsPerHour = 10;
        private const int CommandEventsPerHour = 1000;
        private const int MaxElements = 1000;

        private const string SessionStartedKey = "Unity.Pipeline.Analytics.SessionStarted";
        private const string SessionStartTicksKey = "Unity.Pipeline.Analytics.SessionStartTicks";

        /// <summary>
        /// Assemblies whose commands ship with the package. Anything else carrying [CliCommand] was
        /// written by the project, which is what isUserDefinedCommand reports.
        /// </summary>
        private static readonly HashSet<string> PackageAssemblies =
            new HashSet<string> { "Unity.Pipeline", "Unity.Pipeline.Editor" };

        /// <summary>
        /// The tag every code-evaluation command carries (eval, eval_file, run_script). Matching the
        /// tag rather than a name list covers a new eval command the moment it is tagged, and the
        /// suite already enforces that shipped commands carry well-formed tags.
        /// </summary>
        private const string EvalTag = "scripts/eval";

        /// <summary>
        /// Reported in place of a project-declared command's real name. A command name is text the
        /// project's own authors wrote, so it can name an unannounced feature or an internal system;
        /// none of that belongs in an event leaving the machine. Its tags are withheld for the same
        /// reason — nothing constrains a project to the documented taxonomy, so a tag can carry a
        /// project-chosen word exactly as a name can. Counting how much traffic is project-declared
        /// is the useful signal, and isUserDefinedCommand already carries it.
        /// </summary>
        private const string RedactedCommandName = "<customUserCommand>";

        /// <summary>
        /// The send call, indirected so tests can capture events instead of shipping them.
        /// </summary>
        internal static Action<IAnalytic> s_Send = analytic => EditorAnalytics.SendAnalytic(analytic);

        /// <summary>
        /// The session latch as stored in SessionState: whether the start event has been sent this
        /// Unity process, and the UTC tick count it was sent at. Exposed so a fixture driving the
        /// reporter can save and restore the live latch.
        /// </summary>
        internal static (bool started, long startTicks) SessionLatch
        {
            get
            {
                var started = SessionState.GetBool(SessionStartedKey, false);
                long.TryParse(SessionState.GetString(SessionStartTicksKey, "0"), out var ticks);
                return (started, ticks);
            }
            set
            {
                SessionState.SetBool(SessionStartedKey, value.started);
                SessionState.SetString(SessionStartTicksKey, value.startTicks.ToString());
            }
        }

        /// <summary>
        /// Report one executed command, opening the session first if this is the first one. An
        /// info with no command is a request that was rejected before executing anything, which is
        /// not a command execution and is skipped. Main thread.
        /// </summary>
        internal static void RecordCommandExecuted(in CommandExecutionInfo info)
        {
            if (info.Command == null)
                return;

            try
            {
                EnsureSessionStarted();

                var command = info.Command;
                var userDefined = IsUserDefinedCommand(command.Package);
                s_Send(new CommandExecutedAnalytic(new CommandExecutedData
                {
                    // Only a command that shipped with the package describes itself; a
                    // project-declared one reports the placeholder and no tags.
                    commandName = userDefined ? RedactedCommandName : command.Name,
                    commandTags = userDefined ? Array.Empty<string>() : ToArray(command.Tags),
                    commandSuccess = info.Success,
                    commandDuration = info.DurationMs,
                    isUserDefinedCommand = userDefined,
                    isEval = IsEvalCommand(command.Tags),
                    isEvalWithExistingCommandAvailable = EvalCoversAnExistingCommand(command, info.Parameters),
                }));
            }
            catch (Exception ex)
            {
                // Reporting must never break request handling.
                Debug.LogWarning($"Pipeline analytics failed to report a command: {ex.Message}");
            }
        }

        /// <summary>
        /// Send the session-stop event if this process ever opened a session, and close the latch so
        /// a second call is a no-op. Main thread.
        /// </summary>
        internal static void SendSessionStoppedIfStarted()
        {
            try
            {
                var (started, startTicks) = SessionLatch;
                if (!started)
                    return;

                SessionLatch = (false, 0);

                var elapsedMs = startTicks > 0
                    ? Math.Max(0, (DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond)
                    : 0;

                s_Send(new SessionStoppedAnalytic(new SessionStoppedData
                {
                    sessionDuration = elapsedMs,
                }));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline analytics failed to report the session end: {ex.Message}");
            }
        }

        /// <summary>
        /// Send the session-start event once per Unity process, describing how this editor and its
        /// server were launched. The latch is set before sending, so a throwing send cannot make a
        /// later command try again.
        /// </summary>
        private static void EnsureSessionStarted()
        {
            if (SessionLatch.started)
                return;

            SessionLatch = (true, DateTime.UtcNow.Ticks);

            var server = PipelineServerStartup.Server;
            s_Send(new SessionStartedAnalytic(new SessionStartedData
            {
                batchmode = Application.isBatchMode,
                automated = EditorPipelineServer.DetectAutomatedMode(Environment.GetCommandLineArgs()),
                // Both fall back to what the server starts with when no settings asset is authored:
                // browser clients refused, port auto-assigned from the range.
                browserAllowed = server?.AllowSandboxedBrowserClients ?? false,
                dynamicPortAssignation = server?.PortAutoAssigned ?? true,
                hotReloadWatcherEnabled = EditorHotReloadWatcher.IsWatching,
            }));
        }

        /// <summary>
        /// Whether a command came from the project rather than the package, judged by its declaring
        /// assembly. An unresolved assembly is not evidence of a project command, so it counts as
        /// shipped.
        /// </summary>
        internal static bool IsUserDefinedCommand(string package)
        {
            return !string.IsNullOrEmpty(package) && !PackageAssemblies.Contains(package);
        }

        /// <summary>
        /// Whether the snippet this eval ran touches an API that an existing command already covers
        /// — the "you did not need eval for this" signal.
        ///
        /// Same judgement the report_evals command makes, on one invocation instead of the whole log:
        /// fingerprint the body, look each fingerprint up in the curated
        /// <see cref="EvalCoverageMap"/>, and count it only if the command it names is actually in the
        /// live catalog. The map is a hint layer, so a false here means "no mapped-and-present
        /// command", not "no command could ever cover this".
        ///
        /// Answerable only where the body is reachable from the parameters: <c>eval</c> passes it
        /// inline and <c>eval_file</c> names a file to read. <c>run_script</c> runs a project file
        /// with a named entry point rather than a snippet, and the fingerprinter parses script-kind
        /// bodies, so it is not fingerprinted and reports false.
        /// </summary>
        internal static bool EvalCoversAnExistingCommand(CommandInfo command, JObject parameters)
        {
            if (parameters == null || !IsEvalCommand(command.Tags))
                return false;

            var code = ReadEvalBody(command.Name, parameters);
            if (string.IsNullOrWhiteSpace(code))
                return false;

            var fingerprints = EvalUsageFingerprinter.Analyze(code).Fingerprints;
            if (fingerprints == null || fingerprints.Count == 0)
                return false;

            var available = new HashSet<string>(CommandRegistry.DiscoverCommands().Select(c => c.Name));
            foreach (var fingerprint in fingerprints)
            {
                if (EvalCoverageMap.Default.TryGetValue(fingerprint, out var mapped) && available.Contains(mapped))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The evaluated body, from the parameters the command was invoked with. Returns null for a
        /// command whose body is not reachable that way.
        /// </summary>
        private static string ReadEvalBody(string commandName, JObject parameters)
        {
            switch (commandName)
            {
                case "eval":
                    return parameters["code"]?.ToString();

                case "eval_file":
                    // The command read this file moments ago; re-reading it costs one small read and
                    // keeps eval_file from reporting a permanent false that would be indistinguishable
                    // from a genuine "nothing covers this".
                    var file = parameters["file"]?.ToString();
                    if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file))
                        return null;
                    try
                    {
                        return System.IO.File.ReadAllText(file);
                    }
                    catch
                    {
                        return null;
                    }

                default:
                    return null;
            }
        }

        internal static bool IsEvalCommand(IReadOnlyList<string> tags)
        {
            for (var i = 0; i < tags.Count; i++)
            {
                if (tags[i] == EvalTag)
                    return true;
            }

            return false;
        }

        private static string[] ToArray(IReadOnlyList<string> tags)
        {
            var array = new string[tags.Count];
            for (var i = 0; i < tags.Count; i++)
                array[i] = tags[i];
            return array;
        }

        // The payload field names below ARE the registered event schema. Renaming one silently stops
        // that column being populated, so they keep the schema casing rather than C# casing.

        [Serializable]
        internal struct SessionStartedData : IAnalytic.IData
        {
            public bool batchmode;
            public bool automated;
            public bool browserAllowed;
            public bool dynamicPortAssignation;
            public bool hotReloadWatcherEnabled;
        }

        [Serializable]
        internal struct CommandExecutedData : IAnalytic.IData
        {
            public string commandName;
            public string[] commandTags;
            public bool commandSuccess;
            public long commandDuration;
            public bool isUserDefinedCommand;
            public bool isEval;
            public bool isEvalWithExistingCommandAvailable;
        }

        [Serializable]
        internal struct SessionStoppedData : IAnalytic.IData
        {
            public long sessionDuration;
        }

        [AnalyticInfo(eventName: SessionStartedEventName, vendorKey: VendorKey,
            maxEventsPerHour: SessionEventsPerHour, maxNumberOfElements: MaxElements)]
        internal class SessionStartedAnalytic : IAnalytic
        {
            private readonly SessionStartedData m_Data;

            public SessionStartedAnalytic(SessionStartedData data) => m_Data = data;

            public bool TryGatherData(out IAnalytic.IData data, out Exception error)
            {
                data = m_Data;
                error = null;
                return true;
            }
        }

        [AnalyticInfo(eventName: CommandExecutedEventName, vendorKey: VendorKey,
            maxEventsPerHour: CommandEventsPerHour, maxNumberOfElements: MaxElements)]
        internal class CommandExecutedAnalytic : IAnalytic
        {
            private readonly CommandExecutedData m_Data;

            public CommandExecutedAnalytic(CommandExecutedData data) => m_Data = data;

            public bool TryGatherData(out IAnalytic.IData data, out Exception error)
            {
                data = m_Data;
                error = null;
                return true;
            }
        }

        [AnalyticInfo(eventName: SessionStoppedEventName, vendorKey: VendorKey,
            maxEventsPerHour: SessionEventsPerHour, maxNumberOfElements: MaxElements)]
        internal class SessionStoppedAnalytic : IAnalytic
        {
            private readonly SessionStoppedData m_Data;

            public SessionStoppedAnalytic(SessionStoppedData data) => m_Data = data;

            public bool TryGatherData(out IAnalytic.IData data, out Exception error)
            {
                data = m_Data;
                error = null;
                return true;
            }
        }
    }
}
