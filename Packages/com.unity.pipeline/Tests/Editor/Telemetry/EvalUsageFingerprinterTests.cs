using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Pipeline.Telemetry;

namespace Unity.Pipeline.Tests.Editor.Telemetry
{
    /// <summary>
    /// Tests for <see cref="EvalUsageFingerprinter"/>: the syntax-tree fingerprint extractor and the
    /// one-liner vs multi-statement classifier (AUTHAPI-29). Pure string in / string out, so they run
    /// hermetically with no file or Editor state.
    /// </summary>
    class EvalUsageFingerprinterTests
    {
        private static List<string> Fingerprints(string code) =>
            EvalUsageFingerprinter.ExtractFingerprints(code).ToList();

        [TestCase("AssetDatabase.Refresh();", "AssetDatabase.Refresh",
            TestName = "StaticMethodCall_ProducesMemberPath")]
        [TestCase("TestBot.Enabled = true;", "TestBot.Enabled",
            TestName = "StaticPropertySet_ProducesLeftHandMemberPath")]
        [TestCase("Object.FindFirstObjectByType<Player>();", "Object.FindFirstObjectByType<T>",
            TestName = "GenericMethod_NormalizesTypeArgumentToPlaceholder")]
        [TestCase("PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);", "PlayerSettings.SetScriptingBackend",
            TestName = "PlayerSettingsSetter_IsCapturedAsMethodCall")]
        public void Fingerprint_ProducesExpectedMemberPath(string code, string expected)
        {
            CollectionAssert.Contains(Fingerprints(code), expected);
        }

        [Test]
        public void InstanceChain_ProducesFullChainPath()
        {
            var fps = Fingerprints("return MatchManager.Instance.State;");
            CollectionAssert.Contains(fps, "MatchManager.Instance.State");
            // The full outermost chain is captured once; the inner "MatchManager.Instance" prefix is
            // not double-counted as its own fingerprint.
            CollectionAssert.DoesNotContain(fps, "MatchManager.Instance");
        }

        [Test]
        public void GenericMethod_DifferentTypeArgs_ShareOneFingerprint()
        {
            // Normalising <Foo>/<Bar> to <T> is what lets the report rank a generic API as one line.
            Assert.AreEqual(
                Fingerprints("Object.FindFirstObjectByType<Player>();").Single(f => f.StartsWith("Object.FindFirstObjectByType")),
                Fingerprints("Object.FindFirstObjectByType<Enemy>();").Single(f => f.StartsWith("Object.FindFirstObjectByType")));
        }

        [Test]
        public void MultiStatementBody_ProducesMultipleFingerprints()
        {
            var fps = Fingerprints("Debug.Log(\"x\"); AssetDatabase.Refresh(); return MatchManager.Instance.State;");
            CollectionAssert.Contains(fps, "Debug.Log");
            CollectionAssert.Contains(fps, "AssetDatabase.Refresh");
            CollectionAssert.Contains(fps, "MatchManager.Instance.State");
            Assert.GreaterOrEqual(fps.Count, 3);
        }

        [Test]
        public void ArgumentReads_AreCapturedAsTheirOwnSites()
        {
            var fps = Fingerprints("Debug.Log(MatchManager.Instance.State);");
            CollectionAssert.Contains(fps, "Debug.Log");
            CollectionAssert.Contains(fps, "MatchManager.Instance.State");
        }

        // ---- Privacy: strict whitelist rendering (no raw source can leak into a fingerprint) ----

        [TestCase("return \"secret-key\".Length;", ".Length", "secret-key",
            TestName = "StringLiteralReceiver_RendersAsPlaceholder_LeaksNoLiteral")]
        [TestCase("return (1 + 2).ToString();", ".ToString", "1 + 2",
            TestName = "ArithmeticReceiver_RendersAsPlaceholder")]
        public void NonWhitelistedReceiver_RendersAsPlaceholder(string code, string memberSuffix, string leaked)
        {
            var fps = Fingerprints(code);
            CollectionAssert.Contains(fps, EvalUsageFingerprinter.Placeholder + memberSuffix);
            AssertNoSourceLeak(fps, leaked);
        }

        [Test]
        public void InterpolatedStringReceiver_RendersAsPlaceholder_LeaksNoFragment()
        {
            var fps = Fingerprints("$\"{a}b\".Trim();");
            CollectionAssert.AreEquivalent(new[] { EvalUsageFingerprinter.Placeholder + ".Trim" }, fps);
            AssertNoSourceLeak(fps, "{a}b");
        }

