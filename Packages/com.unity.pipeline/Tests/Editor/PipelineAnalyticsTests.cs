using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Models;
using UnityEngine;
using UnityEditor;
using UnityEngine.Analytics;
using UnityEngine.TestTools;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for the analytics reporter and for the post-command info it is driven by.
    ///
    /// The send call is indirected via <see cref="PipelineAnalytics.s_Send"/> so nothing reaches the
    /// Data Platform from a test run, and the session latch lives in SessionState shared with the
    /// live editor session, so SetUp/TearDown save and restore both.
    /// </summary>
    public class PipelineAnalyticsTests
    {
        Action<IAnalytic> m_OriginalSend;
        (bool started, long startTicks) m_OriginalLatch;
        List<IAnalytic> m_Sent;

        [SetUp]
        public void SetUp()
        {
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());

            m_OriginalSend = PipelineAnalytics.s_Send;
            m_OriginalLatch = PipelineAnalytics.SessionLatch;

            m_Sent = new List<IAnalytic>();
            PipelineAnalytics.s_Send = analytic => m_Sent.Add(analytic);
            PipelineAnalytics.SessionLatch = (false, 0);
        }

        [TearDown]
        public void TearDown()
        {
            PipelineAnalytics.s_Send = m_OriginalSend;
            PipelineAnalytics.SessionLatch = m_OriginalLatch;
        }

        [MenuItem("Window/Pipeline/Tests/StopAnalyticSession", priority = 1000)]
        static void StopAnalyticSession()
        {
            PipelineAnalytics.SendSessionStoppedIfStarted();
        }

        #region Session start

        [Test]
        public void RecordCommandExecuted_OnFirstCommand_SendsSessionStartedFirst()
        {
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("editor_status")));

            Assert.AreEqual(2, m_Sent.Count, "The first command should send the session start and the command");
            Assert.IsInstanceOf<PipelineAnalytics.SessionStartedAnalytic>(m_Sent[0],
                "The session must be opened before the command that opened it is reported");
            Assert.IsInstanceOf<PipelineAnalytics.CommandExecutedAnalytic>(m_Sent[1]);
        }

        [Test]
        public void RecordCommandExecuted_OnLaterCommands_DoesNotReopenTheSession()
        {
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("editor_status")));
            m_Sent.Clear();

            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("recompile")));
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("console")));

            Assert.AreEqual(2, m_Sent.Count, "Two commands should send two events and nothing else");
            CollectionAssert.AllItemsAreInstancesOfType(m_Sent, typeof(PipelineAnalytics.CommandExecutedAnalytic),
                "The session must be opened exactly once per Unity process");
        }

        [Test]
        public void RecordCommandExecuted_OutlivesADomainReload_ViaTheSessionStateLatch()
        {
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("editor_status")));
            var latchAfterFirstCommand = PipelineAnalytics.SessionLatch;
            m_Sent.Clear();

            // A domain reload wipes statics but not SessionState, so the reporter coming back must
            // read the latch and leave the session alone.
            Assert.IsTrue(latchAfterFirstCommand.started, "The latch must be persisted, not held in a static");
            Assert.Greater(latchAfterFirstCommand.startTicks, 0, "The session start time must be persisted too");

            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("recompile")));

            Assert.AreEqual(1, m_Sent.Count, "A reload must not start a second session");
            Assert.IsInstanceOf<PipelineAnalytics.CommandExecutedAnalytic>(m_Sent[0]);
        }

        [Test]
        public void RecordCommandExecuted_WithNoCommand_ReportsNothing()
        {
            // What a request rejected before execution looks like: a transaction, no command.
            var rejected = new CommandExecutionInfo("{}", "{\"error\":\"Invalid Request\"}", null, false, 0);

            PipelineAnalytics.RecordCommandExecuted(rejected);

            Assert.IsEmpty(m_Sent, "A request that never ran a command is not a command execution");
            Assert.IsFalse(PipelineAnalytics.SessionLatch.started,
                "A rejected request must not open a pipeline session");
        }

        #endregion

        #region Command fields

        [Test]
        public void RecordCommandExecuted_ReportsNameTagsSuccessAndDuration()
        {
            var command = Command("create_asset", new[] { "assets/import", "assets" });

            PipelineAnalytics.RecordCommandExecuted(new CommandExecutionInfo("{}", "{}", command, true, 42));

            var data = CommandData();
            Assert.AreEqual("create_asset", data.commandName);
            CollectionAssert.AreEqual(new[] { "assets/import", "assets" }, data.commandTags,
                "Tags are reported in declaration order, as the registry exposes them");
            Assert.IsTrue(data.commandSuccess);
            Assert.AreEqual(42, data.commandDuration);
        }

        [Test]
        public void RecordCommandExecuted_WithUntaggedCommand_ReportsEmptyTags()
        {
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("untagged")));

            Assert.IsNotNull(CommandData().commandTags, "Tags must be an empty array, never null");
            Assert.IsEmpty(CommandData().commandTags);
        }

        [Test]
        public void RecordCommandExecuted_WithFailedExecution_ReportsFailure()
        {
            PipelineAnalytics.RecordCommandExecuted(new CommandExecutionInfo("{}", "{}", Command("build"), false, 7));

            Assert.IsFalse(CommandData().commandSuccess);
        }

        [Test]
        public void RecordCommandExecuted_WithEvalTaggedCommand_ReportsIsEval()
        {
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("eval", new[] { "scripts/eval" })));

            Assert.IsTrue(CommandData().isEval, "A command tagged scripts/eval is a code evaluation");
        }

        [Test]
        public void RecordCommandExecuted_WithEvalCoveredByACommand_ReportsItAsAvoidable()
        {
            // PlayerSettings.SetScriptingBackend is in EvalCoverageMap and set_player_settings is a
            // shipped command, so this eval did not need to be an eval.
            PipelineAnalytics.RecordCommandExecuted(EvalInfo(
                "PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);"));

            Assert.IsTrue(CommandData().isEvalWithExistingCommandAvailable,
                "An eval whose API maps to a shipped command must be reported as avoidable");
        }

        [Test]
        public void RecordCommandExecuted_WithEvalNothingCovers_ReportsItAsNotAvoidable()
        {
            PipelineAnalytics.RecordCommandExecuted(EvalInfo("return UnityEngine.Random.value;"));

            Assert.IsFalse(CommandData().isEvalWithExistingCommandAvailable,
                "An eval touching no mapped API has no existing command to point at");
        }

        [Test]
        public void RecordCommandExecuted_WithEvalMappedToAnAbsentCommand_ReportsItAsNotAvoidable()
        {
            // AssetDatabase.Refresh is mapped to refresh_assets, which is deliberately NOT shipped:
            // the map is a hint layer, so a mapping alone is not coverage. That is a gap for
            // report_evals to surface, not an avoidable eval.
            Assert.IsFalse(CommandRegistry.DiscoverCommands().Any(c => c.Name == "refresh_assets"),
                "Precondition: refresh_assets is still an unbuilt command");

            PipelineAnalytics.RecordCommandExecuted(EvalInfo("AssetDatabase.Refresh();"));

            Assert.IsFalse(CommandData().isEvalWithExistingCommandAvailable,
                "A mapping to a command that does not exist is a gap, not an avoidable eval");
        }

        [Test]
        public void RecordCommandExecuted_WithRunScript_ReportsNotAvoidable()
        {
            // run_script carries the eval tag but runs a project file through a named entry point,
            // not a script-kind snippet, so its body is deliberately not fingerprinted.
            var parameters = new JObject
            {
                ["file"] = "Assets/Whatever.cs",
                ["entry_point"] = "Run",
            };
            PipelineAnalytics.RecordCommandExecuted(new CommandExecutionInfo(
                "{}", "{}", Command("run_script", new[] { "scripts/eval" }), true, 1, parameters));

            var data = CommandData();
            Assert.IsTrue(data.isEval, "run_script is tagged scripts/eval");
            Assert.IsFalse(data.isEvalWithExistingCommandAvailable);
        }

        [Test]
        public void RecordCommandExecuted_WithNoParameters_ReportsNotAvoidable()
        {
            // A detached job reports no parameters; there is no body to judge.
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("eval", new[] { "scripts/eval" })));

            Assert.IsFalse(CommandData().isEvalWithExistingCommandAvailable);
        }

        [Test]
        public void EvalCoversAnExistingCommand_IgnoresANonEvalCommand()
        {
            // The same API written into a non-eval command's parameters must not be judged.
            var parameters = new JObject
            {
                ["code"] = "PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);",
            };

            Assert.IsFalse(PipelineAnalytics.EvalCoversAnExistingCommand(
                Command("write_text_file", new[] { "assets" }), parameters),
                "Only a scripts/eval-tagged command evaluates a snippet");
        }

        [Test]
        public void RecordCommandExecuted_WithOrdinaryCommand_ReportsNotEval()
        {
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("build", new[] { "build" })));

            Assert.IsFalse(CommandData().isEval);
        }

        [Test]
        public void RecordCommandExecuted_ReportsWhetherTheCommandIsUserDefined()
        {
            PipelineAnalytics.RecordCommandExecuted(
                InfoFor(Command("shipped", package: "Unity.Pipeline.Editor")));
            Assert.IsFalse(CommandData().isUserDefinedCommand, "A package command is not user defined");

            m_Sent.Clear();
            PipelineAnalytics.RecordCommandExecuted(
                InfoFor(Command("theirs", package: "Acme.Studio.Tools")));
            Assert.IsTrue(CommandData().isUserDefinedCommand, "A project command is user defined");
        }

        [Test]
        public void RecordCommandExecuted_WithUserDefinedCommand_RedactsNameAndTags()
        {
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command(
                "summon_the_unannounced_feature",
                new[] { "acme/unannounced_thing", "acme" },
                package: "Acme.Studio.Tools")));

            var data = CommandData();
            Assert.AreEqual("<customUserCommand>", data.commandName,
                "A project-declared command name must never leave the machine");
            Assert.IsNotNull(data.commandTags, "Tags must be an empty array, never null");
            Assert.IsEmpty(data.commandTags,
                "Tags are author-chosen too, so a project-declared command reports none");
            Assert.IsTrue(data.isUserDefinedCommand,
                "isUserDefinedCommand still carries the fact the redaction hides the details of");
        }

        [Test]
        public void RecordCommandExecuted_WithPackageCommand_ReportsTheRealNameAndTags()
        {
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command(
                "create_asset", new[] { "assets/import", "assets" }, package: "Unity.Pipeline.Editor")));

            var data = CommandData();
            Assert.AreEqual("create_asset", data.commandName,
                "Redaction must not swallow the shipped command names the data is for");
            CollectionAssert.AreEqual(new[] { "assets/import", "assets" }, data.commandTags,
                "...nor their tags");
        }

        [Test]
        public void IsUserDefinedCommand_CoversBothPackageAssembliesAndAnUnknownOne()
        {
            Assert.IsFalse(PipelineAnalytics.IsUserDefinedCommand("Unity.Pipeline"));
            Assert.IsFalse(PipelineAnalytics.IsUserDefinedCommand("Unity.Pipeline.Editor"));
            Assert.IsFalse(PipelineAnalytics.IsUserDefinedCommand(null),
                "An unresolved declaring assembly is not evidence of a project command");
            Assert.IsTrue(PipelineAnalytics.IsUserDefinedCommand("Assembly-CSharp"));
        }

        #endregion

        #region Session stop

        [Test]
        public void SendSessionStoppedIfStarted_WithNoSession_ReportsNothing()
        {
            PipelineAnalytics.SendSessionStoppedIfStarted();

            Assert.IsEmpty(m_Sent, "An editor that never ran a command has no pipeline session to close");
        }

        [Test]
        public void SendSessionStoppedIfStarted_AfterASession_ReportsADuration()
        {
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("editor_status")));
            m_Sent.Clear();

            PipelineAnalytics.SendSessionStoppedIfStarted();

            Assert.AreEqual(1, m_Sent.Count);
            var data = DataOf<PipelineAnalytics.SessionStoppedData>(m_Sent[0]);
            Assert.GreaterOrEqual(data.sessionDuration, 0, "A session cannot have run for negative time");
        }

        [Test]
        public void SendSessionStoppedIfStarted_CalledTwice_ReportsOnce()
        {
            PipelineAnalytics.RecordCommandExecuted(InfoFor(Command("editor_status")));
            m_Sent.Clear();

            PipelineAnalytics.SendSessionStoppedIfStarted();
            PipelineAnalytics.SendSessionStoppedIfStarted();

            Assert.AreEqual(1, m_Sent.Count, "Closing the session must be idempotent");
        }

        #endregion

        #region What the server reports

        [Test]
        public void OnCommandDone_ForAnExecutedCommand_CarriesTheCommandAndATransaction()
        {
            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("editor_status", null);
                Assert.IsTrue(response.IsSuccess, $"editor_status should succeed: {response.Error}");

                Assert.AreEqual(1, server.Reported.Count, "One request should report exactly once");
                var info = server.Reported[0];
                Assert.IsNotNull(info.Command, "An executed command must be reported");
                Assert.AreEqual("editor_status", info.Command.Name);
                Assert.IsTrue(info.Success);
                Assert.IsNotNull(info.RequestJson, "An /api/exec call is also a transaction");
                Assert.IsNotNull(info.ResponseJson);
                Assert.GreaterOrEqual(info.DurationMs, 0);
            }
        }

        [Test]
        public void OnCommandDone_ForAnExecutedCommand_ReachesTheMainThread()
        {
            using (var server = new PipelineTestServer())
            {
                server.Execute("editor_status", null);

                // Analytics runs from here, so a command that never crosses over is never reported.
                Assert.AreEqual(1, server.ReportedOnMainThread.Count,
                    "An executed command must be queued and drained to the main thread");
                Assert.AreEqual("editor_status", server.ReportedOnMainThread[0].Command.Name);
            }
        }

        [Test]
        public void OnCommandDone_ForAnUnknownCommand_ReportsATransactionButNoExecution()
        {
            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("no_such_command_exists", null);
                Assert.IsFalse(response.IsSuccess, "An unknown command must not report success");

                Assert.AreEqual(1, server.Reported.Count,
                    "The transaction log still wants a rejected request");
                var info = server.Reported[0];
                Assert.IsNull(info.Command, "Nothing executed, so there is no command to report");
                Assert.IsNotNull(info.RequestJson);

                Assert.IsEmpty(server.ReportedOnMainThread,
                    "With no command there is nothing for analytics to say, so it is not queued at all");
            }

            LogAssert.Expect(new Regex("^ExecuteCommandByName: No command named"));
        }

        [Test]
        public void OnCommandDone_ForACommandThatReturnsFailure_ReportsFailureNotEnvelopeSuccess()
        {
            using (var server = new PipelineTestServer())
            {
                // eval reports a compile failure by RETURNING a failed EvalResponse rather than
                // throwing, so nothing classifies it as an error and the outer envelope reports
                // success. The reported info must not inherit that.
                var response = server.Execute("eval", new { code = "return 2 +;", timeout = 5000 });
                Assert.IsTrue(response.IsSuccess, "The exec envelope itself succeeds: nothing threw");
                Assert.IsFalse(response.GetTypedResponse<EvalResponse>().Success,
                    "...while the command's own result reports the compile failure");

                Assert.AreEqual(1, server.Reported.Count);
                Assert.IsFalse(server.Reported[0].Success,
                    "A command that returns its own failure must be reported as a failure");
            }
        }

        #endregion

        #region Helpers

        static CommandExecutionInfo InfoFor(CommandInfo command)
        {
            return new CommandExecutionInfo("{}", "{}", command, true, 1);
        }

        static CommandExecutionInfo EvalInfo(string code)
        {
            return new CommandExecutionInfo("{}", "{}", Command("eval", new[] { "scripts/eval" }), true, 1,
                new JObject { ["code"] = code });
        }

        static CommandInfo Command(string name, string[] tags = null, string package = "Unity.Pipeline.Editor")
        {
            var method = typeof(PipelineAnalyticsTests)
                .GetMethod(nameof(CommandBody), BindingFlags.NonPublic | BindingFlags.Static);

            return new CommandInfo(name, "fixture command", true, method,
                Array.Empty<CommandParameterInfo>(), tags: tags ?? Array.Empty<string>(), package: package);
        }

        // Stands in for a real command implementation: CommandInfo requires a MethodInfo, and these
        // tests never invoke it.
        static void CommandBody()
        {
        }

        PipelineAnalytics.CommandExecutedData CommandData()
        {
            var analytic = m_Sent.OfType<PipelineAnalytics.CommandExecutedAnalytic>().LastOrDefault();
            Assert.IsNotNull(analytic, "Expected a command execution event to have been sent");
            return DataOf<PipelineAnalytics.CommandExecutedData>(analytic);
        }

        static T DataOf<T>(IAnalytic analytic) where T : struct, IAnalytic.IData
        {
            Assert.IsTrue(analytic.TryGatherData(out var data, out var error), "TryGatherData should succeed");
            Assert.IsNull(error, "TryGatherData should not report an error");
            Assert.IsInstanceOf<T>(data, $"Expected the payload to be a {typeof(T).Name}");
            return (T)data;
        }

        #endregion
    }
}
