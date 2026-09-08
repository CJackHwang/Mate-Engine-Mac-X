using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Pipeline;
using Unity.Pipeline.Commands;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Unit tests for CommandLineBinder - binds argv tokens to a command's declared
    /// parameters, accumulating every defect rather than bailing on the first.
    ///
    /// The load-bearing invariant is CONVERGENCE: the JObject produced from a command line
    /// must be indistinguishable from the structured <c>parameters</c> object a client would
    /// have sent, so everything from ExtractCommandParameters downwards has exactly one code
    /// path. That is what
    /// <see cref="TryBind_PositionalAndFlagAndStructured_AllProduceTheSameObject"/> pins.
    /// </summary>
    class CommandLineBinderTests
    {
        // The binder reads only Name and Parameters, but CommandInfo requires a non-null
        // MethodInfo. Any method will do; this one exists solely to satisfy that.
        private static void Placeholder() { }

        private static readonly MethodInfo s_Placeholder =
            typeof(CommandLineBinderTests).GetMethod(nameof(Placeholder),
                BindingFlags.NonPublic | BindingFlags.Static);

        private static CommandParameterInfo Param(string name, System.Type type, bool required = false,
            object defaultValue = null) =>
            new CommandParameterInfo(name, $"{name} description", required, type, defaultValue);

        private static CommandInfo Command(string name, params CommandParameterInfo[] parameters) =>
            new CommandInfo(name, $"{name} description", false, s_Placeholder, parameters);

        /// <summary>log_editor: one required string, the simplest real shape.</summary>
        private static CommandInfo LogEditor() =>
            Command("log_editor", Param("message", typeof(string), required: true));

        private static JObject Bind(CommandInfo command, params string[] args)
        {
            Assert.IsTrue(CommandLineBinder.TryBind(command, args, out var parameters, out var problems),
                $"Expected a clean bind, got problems: {Describe(problems)}");
            Assert.IsEmpty(problems, "A successful bind must report no problems");
            return parameters;
        }

        private static List<ArgProblem> BindProblems(CommandInfo command, params string[] args)
        {
            Assert.IsFalse(CommandLineBinder.TryBind(command, args, out _, out var problems),
                "Expected the bind to be rejected");
            Assert.IsNotEmpty(problems, "A failed bind must report at least one problem");
            return problems;
        }

        private static string Describe(List<ArgProblem> problems)
        {
            if (problems == null) return "<null>";
            var parts = new List<string>();
            foreach (var p in problems)
                parts.Add($"{p.Kind}(name={p.Name}, token={p.Token}, suggestion={p.Suggestion})");
            return string.Join("; ", parts);
        }

        // ---------------------------------------------------------------- convergence

        [Test]
        public void TryBind_PositionalAndFlagAndStructured_AllProduceTheSameObject()
        {
            var expected = new JObject { ["message"] = "hi" };

            Assert.IsTrue(JToken.DeepEquals(expected, Bind(LogEditor(), "hi")),
                "positional form must match the structured equivalent");
            Assert.IsTrue(JToken.DeepEquals(expected, Bind(LogEditor(), "--message", "hi")),
                "--key value form must match the structured equivalent");
            Assert.IsTrue(JToken.DeepEquals(expected, Bind(LogEditor(), "--message=hi")),
                "--key=value form must match the structured equivalent");
        }

        [Test]
        public void TryBind_StoresStringValues_SoTheExecutorSeesOneShape()
        {
            // Values are stored as string JValues, never pre-converted. ConvertParameterToken
            // does the widening downstream, identically for both request forms.
            var command = Command("test_types", Param("count", typeof(int), defaultValue: 1));

            var bound = Bind(command, "--count", "5");

            Assert.AreEqual(JTokenType.String, bound["count"].Type,
                "the binder must not pre-convert; coercion happens in the executor");
            Assert.AreEqual("5", bound["count"].Value<string>());
        }

        [Test]
        public void TryBind_NoArguments_ProducesEmptyObject()
        {
            var bound = Bind(Command("test_tagged"));
            Assert.AreEqual(0, bound.Count);
        }

        // ---------------------------------------------------------------- flag classification

        [Test]
        public void TryBind_EmptyStringValue_IsTheEmptyStringNotTrue()
        {
            // Conflating "no next token" with "empty next token" made `--message ""`
            // send boolean true. Absence and emptiness are different.
            var bound = Bind(LogEditor(), "--message", "");

            Assert.AreEqual(JTokenType.String, bound["message"].Type);
            Assert.AreEqual(string.Empty, bound["message"].Value<string>());
        }

        [Test]
        public void TryBind_FlagFollowedBySingleDashToken_ConsumesItAsTheValue()
        {
            // `--message -foo` is the escape hatch for values that look like flags.
            // The follower is consumed unless it starts with "--".
            var bound = Bind(LogEditor(), "--message", "-foo");
            Assert.AreEqual("-foo", bound["message"].Value<string>());
        }

        [Test]
        public void TryBind_DuplicateFlag_ReportsDuplicate()
        {
            var problems = BindProblems(LogEditor(), "--message", "a", "--message", "b");

            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(ArgProblemKind.Duplicate, problems[0].Kind);
            Assert.AreEqual("message", problems[0].Name);
        }

        [Test]
        public void TryBind_FlagWithEmptyName_ReportsEmptyName()
        {
            var problems = BindProblems(LogEditor(), "--=oops");

            Assert.AreEqual(ArgProblemKind.EmptyName, problems[0].Kind);
            Assert.AreEqual("--=oops", problems[0].Token);
        }

        [Test]
        public void TryBind_FlagWithTrailingEquals_ReportsEmptyValue()
        {
            var problems = BindProblems(LogEditor(), "--message=");

            Assert.AreEqual(ArgProblemKind.EmptyValue, problems[0].Kind);
            Assert.AreEqual("message", problems[0].Name);
        }

        [Test]
        public void TryBind_UnknownFlag_ReportsUnknownNameWithDidYouMean()
        {
            var problems = BindProblems(LogEditor(), "--mesage", "hi");

            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(ArgProblemKind.UnknownName, problems[0].Kind);
            Assert.AreEqual("mesage", problems[0].Name);
            Assert.AreEqual("message", problems[0].Suggestion, "a one-character typo must suggest");
        }

        [Test]
        public void TryBind_UnknownFlagUnlikeAnyParameter_ReportsNoSuggestion()
        {
            var problems = BindProblems(LogEditor(), "--zzzzzzzzz", "hi");

            Assert.AreEqual(ArgProblemKind.UnknownName, problems[0].Kind);
            Assert.IsNull(problems[0].Suggestion, "an unrelated name must not get a spurious suggestion");
        }

        [Test]
        public void TryBind_AccumulatesEveryProblem_RatherThanBailingOnTheFirst()
        {
            var problems = BindProblems(LogEditor(), "--nope", "x", "--=bad");

            Assert.AreEqual(2, problems.Count, $"expected both problems, got: {Describe(problems)}");
        }

        // ---------------------------------------------------------------- positional slots

        [Test]
        public void TryBind_Positionals_FillRequiredThenOptionalInDeclarationOrder()
        {
            // Declaration order is: optional 'first', required 'second'. Required wins the
            // first positional slot regardless of where it is declared.
            var command = Command("two_slots",
                Param("first", typeof(string)),
                Param("second", typeof(string), required: true));

            var bound = Bind(command, "A", "B");

            Assert.AreEqual("A", bound["second"].Value<string>(), "required slot fills first");
            Assert.AreEqual("B", bound["first"].Value<string>());
        }

        /// <summary>
        /// The <c>find_assets</c> shape: every parameter optional, so there is no required slot
        /// to anchor on. The first positional must still land on the FIRST DECLARED parameter.
        ///
        /// A client that binds against a fetched catalog has a failure mode here that this
        /// design does not: lose the schema and there is nothing left to order by, so a
        /// positional has to be guessed at. Binding against the reflected signature cannot
        /// degrade that way — the declaration is always present.
        /// </summary>
        [Test]
        public void TryBind_AllOptionalParameters_StillFillInDeclarationOrder()
        {
            var findAssets = Command("find_assets",
                Param("type", typeof(string)),
                Param("name", typeof(string)),
                Param("label", typeof(string)),
                Param("search_in", typeof(string)),
                Param("limit", typeof(int), defaultValue: 200));

            var one = Bind(findAssets, "Texture2D");
            Assert.IsTrue(JToken.DeepEquals(new JObject { ["type"] = "Texture2D" }, one),
                "a single positional binds to the first declared parameter and nothing else");

            var two = Bind(findAssets, "Texture2D", "Grass");
            Assert.AreEqual("Texture2D", two["type"].Value<string>());
            Assert.AreEqual("Grass", two["name"].Value<string>());
            Assert.IsNull(two["label"], "unfilled optional slots stay absent, not null");
        }

        [Test]
        public void TryBind_AllOptionalParameters_FlagAndPositionalCombine()
        {
            // `find_assets Texture2D --limit 5`: the flag takes its own slot, and the positional
            // still lands on `type` rather than being pushed along by it.
            var findAssets = Command("find_assets",
                Param("type", typeof(string)),
                Param("name", typeof(string)),
                Param("limit", typeof(int), defaultValue: 200));

            var bound = Bind(findAssets, "Texture2D", "--limit", "5");

            Assert.AreEqual("Texture2D", bound["type"].Value<string>());
            Assert.AreEqual("5", bound["limit"].Value<string>());
            Assert.IsNull(bound["name"]);
        }

        [Test]
        public void TryBind_SlotAlreadyFilledByFlag_IsSkippedNotOverwritten()
        {
            var command = Command("two_slots",
                Param("a", typeof(string), required: true),
                Param("b", typeof(string)));

            var bound = Bind(command, "--a", "viaFlag", "viaPositional");

            Assert.AreEqual("viaFlag", bound["a"].Value<string>(), "the flag must win its own slot");
            Assert.AreEqual("viaPositional", bound["b"].Value<string>(), "the positional moves to the next free slot");
        }

        [Test]
        public void TryBind_MorePositionalsThanSlots_ReportsExcessWithCapacityAndGiven()
        {
            var problems = BindProblems(LogEditor(), "one", "two", "three");

            Assert.AreEqual(ArgProblemKind.ExcessPositional, problems[0].Kind);
            Assert.AreEqual(1, problems[0].Capacity, "log_editor declares one slot");
            Assert.AreEqual(3, problems[0].Given);
        }

        [Test]
        public void TryBind_CommandWithNoParameters_RejectsAnyPositional()
        {
            var problems = BindProblems(Command("test_tagged"), "stray");

            Assert.AreEqual(ArgProblemKind.ExcessPositional, problems[0].Kind);
            Assert.AreEqual(0, problems[0].Capacity);
        }

        [Test]
        public void TryBind_SingleDashInPositionalPosition_IsReported()
        {
            var problems = BindProblems(LogEditor(), "-foo");

            Assert.AreEqual(ArgProblemKind.SingleDash, problems[0].Kind);
            Assert.AreEqual("-foo", problems[0].Token);
        }

        [Test]
        public void TryBind_NegativeNumberInPositionalPosition_IsNotASingleDashProblem()
        {
            // -5 and -1.5 are values, not malformed flags.
            var command = Command("takes_number", Param("value", typeof(string), required: true));

            Assert.AreEqual("-5", Bind(command, "-5")["value"].Value<string>());
            Assert.AreEqual("-1.5", Bind(command, "-1.5")["value"].Value<string>());
            Assert.AreEqual("-", Bind(command, "-")["value"].Value<string>());
        }

        [Test]
        public void TryBind_SingleDashRule_IsLetterAnchoredExactlyLikeTheCli()
        {
            // The CLI keeps this check client-side (offline, no Editor boot), which is only safe
            // while its rule is a strict SUBSET of ours. Its rule is char.IsAsciiLetter(token[1]),
            // so '-5x' must be a VALUE here too -- a looser "not a number" test would reject it
            // server-side after the CLI had already waved it through.
            var command = Command("takes_value", Param("value", typeof(string), required: true));

            Assert.AreEqual("-5x", Bind(command, "-5x")["value"].Value<string>());
            Assert.AreEqual(ArgProblemKind.SingleDash, BindProblems(command, "-foo")[0].Kind);
        }

        [Test]
        public void TryBind_BareAssignmentNamingAParameter_IsReportedButStillCountsAsPositional()
        {
            // Rejects `create_folder path=Assets/Foo` (a misremembered flag syntax) while
            // leaving `eval "obj=null;"` alone -- see the eval test below.
            var command = Command("create_folder", Param("path", typeof(string), required: true));

            var problems = BindProblems(command, "path=Assets/Foo");

            Assert.AreEqual(ArgProblemKind.BareAssignment, problems[0].Kind);
            Assert.AreEqual("path=Assets/Foo", problems[0].Token);
        }

        [Test]
        public void TryBind_EvalCodeContainingAnAssignment_BindsCleanly()
        {
            // THE case the bare-assignment heuristic must not break: 'obj' is not a
            // declared parameter of eval, so this is code, not a misspelled flag.
            var eval = Command("eval",
                Param("code", typeof(string), required: true),
                Param("timeout", typeof(int)));

            var bound = Bind(eval, "obj=null;");

            Assert.AreEqual("obj=null;", bound["code"].Value<string>());
        }

        [Test]
        public void TryBind_DoubleDash_EndsFlagParsingAndExemptsTheRest()
        {
            var command = Command("two_slots",
                Param("a", typeof(string), required: true),
                Param("b", typeof(string)));

            // After --, `-x` is a value and `a=1` is not a bare assignment.
            var bound = Bind(command, "--", "-x", "a=1");

            Assert.AreEqual("-x", bound["a"].Value<string>());
            Assert.AreEqual("a=1", bound["b"].Value<string>());
        }

        // ---------------------------------------------------------------- boundaries

        [Test]
        public void TryBind_MissingRequiredParameter_IsNotABinderProblem()
        {
            // Required-parameter checking stays in ValidateCommandParameters so the message
            // and error type are byte-identical across both request forms.
            var bound = Bind(LogEditor());

            Assert.AreEqual(0, bound.Count);
        }

        // ---------------------------------------------------------------- coercion (dry run)

        /// <summary>Declared locally so the binder's enum handling is testable without
        /// touching DummyCommands, which other suites share.</summary>
        public enum SampleMode { Editor, Player, Headless }

        [Test]
        public void TryBind_IntParameterGivenNonNumericToken_ReportsTypeMismatch()
        {
            // Without this, ConvertParameterToken throws inside the executor and the request
            // dies as a 500-ish "Command Execution Failed" instead of a usage error.
            var command = Command("test_types", Param("count", typeof(int), defaultValue: 1));

            var problems = BindProblems(command, "--count", "abc");

            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(ArgProblemKind.TypeMismatch, problems[0].Kind);
            Assert.AreEqual("count", problems[0].Name);
            Assert.AreEqual("abc", problems[0].Token);
            Assert.AreEqual("Int32", problems[0].ExpectedType);
        }

        [Test]
        public void TryBind_IntParameterGivenNumericToken_BindsCleanly()
        {
            var command = Command("test_types", Param("count", typeof(int), defaultValue: 1));

            Assert.AreEqual("5", Bind(command, "--count", "5")["count"].Value<string>());
        }

        [Test]
        public void TryBind_FloatParameterGivenDecimalToken_BindsCleanly()
        {
            var command = Command("test_types", Param("factor", typeof(float), defaultValue: 1.0f));

            Assert.AreEqual("1.5", Bind(command, "--factor", "1.5")["factor"].Value<string>());
        }

        [Test]
        public void TryBind_StringParameterGivenLeadingZeros_KeepsThemAsText()
        {
            // Regression guard, not a driver: this already holds because the binder stores
            // strings. It is the semantic FIX over the CLI's old shape-guessing ParseValue,
            // which turned 007 into the number 7 even for a declared string parameter.
            var command = Command("reload_file", Param("path", typeof(string), required: true));

            Assert.AreEqual("007", Bind(command, "007")["path"].Value<string>());
        }

        [Test]
        public void TryBind_BoolParameterAsBareFlag_BindsTrue()
        {
            var command = Command("test_types", Param("enabled", typeof(bool), defaultValue: false));

            var bound = Bind(command, "--enabled");

            Assert.IsTrue(JToken.DeepEquals(new JObject { ["enabled"] = true }, bound),
                "a bare flag on a bool must match the structured {\"enabled\":true}");
        }

        [Test]
        public void TryBind_NonBoolParameterAsBareFlag_IsReportedNotCoercedToTheStringTrue()
        {
            // JValue(true).ToObject<string>() silently yields "True". Catching it here is what
            // stops `log_editor --message` from logging the word True.
            var problems = BindProblems(LogEditor(), "--message");

            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(ArgProblemKind.EmptyValue, problems[0].Kind,
                "the user's mistake is a missing value, so EmptyValue reads better than TypeMismatch");
            Assert.AreEqual("message", problems[0].Name);
        }

        // Enum binding defers entirely to the executor's own converter, so these cases ARE the
        // parity contract: whatever the structured path accepts, the raw path must accept too.
        [TestCase(typeof(SampleMode), "player", TestName = "TryBind_EnumName_IsAccepted")]
        [TestCase(typeof(Severity), "warning", TestName = "TryBind_EnumName_IsAcceptedCaseInsensitively")]
        // A [Flags] enum takes a comma-separated combination natively. A dry run that reimplemented
        // name matching found no single name equal to "Info,Warning" and rejected a request the
        // structured path accepts.
        [TestCase(typeof(Channels), "Info,Warning", TestName = "TryBind_FlagsEnumCombination_IsAccepted")]
        public void TryBind_EnumValueTheConverterAccepts_BindsTheTokenVerbatim(
            System.Type parameterType, string token)
        {
            var command = Command("log", Param("level", parameterType, required: true));

            // Stored as the token's own text, like every other declared type: the executor
            // re-converts from the same string, so binding and execution cannot diverge.
            Assert.IsTrue(JToken.DeepEquals(new JObject { ["level"] = token }, Bind(command, token)));
        }

        // The point of deferring to the converter is parity, not leniency. ExpectedType must name
        // the enum itself, never Nullable`1.
        [TestCase(typeof(SampleMode), "sideways", "SampleMode",
            TestName = "TryBind_UnknownEnumName_ReportsTypeMismatch")]
        [TestCase(typeof(Severity), "Verbose", "Severity",
            TestName = "TryBind_UnknownEnumName_NamesTheEnum")]
        [TestCase(typeof(Severity?), "Verbose", "Severity",
            TestName = "TryBind_UnknownEnumNameOnANullableEnum_NamesTheUnderlyingType")]
        public void TryBind_EnumValueTheConverterRejects_ReportsTypeMismatch(
            System.Type parameterType, string token, string expectedTypeName)
        {
            var command = Command("log", Param("level", parameterType, required: true));

            var problems = BindProblems(command, token);

            Assert.AreEqual(ArgProblemKind.TypeMismatch, problems[0].Kind);
            Assert.AreEqual("level", problems[0].Name);
            Assert.AreEqual(token, problems[0].Token);
            Assert.AreEqual(expectedTypeName, problems[0].ExpectedType);
        }

        [Test]
        public void TryBind_JsonObjectStringForStructuredParameter_BindsCleanlyAndStaysAString()
        {
            // The '{'-prefixed JSON re-parse guard in ConvertParameterToken must be exercised by
            // the dry run, and the dry run must NOT rewrite what we store -- the executor redoes
            // the conversion from the same string.
            var command = Command("batch", Param("operations", typeof(JObject), required: true));

            var bound = Bind(command, "{\"a\":1}");

            Assert.AreEqual(JTokenType.String, bound["operations"].Type);
            Assert.AreEqual("{\"a\":1}", bound["operations"].Value<string>());
        }

        // ------------------------------------------------------- untyped parameters (Risk #4)

        // 14 parameters across 12 shipped commands declare JToken/JObject/JArray — e.g.
        // set_serialized_field.value, add_animator_parameter.defaultValue. For a DECLARED type the
        // token's shape is irrelevant (a string coerces to int identically), but an untyped
        // parameter has nothing to coerce toward, so the shape is the only signal there is. The CLI
        // used to guess it client-side; dropping that guess would silently turn a working
        // `--value 5` into the string "5". These cases reproduce the CLI's rules exactly.
        [TestCase("5", JTokenType.Integer, TestName = "TryBind_UntypedInteger_KeepsItsJsonType")]
        [TestCase("1.5", JTokenType.Float, TestName = "TryBind_UntypedFloat_KeepsItsJsonType")]
        [TestCase("-17", JTokenType.Integer, TestName = "TryBind_UntypedNegativeInteger_KeepsItsJsonType")]
        [TestCase("true", JTokenType.Boolean, TestName = "TryBind_UntypedBoolean_KeepsItsJsonType")]
        // The old rule lower-cased before comparing.
        [TestCase("TRUE", JTokenType.Boolean, TestName = "TryBind_UntypedUppercaseBoolean_KeepsItsJsonType")]
        // 07 is a number (digit-accumulated, no radix handling), 1.2.3 is not, a bare dash is not.
        [TestCase("07", JTokenType.Integer, TestName = "TryBind_UntypedLeadingZeroInteger_KeepsItsJsonType")]
        [TestCase("1.2.3", JTokenType.String, TestName = "TryBind_UntypedSecondDot_StaysAString")]
        [TestCase("-", JTokenType.String, TestName = "TryBind_UntypedBareDash_StaysAString")]
        [TestCase("Assets/Foo.prefab", JTokenType.String, TestName = "TryBind_UntypedAssetPath_StaysAString")]
        public void TryBind_ScalarForAnUntypedParameter_KeepsItsJsonType(
            string token, JTokenType expected)
        {
            var command = Command("set_serialized_field", Param("value", typeof(JToken), required: true));

            Assert.AreEqual(expected, Bind(command, token)["value"].Type);
        }

        [Test]
        public void TryBind_UntypedLeadingZeroInteger_ParsesWithoutRadix()
        {
            // Shape alone is not the whole contract: 07 must become 7, not 8 (octal) or "07".
            var command = Command("set_serialized_field", Param("value", typeof(JToken), required: true));

            Assert.AreEqual(7, Bind(command, "07")["value"].Value<int>());
        }

        // The counterpart: a DECLARED type means the shape must NOT be guessed. This is the
        // semantic fix over the old client-side guess, which turned 007 into the number 7 even for
        // a declared string.
        [TestCase(typeof(string), "007", TestName = "TryBind_DeclaredStringGivenDigits_StaysAString")]
        [TestCase(typeof(int), "5", TestName = "TryBind_DeclaredIntGivenDigits_IsStillStoredAsAString")]
        public void TryBind_ScalarForATypedParameter_StaysAStringRegardlessOfShape(
            System.Type parameterType, string token)
        {
            var command = Command("reload_file", Param("value", parameterType, required: true));

            Assert.AreEqual(JTokenType.String, Bind(command, token)["value"].Type);
        }

        [Test]
        public void TryBind_UnparseableJsonForStructuredParameter_ReportsTypeMismatch()
        {
            // The silent-drop class: ToObject returns NULL rather than throwing, so today the
            // parameter vanishes and the command runs with a default. That must be an error.
            var command = Command("batch", Param("operations", typeof(float[]), required: true));

            var problems = BindProblems(command, "not-json-at-all");

            Assert.AreEqual(ArgProblemKind.TypeMismatch, problems[0].Kind);
            Assert.AreEqual("operations", problems[0].Name);
        }

        // ------------------------------------------------- accumulation across value defects

        [Test]
        public void TryBind_DuplicateWhoseSecondUseIsEmpty_ReportsBothDefects()
        {
            // Accumulation is this class's contract: a user fixing a command line must see every
            // defect at once. The empty-value branch used to return before the duplicate check,
            // so resolving the reported EmptyValue left a still-unreported duplicate behind.
            var problems = BindProblems(LogEditor(), "--message", "hi", "--message=");

            Assert.AreEqual(2, problems.Count, $"expected both defects, got: {Describe(problems)}");
            CollectionAssert.AreEquivalent(
                new[] { ArgProblemKind.Duplicate, ArgProblemKind.EmptyValue },
                new[] { problems[0].Kind, problems[1].Kind });
        }

        // ------------------------------------------------- positional overflow classification

        /// <summary>a is required, b is optional - two slots, so three values is one too many.</summary>
        private static CommandInfo TwoSlots() =>
            Command("two_slots",
                Param("a", typeof(string), required: true),
                Param("b", typeof(string)));

        [Test]
        public void TryBind_OverflowPastASlotAFlagDidNotFill_IsExcessPositional()
        {
            // `--a X Y Z`: Y takes slot b, so Z overflows because the command has two slots and
            // three values were supplied - NOT because Z collided with the --a flag. Classifying
            // it as a conflict told the client something factually untrue about Z.
            var problems = BindProblems(TwoSlots(), "--a", "X", "Y", "Z");

            Assert.AreEqual(1, problems.Count, Describe(problems));
            Assert.AreEqual(ArgProblemKind.ExcessPositional, problems[0].Kind);
            Assert.AreEqual("Z", problems[0].Token);
            Assert.AreEqual(1, problems[0].Capacity, "one slot was left free after --a took its own");
            Assert.AreEqual(2, problems[0].Given, "capacity and given must be commensurable: free slots vs positionals");
        }

        [Test]
        public void TryBind_PositionalWhoseOwnSlotAFlagFilled_IsAConflictNamingThatParameter()
        {
            // One slot, filled by --message, then a positional with nowhere to go. This IS the
            // conflict case, and the rendered message reads "<name> is already set by --<name>",
            // so Name has to be populated or the client prints an empty subject.
            var problems = BindProblems(LogEditor(), "--message", "hi", "orphan");

            Assert.AreEqual(1, problems.Count, Describe(problems));
            Assert.AreEqual(ArgProblemKind.PositionalConflict, problems[0].Kind);
            Assert.AreEqual("message", problems[0].Name, "the conflicting parameter must be named");
            Assert.AreEqual("orphan", problems[0].Token);
        }

        [Test]
        public void TryBind_ExcessPositional_NamesTheFirstTokenThatOverflowed()
        {
            // The prose renders "... but 3 were given (starting at 'X')", so the token has to be
            // the first one with no slot, not the last one supplied.
            var problems = BindProblems(LogEditor(), "one", "two", "three");

            Assert.AreEqual(1, problems.Count, Describe(problems));
            Assert.AreEqual(ArgProblemKind.ExcessPositional, problems[0].Kind);
            Assert.AreEqual("two", problems[0].Token, "'one' filled the only slot, so 'two' is the first excess");
            Assert.AreEqual(1, problems[0].Capacity);
            Assert.AreEqual(3, problems[0].Given);
        }

        // ------------------------------------------------- bare assignment precision

        /// <summary>eval: a required code string plus an optional integer timeout.</summary>
        private static CommandInfo Eval() =>
            Command("eval",
                Param("code", typeof(string), required: true),
                Param("timeout", typeof(int)));

        [Test]
        public void TryBind_PositionalAssigningAParameterOtherThanItsOwnSlot_IsAValue()
        {
            // `eval "timeout=5;"` is legal C#. It lands on the code slot, and code is not what
            // the text before `=` names, so nothing about it suggests a misremembered flag.
            // Matching ANY declared name rejected it, while the identical structured request
            // {"code":"timeout=5;"} succeeded.
            var bound = Bind(Eval(), "timeout=5;");

            Assert.IsTrue(JToken.DeepEquals(new JObject { ["code"] = "timeout=5;" }, bound),
                $"expected the token to bind as code, got {bound}");
        }

        [Test]
        public void TryBind_PositionalAssigningItsOwnSlot_IsStillABareAssignment()
        {
            // `create_folder path=Assets/Foo` is the case the check exists for: the token lands
            // on `path` and its own text names `path`, which is a misremembered `--path`.
            var command = Command("create_folder", Param("path", typeof(string), required: true));

            var problems = BindProblems(command, "path=Assets/Foo");

            Assert.AreEqual(ArgProblemKind.BareAssignment, problems[0].Kind);
            Assert.AreEqual("path=Assets/Foo", problems[0].Token);
        }

        [Test]
        public void TryBind_PositionalAfterTheSeparator_IsNeverABareAssignment()
        {
            // The documented escape hatch, and what makes the residual ambiguity of a token that
            // assigns its own slot (`eval "code=1;"`) recoverable rather than fatal.
            var bound = Bind(Eval(), "--", "code=1;");

            Assert.IsTrue(JToken.DeepEquals(new JObject { ["code"] = "code=1;" }, bound),
                $"expected the token to bind as code, got {bound}");
        }

        // ------------------------------------------------- untyped targets

        [Test]
        public void TryBind_BareFlagOnAJsonObjectParameter_IsAnEmptyValue()
        {
            // A JObject cannot hold `true`, so a bare `--metadata` is a missing value, not a type
            // error. The untyped exemption covered every JToken subtype, so the implied `true`
            // reached the converter and surfaced as TypeMismatch instead.
            var command = Command("tag", Param("metadata", typeof(JObject)));

            var problems = BindProblems(command, "--metadata");

            Assert.AreEqual(1, problems.Count, Describe(problems));
            Assert.AreEqual(ArgProblemKind.EmptyValue, problems[0].Kind);
            Assert.AreEqual("metadata", problems[0].Name);
        }

        [Test]
        public void TryBind_BareFlagOnAFullyUntypedParameter_IsStillAccepted()
        {
            // `object` and bare `JToken` CAN hold true, so the exemption still applies to them.
            var command = Command("tag", Param("anything", typeof(object)));

            Assert.IsTrue(JToken.DeepEquals(new JObject { ["anything"] = true },
                Bind(command, "--anything")));
        }

        [Test]
        public void TryBind_TypeMismatchOnAnUntypedTarget_EchoesTheTokenTheUserTyped()
        {
            // An untyped target stores a literal-parsed JValue, so the stored value is not a
            // string and the token text was dropped - rendering "expects JObject, but got ''".
            var command = Command("tag", Param("metadata", typeof(JObject)));

            var problems = BindProblems(command, "--metadata", "5");

            Assert.AreEqual(1, problems.Count, Describe(problems));
            Assert.AreEqual(ArgProblemKind.TypeMismatch, problems[0].Kind);
            Assert.AreEqual("5", problems[0].Token, "the diagnostic must name what the user typed");
        }

        [Test]
        public void TryBind_TypeMismatchOnAnUntypedTarget_EchoesTheTokenVerbatim()
        {
            // The literal rules turn `007` into 7, so echoing the stored value would misquote the
            // user back at themselves.
            var command = Command("tag", Param("metadata", typeof(JObject)));

            var problems = BindProblems(command, "--metadata", "007");

            Assert.AreEqual("007", problems[0].Token);
        }

        // ------------------------------------------------- enums

        private enum Severity { Info = 1, Warning = 2, Error = 4 }

        [System.Flags]
        private enum Channels { None = 0, Info = 1, Warning = 2, Error = 4 }

        [Test]
        public void TryBind_EnumGivenAsAFlagValue_ReachesTheSameConverter()
        {
            // The table above binds enums positionally; a --flag value takes a different path
            // through pass 1, so pin that it lands on the same coercion check.
            var command = Command("log", Param("channels", typeof(Channels)));

            Assert.IsTrue(JToken.DeepEquals(new JObject { ["channels"] = "Info,Warning" },
                Bind(command, "--channels", "Info,Warning")));
        }
    }
}