        [Test]
        public void ObjectCreationChain_RendersAsNewTypeName_LeaksNoConstructorArgument()
        {
            var fps = Fingerprints("new GameObject(\"Name\").AddComponent<Rigidbody>();");
            CollectionAssert.AreEquivalent(new[] { "new GameObject.AddComponent<T>" }, fps);
            AssertNoSourceLeak(fps, "Name");
        }

        [Test]
        public void CastReceiver_UnwrapsToInnerExpression()
        {
            var fps = Fingerprints("((Foo)o).Bar = 1;");
            CollectionAssert.Contains(fps, "o.Bar");
            Assert.IsFalse(fps.Any(f => f.Contains("Foo")), $"The cast's type name must be unwrapped away: [{string.Join(", ", fps)}]");
            AssertNoSourceLeak(fps);
        }

        /// <summary>
        /// No fingerprint may carry raw source fragments: literal quoting/interpolation characters are
        /// banned outright, and any caller-supplied literal text must be absent. Identifiers, member
        /// names, and the structural tokens the renderer itself emits (".", "()", "[]", "&lt;T&gt;",
        /// "new ", "&lt;expr&gt;") are the only permitted content.
        /// </summary>
        private static void AssertNoSourceLeak(List<string> fps, params string[] literalFragments)
        {
            foreach (var fp in fps)
            {
                StringAssert.DoesNotContain("\"", fp, "Fingerprints must never contain string-literal quotes");
                StringAssert.DoesNotContain("{", fp, "Fingerprints must never contain interpolation braces");
                foreach (var fragment in literalFragments)
                    StringAssert.DoesNotContain(fragment, fp, "Fingerprints must never contain literal/source fragments");
            }
        }

        // ---- Review regression: a call/parenthesized receiver was previously dropped entirely
        // (IsMemberOrName rejected it, so Add() was never reached — not even the placeholder) ----

        [TestCase("GetHandler()();", "GetHandler()",
            TestName = "InvocationCallee_RendersAsCalleeInvocation")]
        // Not "(A.B)();": a parenthesized type-looking name directly followed by '(' is a cast
        // grammar ambiguity Roslyn resolves in favor of a (here invalid) cast, never reaching
        // InvocationExpressionSyntax at all. An indexer receiver isn't type-syntax, so it stays
        // unambiguously a parenthesized invocation callee — and still exercises the cast-unwrap
        // rendering path via a realistic delegate-cast-and-invoke shape.
        [TestCase("(handlers[0])();", "handlers[]",
            TestName = "ParenthesizedElementAccessCallee_UnwrapsToInnerExpression")]
        [TestCase("((Action)handler)();", "handler",
            TestName = "ParenthesizedCastCallee_UnwrapsToInnerExpression")]
        public void CallOrParenthesizedCallee_OnInvocation_ProducesFingerprint(string code, string expected)
        {
            CollectionAssert.Contains(Fingerprints(code), expected);
        }

        [Test]
        public void CallReceiver_OnElementAccess_ProducesFingerprint()
        {
            var fps = Fingerprints("GetList()[0];");
            CollectionAssert.Contains(fps, "GetList()[]");
        }

        // ---- Bounds: fingerprint cap and depth guard ----

        [Test]
        public void ManyAccessSites_AreCappedAtMaxFingerprints_KeepingTreeOrder()
        {
            // 40 access sites (> MaxFingerprints = 32). Truncation keeps the FIRST N in tree (source)
            // order — not the most frequent — so the earliest sites survive and the tail is dropped.
            var body = string.Concat(Enumerable.Range(0, 40).Select(i => $"Type{i}.Member{i}();\n"));

            var fps = Fingerprints(body);

            Assert.AreEqual(EvalUsageFingerprinter.MaxFingerprints, fps.Count);
            Assert.AreEqual("Type0.Member0", fps.First(), "First-in-source access site survives truncation");
            CollectionAssert.DoesNotContain(fps, "Type39.Member39");
        }

        [Test]
        public void PathologicallyDeepNesting_IsAbandonedWithoutThrowing()
        {
            // 100 nested invocations-in-arguments — several syntax levels per nesting, far past the
            // walker's 64-level depth guard. The walk must terminate cleanly (best-effort telemetry),
            // capturing the shallow sites and abandoning the deep ones.
            var code = "A99.M()";
            for (var i = 98; i >= 0; i--)
                code = $"A{i}.M({code})";
            code += ";";

            List<string> fps = null;
            Assert.DoesNotThrow(() => fps = Fingerprints(code));
            CollectionAssert.Contains(fps, "A0.M");
            CollectionAssert.DoesNotContain(fps, "A99.M");
        }

