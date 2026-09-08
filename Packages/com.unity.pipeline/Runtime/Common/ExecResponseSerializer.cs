using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Unity.Pipeline.Models;

namespace Unity.Pipeline
{
    /// <summary>
    /// Single source of truth for how <c>/api/exec</c> replies are serialized (AUTHAPI-21). Centralized
    /// here so the server and the envelope byte-size regression test share one contract.
    ///
    /// Every mode serializes with <see cref="Formatting.None"/> (compact): agents consume the JSON, and
    /// an MCP client can pretty-print for a human if ever needed — the wire form never needs indentation.
    ///
    /// Two independent request flags select the mode:
    ///
    /// * <c>verbose</c> — envelope metadata. Off (default): the always-on metadata (executedAt,
    ///   command, executionTimeMs) is stripped from every <see cref="BaseResponse"/>-derived object
    ///   in the graph (the outer envelope AND responses nested as <c>result</c>, e.g. eval's), and
    ///   the envelope's own null keys (message/error/errorDetails when null, result on failure — all
    ///   redundant given <c>success</c>) are omitted, so a minimal success is just
    ///   <c>{"success":true,"result":...}</c>. On: full fidelity for debugging/correlation.
    /// * <c>omitNulls</c> — payload null fidelity. Off (default): the command's own <c>result</c>
    ///   payload keeps explicit nulls — an agent must be able to distinguish "this field is null"
    ///   from "this field doesn't exist / I got the name wrong". On: null keys are dropped
    ///   everywhere, for callers that already know the result schema and want the bytes back
    ///   (e.g. bulk list reads where a null column repeats hundreds of times).
    ///
    /// One invariant holds in EVERY mode: a successful reply always carries <c>result</c>, even
    /// when its value is null — a null result on success is the command's actual value (e.g. a
    /// format="value" read of a genuinely-null field), never a droppable envelope null.
    ///
    /// The two axes are deliberately separate booleans: envelope metadata and payload null fidelity
    /// are orthogonal concerns, and each combination is meaningful.
    ///
    /// The gating lives in a contract resolver scoped to THIS serializer, not in ShouldSerialize*
    /// hooks on the models: serializing these types anywhere else (other endpoints, logs, tests) is
    /// unaffected, and Serialize never mutates the response object.
    /// </summary>
    internal static class ExecResponseSerializer
    {
        /// <summary>
        /// Lean-envelope gating for every <see cref="BaseResponse"/>-derived object in the graph
        /// (nested results included): strips the always-on metadata and omits the envelope's own
        /// null keys — WITHOUT touching null handling on non-envelope payload objects, which keep
        /// the serializer-level default. One shared instance per settings object:
        /// DefaultContractResolver caches resolved contracts per resolver instance (thread-safe),
        /// so the reflection cost is paid once per type, not per request.
        /// </summary>
        private class LeanEnvelopeContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);

                // DeclaringType guards against unrelated same-named members (e.g.
                // CommandExecutionRequest.Command must keep serializing).
                if (member.DeclaringType != null && typeof(BaseResponse).IsAssignableFrom(member.DeclaringType))
                {
                    switch (member.Name)
                    {
                        case nameof(BaseResponse.ExecutedAt):
                        case nameof(CommandExecutionResponse.Command):
                        case nameof(CommandExecutionResponse.ExecutionTimeMs):
                            property.ShouldSerialize = _ => false;
                            break;

                        // The ONLY keys lean mode may null-drop: genuinely redundant given
                        // `success` (error/errorDetails are null exactly on success, message on
                        // both paths when there is nothing to say). Everything else declared on
                        // a BaseResponse-DERIVED type is payload (TestExecutionResponse's
                        // summary/results/filterApplied, EvalResponse.Output, …) and keeps
                        // explicit nulls — a dropped payload key is indistinguishable from a
                        // nonexistent or misspelled one; e.g. an async run_tests poller could
                        // not tell "not ready yet" from "not a field". Enumerated per key
                        // instead of type-wide for exactly that reason.
                        case nameof(BaseResponse.Message):
                        case nameof(BaseResponse.Error):
                        case nameof(BaseResponse.ErrorDetails):
                            property.NullValueHandling = NullValueHandling.Ignore;
                            break;

                        case nameof(CommandExecutionResponse.Result):
                            // A null result on SUCCESS is not a redundant envelope null — it is
                            // the command's actual value (e.g. get_serialized_fields
                            // format="value" reading a genuinely-null field), so a successful
                            // reply always carries "result", explicitly null when null (Include
                            // also defeats the omitNulls serializer settings). On failure the
                            // result is structurally meaningless and stays omitted.
                            property.NullValueHandling = NullValueHandling.Include;
                            property.ShouldSerialize = instance =>
                                !(instance is CommandExecutionResponse response) || response.Success;
                            break;
                    }
                }

                return property;
            }
        }

        private static readonly JsonSerializerSettings m_LeanSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Include,
            ContractResolver = new LeanEnvelopeContractResolver(),
        };

        private static readonly JsonSerializerSettings m_LeanOmitNullsSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new LeanEnvelopeContractResolver(),
        };

        private static readonly JsonSerializerSettings m_VerboseSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Include,
        };

        private static readonly JsonSerializerSettings m_VerboseOmitNullsSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>
        /// Serialize an exec response body with the contract selected by the request's flags (see
        /// the class doc for the two axes). Pure: the body is never mutated — the mode is carried
        /// entirely by the serializer settings, so serializing the same instance repeatedly (in
        /// any order) always yields the same JSON per mode.
        /// </summary>
        public static string Serialize(BaseResponse body, bool verbose, bool omitNulls = false)
        {
            var settings = verbose
                ? (omitNulls ? m_VerboseOmitNullsSettings : m_VerboseSettings)
                : (omitNulls ? m_LeanOmitNullsSettings : m_LeanSettings);
            return JsonConvert.SerializeObject(body, settings);
        }
    }
}
