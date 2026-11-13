using System.Text;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.Tool;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class ArgumentsLengthGuardTests
    {
        private const string LeakedToken = "{\"file_name\":\"x<|channel>";

        private static string Check(IToolCallArgumentsGuard guard, string functionName, string arguments)
        {
            return guard.Check(functionName, new StringBuilder(arguments), arguments);
        }

        private static string Args(int length)
        {
            return new string('a', length);
        }

        // ================ ToolArgumentLimits ================

        [TestCase("read_file_lines")]
        [TestCase("search_file_content")]
        [TestCase("find_files")]
        [TestCase("get_changes_diff")]
        [TestCase("FIND_FILES")]
        public void Limits_SimpleTool_HasSimpleLimit(string toolName)
        {
            Assert.That(ToolArgumentLimits.GetMaxLength(toolName), Is.EqualTo(ToolArgumentLimits.SimpleToolMaxLength));
        }

        [TestCase("code_explorer_subagent")]
        [TestCase("code_reader_subagent")]
        [TestCase("symbol_analyzer_subagent")]
        [TestCase("build_subagent")]
        [TestCase("context_retriever_subagent")]
        public void Limits_ShortTaskSubAgent_HasSubAgentLimit(string toolName)
        {
            Assert.That(ToolArgumentLimits.GetMaxLength(toolName), Is.EqualTo(ToolArgumentLimits.ShortTaskSubAgentMaxLength));
        }

        [TestCase("create_file")]
        [TestCase("replace_file_content")]
        [TestCase("replace_file_lines")]
        [TestCase("insert_file_lines")]
        [TestCase("reviewer_subagent")]
        [TestCase("task_planner_subagent")]
        [TestCase("architect_subagent")]
        [TestCase("some_mcp_tool")]
        [TestCase("")]
        [TestCase(null)]
        public void Limits_WriteToolsContentSubAgentsAndUnknown_NoLimit(string toolName)
        {
            Assert.That(ToolArgumentLimits.GetMaxLength(toolName), Is.Null);
        }

        [Test]
        public void Limits_NoWriteToolIsLimited()
        {
            foreach (var name in new[] { "create_file", "replace_file_content", "replace_file_lines", "insert_file_lines" })
            {
                Assert.That(WriteTools.IsWriteTool(name), Is.True, name);
                Assert.That(ToolArgumentLimits.GetMaxLength(name), Is.Null, name);
            }
        }

        // ================ ArgumentsLengthGuard ================

        [Test]
        public void Guard_SimpleToolAtLimit_NotStopped()
        {
            Assert.That(Check(ArgumentsLengthGuard.Instance, "find_files", Args(ToolArgumentLimits.SimpleToolMaxLength)), Is.Null);
        }

        [Test]
        public void Guard_SimpleToolOverLimit_Stopped()
        {
            string reason = Check(ArgumentsLengthGuard.Instance, "find_files", Args(ToolArgumentLimits.SimpleToolMaxLength + 1));

            Assert.That(reason, Does.Contain("find_files").And.Contain(ToolArgumentLimits.SimpleToolMaxLength.ToString()));
        }

        [Test]
        public void Guard_ShortTaskSubAgent_StoppedOnlyOverItsLimit()
        {
            Assert.That(Check(ArgumentsLengthGuard.Instance, "code_reader_subagent", Args(ToolArgumentLimits.ShortTaskSubAgentMaxLength)), Is.Null);
            Assert.That(Check(ArgumentsLengthGuard.Instance, "code_reader_subagent", Args(ToolArgumentLimits.ShortTaskSubAgentMaxLength + 1)), Is.Not.Null);
        }

        [TestCase("replace_file_lines")]
        [TestCase("create_file")]
        [TestCase("reviewer_subagent")]
        [TestCase("some_mcp_tool")]
        [TestCase(null)]
        public void Guard_UnlimitedOrUnknownTool_NeverStopped(string toolName)
        {
            Assert.That(Check(ArgumentsLengthGuard.Instance, toolName, Args(100000)), Is.Null);
        }

        [Test]
        public void Guard_GrowsFragmentByFragment_StopsOnFragmentThatCrossesLimit()
        {
            var arguments = new StringBuilder();
            int stoppedAt = -1;
            for (int i = 0; i < 1000 && stoppedAt < 0; i++)
            {
                const string fragment = "0123456789";
                arguments.Append(fragment);
                if (ArgumentsLengthGuard.Instance.Check("search_file_content", arguments, fragment) != null)
                    stoppedAt = i;
            }

            // 400 fragments of 10 chars reach the limit exactly; the 401st (index 400) crosses it.
            Assert.That(stoppedAt, Is.EqualTo(ToolArgumentLimits.SimpleToolMaxLength / 10));
        }

        [Test]
        public void Guard_ControlTokens_Ignored()
        {
            Assert.That(Check(ArgumentsLengthGuard.Instance, "find_files", LeakedToken), Is.Null);
        }

        // ================ CompositeToolCallArgumentsGuard ================

        private sealed class FixedGuard : IToolCallArgumentsGuard
        {
            private readonly string _reason;
            public int Calls;

            public FixedGuard(string reason)
            {
                _reason = reason;
            }

            public string Check(string functionName, StringBuilder arguments, string fragment)
            {
                Calls++;
                return _reason;
            }
        }

        [Test]
        public void Composite_ReturnsFirstReasonAndSkipsTheRest()
        {
            var first = new FixedGuard(null);
            var second = new FixedGuard("second");
            var third = new FixedGuard("third");

            string reason = Check(new CompositeToolCallArgumentsGuard(first, second, third), "find_files", "{}");

            Assert.That(reason, Is.EqualTo("second"));
            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(third.Calls, Is.EqualTo(0));
        }

        [Test]
        public void Composite_NoGuardStops_ReturnsNull()
        {
            Assert.That(Check(new CompositeToolCallArgumentsGuard(new FixedGuard(null), new FixedGuard(null)), "find_files", "{}"), Is.Null);
        }

        // ================ ToolCallArgumentsGuardFactory ================

        [TestCase("google_gemma-4-e4b-it")]
        [TestCase("google/gemma-3-12b")]
        [TestCase("GEMMA-2")]
        public void Factory_GemmaModel_StopsOnControlTokenAndOnLength(string modelId)
        {
            var guard = new ToolCallArgumentsGuardFactory().Create(modelId);

            Assert.That(guard, Is.Not.Null);
            Assert.That(Check(guard, "find_files", LeakedToken), Does.Contain("<|channel>"));
            Assert.That(Check(guard, "find_files", Args(ToolArgumentLimits.SimpleToolMaxLength + 1)), Does.Contain("exceed"));
            Assert.That(Check(guard, "find_files", "{\"file_name\":\"*Guard*\"}"), Is.Null);
        }

        [TestCase("qwen/qwen3-coder-30b")]
        [TestCase("deepseek-v4")]
        [TestCase("")]
        [TestCase(null)]
        public void Factory_OtherModel_StopsOnLengthOnly(string modelId)
        {
            var guard = new ToolCallArgumentsGuardFactory().Create(modelId);

            Assert.That(guard, Is.SameAs(ArgumentsLengthGuard.Instance));
            Assert.That(Check(guard, "find_files", LeakedToken), Is.Null);
            Assert.That(Check(guard, "find_files", Args(ToolArgumentLimits.SimpleToolMaxLength + 1)), Is.Not.Null);
        }
    }
}