        [Test]
        public void PathologicallyDeepChainBelowHead_RendersWithoutThrowing()
        {
            // A deeply parenthesised receiver hangs BELOW the chain head, so it is bounded by the
            // renderer's own depth cap (not the walker's): the tail degrades to the placeholder.
            var receiver = "x";
            for (var i = 0; i < 100; i++)
                receiver = "(" + receiver + ")";

            List<string> fps = null;
            Assert.DoesNotThrow(() => fps = Fingerprints($"{receiver}.Foo();"));
            CollectionAssert.Contains(fps, EvalUsageFingerprinter.Placeholder + ".Foo");
        }

        [TestCase("AssetDatabase.Refresh();", EvalClassification.SingleExpression,
            TestName = "Classify_SingleExpressionStatement_IsSingleExpression")]
        [TestCase("return MatchManager.Instance.State;", EvalClassification.SingleExpression,
            TestName = "Classify_SingleReturn_IsSingleExpression")]
        [TestCase("Debug.Log(\"x\"); return 1;", EvalClassification.Statements,
            TestName = "Classify_MultipleStatements_IsStatements")]
        // A single for/while statement is a statement body, not a one-liner (the polling-loop case).
        [TestCase("for (int i = 0; i < 10; i++) { Debug.Log(i); }", EvalClassification.Statements,
            TestName = "Classify_LonePollingLoop_IsStatements")]
        public void Classify_ProducesExpectedClassification(string code, string expected)
        {
            Assert.AreEqual(expected, EvalUsageFingerprinter.Classify(code));
        }

        [Test]
        public void ConditionalAccessChain_RendersWholeChain_NormalizedToDots()
        {
            // Review regression: the old rendering dropped the receiver of a ?. chain and produced
            // a colliding "State.Value" entry — the whole chain must rank as one normalized path.
            var fps = Fingerprints("return MatchManager.Instance?.State.Value;");
            CollectionAssert.Contains(fps, "MatchManager.Instance.State.Value");
            CollectionAssert.DoesNotContain(fps, "State.Value");
            CollectionAssert.DoesNotContain(fps, "MatchManager.Instance");
        }

        [Test]
        public void ConditionalAccessCall_RanksWithItsUnconditionalForm()
        {
            // a?.B() must fingerprint identically to a.B() (callee convention, ?. normalized).
            var conditional = Fingerprints("go?.GetComponent<Rigidbody>();");
            var plain = Fingerprints("go.GetComponent<Rigidbody>();");
            CollectionAssert.Contains(conditional, "go.GetComponent<T>");
            CollectionAssert.AreEquivalent(plain, conditional);
        }

        [Test]
        public void NestedConditionalAccess_NormalizesEveryHop()
        {
            var fps = Fingerprints("return a?.b?.c;");
            CollectionAssert.Contains(fps, "a.b.c");
        }

        [Test]
        public void ConditionalElementAccess_RanksWithItsUnconditionalForm()
        {
            // cache?[key] must fingerprint identically to cache[key] ("cache[]") — the
            // element-access sibling of the a?.b.c collision fixed earlier.
            var conditional = Fingerprints("return cache?[key];");
            var plain = Fingerprints("return cache[key];");
            CollectionAssert.Contains(conditional, "cache[]");
            CollectionAssert.AreEquivalent(plain, conditional);
        }

        [Test]
        public void ConditionalAccess_ReceiverCallArguments_AreNotWalked()
        {
            // Review regression: the argument-list sweep previously started from the whole
            // conditional node, including the RECEIVER before '?.' — so a call there (unlike the
            // unconditional form, which never walks its callee chain's arguments) got its nested
            // calls captured anyway. An equivalent eval body must not get a more complete
            // fingerprint set purely because it happened to use '?.'.
            var conditional = Fingerprints("Configure(Settings.Load())?.Apply();");
            var plain = Fingerprints("Configure(Settings.Load()).Apply();");
            CollectionAssert.AreEquivalent(plain, conditional);
            CollectionAssert.DoesNotContain(conditional, "Settings.Load");
        }

        [Test]
        public void ConditionalAccessBracketedArgument_CapturesNestedCall()
        {
            // Review regression: the argument sweep matched only ArgumentListSyntax, so an
            // indexer's BracketedArgumentListSyntax inside a '?.' chain hid a nested call
            // (SomeCall) from the walker entirely.
            var fps = Fingerprints("return a?.b[SomeCall()];");
            CollectionAssert.Contains(fps, "a.b[]");
            CollectionAssert.Contains(fps, "SomeCall");
        }

        [Test]
        public void Analyze_EmptyOrWhitespace_IsSafe()
        {
            var analysis = EvalUsageFingerprinter.Analyze("   ");
            Assert.AreEqual(EvalClassification.Statements, analysis.Classification);
            Assert.IsEmpty(analysis.Fingerprints);
        }
    }
}
