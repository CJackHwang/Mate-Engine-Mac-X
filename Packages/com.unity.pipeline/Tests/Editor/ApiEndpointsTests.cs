using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Models;
using Newtonsoft.Json.Linq;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for HTTP API endpoints that CLI tools will consume.
    /// These test the complete server API surface for remote command execution.
    /// </summary>
    class ApiEndpointsTests
    {
        private EditorPipelineServer m_Server;
        private Unity.Pipeline.Tests.Runtime.PipelineClient m_PipelineClient;

        [SetUp]
        public void SetUp()
        {
            // Setup command discovery for tests
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());

            // Start an ISOLATED test server (ports 7850-7899, writes no descriptor) for endpoint
            // testing, so we never bind the live server's port (7800) or clobber its descriptor.
            m_Server = new TestEditorPipelineServer();
            m_Server.Start();

            m_PipelineClient = new Unity.Pipeline.Tests.Runtime.PipelineClient(m_Server);
        }

        [TearDown]
        public void TearDown()
        {
            m_PipelineClient?.Dispose();
            m_Server?.Stop();
        }

        [Test]
        public async Task ApiCommands_GetEndpoint_ReturnsCommandList()
        {
            // Act - Call /api/commands endpoint using unified Pipeline client
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - Response structure
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"Commands endpoint should return success, got: {httpResponse.StatusCode}");
            Assert.AreEqual("application/json", httpResponse.Content.Headers.ContentType.MediaType,
                "Commands endpoint should return JSON content type");

            // Assert - JSON parsing
            var responseJson = JObject.Parse(jsonContent);
            Assert.IsNotNull(responseJson, "Should be able to parse commands JSON");

            // Verify response structure
            Assert.IsNotNull(responseJson["commands"], "Response should have commands array");
            Assert.IsNotNull(responseJson["count"], "Response should have count field");
            Assert.IsNotNull(responseJson["server"], "Response should have server info");

            // Verify commands array contains discovered commands
            var commands = responseJson["commands"] as JArray;
            Assert.Greater(commands.Count, 0, "Should have at least one discovered command");

            // Verify a specific test command is included
            var testCommand = commands.Cast<JObject>()
                .FirstOrDefault(cmd => cmd["name"]?.ToString() == "log_editor");
            Assert.IsNotNull(testCommand, "Should include log_editor test command");
            Assert.AreEqual("Log a message to Unity Editor console", testCommand["description"]?.ToString());
        }

        [Test]
        public async Task ApiCommands_OnEditorServer_ExcludesRuntimeOnlyCommands()
        {
            // Act - List commands from the Editor server
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();
            var responseJson = JObject.Parse(jsonContent);
            var commandNames = (responseJson["commands"] as JArray)
                .Cast<JObject>()
                .Select(cmd => cmd["name"]?.ToString())
                .ToList();

            // Assert - editor commands are listed, runtime-only commands are hidden
            Assert.Contains("editor_status", commandNames, "Editor command should be listed");
            CollectionAssert.DoesNotContain(commandNames, "runtime_status", "Runtime-only eval should be hidden on the Editor server");
            CollectionAssert.DoesNotContain(commandNames, "set_target_framerate", "Runtime-only reload_file_override should be hidden on the Editor server");
        }

        [Test]
        public async Task ApiCommands_DefaultDetail_ReturnsFullMetadata()
        {
            // Act - no detail parameter: full is the default (back-compat with pre-detail clients)
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"Commands endpoint should return success, got: {httpResponse.StatusCode}");
            var responseJson = JObject.Parse(jsonContent);
            var commands = responseJson["commands"] as JArray;
            Assert.Greater(commands.Count, 0, "Should have at least one discovered command");

            var logEditor = commands.Cast<JObject>()
                .First(cmd => cmd["name"]?.ToString() == "log_editor");
            Assert.IsNotNull(logEditor["parameters"], "default detail should include parameters");
            Assert.IsNotNull(logEditor["schema"], "default detail should include schema");
            Assert.IsNotNull(logEditor["tags"], "default detail should include tags");
            Assert.IsNotNull(logEditor["package"], "default detail should include package");
        }

        [Test]
        public async Task ApiCommands_DetailCompact_ReturnsLightweightIndex()
        {
            // Act - detail=compact opts into the lightweight browse/discovery projection
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"detail=compact should return success, got: {httpResponse.StatusCode}");
            var commands = JObject.Parse(jsonContent)["commands"] as JArray;
            Assert.Greater(commands.Count, 0, "Should have at least one discovered command");

            foreach (var cmd in commands.Cast<JObject>())
            {
                Assert.IsNotNull(cmd["name"], "compact entry should have name");
                Assert.IsNotNull(cmd["description"], "compact entry should have description");
                Assert.IsNotNull(cmd["tags"], "compact entry should have tags");
                Assert.IsNotNull(cmd["package"], "compact entry should have package");
                Assert.IsNull(cmd["parameters"], $"compact entry '{cmd["name"]}' should omit parameters");
                Assert.IsNull(cmd["schema"], $"compact entry '{cmd["name"]}' should omit schema");
            }
        }

        [Test]
        public async Task ApiCommands_QueryFilter_MatchesTagCaseInsensitively()
        {
            // Act - 'REGISTRATION' only appears in test_tagged's 'test/registration' tag
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&query=REGISTRATION");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"query filter should return success, got: {httpResponse.StatusCode}");
            var responseJson = JObject.Parse(jsonContent);
            var names = (responseJson["commands"] as JArray)
                .Cast<JObject>()
                .Select(cmd => cmd["name"]?.ToString())
                .ToList();
            Assert.Contains("test_tagged", names, "query should match a command via its tag, case-insensitively");
            CollectionAssert.DoesNotContain(names, "log_editor", "commands matching neither name, description nor tag should be filtered out");
        }

        [Test]
        public async Task ApiCommands_QueryFilter_MatchesNameByPrefix()
        {
            // Act - 'log_edi' is a strict prefix of the 'log_editor' command name
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&query=log_edi");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"query filter should return success, got: {httpResponse.StatusCode}");
            var names = (JObject.Parse(jsonContent)["commands"] as JArray)
                .Cast<JObject>()
                .Select(cmd => cmd["name"]?.ToString())
                .ToList();
            Assert.Contains("log_editor", names, "a query that is a prefix of a command name should match that command");
        }

        [Test]
        public async Task ApiCommands_QueryFilter_NoMatch_ReturnsEmptyNotError()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?query=zzz_definitely_no_match_zzz");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - empty result, not an error
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"a no-match query should still return success, got: {httpResponse.StatusCode}");
            var responseJson = JObject.Parse(jsonContent);
            Assert.AreEqual(0, (responseJson["commands"] as JArray).Count, "no-match query should return an empty commands array");
            Assert.AreEqual(0, responseJson["total"]?.ToObject<int>(), "no-match query should report total 0");
            Assert.AreEqual(0, responseJson["count"]?.ToObject<int>(), "no-match query should report count 0");
        }

        [Test]
        public async Task ApiCommands_TagFilter_MatchesSubtreeBySegmentPrefix()
        {
            // Act / Assert - 'test' matches the exact 'test' tag and the 'test/registration' subtree
            var jsonContent = await (await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&tag=test"))
                .Content.ReadAsStringAsync();
            var names = (JObject.Parse(jsonContent)["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            Assert.Contains("test_tagged", names, "tag=test should match the tagged test command");
            CollectionAssert.DoesNotContain(names, "log_editor", "untagged commands should be filtered out");

            // Drilling into a subtag still matches
            jsonContent = await (await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&tag=test/registration"))
                .Content.ReadAsStringAsync();
            names = (JObject.Parse(jsonContent)["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            Assert.Contains("test_tagged", names, "tag=test/registration should match the subtag directly");

            // Segment-aware: 'tes' is not a whole-segment prefix of 'test'
            jsonContent = await (await m_PipelineClient.GetHttpAsync("/api/commands?tag=tes"))
                .Content.ReadAsStringAsync();
            Assert.AreEqual(0, JObject.Parse(jsonContent)["total"]?.ToObject<int>(),
                "tag matching should respect '/' segment boundaries, not raw string prefixes");
        }

        [Test]
        public async Task ApiCommands_CombinedFilters_AreAnded()
        {
            // Act - log_editor matches the query but carries no tags, so the tag filter excludes it
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?query=log_editor&tag=test");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"combined filters should return success, got: {httpResponse.StatusCode}");
            Assert.AreEqual(0, JObject.Parse(jsonContent)["total"]?.ToObject<int>(),
                "filters should combine with AND");
        }

        [Test]
        public async Task ApiCommands_GroupByPackage_GroupsCommandsByOriginatingAssembly()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&group_by=package&query=log_editor");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - grouped responses carry 'groups' instead of 'commands'
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"group_by=package should return success, got: {httpResponse.StatusCode}");
            var responseJson = JObject.Parse(jsonContent);
            Assert.IsNull(responseJson["commands"], "grouped response should not have a flat commands array");
            var groups = responseJson["groups"] as JArray;
            Assert.IsNotNull(groups, "grouped response should have a groups array");

            var testGroup = groups.Cast<JObject>()
                .FirstOrDefault(g => g["package"]?.ToString() == "Unity.Pipeline.Tests.Editor");
            Assert.IsNotNull(testGroup, "log_editor's assembly should appear as a package group");
            var groupNames = (testGroup["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            Assert.Contains("log_editor", groupNames, "the group should contain the matching command");
        }

        [Test]
        public async Task ApiCommands_GroupByTag_ReturnsNestedTagTree()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&group_by=tag&tag=test");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - top-level 'test' node with a nested 'test/registration' child
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"group_by=tag should return success, got: {httpResponse.StatusCode}");
            var groups = JObject.Parse(jsonContent)["groups"] as JArray;
            Assert.IsNotNull(groups, "grouped response should have a groups array");

            var testNode = groups.Cast<JObject>().FirstOrDefault(g => g["tag"]?.ToString() == "test");
            Assert.IsNotNull(testNode, "should have a top-level 'test' tag node");
            var directNames = (testNode["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            Assert.Contains("test_tagged", directNames, "test_tagged carries the exact 'test' tag");

            var childNode = (testNode["children"] as JArray)?.Cast<JObject>()
                .FirstOrDefault(g => g["tag"]?.ToString() == "test/registration");
            Assert.IsNotNull(childNode, "'test/registration' should nest under 'test'");
            var childNames = (childNode["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            Assert.Contains("test_tagged", childNames, "test_tagged also carries the 'test/registration' tag");
        }

        [Test]
        public async Task ApiCommands_GroupByInvalid_ReturnsBadRequestListingAcceptedValues()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?group_by=namespace");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"Invalid group_by value should be rejected with 400. Response: {jsonContent}");
            StringAssert.Contains("flat", jsonContent, "error should list accepted value 'flat'");
            StringAssert.Contains("package", jsonContent, "error should list accepted value 'package'");
            StringAssert.Contains("tag", jsonContent, "error should list accepted value 'tag'");
        }

        [Test]
        public async Task ApiCommands_Pagination_SlicesNameSortedListDeterministically()
        {
            // Arrange - the unpaginated listing (name-sorted for deterministic pages)
            var allJson = await (await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact"))
                .Content.ReadAsStringAsync();
            var allNames = (JObject.Parse(allJson)["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            CollectionAssert.AreEqual(allNames.OrderBy(n => n, System.StringComparer.Ordinal).ToList(), allNames,
                "commands should be name-sorted so pagination windows are deterministic");

            // Act
            var pageJson = await (await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&offset=1&limit=2"))
                .Content.ReadAsStringAsync();
            var pageResponse = JObject.Parse(pageJson);
            var pageNames = (pageResponse["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();

            // Assert
            CollectionAssert.AreEqual(allNames.Skip(1).Take(2).ToList(), pageNames,
                "offset/limit should slice the same ordering the unpaginated listing uses");
            Assert.AreEqual(2, pageResponse["count"]?.ToObject<int>(), "count should be the returned page size");
            Assert.AreEqual(allNames.Count, pageResponse["total"]?.ToObject<int>(), "total should be the match count before pagination");
            Assert.AreEqual(1, pageResponse["offset"]?.ToObject<int>(), "offset should be echoed");
            Assert.AreEqual(2, pageResponse["limit"]?.ToObject<int>(), "limit should be echoed");
        }

        [Test]
        public async Task ApiCommands_PaginationInvalid_ReturnsBadRequest()
        {
            // Act / Assert - negative offset
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?offset=-1");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"negative offset should be rejected with 400. Response: {jsonContent}");
            StringAssert.Contains("offset", jsonContent, "error should name the offending parameter");

            // Non-numeric limit
            httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?limit=abc");
            jsonContent = await httpResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"non-numeric limit should be rejected with 400. Response: {jsonContent}");
            StringAssert.Contains("limit", jsonContent, "error should name the offending parameter");
        }

        [Test]
        public async Task ApiCommands_SortByPackage_OrdersByPackageThenName()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&sort=package");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - ordered by originating package, ties broken by name
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"sort=package should return success, got: {httpResponse.StatusCode}");
            var pairs = (JObject.Parse(jsonContent)["commands"] as JArray)
                .Cast<JObject>()
                .Select(cmd => (Package: cmd["package"]?.ToString(), Name: cmd["name"]?.ToString()))
                .ToList();
            var expected = pairs
                .OrderBy(p => p.Package, System.StringComparer.Ordinal)
                .ThenBy(p => p.Name, System.StringComparer.Ordinal)
                .ToList();
            CollectionAssert.AreEqual(expected, pairs,
                "sort=package should order by package with name as tiebreak");
        }

        [Test]
        public async Task ApiCommands_OrderDesc_ReversesNameSort()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&order=desc");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"order=desc should return success, got: {httpResponse.StatusCode}");
            var names = (JObject.Parse(jsonContent)["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            CollectionAssert.AreEqual(names.OrderByDescending(n => n, System.StringComparer.Ordinal).ToList(), names,
                "order=desc should reverse the default name sort");
        }

        [Test]
        public async Task ApiCommands_SortInvalid_ReturnsBadRequestListingAcceptedValues()
        {
            // Act / Assert - unknown sort key
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?sort=alphabetical");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"Invalid sort value should be rejected with 400. Response: {jsonContent}");
            StringAssert.Contains("name", jsonContent, "error should list accepted value 'name'");
            StringAssert.Contains("package", jsonContent, "error should list accepted value 'package'");

            // Unknown order direction
            httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?order=upside_down");
            jsonContent = await httpResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"Invalid order value should be rejected with 400. Response: {jsonContent}");
            StringAssert.Contains("asc", jsonContent, "error should list accepted value 'asc'");
            StringAssert.Contains("desc", jsonContent, "error should list accepted value 'desc'");
        }

        [Test]
        public async Task ApiCommands_DetailFull_ReturnsFullMetadataWithTagsAndPackage()
        {
            // Act - detail=full opts into the complete per-command metadata
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=full");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"detail=full should return success, got: {httpResponse.StatusCode}");
            var commands = JObject.Parse(jsonContent)["commands"] as JArray;
            var logEditor = commands.Cast<JObject>()
                .First(cmd => cmd["name"]?.ToString() == "log_editor");
            Assert.IsNotNull(logEditor["parameters"], "full detail should include parameters");
            Assert.IsNotNull(logEditor["schema"], "full detail should include schema");
            Assert.IsNotNull(logEditor["tags"], "full detail should include tags");
            Assert.AreEqual("Unity.Pipeline.Tests.Editor", logEditor["package"]?.ToString(),
                "full detail should include the originating package");
        }

        [Test]
        public async Task ApiCommands_InvalidDetail_ReturnsBadRequestListingAcceptedValues()
        {
            // Act - an unsupported detail value
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=verbose");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - rejected with a clear error naming the accepted values
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"Invalid detail value should be rejected with 400. Response: {jsonContent}");
            var responseJson = JObject.Parse(jsonContent);
            Assert.IsNotNull(responseJson["error"], "400 response should have error field");
            StringAssert.Contains("compact", jsonContent, "error should list accepted value 'compact'");
            StringAssert.Contains("full", jsonContent, "error should list accepted value 'full'");
        }

        [Test]
        public async Task ApiCommands_CommandStructure_ContainsRequiredFields()
        {
            // Act - Call /api/commands endpoint using unified Pipeline client
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();
            var responseJson = JObject.Parse(jsonContent);

            // Assert - Command structure validation
            var commands = responseJson["commands"] as JArray;
            var firstCommand = commands[0] as JObject;

            // Required command fields
            Assert.IsNotNull(firstCommand["name"], "Command should have name field");
            Assert.IsNotNull(firstCommand["description"], "Command should have description field");
            Assert.IsNotNull(firstCommand["parameters"], "Command should have parameters array");
            Assert.IsNotNull(firstCommand["schema"], "Command should have JSON schema");
            Assert.IsNotNull(firstCommand["mainThreadRequired"], "Command should have mainThreadRequired field");

            // Verify schema is valid JSON
            var schema = firstCommand["schema"]?.ToString();
            var schemaJson = JObject.Parse(schema);
            Assert.AreEqual(firstCommand["name"]?.ToString(), schemaJson["title"]?.ToString(),
                "Schema title should match command name");
        }

        [Test]
        public async Task ApiStatus_GetBasicStatus_ReturnsServerInfo()
        {
            // Act - Call basic /api/status endpoint using unified Pipeline client
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/status");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - Response structure
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"Basic status endpoint should return success, got: {httpResponse.StatusCode}");
            Assert.AreEqual("application/json", httpResponse.Content.Headers.ContentType.MediaType,
                "Basic status endpoint should return JSON content type");

            // Assert - JSON structure (basic server info only, no Editor APIs)
            var statusJson = JObject.Parse(jsonContent);
            Assert.IsNotNull(statusJson["status"], "Should have status field");

            // Verify basic values
            Assert.AreEqual("ready", statusJson["status"]?.ToString());
        }

        [Test]
        public async Task ApiEditorStatus_GetDetailedStatus_ReturnsEditorInfo()
        {
            // Act - Call rich /api/editor_status endpoint using unified Pipeline client
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/editor_status");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - Response structure
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"Editor status endpoint should return success, got: {httpResponse.StatusCode}. Response: {jsonContent}");
            Assert.AreEqual("application/json", httpResponse.Content.Headers.ContentType.MediaType,
                "Editor status endpoint should return JSON content type");

            // Assert - Rich Editor status structure
            var statusJson = JObject.Parse(jsonContent);
            Assert.IsNotNull(statusJson["status"], "Should have overall status");
            Assert.IsNotNull(statusJson["compiling"], "Should have compiling state");
            Assert.IsNotNull(statusJson["domainReloadInProgress"], "Should have domain reload state");
            Assert.IsNotNull(statusJson["playMode"], "Should have play mode state");
            Assert.IsNotNull(statusJson["unityVersion"], "Should have Unity version");

            // Verify Editor-specific data is present
            Assert.Contains(statusJson["status"]?.ToString(), new[] { "ready", "compiling", "playing", "reloading" });
            Assert.Contains(statusJson["playMode"]?.ToString(), new[] { "stopped", "playing", "paused" });
            Assert.IsInstanceOf<bool>(statusJson["compiling"]?.ToObject<bool>());
        }

        [Test]
        public async Task ApiExec_PostCommand_ExecutesSuccessfully()
        {
            // Arrange
            var commandRequest = new CommandExecutionRequest("log_editor");
            commandRequest.Parameters["message"] = "Test message from CLI";

            // Act - Execute command via /api/exec endpoint using unified Pipeline client
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", commandRequest);
            var responseContent = response.RawResponse;

            // Assert - Response structure
            Assert.IsTrue(response.IsSuccess,
                $"Exec endpoint should return success, got: {response.StatusCode}. Response: {responseContent}");

            // Assert - JSON parsing and structure
            Assert.IsTrue(response.HasValidJson, "Response should have valid JSON");
            var responseJson = response.JsonResponse;

            // Lean default (AUTHAPI-21): a success is just success + result. The always-on metadata
            // (command, executedAt, executionTimeMs) is dropped unless the request opts into verbose.
            Assert.IsNotNull(responseJson["success"], "Response should have success field");
            Assert.IsTrue(responseJson["success"].ToObject<bool>(), "Command should execute successfully");
            Assert.IsNull(responseJson["command"], "Lean envelope should omit the command echo");
            Assert.IsNull(responseJson["executedAt"], "Lean envelope should omit executedAt");
            Assert.IsNull(responseJson["executionTimeMs"], "Lean envelope should omit executionTimeMs");
        }

        [Test]
        public async Task ApiExec_VerboseFlag_IncludesEnvelopeMetadata()
        {
            // Arrange - opt into the full envelope via the request's verbose flag.
            var commandRequest = new CommandExecutionRequest("log_editor") { Verbose = true };
            commandRequest.Parameters["message"] = "Test message from CLI";

            // Act
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", commandRequest);

            // Assert - the metadata the lean envelope drops is present again.
            Assert.IsTrue(response.IsSuccess,
                $"Exec endpoint should return success, got: {response.StatusCode}. Response: {response.RawResponse}");
            var responseJson = response.JsonResponse;
            Assert.IsTrue(responseJson["success"].ToObject<bool>(), "Command should execute successfully");
            Assert.IsNotNull(responseJson["command"], "Verbose envelope should include the command");
            Assert.AreEqual("log_editor", responseJson["command"]?.ToString());
            Assert.IsNotNull(responseJson["executedAt"], "Verbose envelope should include executedAt");
        }

        [Test]
        public async Task ApiExec_OmitNullsFlag_AcceptedEndToEnd()
        {
            // The payload-null semantics themselves are pinned at the serializer level
            // (ResponseEnvelopeSizeTests); this covers the HTTP wiring: the request flag
            // deserializes and the reply still parses.
            var request = new CommandExecutionRequest("log_editor") { OmitNulls = true };
            request.Parameters["message"] = "omitNulls round-trip";

            var response = await m_PipelineClient.PostJsonAsync("/api/exec", request);

            Assert.IsTrue(response.IsSuccess,
                $"Exec with omitNulls should succeed, got: {response.StatusCode}. Response: {response.RawResponse}");
            Assert.IsTrue(response.JsonResponse["success"].ToObject<bool>());
        }

        [Test]
        public async Task ApiExec_ValidationFailure_HonorsVerboseFlag()
        {
            // A post-parse validation failure (structurally valid JSON, invalid content) can and
            // must honor the request's verbose flag — that's precisely when a debugging client
            // wants the full envelope. Pre-parse failures (unparseable JSON) can't, by definition.
            var verboseResponse = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { command = "log_editor", timeout = -1, verbose = true });
            var leanResponse = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { command = "log_editor", timeout = -1 });

            Assert.AreEqual(400, verboseResponse.StatusCode, "Invalid timeout should be rejected");
            Assert.IsNotNull(verboseResponse.JsonResponse["executedAt"],
                "A verbose request's validation failure should carry the full envelope");

            Assert.AreEqual(400, leanResponse.StatusCode, "Invalid timeout should be rejected");
            Assert.IsNull(leanResponse.JsonResponse["executedAt"],
                "A lean request's validation failure should stay lean");
        }

        [Test]
        public async Task ApiExec_NullJsonBody_Returns400InvalidRequest()
        {
            // A body of literal JSON "null" parses without a JsonException but deserializes to a
            // null request — it must be rejected cleanly, not NRE inside the handler.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", null);

            Assert.AreEqual(400, response.StatusCode,
                $"A null JSON body should be rejected with 400. Response: {response.RawResponse}");
            Assert.IsTrue(response.HasValidJson, "The rejection should be a structured JSON reply");
            Assert.AreEqual("Invalid Request", response.JsonResponse["error"]?.ToString());
            StringAssert.Contains("JSON object", response.JsonResponse["errorDetails"]?.ToString());
        }

        [Test]
        public async Task ApiExec_InvalidCommand_ReturnsError()
        {
            // Arrange
            var invalidRequest = new CommandExecutionRequest("nonexistent_command");

            // Act - Execute invalid command via /api/exec endpoint using unified Pipeline client
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", invalidRequest);
            var responseContent = response.RawResponse;

            LogAssert.Expect(new Regex("^ExecuteCommandByName: No command named"));

            // Assert - Should return error
            Assert.IsFalse(response.IsSuccess, "Should return error for invalid command");

            Assert.IsTrue(response.HasValidJson, "Error response should have valid JSON");
            var responseJson = response.JsonResponse;
            Assert.IsNotNull(responseJson["error"], "Error response should have error field");
            // A failure has no `message` (that's for success), so the lean envelope omits it; assert on
            // the fields a failure actually carries instead. (AUTHAPI-21)
            Assert.IsFalse(responseJson["success"].ToObject<bool>(), "Error response should report success=false");
        }

        [Test]
        public async Task ApiExec_MissingRequiredParameter_ReturnsValidationError()
        {
            // Arrange - Try to execute log_editor without required message parameter
            var invalidRequest = new CommandExecutionRequest("log_editor");
            // Intentionally not setting the 'message' parameter to test validation

            // Act - Execute command with missing parameter via /api/exec endpoint using unified Pipeline client
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", invalidRequest);
            var responseContent = response.RawResponse;

            LogAssert.Expect("ExecuteCommandByName: Parameter validation failed: Required parameter 'message' is missing or empty");

            // Assert - Should return validation error
            Assert.IsFalse(response.IsSuccess, "Should return error for missing required parameter");

            Assert.IsTrue(response.HasValidJson, "Error response should have valid JSON");
            var responseJson = response.JsonResponse;
            Assert.IsNotNull(responseJson["error"], "Should have error field");
            Assert.That(responseJson["errorDetails"]?.ToString(),
                Contains.Substring("message").IgnoreCase,
                "Error should mention missing message parameter");
        }

        [Test]
        public async Task ApiExec_OversizedBody_ReturnsPayloadTooLarge()
        {
            // Arrange - a request whose body exceeds the 1 MiB cap (Content-Length will advertise it).
            var oversized = new CommandExecutionRequest("log_editor");
            oversized.Parameters["message"] = new string('a', (1 * 1024 * 1024) + 1024);

            // Act
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", oversized);

            // Assert - rejected with 413 before the command ever runs.
            Assert.AreEqual(413, response.StatusCode,
                $"Oversized body should be rejected with 413. Response: {response.RawResponse}");
            Assert.IsTrue(response.HasValidJson, "413 response should have valid JSON");
            Assert.That(response.JsonResponse["error"]?.ToString(),
                Contains.Substring("Payload Too Large"),
                "413 response should identify the payload-too-large error");
        }

        [Test]
        public async Task ApiExec_UnconvertibleParameter_ReturnsErrorInsteadOfSubstitutingDefault()
        {
            // Arrange - 'limit' is an int defaulting to 100. Falling back to that default would
            // hand the caller far more entries than it asked for, with no way to detect it.
            var request = new CommandExecutionRequest("get_console_logs");
            request.Parameters["limit"] = "not-a-number";

            // Act
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", request);

            LogAssert.Expect(new Regex("^ExecuteCommandByName: Parameter conversion failed: Parameter 'limit'"));

            // Assert - pin the HTTP contract, not just "some failure": without the status and
            // category, a regression to the command-execution handler would still pass.
            Assert.IsFalse(response.IsSuccess, "Unconvertible argument should not report success");
            Assert.AreEqual(400, response.StatusCode,
                $"Unconvertible argument should be a 400, not another failure class. Response: {response.RawResponse}");
            Assert.IsTrue(response.HasValidJson, "Error response should have valid JSON");

            var responseJson = response.JsonResponse;
            Assert.AreEqual("Parameter Validation Failed", responseJson["error"]?.ToString(),
                "Should be classified as a parameter validation failure");
            Assert.That(responseJson["errorDetails"]?.ToString(),
                Contains.Substring("limit"),
                "Error should name the offending parameter");
            Assert.That(responseJson["errorDetails"]?.ToString(),
                Contains.Substring("Int32"),
                "Error should name the type the value could not be converted to");
        }

        [Test]
        public async Task ApiExec_NumericStringParameter_IsConvertedAndApplied()
        {
            // Arrange - a numeric *string* is what MCP clients send today, since the tool schema
            // advertises numeric params as strings. Seed more entries than the limit asked for:
            // asserting success alone would pass even if "5" were dropped for the default of 100,
            // so the count is what proves the value was applied.
            for (var i = 0; i < 12; i++)
            {
                UnityEngine.Debug.Log($"CLI978_numeric_string_seed_{i}");
            }

            var request = new CommandExecutionRequest("get_console_logs");
            request.Parameters["limit"] = "5";

            // Act
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", request);

            // Assert
            Assert.IsTrue(response.IsSuccess,
                $"A parseable numeric string should still be accepted. Response: {response.RawResponse}");
            Assert.AreEqual(5, ReadReturnedCount(response.JsonResponse),
                "limit=\"5\" must be honoured, not replaced by the command's default");
        }

        /// <summary>
        /// Reads <c>result.returned</c> from an /api/exec envelope. The envelope carries `result`
        /// either as a nested object or as a JSON string depending on the command, so handle both.
        /// </summary>
        private static int ReadReturnedCount(JObject responseJson)
        {
            var result = responseJson["result"];
            if (result is JValue { Type: JTokenType.String } stringResult)
            {
                result = JToken.Parse(stringResult.Value<string>());
            }

            var returned = result?["returned"];
            Assert.IsNotNull(returned, $"Response should carry result.returned: {responseJson}");
            return returned.ToObject<int>();
        }

        [Test]
        public async Task ApiExec_ConverterDeclinedToken_IsRejectedNotTreatedAsOmitted()
        {
            // ObjectRefConverter returns null for an unsupported token kind instead of throwing, so
            // {"parent": true} used to read as an omitted argument and create_gameobject ran with
            // parent == null — creating the object at the scene root rather than rejecting the
            // malformed handle. A declined token must be a conversion error, not a silent default.
            var request = new CommandExecutionRequest("create_gameobject");
            request.Parameters["name"] = "CLI978_should_not_be_created";
            request.Parameters["parent"] = true;

            var response = await m_PipelineClient.PostJsonAsync("/api/exec", request);

            LogAssert.Expect(new Regex("^ExecuteCommandByName: Parameter conversion failed: Parameter 'parent'"));

            Assert.IsFalse(response.IsSuccess, "A declined token should not succeed");
            Assert.AreEqual(400, response.StatusCode,
                $"Should be a parameter-validation 400. Response: {response.RawResponse}");
            Assert.AreEqual("Parameter Validation Failed", response.JsonResponse["error"]?.ToString());
            Assert.That(response.JsonResponse["errorDetails"]?.ToString(),
                Contains.Substring("parent"), "Error should name the offending parameter");
            Assert.That(response.JsonResponse["errorDetails"]?.ToString(),
                Contains.Substring("ObjectRef"), "Error should name the target type");
        }

        [Test]
        public async Task ApiExec_ExplicitJsonNull_IsStillAcceptedAsAValue()
        {
            // The guard above must not catch a legitimately null optional argument: an explicit JSON
            // null is the one value allowed to convert to null, and it falls back to the default.
            var request = new CommandExecutionRequest("get_console_logs");
            request.Parameters["severity"] = null;
            request.Parameters["limit"] = 5;

            var response = await m_PipelineClient.PostJsonAsync("/api/exec", request);

            Assert.IsTrue(response.IsSuccess,
                $"An explicit JSON null should remain acceptable. Response: {response.RawResponse}");
            Assert.IsTrue(response.JsonResponse["success"].ToObject<bool>(), "Command should succeed");
        }

        [Test]
        public async Task ApiExec_EmptyObjectRefString_IsAcceptedNotRejectedAsUnconvertible()
        {
            // An empty ObjectRef string is a documented value, not a malformed one:
            // ObjectRefConverter.FromString returns null for it and set_parent advertises
            // "Omit (or empty) to move the object to the scene root". The declined-token guard must
            // not reject it. Uses a target that cannot resolve, so this asserts parameter BINDING
            // without reparenting anything: the call still fails, but not as a validation failure.
            var request = new CommandExecutionRequest("set_parent");
            request.Parameters["target"] = "/CLI978_no_such_object";
            request.Parameters["parent"] = "";

            var response = await m_PipelineClient.PostJsonAsync("/api/exec", request);

            // The command runs and fails at target resolution — which is itself the proof that
            // `parent` bound. That failure logs an error, so it has to be declared.
            LogAssert.Expect(new Regex("Could not resolve 'target'"));

            // The error CATEGORY is not the discriminator here: set_parent's own resolve failure
            // also throws ArgumentException, so it lands in "Parameter Validation Failed" too. What
            // separates binding from execution is the detail text.
            Assert.IsFalse(response.IsSuccess, "An unresolvable target should still fail");
            Assert.That(response.JsonResponse["errorDetails"]?.ToString() ?? string.Empty,
                Does.Not.Contain("could not be converted"),
                $"An empty ObjectRef string must bind, not be rejected as unconvertible. Response: {response.RawResponse}");
            Assert.That(response.JsonResponse["errorDetails"]?.ToString() ?? string.Empty,
                Contains.Substring("Could not resolve 'target'"),
                "The failure must come from the command body, proving 'parent' bound");
        }

        // ------------------------------------------------------------- raw command-line forms
        // {"argv":[...]} and {"commandLine":"..."}. The server
        // tokenizes/binds, so a client needs no schema knowledge and spends one request
        // instead of two.

        [Test]
        public async Task ApiExec_ArgvForm_ExecutesTheCommand()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "test_types", "--count", "5" } });

            Assert.AreEqual(200, response.StatusCode, $"Response: {response.RawResponse}");
            Assert.IsTrue(response.IsCommandSuccess, $"Response: {response.RawResponse}");
        }

        [Test]
        public async Task ApiExec_CommandLineForm_ExecutesTheCommand()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { commandLine = "test_types --count 5" });

            Assert.AreEqual(200, response.StatusCode, $"Response: {response.RawResponse}");
            Assert.IsTrue(response.IsCommandSuccess, $"Response: {response.RawResponse}");
        }

        [Test]
        public async Task ApiExec_RawForm_EchoesTheBoundParameters()
        {
            // The CLI renders this in four output formats; without the echo it has nothing
            // to show, because it no longer binds client-side.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "test_types", "--count", "5" } });

            var echoed = response.JsonResponse["parameters"] as JObject;
            Assert.IsNotNull(echoed, $"argv requests must echo bound parameters. Response: {response.RawResponse}");
            Assert.AreEqual("5", echoed["count"]?.ToString());
        }

        [Test]
        public async Task ApiExec_StructuredForm_DoesNotEchoParameters()
        {
            // Back-compat: the structured path's envelope must not grow a key.
            var request = new CommandExecutionRequest("test_types");
            request.Parameters["count"] = 5;

            var response = await m_PipelineClient.PostJsonAsync("/api/exec", request);

            Assert.IsNull(response.JsonResponse["parameters"],
                $"structured requests must be byte-identical to before. Response: {response.RawResponse}");
        }

        [Test]
        public async Task ApiExec_ArgvWithSpacesInAToken_IsNotRetokenized()
        {
            // argv is already split; re-splitting it would corrupt any value containing a space.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "log_editor", "hello world" } });

            var echoed = response.JsonResponse["parameters"] as JObject;
            Assert.IsNotNull(echoed, $"Response: {response.RawResponse}");
            Assert.AreEqual("hello world", echoed["message"]?.ToString());
        }

        [Test]
        public async Task ApiExec_CommandLineQuoting_GroupsIntoOneToken()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { commandLine = "log_editor \"hello world\"" });

            var echoed = response.JsonResponse["parameters"] as JObject;
            Assert.IsNotNull(echoed, $"Response: {response.RawResponse}");
            Assert.AreEqual("hello world", echoed["message"]?.ToString());
        }

        // ------------------------------------------------------------------ mutual exclusion

        [Test]
        public async Task ApiExec_CommandAndArgvTogether_IsRejected()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { command = "test_types", argv = new[] { "test_types" } });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
        }

        [Test]
        public async Task ApiExec_ArgvAndCommandLineTogether_IsRejected()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "test_types" }, commandLine = "test_types" });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
        }

        [Test]
        public async Task ApiExec_ParametersAlongsideArgv_IsRejectedNotIgnored()
        {
            // Silently dropping the payload would recreate exactly the absent-vs-null
            // ambiguity this codebase legislates against in the lean envelope.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "test_types" }, parameters = new { count = 5 } });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
        }

        [Test]
        public async Task ApiExec_EmptyArgv_IsRejected()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new string[0] });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
        }

        [Test]
        public async Task ApiExec_BlankCommandLine_IsRejected()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { commandLine = "   " });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
        }

        // ------------------------------------------------------------------ argument errors

        [Test]
        public async Task ApiExec_UnknownFlag_ReturnsInvalidCommandArgsWithProblemsAndSchema()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "log_editor", "--mesage", "hi" } });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");

            var json = response.JsonResponse;
            Assert.AreEqual("INVALID_COMMAND_ARGS", json["errorCode"]?.ToString(),
                "errorCode is the discriminator; every /api/exec failure is already 400");

            var problems = json["argProblems"] as JArray;
            Assert.IsNotNull(problems, $"Response: {response.RawResponse}");
            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual("unknownName", problems[0]["kind"]?.ToString(),
                "kind is camelCase on the wire even though C# spells it PascalCase");
            Assert.AreEqual("mesage", problems[0]["name"]?.ToString());
            Assert.AreEqual("message", problems[0]["suggestion"]?.ToString());

            // Replaces the retired per-exec schema fetch: shaped like a /api/commands entry
            // so the CLI's existing renderer consumes it with no new code.
            var schema = json["commandSchema"] as JObject;
            Assert.IsNotNull(schema, "an argument error must carry the command's schema");
            Assert.AreEqual("log_editor", schema["name"]?.ToString());
            Assert.IsNotNull(schema["parameters"] as JArray);
        }

        [Test]
        public async Task ApiExec_TypeMismatch_ReturnsTypeMismatchProblem()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "test_types", "--count", "abc" } });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");

            var problems = response.JsonResponse["argProblems"] as JArray;
            Assert.IsNotNull(problems, $"Response: {response.RawResponse}");
            Assert.AreEqual("typeMismatch", problems[0]["kind"]?.ToString());
            Assert.AreEqual("Int32", problems[0]["expectedType"]?.ToString());
        }

        [Test]
        public async Task ApiExec_UnbalancedQuoteInCommandLine_IsRejected()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { commandLine = "log_editor \"unbalanced" });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
            Assert.That(response.JsonResponse["errorDetails"]?.ToString(),
                Contains.Substring("quote").IgnoreCase);
        }

        [Test]
        public async Task ApiExec_ATokenizerFailure_IsAnInvalidRequestWithNoErrorCode()
        {
            // A command line that will not tokenize never reached a command, so it is a malformed
            // request shape - the same family as two command forms at once. Reporting it as
            // INVALID_COMMAND_ARGS promised an argument diagnosis the reply cannot carry: there is
            // no commandSchema and no argProblems, because there is no command. A client keying on
            // errorCode to render usage found nothing to render.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { commandLine = "log_editor \"unbalanced" });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
            Assert.AreEqual("Invalid Request", response.JsonResponse["error"]?.ToString());
            Assert.IsNull(response.JsonResponse["errorCode"],
                "no command was resolved, so the argument-error discriminator must be absent");
            Assert.IsNull(response.JsonResponse["commandSchema"]);
            Assert.IsNull(response.JsonResponse["argProblems"]);
        }

        [Test]
        public async Task ApiExec_ARequestWithNoCommandForm_IsAnInvalidRequest()
        {
            // Relaxing `command` from Required.Always (so the raw forms may omit it) moved this
            // case out of JSON.NET's deserializer and into Validate(). Both answer 400, but the
            // `error` value changed from "Invalid JSON" to "Invalid Request" - pinned here so the
            // wording is a decision rather than a side effect.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", new { });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
            Assert.AreEqual("Invalid Request", response.JsonResponse["error"]?.ToString());
            Assert.AreEqual("Command name is required",
                response.JsonResponse["errorDetails"]?.ToString());
            Assert.IsNull(response.JsonResponse["errorCode"]);
        }

        [Test]
        public async Task ApiExec_ArgumentErrorSchema_MatchesTheCatalogEntryWithoutTheGeneratedSchema()
        {
            // commandSchema is the /api/commands entry minus the generated JSON schema, and it is
            // built without generating that schema at all. Pinning both halves keeps the shape
            // from drifting when the wasteful generate-then-discard is removed.
            var error = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "log_editor", "--mesage", "hi" } });
            Assert.AreEqual(400, error.StatusCode, $"Response: {error.RawResponse}");

            var entry = error.JsonResponse["commandSchema"] as JObject;
            Assert.IsNotNull(entry, $"Response: {error.RawResponse}");
            Assert.IsNull(entry["schema"], "the generated schema must not ride along on an error");

            var catalogHttp = await m_PipelineClient.GetHttpAsync("/api/commands");
            var catalog = JObject.Parse(await catalogHttp.Content.ReadAsStringAsync());
            var served = (catalog["commands"] as JArray)
                ?.FirstOrDefault(c => c["name"]?.ToString() == "log_editor") as JObject;
            Assert.IsNotNull(served, "log_editor must be in the catalog");
            Assert.IsNotNull(served["schema"], "/api/commands still carries the generated schema");

            foreach (var property in served.Properties())
            {
                if (property.Name == "schema")
                    continue;
                Assert.IsTrue(JToken.DeepEquals(property.Value, entry[property.Name]),
                    $"commandSchema.{property.Name} must match the catalog entry verbatim");
            }

            Assert.AreEqual(served.Properties().Count() - 1, entry.Properties().Count(),
                "commandSchema must carry every catalog property except the generated schema");
        }

        [Test]
        public async Task ApiExec_UnknownCommandViaArgv_MatchesTheStructuredEnvelope()
        {
            // Both calls log the same registry error; expect one per call.
            LogAssert.Expect(new Regex("^ExecuteCommandByName: No command named"));
            LogAssert.Expect(new Regex("^ExecuteCommandByName: No command named"));

            var viaArgv = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "no_such_command_xyz" } });
            var viaStructured = await m_PipelineClient.PostJsonAsync("/api/exec",
                new CommandExecutionRequest("no_such_command_xyz"));

            Assert.AreEqual(viaStructured.StatusCode, viaArgv.StatusCode);
            Assert.AreEqual(viaStructured.JsonResponse["error"]?.ToString(),
                viaArgv.JsonResponse["error"]?.ToString(),
                "an unknown raw command name must produce the identical Command Not Found envelope");
        }

        [Test]
        public async Task ApiExec_MissingRequiredParameterViaArgv_IsRejectedBeforeExecution()
        {
            // A raw form is validated up front, so it never reaches ExecuteCommandByName and
            // therefore logs no execution error. Message parity with the structured form is
            // asserted directly by
            // ApiExec_RawFormMissingRequiredParameter_ReportsTheSameMessageInBothForms, which is
            // a stronger check than the log line this used to expect.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "log_editor" } });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
            Assert.AreEqual("Parameter Validation Failed", response.JsonResponse["error"]?.ToString());
            Assert.That(response.JsonResponse["errorDetails"]?.ToString(),
                Contains.Substring("message").IgnoreCase);
        }

        [Test]
        public async Task ApiExec_RawFormFailure_HonorsVerboseFlag()
        {
            // Reply-shape flags are captured before the raw-form branch, so a malformed
            // command line still honours them.
            var lean = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "log_editor", "--mesage", "hi" } });
            var verbose = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "log_editor", "--mesage", "hi" }, verbose = true });

            Assert.AreEqual(400, lean.StatusCode);
            Assert.AreEqual(400, verbose.StatusCode);
            Assert.IsNull(lean.JsonResponse["executedAt"], "lean mode drops envelope metadata");
            Assert.IsNotNull(verbose.JsonResponse["executedAt"], "verbose mode restores it");
        }

        [Test]
        public async Task ApiExec_JobWithCommandLine_ReturnsAJobId()
        {
            // RunJobDetached re-reads the request object, so normalizing in place means the
            // detached path needs no changes of its own.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { commandLine = "test_types --count 5", job = true });

            Assert.AreEqual(200, response.StatusCode, $"Response: {response.RawResponse}");
            var result = response.JsonResponse["result"] as JObject;
            Assert.IsNotNull(result?["jobId"] ?? result?["id"],
                $"detached submission should return a job id. Response: {response.RawResponse}");
        }

        [Test]
        public async Task ApiStatus_AdvertisesTheRawFormCapabilities()
        {
            // Capability negotiation exists so a CLI can tell "this server predates argv" from
            // "your request was malformed" WITHOUT a probe request -- an old server answers an
            // argv request with a bare Invalid JSON, which the CLI would otherwise render as
            // exit 6 plus "Run `unity bug`", i.e. version skew reading as a CLI defect.
            var response = await m_PipelineClient.GetAsync("/api/status");

            var capabilities = response.JsonResponse?["capabilities"] as JArray;
            Assert.IsNotNull(capabilities, $"/api/status must advertise capabilities. Response: {response.RawResponse}");

            var tokens = capabilities.Select(t => t.ToString()).ToList();
            CollectionAssert.Contains(tokens, "exec.argv");
            CollectionAssert.Contains(tokens, "exec.commandLine");
        }

        [Test]
        public void ServerCapabilities_AreASingleSharedSource()
        {
            // The descriptor and /api/status must not be able to drift: one array feeds both.
            CollectionAssert.Contains(BasePipelineServer.Capabilities, "exec.argv");
            CollectionAssert.Contains(BasePipelineServer.Capabilities, "exec.commandLine");
        }

        [Test]
        public async Task ApiExec_RawFormMissingRequiredParameter_NeverCreatesAJob()
        {
            // A deterministically invalid submission must not be acknowledged with a job id.
            // Binding SUCCEEDS here -- a missing required parameter is not a binder problem, it is
            // ValidateCommandParameters' job -- so without a preflight this reached job creation,
            // returned 200 plus an id, and only failed later inside the detached run. A client
            // polling that id learns minutes later what was knowable up front.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "log_editor" }, job = true });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
            Assert.IsNull(response.JsonResponse["result"], "no job id for a known-invalid submission");
            Assert.That(response.JsonResponse["errorDetails"]?.ToString(),
                Contains.Substring("message").IgnoreCase);
        }

        [Test]
        public async Task ApiExec_RawFormMissingRequiredParameter_ReportsTheSameMessageInBothForms()
        {
            // The preflight must reproduce ValidateCommandParameters' wording exactly, or the two
            // request forms would diagnose the same mistake differently.
            LogAssert.Expect("ExecuteCommandByName: Parameter validation failed: Required parameter 'message' is missing or empty");

            var viaArgv = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "log_editor" } });
            var viaStructured = await m_PipelineClient.PostJsonAsync("/api/exec",
                new CommandExecutionRequest("log_editor"));

            Assert.AreEqual(400, viaArgv.StatusCode);
            Assert.AreEqual(400, viaStructured.StatusCode);
            Assert.AreEqual(
                viaStructured.JsonResponse["error"]?.ToString(),
                viaArgv.JsonResponse["error"]?.ToString());
            Assert.AreEqual(
                viaStructured.JsonResponse["errorDetails"]?.ToString(),
                viaArgv.JsonResponse["errorDetails"]?.ToString(),
                "both forms must report the identical validation message");
        }

        [Test]
        public async Task ApiExec_CommandLineWithAnEmptyFirstToken_IsAnInvalidRequest()
        {
            // `"" --message hi` is a NONBLANK source string, so Validate() passes, but it
            // tokenizes to an empty first token. Left unchecked it became a Command Not Found,
            // telling the caller an empty command is merely unavailable. The argv equivalent is
            // already rejected as a bad request, so the two raw forms must agree.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { commandLine = "\"\" --message hi" });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
            Assert.AreEqual("Invalid Request", response.JsonResponse["error"]?.ToString(),
                "an empty command name is a malformed request, not a missing command");
        }

        [Test]
        public async Task ApiExec_CommandLineWithAWhitespaceFirstToken_IsAlsoRejected()
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { commandLine = "\" \" --message hi" });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
            Assert.AreEqual("Invalid Request", response.JsonResponse["error"]?.ToString());
        }

        [Test]
        public async Task ApiExec_MalformedArgumentsNeverCreateAJob()
        {
            // A mistyped parameter must not return a job id and exit 0.
            var response = await m_PipelineClient.PostJsonAsync("/api/exec",
                new { argv = new[] { "log_editor", "--mesage", "hi" }, job = true });

            Assert.AreEqual(400, response.StatusCode, $"Response: {response.RawResponse}");
            Assert.IsNull(response.JsonResponse["result"], "no job id for a malformed command line");
        }
    }
}