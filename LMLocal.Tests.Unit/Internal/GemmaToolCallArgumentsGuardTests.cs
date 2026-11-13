using System.Text;
using LMLocal.Application.ChatSessionStream;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class GemmaToolCallArgumentsGuardTests
    {
        // Argument fragments of real broken calls, as LM Studio streamed them (server logs), up to the first protocol junction.

        // 2026-11-10 11:33, explorer, search_file_content: valid JSON with leaked tokens in "text".
        private static readonly string[] Case1SearchFileContent = { "{", "\"max", "_", "results", "\":", "1", "0", "0", ",", "\"page", "_", "token", "\":", "\"", "1", "0", "\"", ",", "\"path", "\":", "\"", "L", "ML", "ocal", "\\\\", "Application", "\\\\", "Chat", "\\\\", "Chat", "History", "Manager", ".", "cs", "\"", ",", "\"text", "\":", "\"", "}", "<tool_call|>", "<|tool_call>" };

        // 2026-11-12 14:31, explorer, search_file_content: unclosed string value, then a runaway up to max_tokens.
        private static readonly string[] Case2SearchFileContent = { "{", "\"max", "_", "results", "\":", "1", "0", ",", "\"path", "\":", "\"", "Docs", "\\\\", "token", "-", "speed", "-", "plan", ".", "md", "\"", ",", "\"text", "\":", "\"", "`", "Stream", "Processor", ".", "cs", "`", " —", " вместо", " ``", "}", "<tool_call|>", "<|channel>" };

        // 2026-11-12 19:31 (reproduction of 13:36), explorer, find_files: key=value syntax instead of the string delimiter.
        private static readonly string[] Case3FindFiles = { "{", "\"file", "_", "name", "=\\\"", "*", "Stream", "Error", "Classifier", "*\\\",", " max", "_", "results", "=", "5", "0", "0", "<|\\\"|>", "\\n", "<|channel>", "thought", "\\n", "The", " user", " requested", " to", " find", " files", " named", " \\\"", "Stream", "Error", "Classifier", "\\\"", " or", " \\\"", "I", "Reconnect", "Policy", "\\\"", " or", " \\\"", "Default", "Reconnect", "Policy", "\\\".", "\\n", "I", " have", " executed", " the", " first", " search", " for", " `", "*", "Stream", "Error", "Classifier", "*", "`.", " I", " need", " to", " continue", " with", " the", " other", " two", " searches", ".", "\\n\\n", "Next", ",", " I", " will", " search", " for", " files", " containing", " \\\"", "I", "Reconnect", "Policy", "\\\".", "<channel|>", "<|tool_call>" };

        /// <summary>
        /// Feeds the fragments one by one, as the accumulator does. Returns the index of the fragment that stopped the call, or -1.
        /// </summary>
        private static int FirstRejectedFragment(string functionName, string[] fragments, out string reason)
        {
            var arguments = new StringBuilder();
            for (int i = 0; i < fragments.Length; i++)
            {
                arguments.Append(fragments[i]);
                reason = GemmaToolCallArgumentsGuard.Instance.Check(functionName, arguments, fragments[i]);
                if (reason != null)
                    return i;
            }

            reason = null;
            return -1;
        }

        private static string Check(string functionName, string arguments)
        {
            return GemmaToolCallArgumentsGuard.Instance.Check(functionName, new StringBuilder(arguments), arguments);
        }

        private static readonly string[] AllTokens = GemmaControlTokens.All;

        // ================ GemmaControlTokens ================

        [Test]
        public void ControlTokens_AllEndWithGreaterThan()
        {
            foreach (var token in GemmaControlTokens.All)
                Assert.That(token, Does.EndWith(">"), token);
        }

        [TestCaseSource(nameof(AllTokens))]
        public void MatchEndingAt_EachToken_Found(string token)
        {
            var text = new StringBuilder("abc" + token);

            Assert.That(GemmaControlTokens.MatchEndingAt(text, text.Length - 1), Is.EqualTo(token));
        }

        [TestCase("a => b")]
        [TestCase("List<int>")]
        [TestCase("x > 0")]
        [TestCase("<b>")]
        [TestCase("|>")]
        [TestCase("<|tool_call|>")]
        [TestCase(">")]
        public void MatchEndingAt_OrdinaryText_NoToken(string text)
        {
            var builder = new StringBuilder(text);

            Assert.That(GemmaControlTokens.MatchEndingAt(builder, builder.Length - 1), Is.Null);
        }

        [Test]
        public void MatchEndingAt_PositionIsNotGreaterThan_NoToken()
        {
            var text = new StringBuilder("<|channel>x");

            Assert.That(GemmaControlTokens.MatchEndingAt(text, text.Length - 1), Is.Null);
            Assert.That(GemmaControlTokens.MatchEndingAt(text, -1), Is.Null);
            Assert.That(GemmaControlTokens.MatchEndingAt(text, 100), Is.Null);
        }

        [TestCase(GemmaControlTokens.ToolCallClose, GemmaControlTokens.ToolCallOpen, true)]
        [TestCase(GemmaControlTokens.ToolCallClose, GemmaControlTokens.ChannelOpen, true)]
        [TestCase(GemmaControlTokens.ChannelClose, GemmaControlTokens.ToolCallOpen, true)]
        [TestCase(GemmaControlTokens.ToolCallOpen, GemmaControlTokens.ToolCallClose, false)]
        [TestCase(GemmaControlTokens.EscapedStringDelimiter, GemmaControlTokens.EscapedStringDelimiter, false)]
        [TestCase(GemmaControlTokens.ChannelOpen, GemmaControlTokens.ChannelClose, false)]
        public void IsJunction_OnlyCloseThenOpen(string previous, string next, bool expected)
        {
            Assert.That(GemmaControlTokens.IsJunction(previous, next), Is.EqualTo(expected));
        }

        // ================ Guard rules ================

        [TestCaseSource(nameof(AllTokens))]
        public void Check_ReadTool_AnySingleToken_Stops(string token)
        {
            Assert.That(Check("search_file_content", "{\"text\":\"a" + token), Is.Not.Null);
        }

        [TestCase("replace_file_lines")]
        [TestCase("create_file")]
        [TestCase(null)]
        public void Check_WriteToolOrUnknown_SingleToken_Continues(string functionName)
        {
            Assert.That(Check(functionName, "{\"new_lines\":\"x <|channel> y <tool_call|> z <|\\\"|>"), Is.Null);
        }

        [TestCase("replace_file_lines")]
        [TestCase(null)]
        public void Check_WriteToolOrUnknown_Junction_Stops(string functionName)
        {
            Assert.That(Check(functionName, "{\"new_lines\":\"x}<tool_call|><|channel>thought"), Is.Not.Null);
        }

        [Test]
        public void Check_WriteTool_TokensWithTextBetween_Continues()
        {
            Assert.That(Check("replace_file_lines", "{\"new_lines\":\"<tool_call|> <|channel>"), Is.Null);
        }

        [Test]
        public void Check_ReadTool_OrdinaryGreaterThan_Continues()
        {
            Assert.That(Check("search_file_content", "{\"text\":\"x => x > 0 && List<int>\",\"path\":\"a.cs\"}"), Is.Null);
        }

        [Test]
        public void Check_FragmentWithoutGreaterThan_Continues()
        {
            // The token is already in the buffer, but only a fragment with '>' can complete a token, so no new check is made.
            var arguments = new StringBuilder("{\"text\":\"<|channel>abc");

            Assert.That(GemmaToolCallArgumentsGuard.Instance.Check("search_file_content", arguments, "abc"), Is.Null);
        }

        [Test]
        public void Check_TokenSplitAcrossFragments_Stops()
        {
            int rejected = FirstRejectedFragment("find_files", new[] { "{\"file_name\":\"a", "<|cha", "nn", "el>" }, out var reason);

            Assert.That(rejected, Is.EqualTo(3));
            Assert.That(reason, Does.Contain(GemmaControlTokens.ChannelOpen));
        }

        [Test]
        public void Check_JunctionSplitAcrossFragments_Stops()
        {
            int rejected = FirstRejectedFragment("create_file", new[] { "{\"content\":\"a<tool_", "call|><|tool", "_call>" }, out _);

            Assert.That(rejected, Is.EqualTo(2));
        }

        // ================ Real broken calls ================

        [Test]
        public void Check_Case1_LeakedTokensInValidJson_StopsAtFirstToken()
        {
            int rejected = FirstRejectedFragment("search_file_content", Case1SearchFileContent, out var reason);

            Assert.That(rejected, Is.EqualTo(40), reason);
            Assert.That(Case1SearchFileContent[rejected], Is.EqualTo(GemmaControlTokens.ToolCallClose));
        }

        [Test]
        public void Check_Case2_UnclosedString_StopsAtFirstToken()
        {
            int rejected = FirstRejectedFragment("search_file_content", Case2SearchFileContent, out var reason);

            Assert.That(rejected, Is.EqualTo(35), reason);
            Assert.That(Case2SearchFileContent[rejected], Is.EqualTo(GemmaControlTokens.ToolCallClose));
        }

        [Test]
        public void Check_Case3_KeyValueSyntax_StopsAtEscapedStringDelimiter()
        {
            int rejected = FirstRejectedFragment("find_files", Case3FindFiles, out var reason);

            Assert.That(rejected, Is.EqualTo(17), reason);
            Assert.That(Case3FindFiles[rejected], Is.EqualTo(GemmaControlTokens.EscapedStringDelimiter));
        }

        [Test]
        public void Check_RealCases_AsWriteTool_StopAtJunction()
        {
            // The lenient rule still stops all three cases, at the first junction (the last fragment of each array).
            Assert.That(FirstRejectedFragment("replace_file_lines", Case1SearchFileContent, out _), Is.EqualTo(Case1SearchFileContent.Length - 1));
            Assert.That(FirstRejectedFragment("replace_file_lines", Case2SearchFileContent, out _), Is.EqualTo(Case2SearchFileContent.Length - 1));
            Assert.That(FirstRejectedFragment("replace_file_lines", Case3FindFiles, out _), Is.EqualTo(Case3FindFiles.Length - 1));
        }
    }
}
