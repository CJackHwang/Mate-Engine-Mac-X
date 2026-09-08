using System.Collections.Generic;
using NUnit.Framework;
using Unity.Pipeline;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Unit tests for CommandLineTokenizer - the POSIX-ish lexer that splits a raw
    /// <c>commandLine</c> string into argv tokens before binding.
    ///
    /// These tests are the written form of the dialect contract: the dialect is the
    /// pipeline's OWN, identical on every OS, and documented in connectivity.md. Changing
    /// any expectation here is a wire-contract change, not a refactor. In particular the
    /// <c>WindowsDoubledQuoteIsNotAnEscape</c> case pins us to POSIX rules over
    /// <c>CommandLineToArgvW</c>.
    ///
    /// The dialect is a pure input-to-output function, so the cases are a table rather than
    /// ~18 near-identical methods. Each case keeps its own name and, where the rule is not
    /// self-evident, the reason it is that way.
    /// </summary>
    class CommandLineTokenizerTests
    {
        private static List<string> Tokenize(string commandLine)
        {
            Assert.IsTrue(CommandLineTokenizer.TryTokenize(commandLine, out var tokens, out var error),
                $"Expected '{commandLine}' to tokenize, but failed with: {error}");
            Assert.IsNull(error, "A successful tokenize must not report an error");
            return tokens;
        }

        private static TestCaseData Case(string name, string commandLine, params string[] expected)
        {
            return new TestCaseData(commandLine, expected).SetName($"TryTokenize_{name}");
        }

        private static IEnumerable<TestCaseData> TokenizeCases()
        {
            yield return Case("SimpleWordsSplitOnWhitespace",
                "eval return 2+2;", "eval", "return", "2+2;");

            // Leading, trailing, tabs and repeated spaces all collapse.
            yield return Case("WhitespaceRunsProduceNoEmptyTokens", "   a \t  b  ", "a", "b");

            // A pure lexer has no opinion about needing a command name; the caller rejects an
            // empty token list so the message matches the structured path.
            yield return Case("EmptyStringYieldsNoTokens", "");
            yield return Case("WhitespaceOnlyYieldsNoTokens", "    ");
            yield return Case("NullYieldsNoTokens", null);

            // eval "return 2+2;"
            yield return Case("DoubleQuotesGroupWhitespaceIntoOneToken",
                "eval \"return 2+2;\"", "eval", "return 2+2;");

            // eval 'obj.name = "x";'   <- the reason single quotes are load-bearing
            yield return Case("SingleQuotesGroupWhitespaceAndPassDoubleQuotesThrough",
                "eval 'obj.name = \"x\";'", "eval", "obj.name = \"x\";");

            // "a\"b"  ->  a"b
            yield return Case("BackslashInsideDoubleQuotesEscapesNextCharacter",
                "\"a\\\"b\"", "a\"b");

            // 'a\b'  ->  a\b     (single quotes have NO escapes at all)
            yield return Case("BackslashInsideSingleQuotesIsLiteral", "'a\\b'", "a\\b");

            // a\ b  ->  one token "a b"
            yield return Case("BackslashOutsideQuotesEscapesNextCharacter", "a\\ b", "a b");

            yield return Case("SingleQuoteInsideDoubleQuotesIsLiteral", "\"it's\"", "it's");

            // a"b c"d  ->  ab cd
            yield return Case("AdjacentSpansConcatenateIntoOneToken", "a\"b c\"d", "ab cd");

            // ""  ->  a single EMPTY token, not zero tokens. This is what lets `--message ""`
            // mean the empty string instead of the boolean true.
            yield return Case("EmptyQuotedSpanYieldsOneEmptyToken", "\"\"", string.Empty);

            // PINS THE DIALECT. Windows CommandLineToArgvW reads "a""b" as a"b. POSIX-ish closes
            // the span and opens a new one, so it concatenates to ab.
            yield return Case("WindowsDoubledQuoteIsNotAnEscape", "\"a\"\"b\"", "ab");

            // No expansion of any kind: no operators, no $VAR, no globbing, no ~.
            yield return Case("ShellOperatorsAreLiteralTokens",
                "a && b | c > d $HOME *.cs ~",
                "a", "&&", "b", "|", "c", ">", "d", "$HOME", "*.cs", "~");

            yield return Case("UnityAssetPathSurvivesUnchanged",
                "reload_file Assets/Scripts/Player.cs", "reload_file", "Assets/Scripts/Player.cs");

            // The tokenizer does not interpret flags; the binder does.
            yield return Case("FlagsAndDoubleDashArePlainTokens",
                "log_editor --message hi -- -x", "log_editor", "--message", "hi", "--", "-x");
        }

        [TestCaseSource(nameof(TokenizeCases))]
        public void TryTokenize_ProducesTheExpectedTokens(string commandLine, string[] expected)
        {
            CollectionAssert.AreEqual(expected, Tokenize(commandLine));
        }

        [TestCase("eval \"unbalanced", "quote", TestName = "TryTokenize_UnbalancedDoubleQuote_IsRejected")]
        [TestCase("eval 'unbalanced", "quote", TestName = "TryTokenize_UnbalancedSingleQuote_IsRejected")]
        [TestCase("abc\\", "backslash", TestName = "TryTokenize_DanglingBackslash_IsRejected")]
        public void TryTokenize_MalformedInput_IsRejectedWithAnExplanation(
            string commandLine, string expectedInMessage)
        {
            Assert.IsFalse(CommandLineTokenizer.TryTokenize(commandLine, out _, out var error),
                $"Expected '{commandLine}' to be rejected");
            Assert.IsNotEmpty(error, "A failed tokenize must explain why");
            StringAssert.Contains(expectedInMessage, error.ToLowerInvariant());
        }
    }
}
