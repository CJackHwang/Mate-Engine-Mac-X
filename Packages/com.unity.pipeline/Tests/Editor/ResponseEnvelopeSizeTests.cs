using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using Unity.Pipeline.Editor.Commands.Scripts;
using Unity.Pipeline.Models;
using Unity.Pipeline.Tests.Editor.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Guards the token-efficiency win from AUTHAPI-21 with the canonical "read one scalar" case.
    /// Community feedback measured reading <c>Transform.localPosition</c> at ~823 B (indented JSON) and
    /// ~190 B even via a single <c>--field</c> descriptor. With the lean envelope plus value projection
    /// it collapses to a few dozen bytes; this test fails if the envelope silently regrows.
    ///
    /// It serializes through <see cref="ExecResponseSerializer"/> — the exact path the server's
    /// <c>/api/exec</c> handler uses — so the measured bytes track the real wire format.
    /// </summary>
    class ResponseEnvelopeSizeTests
    {
        // The lean value-projection envelope for a float3 is
        // {"success":true,"result":{"x":0.0,"y":0.0,"z":0.0}} — ~51 B at rest. The cap leaves headroom
        // for float formatting while staying far below the old ~190 B / ~823 B envelopes.
        private const int MaxCanonicalEnvelopeBytes = 96;

        private GameObject m_Go;

        [SetUp]
        public void SetUp() => m_Go = new GameObject("AUTHAPI21_Subject");

        [TearDown]
        public void TearDown()
        {
            if (m_Go != null) Object.DestroyImmediate(m_Go);
            m_Go = null;
        }

        private ObjectRef TransformRef() =>
            new ObjectRef { InstanceId = PipelineUtils.GetObjectId(m_Go.transform) };

        [Test]
        public void ReadTransformLocalPosition_LeanValueEnvelope_StaysTiny()
        {
            // The canonical read: one scalar, value-projected.
            var result = SerializedFieldCommands.GetSerializedFields(
                TransformRef(), field: "m_LocalPosition", format: "value");

            var json = ExecResponseSerializer.Serialize(
                CommandExecutionResponse.CmdSuccess("get_serialized_fields", result), verbose: false);
            var bytes = Encoding.UTF8.GetByteCount(json);

            Assert.LessOrEqual(bytes, MaxCanonicalEnvelopeBytes,
                $"Lean envelope for a Transform.localPosition read regrew to {bytes} B: {json}");

            // The value and success survive; the always-on metadata does not.
            StringAssert.Contains("\"success\":true", json);
            StringAssert.DoesNotContain("executedAt", json);
            StringAssert.DoesNotContain("\"command\"", json);
        }

        [Test]
        public void ReadTransformLocalPosition_VerboseEnvelope_IsLargerThanLean()
        {
            var result = SerializedFieldCommands.GetSerializedFields(
                TransformRef(), field: "m_LocalPosition", format: "value");
            var response = CommandExecutionResponse.CmdSuccess("get_serialized_fields", result);

            var leanBytes = Encoding.UTF8.GetByteCount(ExecResponseSerializer.Serialize(response, verbose: false));
            var verboseBytes = Encoding.UTF8.GetByteCount(ExecResponseSerializer.Serialize(response, verbose: true));

            Assert.Greater(verboseBytes, leanBytes,
                "Verbose envelope should carry the metadata (executedAt/command/executionTimeMs) the lean one drops");
        }

        [Test]
        public void VerboseEnvelope_RestoresMetadataOnNestedResponses()
        {
            // eval-family commands return a BaseResponse-derived object nested under `result`. The
            // lean/verbose contract must apply to the whole graph: lean strips nested metadata too,
            // and verbose restores it (previously the flag never reached nested objects, so verbose
            // silently dropped the nested executedAt/executionTimeMs).
            var nested = EvalResponse.EvalSuccess(42, executionTimeMs: 5);
            var response = CommandExecutionResponse.CmdSuccess("eval", nested);

            var lean = ExecResponseSerializer.Serialize(response, verbose: false);
            var verbose = ExecResponseSerializer.Serialize(response, verbose: true);

            StringAssert.DoesNotContain("executedAt", lean, "Lean should strip metadata from nested responses too");
            StringAssert.Contains("executedAt", verbose, "Verbose must restore metadata on nested responses");
            StringAssert.Contains("executionTimeMs", verbose, "Verbose must restore nested executionTimeMs");
        }

        [Test]
        public void NullKeysInsideResult_KeptByDefault_DroppedOnlyWithOmitNulls()
        {
            // A scene-object AuthoringResult legitimately carries null asset fields. The result
            // payload keeps explicit nulls in every default mode: an absent key would be
            // indistinguishable from a nonexistent or misspelled one, so dropping payload nulls
            // is strictly opt-in via the request's omitNulls flag (review feedback on AUTHAPI-21).
            var result = new AuthoringResult { Type = "GameObject", HierarchyPath = "/AUTHAPI21_Subject" };
            var response = CommandExecutionResponse.CmdSuccess("create_gameobject", result);

            var lean = ExecResponseSerializer.Serialize(response, verbose: false);
            var verbose = ExecResponseSerializer.Serialize(response, verbose: true);
            var leanOmit = ExecResponseSerializer.Serialize(response, verbose: false, omitNulls: true);
            var verboseOmit = ExecResponseSerializer.Serialize(response, verbose: true, omitNulls: true);

            StringAssert.Contains("\"assetPath\":null", lean, "Lean must keep explicit nulls in the result payload");
            StringAssert.Contains("\"assetPath\":null", verbose, "Verbose emits explicit nulls");
            StringAssert.DoesNotContain("assetPath", leanOmit, "omitNulls drops payload nulls in lean mode");
            StringAssert.DoesNotContain("assetPath", verboseOmit, "omitNulls drops payload nulls in verbose mode too (orthogonal axes)");
        }

        [Test]
        public void EnvelopeNullKeys_OmittedInLean_WithoutTouchingPayloadNulls()
        {
            // The envelope's own nulls are redundant (success already disambiguates): a lean
            // failure drops its null result key, a lean success drops error/errorDetails — while
            // nulls INSIDE the payload survive untouched in the same reply.
            var failure = ExecResponseSerializer.Serialize(
                CommandExecutionResponse.CmdFailure("create_gameobject", "Command Execution Failed", "boom"), verbose: false);
            var success = ExecResponseSerializer.Serialize(
                CommandExecutionResponse.CmdSuccess("create_gameobject",
                    new { ok = true, optionalDetail = (string)null }), verbose: false);

            StringAssert.Contains("\"success\":false", failure);
            StringAssert.Contains("\"error\":\"Command Execution Failed\"", failure);
            StringAssert.DoesNotContain("\"result\"", failure, "Lean failure omits its null result key");
            StringAssert.DoesNotContain("\"error\"", success, "Lean success omits its null error keys");
            StringAssert.DoesNotContain("\"message\"", success, "Lean success omits its null message key");
            StringAssert.Contains("\"optionalDetail\":null", success,
                "The payload's own nulls must remain intact in the very reply whose envelope nulls are stripped");
        }

        [Test]
        public void DerivedResponsePayloadNulls_SurviveLean_OnlyEnvelopeKeysAreDropped()
        {
            // The lean null-dropping is enumerated per envelope key (message/error/errorDetails),
            // NOT applied type-wide to everything a BaseResponse-derived type declares: eval's
            // EvalResponse and run_tests' TestExecutionResponse declare PAYLOAD properties, and
            // dropping those when null re-creates exactly the absent-vs-null ambiguity the
            // payload rule exists to prevent.
            var eval = EvalResponse.EvalSuccess(42); // Output stays null: the code logged nothing
            var evalLean = ExecResponseSerializer.Serialize(eval, verbose: false);

            StringAssert.Contains("\"output\":null", evalLean,
                "EvalResponse.Output is payload — its null must stay explicit in lean mode");
            StringAssert.DoesNotContain("\"message\"", evalLean,
                "…while the envelope's own null message key is still dropped");
            StringAssert.DoesNotContain("\"error\"", evalLean,
                "…and its null error keys too");

            // An async run_tests submission has no summary/results yet and no filter applied: a
            // poller must be able to tell "not ready yet" (explicit null) from "not a field".
            var tests = new TestExecutionResponse { Success = true, Summary = null, Results = null };
            var testsLean = ExecResponseSerializer.Serialize(tests, verbose: false);

            StringAssert.Contains("\"Summary\":null", testsLean,
                "TestExecutionResponse.Summary is payload — null means 'not ready yet' to a poller");
            StringAssert.Contains("\"FilterApplied\":null", testsLean,
                "A null FilterApplied (no filter given) is payload state, not an envelope key");
        }

        [Test]
        public void NullResult_OnSuccess_StaysExplicit_InEveryMode()
        {
            // A null result on SUCCESS is the command's actual value (a format="value" read of a
            // genuinely-null field), not a droppable envelope null — every mode must emit it
            // explicitly so the caller can tell "the value is null" from "there was no result".
            var success = CommandExecutionResponse.CmdSuccess("get_serialized_fields", null);

            StringAssert.Contains("\"result\":null", ExecResponseSerializer.Serialize(success, verbose: false),
                "Lean success must carry an explicit null result");
            StringAssert.Contains("\"result\":null", ExecResponseSerializer.Serialize(success, verbose: true),
                "Verbose success must carry an explicit null result");
            StringAssert.Contains("\"result\":null", ExecResponseSerializer.Serialize(success, verbose: false, omitNulls: true),
                "Even omitNulls keeps the success result explicit — it is the command's value, not a payload column");

            var failure = ExecResponseSerializer.Serialize(
                CommandExecutionResponse.CmdFailure("get_serialized_fields", "Command Execution Failed", "boom"), verbose: false);
            StringAssert.DoesNotContain("\"result\"", failure,
                "On failure the result is structurally meaningless and stays omitted");
        }

        [Test]
        public void GetSerializedFields_NullObjectReference_SerializesAsExplicitNullResult()
        {
            // End-to-end for the real-world case behind the rule above: a format="value" read of
            // an unassigned object-reference field yields a null command result
            // (ObjectResolver.Describe(null) returns null), which must reach the wire as
            // "result":null through the exact serializer the exec endpoint uses.
            var comp = m_Go.AddComponent<ScriptCommandTestBehaviour>();
            var result = SerializedFieldCommands.GetSerializedFields(
                new ObjectRef { InstanceId = PipelineUtils.GetObjectId(comp) }, field: "m_Target", format: "value");
            Assert.IsNull(result, "Precondition: an unassigned object reference reads as a null value");

            var lean = ExecResponseSerializer.Serialize(
                CommandExecutionResponse.CmdSuccess("get_serialized_fields", result), verbose: false);

            StringAssert.Contains("\"success\":true", lean);
            StringAssert.Contains("\"result\":null", lean,
                "A null field value must arrive as an explicit null result, never a missing key");
        }

        [Test]
        public void NonExecSerialization_KeepsExecutedAt()
        {
            // The lean gating lives in ExecResponseSerializer's contract resolver, NOT on the models:
            // other endpoints (401/403 rejections, status errors) serialize BaseResponse with default
            // settings and must keep emitting executedAt.
            var json = JsonConvert.SerializeObject(BaseResponse.Failure("Unauthorized", "details"));

            StringAssert.Contains("executedAt", json,
                "Default serialization of BaseResponse must be unaffected by the exec lean contract");
        }

        [Test]
        public void Serialize_DoesNotMutateBody_ModesAreOrderIndependent()
        {
            var response = CommandExecutionResponse.CmdSuccess("noop", new { ok = true });

            // verbose-then-lean and lean-then-verbose must agree call-for-call: the mode is carried by
            // serializer settings only, never stored on the response instance.
            var verboseFirst = ExecResponseSerializer.Serialize(response, verbose: true);
            var leanSecond = ExecResponseSerializer.Serialize(response, verbose: false);
            var verboseAgain = ExecResponseSerializer.Serialize(response, verbose: true);

            Assert.AreEqual(verboseFirst, verboseAgain, "Repeated verbose serialization must be identical");
            StringAssert.DoesNotContain("executedAt", leanSecond, "Lean output must not depend on prior calls");
        }

        // The argument-error envelope fields must behave exactly like Status/Retryable/Warnings:
        // absent when null in all four modes, present when set in all four. They rely on a
        // property-level NullValueHandling.Ignore beating the serializer setting — and the lean
        // settings use NullValueHandling.Include, so forgetting the attribute would emit
        // "argProblems":null on every single reply and silently undo part of the lean-envelope win.

        /// <summary>The same response serialized through every lean/verbose x omitNulls combination.</summary>
        private static string[] AllFourModes(CommandExecutionResponse response) => new[]
        {
            ExecResponseSerializer.Serialize(response, verbose: false, omitNulls: false),
            ExecResponseSerializer.Serialize(response, verbose: false, omitNulls: true),
            ExecResponseSerializer.Serialize(response, verbose: true, omitNulls: false),
            ExecResponseSerializer.Serialize(response, verbose: true, omitNulls: true),
        };

        [Test]
        public void SuccessEnvelope_OmitsTheArgumentFields_InAllFourModes()
        {
            var response = CommandExecutionResponse.CmdSuccess("ping", "pong");

            foreach (var json in AllFourModes(response))
            {
                StringAssert.DoesNotContain("errorCode", json, $"leaked into: {json}");
                StringAssert.DoesNotContain("argProblems", json, $"leaked into: {json}");
                StringAssert.DoesNotContain("commandSchema", json, $"leaked into: {json}");
                StringAssert.DoesNotContain("parameters", json, $"leaked into: {json}");
            }
        }

        [Test]
        public void GenericFailureEnvelope_OmitsTheArgumentFields_InAllFourModes()
        {
            var response = CommandExecutionResponse.CmdFailure("ping", "Boom", "it broke");

            foreach (var json in AllFourModes(response))
            {
                StringAssert.DoesNotContain("errorCode", json, $"leaked into: {json}");
                StringAssert.DoesNotContain("argProblems", json, $"leaked into: {json}");
                StringAssert.DoesNotContain("commandSchema", json, $"leaked into: {json}");
            }
        }

        [Test]
        public void ArgumentErrorEnvelope_CarriesTheArgumentFields_InAllFourModes()
        {
            var problems = new System.Collections.Generic.List<ArgProblem>
            {
                new ArgProblem { Kind = ArgProblemKind.UnknownName, Name = "mesage", Suggestion = "message" },
            };
            var response = CommandExecutionResponse.CmdInvalidArgs("log_editor", "no such parameter",
                problems, new Newtonsoft.Json.Linq.JObject { ["name"] = "log_editor" });

            foreach (var json in AllFourModes(response))
            {
                StringAssert.Contains("INVALID_COMMAND_ARGS", json, $"missing from: {json}");
                StringAssert.Contains("\"kind\":\"unknownName\"", json,
                    $"kind must serialize camelCase for the CLI. Got: {json}");
                StringAssert.Contains("commandSchema", json, $"missing from: {json}");
            }
        }

        [Test]
        public void BoundParameterEcho_SurvivesEveryMode_WhenSet()
        {
            var response = CommandExecutionResponse.CmdSuccess("log_editor", "ok");
            response.BoundParameters = new Newtonsoft.Json.Linq.JObject { ["message"] = "hi" };

            foreach (var json in AllFourModes(response))
                StringAssert.Contains("\"parameters\":{\"message\":\"hi\"}", json,
                    $"the echo is what four CLI output formats render. Got: {json}");
        }
    }
}
