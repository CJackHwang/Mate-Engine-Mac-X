using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace Unity.Pipeline
{
    /// <summary>
    /// Everything known about one finished <c>/api/exec</c> interaction, handed to
    /// <see cref="BasePipelineServer.OnCommandDone"/>.
    ///
    /// The transaction half and the execution half are independent, because the two are not the
    /// same set: a request can produce a transaction without executing anything (rejected for
    /// size, malformed JSON, unknown command, busy host, job queue full), and a detached job
    /// executes long after its own response was sent, producing no transaction at all. Consumers
    /// read the half they care about and skip the ones that lack it.
    /// </summary>
    public readonly struct CommandExecutionInfo
    {
        /// <summary>
        /// Raw request JSON, or null when there is no transaction to report — a detached job
        /// completing after its submission response was already sent.
        /// </summary>
        public string RequestJson { get; }

        /// <summary>
        /// Raw response JSON as it went on the wire, or null alongside a null
        /// <see cref="RequestJson"/>.
        /// </summary>
        public string ResponseJson { get; }

        /// <summary>
        /// The command that ran, or null when the request was rejected before any command
        /// executed.
        /// </summary>
        public CommandInfo Command { get; }

        /// <summary>
        /// The parameters the command was invoked with, or null when no command ran. Lets a consumer
        /// answer questions about WHAT was asked, not just which command served it — the analytics
        /// reporter reads an eval body from here to decide whether a concrete command already covers
        /// it. Never serialized anywhere: it is in-memory only, for the moment between the command
        /// finishing and the post-command work running.
        /// </summary>
        public JObject Parameters { get; }

        /// <summary>
        /// Whether the command reported success. False when it threw AND when it returned a
        /// response carrying its own failure (eval, hot reload and test runs all do the latter).
        /// Meaningless when <see cref="Command"/> is null.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Wall-clock milliseconds spent inside the command itself, excluding the wait for the
        /// one-command-at-a-time execution gate. Zero when <see cref="Command"/> is null.
        /// </summary>
        public long DurationMs { get; }

        /// <summary>Create an info describing one finished interaction.</summary>
        /// <param name="requestJson">Raw request JSON, or null when there is no transaction.</param>
        /// <param name="responseJson">Raw response JSON, or null alongside a null request.</param>
        /// <param name="command">The command that ran, or null when none did.</param>
        /// <param name="success">Whether the command reported success.</param>
        /// <param name="durationMs">Milliseconds spent inside the command.</param>
        /// <param name="parameters">The parameters the command was invoked with, or null.</param>
        public CommandExecutionInfo(string requestJson, string responseJson, CommandInfo command,
            bool success, long durationMs, JObject parameters = null)
        {
            RequestJson = requestJson;
            ResponseJson = responseJson;
            Command = command;
            Success = success;
            DurationMs = durationMs;
            Parameters = parameters;
        }

        /// <summary>
        /// A detached job that just finished: an execution with no transaction, since the client
        /// already received its job handle.
        /// </summary>
        /// <param name="command">The command the job ran.</param>
        /// <param name="success">Whether the command reported success.</param>
        /// <param name="durationMs">Milliseconds spent inside the command.</param>
        /// <param name="parameters">The parameters the job ran the command with.</param>
        /// <returns>An info carrying the execution and no transaction.</returns>
        public static CommandExecutionInfo ForDetachedJob(CommandInfo command, bool success, long durationMs,
            JObject parameters = null)
        {
            return new CommandExecutionInfo(null, null, command, success, durationMs, parameters);
        }
    }
}
