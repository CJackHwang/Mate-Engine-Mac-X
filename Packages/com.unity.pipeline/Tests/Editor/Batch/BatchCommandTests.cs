using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Unity.Pipeline;
using Unity.Pipeline.Editor.Commands.Batch;
using Unity.Pipeline.Editor.Commands.GameObjects;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Tests.Editor.Batch
{
    /// <summary>
    /// Tests for the transactional <c>batch</c> command (AUTHAPI-27), driven end-to-end through an
    /// isolated <see cref="PipelineTestServer"/> (the ViaClient pattern). Covers the acceptance
    /// criteria: cross-op references resolve, a mid-batch failure atomically rolls back every prior
    /// op, a successful batch collapses into a single Undo group, dry-run validates without mutating,
    /// a bad reference errors clearly, <c>on_error=continue</c> applies independent ops without
    /// reverting, and the 200-op cap is enforced.
    /// </summary>
    class BatchCommandTests
    {
        private readonly List<GameObject> m_Spawned = new List<GameObject>();

        private GameObject Track(GameObject go)
        {
            if (go != null)
                m_Spawned.Add(go);
            return go;
        }

        private static ObjectId? InstanceIdOf(JToken opResult) =>
            opResult?["result"]?["instanceId"]?.ToObject<ObjectId?>();

        private GameObject ResolveGameObject(JToken opResult)
        {
            var id = InstanceIdOf(opResult);
            if (!id.HasValue)
                return null;
            return Track(PipelineUtils.IdToObject(id.Value) as GameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in m_Spawned)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            m_Spawned.Clear();

            // Deliberately NO Undo.ClearAll() here: this suite runs inside the dogfood editor, and
            // wiping the global Undo stack would destroy the developer's own undo history. Per-test
            // state is cleaned up by destroying m_Spawned above; the batch Undo groups left on the
            // stack only reference those (now destroyed) test objects and are inert.
        }

        private static JObject Op(string command, JObject parameters = null, string id = null)
        {
            var op = new JObject { ["command"] = command };
            if (id != null)
                op["id"] = id;
            op["params"] = parameters ?? new JObject();
            return op;
        }

        [Test]
        public void Batch_CrossOpReferences_ResolveAndApply()
        {
            // AC: create_gameobject -> add_component -> set_serialized_field, where later ops use
            // $0.instanceId / $1.instanceId, executes in one round-trip with references resolved.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchRef_Root" }),
                    Op("add_component", new JObject { ["target"] = "$0.instanceId", ["type"] = "Rigidbody" }),
                    Op("set_serialized_field", new JObject
                    {
                        ["target"] = "$1.instanceId",
                        ["field"] = "m_Mass",
                        ["value"] = new JValue(2.5f)
                    }),
                };

                var response = server.Execute("batch", new JObject { ["operations"] = operations });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var batch = response.JsonResponse["result"];
                Assert.AreEqual(3, batch["applied"]?.ToObject<int>(), "all three ops should apply");
                Assert.IsFalse(batch["reverted"]?.ToObject<bool>() ?? true, "a successful batch must not revert");
                Assert.IsNotNull(batch["undoGroup"], "execution result should carry the batch Undo group");

                var results = batch["results"] as JArray;
                Assert.AreEqual(3, results.Count);
                for (int i = 0; i < 3; i++)
                    Assert.IsTrue(results[i]["success"]?.ToObject<bool>() ?? false,
                        $"op {i} should succeed: {results[i]["error"]}");

                // The reference chain wired a Rigidbody onto op 0's GameObject and set its mass.
                var root = ResolveGameObject(results[0]);
                Assert.IsNotNull(root, "op 0's GameObject should be resolvable from its returned instanceId");
                var rb = root.GetComponent<Rigidbody>();
                Assert.IsNotNull(rb, "$0.instanceId must resolve so add_component targets op 0's GameObject");
                Assert.AreEqual(2.5f, rb.mass, 0.0001f,
                    "$1.instanceId must resolve so set_serialized_field targets the added Rigidbody");
            }
        }

        [Test]
        public void Batch_Transactional_MidBatchFailure_RevertsAllPriorOps()
        {
            // AC: an op fails mid-batch -> every prior op is reverted; scene equals pre-batch state.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchRollback_A" }),
                    Op("create_gameobject", new JObject { ["name"] = "BatchRollback_B" }),
                    // Fails: unknown component type -> add_component throws before mutating.
                    Op("add_component", new JObject { ["target"] = "$0.instanceId", ["type"] = "NoSuchTypeXYZ" }),
                };

                // The failing sub-op logs an error through the shared command-dispatch path (batch
                // reuses it); the batch surfaces the failure structurally, but the log still fires.
                LogAssert.Expect(LogType.Error, new Regex("add_component' failed: Could not resolve component type 'NoSuchTypeXYZ'"));

                var response = server.Execute("batch", new JObject { ["operations"] = operations });
                Assert.IsTrue(response.IsSuccess, $"batch itself should return HTTP success: {response.RawResponse}");

                var batch = response.JsonResponse["result"];
                Assert.IsTrue(batch["reverted"]?.ToObject<bool>() ?? false, "a transactional failure must revert");
                Assert.AreEqual(2, batch["applied"]?.ToObject<int>(), "two ops ran before the failure");

                var results = batch["results"] as JArray;
                Assert.IsTrue(results[0]["success"]?.ToObject<bool>() ?? false);
                Assert.IsTrue(results[1]["success"]?.ToObject<bool>() ?? false);
                Assert.IsFalse(results[2]["success"]?.ToObject<bool>() ?? true, "op 2 must fail");
                StringAssert.Contains("component type", results[2]["error"]?.ToString(),
                    "the failing op's error should be reported");

                // The two created objects must be gone (reverted), both by name and by their ids.
                Assert.AreEqual(0, GameObjectCommands.FindGameObjects(name: "BatchRollback_A").Count,
                    "op 0's object must be reverted");
                Assert.AreEqual(0, GameObjectCommands.FindGameObjects(name: "BatchRollback_B").Count,
                    "op 1's object must be reverted");

                var id0 = InstanceIdOf(results[0]);
                var id1 = InstanceIdOf(results[1]);
                Assert.IsTrue(id0.HasValue && id1.HasValue, "reverted ops still report the ids they had created");
                Assert.IsNull(PipelineUtils.IdToObject(id0.Value), "op 0's object should no longer resolve");
                Assert.IsNull(PipelineUtils.IdToObject(id1.Value), "op 1's object should no longer resolve");
            }
        }

        [Test]
        public void Batch_SuccessfulBatch_CollapsesIntoSingleUndoGroup()
        {
            // AC: a successful batch shows exactly one Undo group -> a single Undo reverts the whole batch.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchUndo_A" }),
                    Op("create_gameobject", new JObject { ["name"] = "BatchUndo_B" }),
                };

                var response = server.Execute("batch", new JObject { ["operations"] = operations });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                // Track the created objects so TearDown cleans them up even if the Undo below misbehaves.
                var results = response.JsonResponse["result"]["results"] as JArray;
                ResolveGameObject(results[0]);
                ResolveGameObject(results[1]);

                Assert.AreEqual(1, GameObjectCommands.FindGameObjects(name: "BatchUndo_A").Count);
                Assert.AreEqual(1, GameObjectCommands.FindGameObjects(name: "BatchUndo_B").Count);

                // One Undo must revert BOTH creations, proving the whole batch collapsed into one group.
                Undo.PerformUndo();

                Assert.AreEqual(0, GameObjectCommands.FindGameObjects(name: "BatchUndo_A").Count,
                    "a single Undo should revert the first op (batch is one group)");
                Assert.AreEqual(0, GameObjectCommands.FindGameObjects(name: "BatchUndo_B").Count,
                    "a single Undo should revert the second op too (batch is one group)");
            }
        }

        [Test]
        public void Batch_DryRun_CatchesErrors_AndMutatesNothing()
        {
            // AC: dry_run catches unknown command, not_batchable, unknown param, and a bad reference,
            // with zero mutation.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchDry_A" }),            // valid
                    Op("no_such_command_xyz"),                                                    // unknown command
                    Op("build"),                                                                  // not_batchable
                    Op("create_gameobject", new JObject { ["bogus_param"] = "x" }),               // unknown parameter
                    Op("add_component", new JObject { ["target"] = "$9.instanceId", ["type"] = "Rigidbody" }), // bad ref
                };

                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["dry_run"] = true
                });
                Assert.IsTrue(response.IsSuccess, $"dry_run should return HTTP success: {response.RawResponse}");

                var batch = response.JsonResponse["result"];
                Assert.IsTrue(batch["dryRun"]?.ToObject<bool>() ?? false, "result should be marked dryRun");
                Assert.IsFalse(batch["valid"]?.ToObject<bool>() ?? true, "the batch has invalid ops");
                Assert.AreEqual(0, batch["applied"]?.ToObject<int>() ?? -1, "dry_run applies nothing");

                var results = batch["results"] as JArray;
                Assert.IsTrue(results[0]["success"]?.ToObject<bool>() ?? false, "op 0 is valid");
                StringAssert.Contains("unknown command", results[1]["error"]?.ToString());
                StringAssert.Contains("not_batchable", results[2]["error"]?.ToString());
                StringAssert.Contains("unknown parameter", results[3]["error"]?.ToString());
                StringAssert.Contains("unknown operation", results[4]["error"]?.ToString());

                // Zero mutation: the valid create op must NOT have run.
                Assert.AreEqual(0, GameObjectCommands.FindGameObjects(name: "BatchDry_A").Count,
                    "dry_run must not create anything");
            }
        }

        [Test]
        public void Batch_BadReference_ErrorsClearly_AndReverts()
        {
            // AC: a bad reference errors clearly. Here op 1 references a nonexistent op; under the
            // default transactional/abort policy op 0 is reverted.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchBadRef_X" }),
                    Op("add_component", new JObject { ["target"] = "$5.instanceId", ["type"] = "Rigidbody" }),
                };

                var response = server.Execute("batch", new JObject { ["operations"] = operations });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var batch = response.JsonResponse["result"];
                var results = batch["results"] as JArray;
                Assert.IsFalse(results[1]["success"]?.ToObject<bool>() ?? true, "the referencing op must fail");
                var error = results[1]["error"]?.ToString();
                StringAssert.Contains("$5.instanceId", error, "the error should name the offending reference");
                StringAssert.Contains("unknown operation", error, "and explain why it is invalid");

                Assert.IsTrue(batch["reverted"]?.ToObject<bool>() ?? false, "the prior op must be reverted");
                Assert.AreEqual(0, GameObjectCommands.FindGameObjects(name: "BatchBadRef_X").Count,
                    "op 0 must be reverted after the bad reference");
            }
        }

        [Test]
        public void Batch_OnErrorContinue_AppliesIndependentOps_NoRevert()
        {
            // AC: on_error=continue collects failures, applies independent ops, reports reverted:false.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchCont_A" }),
                    Op("add_component", new JObject { ["target"] = "$0.instanceId", ["type"] = "NoSuchTypeXYZ" }), // fails
                    Op("create_gameobject", new JObject { ["name"] = "BatchCont_C" }),                              // independent
                };

                LogAssert.Expect(LogType.Error, new Regex("add_component' failed: Could not resolve component type 'NoSuchTypeXYZ'"));

                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["on_error"] = "continue"
                });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var batch = response.JsonResponse["result"];
                Assert.IsFalse(batch["reverted"]?.ToObject<bool>() ?? true, "continue must not revert");
                Assert.IsFalse(batch["transactional"]?.ToObject<bool>() ?? true, "continue forces transactional=false");
                Assert.AreEqual(2, batch["applied"]?.ToObject<int>(), "the two independent creates apply");

                var results = batch["results"] as JArray;
                Assert.IsTrue(results[0]["success"]?.ToObject<bool>() ?? false);
                Assert.IsFalse(results[1]["success"]?.ToObject<bool>() ?? true, "the middle op fails");
                Assert.IsTrue(results[2]["success"]?.ToObject<bool>() ?? false, "a later independent op still runs");

                // Both independent objects must survive (no rollback).
                Assert.IsNotNull(ResolveGameObject(results[0]), "op 0's object should survive");
                Assert.IsNotNull(ResolveGameObject(results[2]), "op 2's object should survive");
            }
        }

        [Test]
        public void Batch_ExceedsOperationCap_ReturnsError()
        {
            // AC: the 200-op cap is enforced with a structured error (checked before anything runs).
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray();
                for (int i = 0; i < 201; i++)
                    operations.Add(Op("create_gameobject", new JObject { ["name"] = "BatchCap_" + i }));

                LogAssert.Expect(LogType.Error, new Regex("batch supports at most 200 operations"));

                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["dry_run"] = true
                });

                Assert.IsFalse(response.IsSuccess, "a batch over the op cap must be rejected");
                StringAssert.Contains("200", response.RawResponse, "the error should state the 200-op cap");
                Assert.AreEqual(0, GameObjectCommands.FindGameObjects(name: "BatchCap_0").Count,
                    "an over-cap batch must create nothing");
            }
        }

        [Test]
        public void Batch_AsyncCommand_NotBatchable_AtDryRunAndExecution()
        {
            // C1: run_tests/list_tests are async Task commands completed by EditorApplication.update
            // callbacks, which cannot fire while the batch blocks the main thread — awaiting them
            // inline would freeze the editor permanently. They must be rejected, not dispatched.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("run_tests", new JObject { ["mode"] = "EditMode" }),
                    Op("list_tests"),
                };

                // Dry run flags both without executing anything.
                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["dry_run"] = true
                });
                Assert.IsTrue(response.IsSuccess, $"dry_run should return HTTP success: {response.RawResponse}");
                var results = response.JsonResponse["result"]["results"] as JArray;
                foreach (var opResult in results)
                {
                    Assert.IsFalse(opResult["success"]?.ToObject<bool>() ?? true, "async ops must fail validation");
                    StringAssert.Contains("not_batchable", opResult["error"]?.ToString());
                    StringAssert.Contains("async", opResult["error"]?.ToString());
                }

                // Execution rejects them the same way — and returns instead of hanging.
                response = server.Execute("batch", new JObject { ["operations"] = new JArray { Op("run_tests") } });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");
                var exec = response.JsonResponse["result"];
                Assert.AreEqual(0, exec["applied"]?.ToObject<int>(), "nothing must be dispatched");
                StringAssert.Contains("not_batchable", exec["results"][0]["error"]?.ToString());
            }
        }

        [Test]
        public void Batch_Transactional_RejectsNonRevertibleCommand_NonTransactionalAllowsIt()
        {
            // C2: delete_asset mutates the AssetDatabase, which Undo cannot roll back — inside a
            // transactional batch "reverted: true" would be a lie, so it is rejected. With
            // transactional:false it runs and reports revertible:false.
            const string assetPath = "Assets/BatchTests_DeleteTarget.txt";
            File.WriteAllText(assetPath, "batch delete target");
            AssetDatabase.ImportAsset(assetPath);
            try
            {
                using (var server = new PipelineTestServer())
                {
                    var operations = new JArray
                    {
                        Op("delete_asset", new JObject { ["asset"] = assetPath, ["confirm"] = true }),
                    };

                    // Dry run under the default transactional=true flags it.
                    var response = server.Execute("batch", new JObject
                    {
                        ["operations"] = operations,
                        ["dry_run"] = true
                    });
                    var dry = response.JsonResponse["result"];
                    Assert.IsFalse(dry["valid"]?.ToObject<bool>() ?? true);
                    StringAssert.Contains("not_batchable_transactional", dry["results"][0]["error"]?.ToString());

                    // Execution under transactional=true rejects it too; the asset survives.
                    response = server.Execute("batch", new JObject { ["operations"] = operations });
                    var exec = response.JsonResponse["result"];
                    Assert.IsFalse(exec["results"][0]["success"]?.ToObject<bool>() ?? true);
                    StringAssert.Contains("not_batchable_transactional", exec["results"][0]["error"]?.ToString());
                    Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(assetPath),
                        "a rejected delete_asset must not touch the asset");

                    // transactional:false allows it — with an honest revertible:false on the op.
                    response = server.Execute("batch", new JObject
                    {
                        ["operations"] = operations,
                        ["transactional"] = false
                    });
                    exec = response.JsonResponse["result"];
                    Assert.IsTrue(exec["results"][0]["success"]?.ToObject<bool>() ?? false,
                        $"transactional:false must allow delete_asset: {exec["results"][0]["error"]}");
                    Assert.IsFalse(exec["results"][0]["revertible"]?.ToObject<bool>() ?? true,
                        "the op must report revertible:false");
                    Assert.IsNull(AssetDatabase.LoadMainAssetAtPath(assetPath), "the asset must be deleted");
                }
            }
            finally
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                    AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void Batch_OpenScene_AlwaysRejected_EvenNonTransactional()
        {
            // C2: open_scene/create_scene CLEAR the Editor Undo stack, destroying the batch's ability
            // to revert anything before them — they are rejected regardless of the transactional flag.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("open_scene", new JObject { ["path"] = "Assets/Nope.unity" }),
                };

                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["dry_run"] = true
                });
                var dry = response.JsonResponse["result"];
                StringAssert.Contains("not_batchable", dry["results"][0]["error"]?.ToString());
                StringAssert.Contains("Undo stack", dry["results"][0]["error"]?.ToString());

                response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["transactional"] = false
                });
                var exec = response.JsonResponse["result"];
                Assert.IsFalse(exec["results"][0]["success"]?.ToObject<bool>() ?? true);
                StringAssert.Contains("not_batchable", exec["results"][0]["error"]?.ToString());
            }
        }

        [Test]
        public void Batch_FirstOpPartialMutation_IsRolledBack()
        {
            // I1: an op that mutates and THEN throws leaves partial Undo-registered state even when
            // it is the first (and only) op — the transactional rollback must run with applied == 0.
            // set_transform applies 'position' before it parses the invalid 'rotation'.
            using (var server = new PipelineTestServer())
            {
                var go = Track(new GameObject("BatchPartial_Target"));
                go.transform.localPosition = Vector3.zero;

                var operations = new JArray
                {
                    Op("set_transform", new JObject
                    {
                        // Version-stable id: GetInstanceID() is obsolete-as-error from 6000.4.
                        ["target"] = PipelineUtils.GetObjectId(go).RawValue,
                        ["position"] = new JArray(5f, 6f, 7f),
                        ["rotation"] = new JArray(1f, 2f), // invalid: 2 components -> throws after position applied
                    }),
                };

                LogAssert.Expect(LogType.Error, new Regex("set_transform' failed: 'rotation' must have exactly 3 components"));

                var response = server.Execute("batch", new JObject { ["operations"] = operations });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var batch = response.JsonResponse["result"];
                Assert.AreEqual(0, batch["applied"]?.ToObject<int>(), "the only op failed");
                Assert.IsTrue(batch["reverted"]?.ToObject<bool>() ?? false,
                    "a transactional failure must revert even when applied == 0");
                Assert.AreEqual(0f, go.transform.localPosition.magnitude, 0.0001f,
                    "the failing op's partial position change must be reverted");
            }
        }

        [Test]
        public void Batch_TimeBudgetExceeded_SkipsRemainingOps_AndRevertsTransactional()
        {
            // I2: the cooperative time budget is checked before each op after the first. Budget 0 is
            // exhausted immediately after op 0, so ops 1..n are skipped and the transactional batch
            // rolls back with a batch-level error.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchBudget_A" }),
                    Op("create_gameobject", new JObject { ["name"] = "BatchBudget_B" }),
                    Op("create_gameobject", new JObject { ["name"] = "BatchBudget_C" }),
                };

                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["time_budget_ms"] = 0
                });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var batch = response.JsonResponse["result"];
                StringAssert.Contains("batch time budget exceeded", batch["error"]?.ToString());
                Assert.AreEqual(1, batch["applied"]?.ToObject<int>(), "only op 0 runs on a zero budget");
                Assert.IsTrue(batch["reverted"]?.ToObject<bool>() ?? false, "budget abort is a transactional failure");

                var results = batch["results"] as JArray;
                Assert.AreEqual(3, results.Count);
                Assert.IsTrue(results[0]["success"]?.ToObject<bool>() ?? false, "op 0 always gets to run");
                Assert.IsTrue(results[1]["skipped"]?.ToObject<bool>() ?? false, "op 1 must be skipped");
                Assert.IsTrue(results[2]["skipped"]?.ToObject<bool>() ?? false, "op 2 must be skipped");

                Assert.AreEqual(0, GameObjectCommands.FindGameObjects(name: "BatchBudget_A").Count,
                    "op 0 must be reverted by the budget-abort rollback");
                Assert.AreEqual(0, GameObjectCommands.FindGameObjects(name: "BatchBudget_B").Count);
            }
        }

        [Test]
        public void Batch_ReferenceToFailedOp_ErrorsClearly()
        {
            // I3: under on_error=continue an op can fail without stopping the batch; a later $ref
            // into the failed op must error explicitly instead of silently resolving to null.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("add_component", new JObject { ["target"] = "/NoSuchObjectXYZ_00", ["type"] = "Rigidbody" }), // fails
                    Op("rename_gameobject", new JObject { ["target"] = "$0.instanceId", ["name"] = "Renamed" }),
                };

                LogAssert.Expect(LogType.Error, new Regex("add_component' failed:"));

                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["on_error"] = "continue"
                });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var results = response.JsonResponse["result"]["results"] as JArray;
                Assert.IsFalse(results[0]["success"]?.ToObject<bool>() ?? true, "op 0 must fail");
                Assert.IsFalse(results[1]["success"]?.ToObject<bool>() ?? true, "the referencing op must fail");
                var error = results[1]["error"]?.ToString();
                StringAssert.Contains("$0.instanceId", error, "the error should name the offending reference");
                StringAssert.Contains("failed; its result cannot be referenced", error,
                    "and say the referenced op failed");
            }
        }

        [Test]
        public void Resolver_ReferenceToVoidResult_Errors_WholeValueAndPath()
        {
            // I3: a SUCCEEDED op with a null result (void command) is distinct from a failed op —
            // referencing it errors with "returned no result", for whole-value and path refs alike.
            var operations = new List<BatchOperationInput>
            {
                new BatchOperationInput { Command = "noop_a" },
                new BatchOperationInput { Command = "noop_b" },
            };
            var outcomes = new List<BatchOperationResult>
            {
                new BatchOperationResult { Command = "noop_a", Success = true },
                new BatchOperationResult { Command = "noop_b" },
            };
            var results = new JToken[] { JValue.CreateNull(), null };
            var idToIndex = new Dictionary<string, int>();

            var wholeRef = new JObject { ["value"] = "$0" };
            var ex = Assert.Throws<ArgumentException>(() =>
                BatchReferenceResolver.Resolve(wholeRef, 1, operations, idToIndex, results, outcomes));
            StringAssert.Contains("returned no result", ex.Message);

            var pathRef = new JObject { ["value"] = "$0.instanceId" };
            ex = Assert.Throws<ArgumentException>(() =>
                BatchReferenceResolver.Resolve(pathRef, 1, operations, idToIndex, results, outcomes));
            StringAssert.Contains("returned no result", ex.Message);
        }

        [Test]
        public void Resolver_ReferenceToFailedOp_Errors_WholeValueAndPath()
        {
            // I3: whole-value refs into a FAILED op must error just like path refs (the end-to-end
            // test above covers the path form; this pins the whole-value form).
            var operations = new List<BatchOperationInput>
            {
                new BatchOperationInput { Command = "noop_a" },
                new BatchOperationInput { Command = "noop_b" },
            };
            var outcomes = new List<BatchOperationResult>
            {
                new BatchOperationResult { Command = "noop_a", Success = false, Error = "boom" },
                new BatchOperationResult { Command = "noop_b" },
            };
            var results = new JToken[] { null, null };
            var idToIndex = new Dictionary<string, int>();

            var wholeRef = new JObject { ["value"] = "$0" };
            var ex = Assert.Throws<ArgumentException>(() =>
                BatchReferenceResolver.Resolve(wholeRef, 1, operations, idToIndex, results, outcomes));
            StringAssert.Contains("failed; its result cannot be referenced", ex.Message);
        }

        [Test]
        public void Batch_RuntimeOnlyCommand_NotBatchable()
        {
            // I5: runtime-only commands (Player server surface) are discoverable via the registry but
            // must not be dispatchable through an editor-server batch.
            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = new JArray { Op("set_timescale", new JObject { ["scale"] = 0.5f }) },
                    ["dry_run"] = true
                });

                var dry = response.JsonResponse["result"];
                Assert.IsFalse(dry["valid"]?.ToObject<bool>() ?? true);
                var error = dry["results"][0]["error"]?.ToString();
                StringAssert.Contains("not_batchable", error);
                StringAssert.Contains("runtime-only", error);
            }
        }

        [Test]
        public void Batch_MenuCommand_NotBatchable()
        {
            // I6: menu items can open modal dialogs, which would wedge an unattended batch.
            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = new JArray { Op("menu", new JObject { ["path"] = "File/Save" }) },
                    ["dry_run"] = true
                });

                var dry = response.JsonResponse["result"];
                Assert.IsFalse(dry["valid"]?.ToObject<bool>() ?? true);
                StringAssert.Contains("not_batchable", dry["results"][0]["error"]?.ToString());
            }
        }

        [Test]
        public void Batch_NumericOpId_Rejected()
        {
            // M3: a purely numeric id would be ambiguous with a 0-based index selector in "$<id-or-index>".
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchId_A" }, id: "0"),
                };

                LogAssert.Expect(LogType.Error, new Regex("ids must match"));

                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["dry_run"] = true
                });

                Assert.IsFalse(response.IsSuccess, "a numeric op id must be rejected outright");
                StringAssert.Contains("ids must match", response.RawResponse);
            }
        }

        [Test]
        public void Batch_DollarDollar_EscapesToLiteralDollar()
        {
            // M7: "$$..." escapes a literal '$' — the op receives "$literal", not a reference.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "$$literal" }),
                };

                var response = server.Execute("batch", new JObject { ["operations"] = operations });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var results = response.JsonResponse["result"]["results"] as JArray;
                var go = ResolveGameObject(results[0]);
                Assert.IsNotNull(go, "op 0's GameObject should be resolvable");
                Assert.AreEqual("$literal", go.name, "\"$$literal\" must unescape to the literal \"$literal\"");
            }
        }

        [Test]
        public void Batch_IdBasedReference_Resolves()
        {
            // M7: references can select the target op by its id, not only by index.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchIdRef_Root" }, id: "root"),
                    Op("add_component", new JObject { ["target"] = "$root.instanceId", ["type"] = "Rigidbody" }),
                };

                var response = server.Execute("batch", new JObject { ["operations"] = operations });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var batch = response.JsonResponse["result"];
                Assert.AreEqual(2, batch["applied"]?.ToObject<int>(),
                    $"both ops should apply: {batch["results"]}");

                var root = ResolveGameObject((batch["results"] as JArray)[0]);
                Assert.IsNotNull(root);
                Assert.IsNotNull(root.GetComponent<Rigidbody>(), "$root.instanceId must resolve by op id");
            }
        }

        [Test]
        public void Batch_ResultFields_ProjectsOpResult()
        {
            // M7: result_fields keeps only the requested result fields for the op (context economy).
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchProject_A" }),
                };

                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["result_fields"] = new JObject { ["0"] = new JArray("instanceId") }
                });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var results = response.JsonResponse["result"]["results"] as JArray;
                var projected = results[0]["result"] as JObject;
                Assert.IsNotNull(projected, "the projected result should be an object");
                Assert.IsNotNull(projected["instanceId"], "the requested field must be kept");
                Assert.IsNull(projected["hierarchyPath"], "unrequested fields must be dropped");
                Assert.IsNull(projected["globalId"], "unrequested fields must be dropped");

                // Cleanup: the projection kept instanceId, so the object is still resolvable.
                Assert.IsNotNull(ResolveGameObject(results[0]));
            }
        }

        [Test]
        public void Batch_OversizedOpResult_Truncated_ButReferencesStillResolve()
        {
            // M7: an op result over the per-op cap is replaced by a truncation marker in the envelope,
            // but later $refs resolve against the FULL (untruncated) result.
            const string bigPath = "Assets/BatchTests_BigFile.txt";
            File.WriteAllText(bigPath, new string('x', 20000)); // > 16 KiB serialized
            try
            {
                using (var server = new PipelineTestServer())
                {
                    var operations = new JArray
                    {
                        Op("read_text_file", new JObject { ["path"] = bigPath }),
                        // References a scalar of the oversized result — must still resolve.
                        Op("create_gameobject", new JObject { ["name"] = "$0.assetPath" }),
                    };

                    var response = server.Execute("batch", new JObject { ["operations"] = operations });
                    Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                    var results = response.JsonResponse["result"]["results"] as JArray;
                    Assert.IsTrue(results[0]["resultTruncated"]?.ToObject<bool>() ?? false,
                        "the oversized read_text_file result must be truncated");
                    Assert.IsTrue(results[0]["result"]?["resultTruncated"]?.ToObject<bool>() ?? false,
                        "the result must be replaced by the truncation marker");

                    Assert.IsTrue(results[1]["success"]?.ToObject<bool>() ?? false,
                        $"the referencing op must still succeed: {results[1]["error"]}");
                    var go = ResolveGameObject(results[1]);
                    Assert.IsNotNull(go);
                    Assert.AreEqual(bigPath, go.name, "$0.assetPath must resolve against the untruncated result");
                }
            }
            finally
            {
                if (File.Exists(bigPath))
                    File.Delete(bigPath);
                if (File.Exists(bigPath + ".meta"))
                    File.Delete(bigPath + ".meta");
            }
        }

        [Test]
        public void Batch_NestedBatch_Rejected()
        {
            // M7: batch-in-batch is excluded (recursion/auditing hazard).
            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = new JArray { Op("batch", new JObject { ["operations"] = new JArray() }) },
                    ["dry_run"] = true
                });

                var dry = response.JsonResponse["result"];
                Assert.IsFalse(dry["valid"]?.ToObject<bool>() ?? true);
                StringAssert.Contains("not_batchable", dry["results"][0]["error"]?.ToString());
            }
        }

        [Test]
        public void Batch_NonTransactional_Abort_KeepsAppliedOps_SkipsRest()
        {
            // M7: transactional:false + on_error=abort stops at the failing op but keeps what was
            // already applied (no rollback), and marks the rest skipped.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchNoTx_A" }),
                    Op("add_component", new JObject { ["target"] = "$0.instanceId", ["type"] = "NoSuchTypeXYZ" }), // fails
                    Op("create_gameobject", new JObject { ["name"] = "BatchNoTx_C" }),
                };

                LogAssert.Expect(LogType.Error, new Regex("add_component' failed: Could not resolve component type 'NoSuchTypeXYZ'"));

                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["transactional"] = false
                });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var batch = response.JsonResponse["result"];
                Assert.IsFalse(batch["reverted"]?.ToObject<bool>() ?? true, "transactional:false must not revert");
                Assert.AreEqual(1, batch["applied"]?.ToObject<int>());

                var results = batch["results"] as JArray;
                Assert.IsTrue(results[0]["success"]?.ToObject<bool>() ?? false);
                Assert.IsFalse(results[1]["success"]?.ToObject<bool>() ?? true, "op 1 must fail");
                Assert.IsTrue(results[2]["skipped"]?.ToObject<bool>() ?? false, "op 2 must be skipped (abort)");

                Assert.IsNotNull(ResolveGameObject(results[0]), "op 0's object must survive (no rollback)");
                Assert.AreEqual(0, GameObjectCommands.FindGameObjects(name: "BatchNoTx_C").Count,
                    "op 2 never ran");
            }
        }

        [Test]
        public void Batch_InvalidResultFieldsPath_DoesNotFailTheAppliedOp()
        {
            // Review finding: a malformed result_fields JSONPath threw AFTER the op had mutated
            // state, and the outer catch flipped the op's Success to false — rolling back a
            // succeeded op under on_error=abort. The projection must report the bad path instead.
            using (var server = new PipelineTestServer())
            {
                var operations = new JArray { Op("create_gameobject", new JObject { ["name"] = "BatchProj_BadPath" }) };
                var response = server.Execute("batch", new JObject
                {
                    ["operations"] = operations,
                    ["result_fields"] = new JObject { ["0"] = new JArray("foo[") }
                });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");

                var batch = response.JsonResponse["result"];
                var results = batch["results"] as JArray;
                Assert.IsTrue(results[0]["success"]?.ToObject<bool>() ?? false,
                    "a malformed projection path must not flip an already-applied op to failure");
                Assert.IsFalse(batch["reverted"]?.ToObject<bool>() ?? true,
                    "and must not trigger a rollback of the succeeded op");
                StringAssert.Contains("foo[", results[0]["result"]?["invalidResultFields"]?[0]?.ToString(),
                    "the bad path is reported inside the projection");

                var survivors = GameObjectCommands.FindGameObjects(name: "BatchProj_BadPath");
                Assert.AreEqual(1, survivors.Count, "the op's mutation must survive");
                Track(GameObject.Find("BatchProj_BadPath"));
            }
        }

        [Test]
        public void Batch_TransactionalRevert_LeavesUndoStackClean()
        {
            // Review finding: after Undo.RevertAllDownToGroup discarded the batch group, the
            // enclosing AuthoringUndoScope still collapsed that stale group id on dispose. The
            // scope is now canceled after a revert; the contract this pins: the next PerformUndo
            // must undo the USER's previous operation, not be absorbed by a stray batch group.
            using (var server = new PipelineTestServer())
            {
                Undo.IncrementCurrentGroup();
                var marker = Track(new GameObject("BatchUndoStack_Marker"));
                Undo.RegisterCreatedObjectUndo(marker, "create marker");
                Undo.IncrementCurrentGroup();

                var operations = new JArray
                {
                    Op("create_gameobject", new JObject { ["name"] = "BatchUndoStack_Tx" }),
                    Op("add_component", new JObject { ["target"] = "$0.instanceId", ["type"] = "NoSuchTypeXYZ" }),
                };
                LogAssert.Expect(LogType.Error, new Regex("add_component' failed: Could not resolve component type 'NoSuchTypeXYZ'"));
                var response = server.Execute("batch", new JObject { ["operations"] = operations });
                Assert.IsTrue(response.IsSuccess, $"batch should return HTTP success: {response.RawResponse}");
                Assert.IsTrue(response.JsonResponse["result"]["reverted"]?.ToObject<bool>() ?? false,
                    "precondition: the transactional batch reverted");

                Undo.PerformUndo();
                Assert.IsTrue(marker == null,
                    "PerformUndo after a reverted batch must undo the previous user operation " +
                    "(the marker creation) — a surviving marker means a stray batch group absorbed it");
            }
        }

        [Test]
        public void ProjectResult_MeasuresUtf8Bytes_NotUtf16Chars()
        {
            // Review finding: the per-op cap compared UTF-16 char counts while the wire is UTF-8 —
            // a CJK payload at ~12k chars is ~36 KB on the wire and sailed under the 16 KiB cap.
            var op = new BatchOperationInput { Command = "noop" };
            var opResult = new BatchOperationResult();
            var budget = int.MaxValue; // isolate the per-op cap from the aggregate budget
            var cjk = new string('\u597D', 12_000); // 12k chars, ~36 KB UTF-8

            var projected = BatchCommand.ProjectResult(cjk, JToken.FromObject(cjk), op, 0, null, opResult, ref budget);

            Assert.IsTrue(opResult.ResultTruncated == true,
                "a payload under the char count but over the UTF-8 byte cap must be truncated");
            var marker = (JObject)JToken.FromObject(projected);
            Assert.Greater(marker["length"]?.ToObject<int>() ?? 0, 16 * 1024, "length reports wire bytes");
        }

        [Test]
        public void ProjectResult_AggregateBudget_ElidesLaterResults()
        {
            // Review finding: nothing bounded the SUM of per-op results (200 ops x 16 KiB each is a
            // ~3.2 MB reply). Once the aggregate budget is exhausted, later results become markers.
            var op = new BatchOperationInput { Command = "noop" };
            var budget = BatchCommand.MaxBatchResultBytes;
            var big = new string('a', 15_000); // under the per-op cap; ~17 of these exhaust the budget

            object last = null;
            BatchOperationResult lastResult = null;
            var truncatedAt = -1;
            for (var i = 0; i < 30; i++)
            {
                lastResult = new BatchOperationResult();
                last = BatchCommand.ProjectResult(big, JToken.FromObject(big), op, i, null, lastResult, ref budget);
                if (lastResult.ResultTruncated == true) { truncatedAt = i; break; }
            }

            Assert.GreaterOrEqual(truncatedAt, 2, "several results must fit before the budget trips");
            Assert.AreNotEqual(-1, truncatedAt, "the aggregate budget must eventually trip");
            StringAssert.Contains("aggregate result budget", ((JObject)JToken.FromObject(last))["error"]?.ToString(),
                "the marker explains the aggregate elision");
        }
    }
}
