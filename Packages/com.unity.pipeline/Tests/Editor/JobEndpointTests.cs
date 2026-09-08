using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Pipeline;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for the detached-job surface (CLI-335): POST /api/exec with "job": true returns a
    /// job id immediately; GET /api/job?id=… polls state/progress and retains the result for
    /// reattach after a client timeout; POST /api/job/cancel cancels a queued job outright and
    /// requests cooperative cancellation (PipelineCancellation) of a running one.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    class JobEndpointTests
    {
        private EditorPipelineServer m_Server;
        private Unity.Pipeline.Tests.Runtime.PipelineClient m_PipelineClient;

        /// <summary>Gates job_test_wait so tests control exactly when a job completes.</summary>
        private static readonly ManualResetEventSlim m_ReleaseJobCommand = new ManualResetEventSlim(false);

        /// <summary>Gates job_test_delayed_progress so a test can observe it running before it reports anything.</summary>
        private static readonly ManualResetEventSlim m_ReleaseDelayedProgressCommand = new ManualResetEventSlim(false);

        [SetUp]
        public void SetUp()
        {
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());

            m_ReleaseJobCommand.Reset();
            m_ReleaseDelayedProgressCommand.Reset();

            m_Server = new TestEditorPipelineServer();
            m_Server.Start();
            m_Server.JobRegistry.Reset();
            m_Server.Progress.Clear();

            m_PipelineClient = new Unity.Pipeline.Tests.Runtime.PipelineClient(m_Server);
        }

        [TearDown]
        public void TearDown()
        {
            m_ReleaseJobCommand.Set();
            m_ReleaseDelayedProgressCommand.Set();
            m_PipelineClient?.Dispose();
            m_Server?.JobRegistry.Reset();
            m_Server?.Progress.Clear();
            m_Server?.Stop();
        }

        [CliCommand("job_test_wait", "Test command: report progress and wait for release", MainThreadRequired = false)]
        public static string JobTestWait()
        {
            CliProgress.Report("Job Test", "Waiting", 1, 2, 0.5);
            m_ReleaseJobCommand.Wait(TimeSpan.FromSeconds(15));
            return "job done";
        }

        /// <summary>Test command: runs (State becomes Running) but reports nothing until released.</summary>
        [CliCommand("job_test_delayed_progress", "Test command: wait for release, then report progress", MainThreadRequired = false)]
        public static string JobTestDelayedProgress()
        {
            m_ReleaseDelayedProgressCommand.Wait(TimeSpan.FromSeconds(15));
            CliProgress.Report("Job B", "Reporting late");
            return "job b done";
        }

        [CliCommand("job_test_cancellable", "Test command: loop until cooperatively canceled", MainThreadRequired = false)]
        public static string JobTestCancellable()
        {
            for (var i = 0; i < 300; i++)
            {
                PipelineCancellation.ThrowIfCancellationRequested();
                Thread.Sleep(50);
            }
            return "ran to completion";
        }

        private async Task<JObject> SubmitJobAsync(string command)
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", new
            {
                command,
                parameters = new { },
                job = true
            });
            Assert.IsTrue(response.IsSuccess, $"Job submission should succeed: {response.Error}");
            var json = response.JsonResponse;
            Assert.IsNotNull(json, "Job submission should return JSON");
            Assert.AreEqual(true, json["success"]?.Value<bool>());
            // Standard exec envelope: the job handle is the command's result.
            var result = json["result"];
            Assert.IsNotNull(result?["jobId"], "Submission must return a job id immediately");
            Assert.AreEqual("queued", result["state"]?.ToString());
            return (JObject)result;
        }

        private async Task<JObject> GetJobAsync(string jobId)
        {
            var httpResponse = await m_PipelineClient.GetHttpAsync($"/api/job?id={jobId}");
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"/api/job should return success for known id, got: {httpResponse.StatusCode}");
            return JObject.Parse(await httpResponse.Content.ReadAsStringAsync());
        }

        private async Task<JObject> WaitForStateAsync(string jobId, string state, int attempts = 200)
        {
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var json = await GetJobAsync(jobId);
                if (json["state"]?.ToString() == state)
                {
                    return json;
                }
                await Task.Delay(50);
            }
            Assert.Fail($"Job {jobId} never reached state '{state}'");
            return null;
        }

        /// <summary>
        /// Assert a job field is EXPLICITLY null: /api/job includes null keys by default
        /// (AUTHAPI-21 review) so "no value" is a present key with a JSON null, never an absent key.
        /// </summary>
        private static void AssertExplicitJsonNull(JObject json, string key, string message)
        {
            var token = json[key];
            Assert.IsNotNull(token, $"{message} — and the '{key}' key must be present (nulls are explicit by default)");
            Assert.AreEqual(JTokenType.Null, token.Type, message);
        }

        [Test]
        public async Task DetachedJob_ReturnsIdImmediately_RunsAndRetainsResult()
        {
            var submitted = await SubmitJobAsync("job_test_wait");
            var jobId = submitted["jobId"].ToString();

            // While the command is gated open, the job must report running — with the command's
            // CliProgress snapshot attached. Poll until the snapshot is an actual object: between
            // MarkRunning and the command's first CliProgress.Report the endpoint answers
            // "progress": null explicitly, and indexing that JSON-null token would throw (same
            // race class as the ProgressEndpointTests CI failure).
            await WaitForStateAsync(jobId, "running");
            JObject runningProgress = null;
            for (var attempt = 0; attempt < 100 && runningProgress == null; attempt++)
            {
                runningProgress = (await GetJobAsync(jobId))["progress"] as JObject;
                if (runningProgress == null)
                    await Task.Delay(50);
            }
            Assert.IsNotNull(runningProgress, "Running job never surfaced its progress snapshot");
            Assert.AreEqual("Job Test", runningProgress["title"]?.ToString());

            m_ReleaseJobCommand.Set();
            var completed = await WaitForStateAsync(jobId, "completed");
            Assert.AreEqual("job done", completed["result"]?.ToString());

            // Reattach semantics: the result is retained and can be fetched again.
            var again = await GetJobAsync(jobId);
            Assert.AreEqual("completed", again["state"]?.ToString());
            Assert.AreEqual("job done", again["result"]?.ToString());
        }

        [Test]
        public async Task QueuedJob_DoesNotInheritPreviousJobsProgress()
        {
            // Job A reports progress and holds the exec gate open; job B queues behind it and,
            // once it starts, deliberately reports nothing until released — the window this
            // test inspects is exactly the one where A's leftover progress could otherwise leak.
            var jobA = (await SubmitJobAsync("job_test_wait"))["jobId"].ToString();
            await WaitForStateAsync(jobA, "running");
            var jobB = (await SubmitJobAsync("job_test_delayed_progress"))["jobId"].ToString();

            m_ReleaseJobCommand.Set();
            await WaitForStateAsync(jobA, "completed");

            var runningB = await WaitForStateAsync(jobB, "running");
            AssertExplicitJsonNull(runningB, "progress",
                "Job B is running but hasn't reported yet — it must not surface job A's stale progress");

            m_ReleaseDelayedProgressCommand.Set();
            var completedB = await WaitForStateAsync(jobB, "completed");
            Assert.AreEqual("job b done", completedB["result"]?.ToString());
        }

        [Test]
        public async Task CancelQueuedJob_NeverStarts()
        {
            // Job A holds the exec gate open; job B queues behind it.
            var jobA = (await SubmitJobAsync("job_test_wait"))["jobId"].ToString();
            await WaitForStateAsync(jobA, "running");
            var jobB = (await SubmitJobAsync("job_test_wait"))["jobId"].ToString();

            var cancelResponse = await m_PipelineClient.PostJsonAsync("/api/job/cancel", new { id = jobB });
            Assert.IsTrue(cancelResponse.IsSuccess, $"Cancel should succeed: {cancelResponse.Error}");
            Assert.AreEqual(true, cancelResponse.JsonResponse["cancellationRequested"]?.Value<bool>());

            m_ReleaseJobCommand.Set();
            var canceled = await WaitForStateAsync(jobB, "canceled");
            AssertExplicitJsonNull(canceled, "startedAt", "A job canceled while queued must never start");
            await WaitForStateAsync(jobA, "completed");
        }

        [Test]
        public async Task CancelRunningJob_CooperativeCancellationTakesEffect()
        {
            // The command surfaces cancellation by throwing OperationCanceledException,
            // which the command layer logs as an error before the job runner marks the
            // job canceled — expected here, not a test failure.
            LogAssert.Expect(LogType.Error, new Regex("cancellation was requested"));
            var jobId = (await SubmitJobAsync("job_test_cancellable"))["jobId"].ToString();
            await WaitForStateAsync(jobId, "running");

            var cancelResponse = await m_PipelineClient.PostJsonAsync("/api/job/cancel", new { id = jobId });
            Assert.IsTrue(cancelResponse.IsSuccess, $"Cancel should succeed: {cancelResponse.Error}");

            var canceled = await WaitForStateAsync(jobId, "canceled");
            AssertExplicitJsonNull(canceled, "result", "A cooperatively canceled job must not report a result");
        }

        [Test]
        public async Task JobResponse_IncludesNullsByDefault_OmitNullsParamDropsThem()
        {
            // /api/job has no envelope/payload split — every field is payload — so null keys are
            // explicit by default and omitted only on request via omit_nulls=true (AUTHAPI-21).
            var jobId = (await SubmitJobAsync("job_test_wait"))["jobId"].ToString();
            await WaitForStateAsync(jobId, "running");

            try
            {
                var explicitNulls = await GetJobAsync(jobId);
                AssertExplicitJsonNull(explicitNulls, "completedAt", "A running job has no completion time yet");

                var httpResponse = await m_PipelineClient.GetHttpAsync($"/api/job?id={jobId}&omit_nulls=true");
                Assert.IsTrue(httpResponse.IsSuccessStatusCode, $"/api/job with omit_nulls should succeed, got: {httpResponse.StatusCode}");
                var trimmed = JObject.Parse(await httpResponse.Content.ReadAsStringAsync());
                Assert.IsNull(trimmed["completedAt"], "omit_nulls=true should drop null keys entirely");
                Assert.AreEqual("running", trimmed["state"]?.ToString(), "Non-null fields must survive omit_nulls");
            }
            finally
            {
                m_ReleaseJobCommand.Set();
                await WaitForStateAsync(jobId, "completed");
            }
        }

        [Test]
        public async Task OmitNullsUnrecognizedValue_YieldsWarningInsteadOfSilentCoercion()
        {
            // omit_nulls=1 is not silently treated as false: the reply keeps its explicit nulls
            // AND carries a warnings array telling the agent the accepted spelling, so it can
            // correct itself instead of guessing why nulls are still present (AUTHAPI-21 review).
            var jobId = (await SubmitJobAsync("job_test_wait"))["jobId"].ToString();
            await WaitForStateAsync(jobId, "running");

            try
            {
                var httpResponse = await m_PipelineClient.GetHttpAsync($"/api/job?id={jobId}&omit_nulls=1");
                Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                    $"/api/job with a bad omit_nulls value should still answer, got: {httpResponse.StatusCode}");
                var json = JObject.Parse(await httpResponse.Content.ReadAsStringAsync());

                AssertExplicitJsonNull(json, "completedAt", "Nulls stay explicit when omit_nulls could not be parsed");
                var warnings = json["warnings"] as JArray;
                Assert.IsNotNull(warnings, "The reply must carry a warnings array for the unrecognized value");
                StringAssert.Contains("omit_nulls='1' ignored; expected 'true' or 'false'", warnings[0]?.ToString());

                // A well-formed request has nothing to warn about — warnings is explicitly null.
                var clean = await GetJobAsync(jobId);
                AssertExplicitJsonNull(clean, "warnings", "No warnings expected on a well-formed request");
            }
            finally
            {
                m_ReleaseJobCommand.Set();
                await WaitForStateAsync(jobId, "completed");
            }
        }

        [Test]
        public async Task OmitNullsBareFlag_YieldsWarningInsteadOfSilentFalse()
        {
            // "?omit_nulls" with no '=' is indistinguishable from absent via QueryString[key]
            // (.NET files valueless tokens under the null key), so without a dedicated check the
            // intended opt-in silently did nothing. It must warn like any other unparseable value.
            var jobId = (await SubmitJobAsync("job_test_wait"))["jobId"].ToString();
            await WaitForStateAsync(jobId, "running");

            try
            {
                var httpResponse = await m_PipelineClient.GetHttpAsync($"/api/job?id={jobId}&omit_nulls");
                Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                    $"/api/job with a bare omit_nulls flag should still answer, got: {httpResponse.StatusCode}");
                var json = JObject.Parse(await httpResponse.Content.ReadAsStringAsync());

                AssertExplicitJsonNull(json, "completedAt", "Nulls stay explicit when omit_nulls carried no value");
                var warnings = json["warnings"] as JArray;
                Assert.IsNotNull(warnings, "The reply must carry a warnings array for the valueless flag");
                StringAssert.Contains("omit_nulls given without a value ignored; use omit_nulls=true", warnings[0]?.ToString());
            }
            finally
            {
                m_ReleaseJobCommand.Set();
                await WaitForStateAsync(jobId, "completed");
            }
        }

        [Test]
        public async Task UnknownJobId_Returns404()
        {
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/job?id=does-not-exist");
            Assert.AreEqual(404, (int)httpResponse.StatusCode);
        }

        [Test]
        public async Task Eval_AcceptsTimeoutAboveThirtySeconds()
        {
            // CLI-335: the server-side eval cap was a hard 30000ms; long timeouts are now legal.
            var response = await m_PipelineClient.ExecuteCommandAsync("eval", new
            {
                code = "return 21 * 2;",
                timeout = 120000
            });
            Assert.IsTrue(response.IsSuccess, $"eval with a 120s timeout should be accepted: {response.Error}");
        }

        [Test]
        public async Task DetachedJob_UnconvertibleParameter_KeepsParameterValidationCategory()
        {
            // The job path must classify an unconvertible argument the same way the synchronous
            // /api/exec path does; RunJobDetached's generic catch would say "Command Execution
            // Failed" instead. See the ArgumentException arm there.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", new
            {
                command = "get_console_logs",
                parameters = new { limit = "not-a-number" },
                job = true
            });

            Assert.IsTrue(response.IsSuccess, $"Job submission should succeed: {response.Error}");
            var jobId = response.JsonResponse["result"]?["jobId"]?.ToString();
            Assert.IsNotNull(jobId, "Submission must return a job id immediately");

            LogAssert.Expect(LogType.Error,
                new Regex("^ExecuteCommandByName: Parameter conversion failed: Parameter 'limit'"));

            var failed = await WaitForStateAsync(jobId, "failed");
            Assert.AreEqual("Parameter Validation Failed", failed["error"]?.ToString(),
                "Job path must keep the parameter-validation category rather than reporting a command execution failure");
            Assert.That(failed["errorDetails"]?.ToString(), Contains.Substring("limit"),
                "Details should name the offending parameter");
            Assert.That(failed["errorDetails"]?.ToString(), Contains.Substring("Int32"),
                "Details should name the type the value could not be converted to");
        }
    }
}