using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Information about a discovered CLI command.
    /// Contains metadata needed for command execution and CLI help generation.
    /// </summary>
    public class CommandInfo
    {
        /// <summary>
        /// Unique name of the command for CLI execution.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Human-readable description of the command.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Whether this command requires Unity main thread execution.
        /// </summary>
        public bool MainThreadRequired { get; }

        /// <summary>
        /// Whether this command is part of the runtime (Player) command surface only.
        /// Editor servers hide runtime-only commands from their command listing.
        /// </summary>
        public bool RuntimeOnly { get; }

        /// <summary>
        /// Hierarchical tags used to group and browse commands (path-style, '/'-separated).
        /// Empty for untagged commands; never null.
        /// </summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>
        /// Name of the assembly this command originates from, used to group and filter
        /// commands by contributing package/source.
        /// </summary>
        public string Package { get; }

        /// <summary>
        /// Method that implements this command.
        /// </summary>
        public MethodInfo Method { get; } // TODO Maybe look at generating a dynamic Delegate to increase performance of the command call.

        /// <summary>
        /// Parameters that this command accepts.
        /// </summary>
        public IReadOnlyList<CommandParameterInfo> Parameters { get; }

        /// <summary>
        /// Create command information from discovery.
        /// </summary>
        /// <param name="name">Unique name of the command for CLI execution.</param>
        /// <param name="description">Human-readable description of the command.</param>
        /// <param name="mainThreadRequired">Whether this command requires Unity main thread execution.</param>
        /// <param name="method">Method that implements this command.</param>
        /// <param name="parameters">Parameters that this command accepts.</param>
        /// <param name="runtimeOnly">Whether this command is part of the runtime (Player) command surface only.</param>
        /// <param name="tags">Hierarchical tags used to group and browse commands.</param>
        /// <param name="package">Name of the assembly this command originates from.</param>
        public CommandInfo(string name, string description, bool mainThreadRequired,
            MethodInfo method, IReadOnlyList<CommandParameterInfo> parameters, bool runtimeOnly = false,
            IReadOnlyList<string> tags = null, string package = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            MainThreadRequired = mainThreadRequired;
            RuntimeOnly = runtimeOnly;
            Tags = tags ?? Array.Empty<string>();
            Package = package;
        }

        /// <summary>A short diagnostic summary of the command.</summary>
        /// <returns>The summary string.</returns>
        public override string ToString()
        {
            return $"{Name} MainThreadRequired:{MainThreadRequired} Parameters:{Parameters.Count}";
        }
    }
}