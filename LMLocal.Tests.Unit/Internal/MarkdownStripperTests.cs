using System.Collections.Generic;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class MarkdownStripperTests
    {
        // ================ Strip() ================

        [Test]
        public void Strip_RemovesMarkdownSyntax_ReturnsPlainText()
        {
            var input = "# Header\n**bold** *italic* [link](url)\n- item";
            var expected = "Header\nbold italic link\nitem";

            var result = MarkdownStripper.Strip(input);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void Strip_EmptyOrNull_ReturnsInput()
        {
            Assert.That(MarkdownStripper.Strip(null), Is.Null);
            Assert.That(MarkdownStripper.Strip(""), Is.EqualTo(""));
            Assert.That(MarkdownStripper.Strip("   "), Is.EqualTo(""));
        }

        [Test]
        public void Strip_PreservesDunders_AfterUnderlineRemoved()
        {
            // Both UnderlineBold (__(.+?)__) and UnderlineItalic (_(.+?)_) removed
            var input = "Use __init__ and _count in Python";
            var result = MarkdownStripper.Strip(input);

            Assert.That(result, Does.Contain("__init__"));
            Assert.That(result, Does.Contain("_count"));
        }

        [Test]
        public void Strip_RemovesInlineCodeBackticks()
        {
            // InlineCodeRegex removes backticks, preserves content
            var input = "Use `some_code` in Python";
            var result = MarkdownStripper.Strip(input);

            Assert.That(result, Does.Contain("some_code"));
            Assert.That(result, Does.Not.Contain("`"));
        }

        [Test]
        public void Strip_EdgeCases()
        {
            Assert.That(MarkdownStripper.Strip("a\n\n\n\n\nb"), Is.EqualTo("a\n\nb"));
            Assert.That(MarkdownStripper.Strip(""), Is.EqualTo(""));
            Assert.That(MarkdownStripper.Strip("   "), Is.EqualTo(""));
        }

        [Test]
        public void Strip_NullInput_ReturnsNull()
        {
            Assert.That(MarkdownStripper.Strip(null), Is.Null);
        }

        [Test]
        public void Strip_RemovesAllFormatting()
        {
            var input = "# Title\n**bold** *italic* `code`\n- list\n1. ordered\n> quote\n\n---\n\nend";
            var result = MarkdownStripper.Strip(input);

            Assert.That(result, Does.Not.Contain("# Title"));
            Assert.That(result, Does.Not.Contain("**bold**"));
            Assert.That(result, Does.Not.Contain("*italic*"));
            Assert.That(result, Does.Not.Contain("`code`"));
            Assert.That(result, Does.Not.Contain("- list"));
            Assert.That(result, Does.Not.Contain("1. ordered"));
            Assert.That(result, Does.Not.Contain("> quote"));
            Assert.That(result, Does.Not.Contain("---"));
            Assert.That(result, Does.Contain("Title"));
            Assert.That(result, Does.Contain("bold"));
            Assert.That(result, Does.Contain("italic"));
            Assert.That(result, Does.Contain("code"));
            Assert.That(result, Does.Contain("list"));
            Assert.That(result, Does.Contain("ordered"));
            Assert.That(result, Does.Contain("quote"));
            Assert.That(result, Does.Contain("end"));
        }


        [Test]
        public void Strip_PreservesInlineMarkdownInsideFencedCodeBlock()
        {
            var input = "before\n```\n**bold** *italic* inside\n```\nafter";
            var result = MarkdownStripper.Strip(input);

            Assert.That(result, Does.Contain("before"));
            Assert.That(result, Does.Contain("after"));
            Assert.That(result, Does.Contain("**bold** *italic* inside"));
        }

        [Test]
        public void Strip_StripsFormattingAroundCodeBlocks()
        {
            var input = "**outside**\n```\ncode\n```\n*outside*";
            var result = MarkdownStripper.Strip(input);

            Assert.That(result, Does.Contain("outside"));
            Assert.That(result, Does.Contain("code"));
            Assert.That(result, Does.Not.Contain("**outside**"));
            Assert.That(result, Does.Not.Contain("*outside*"));
        }
        // ================ StripMessages() ================

        [Test]
        public void StripMessages_NullOrEmptyList_ReturnsEmptyList()
        {
            Assert.That(MarkdownStripper.StripMessages(null), Is.Empty);
            Assert.That(MarkdownStripper.StripMessages(new List<ChatMessage>()), Is.Empty);
        }

        [Test]
        public void StripMessages_StripsContent()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage("system", "# System prompt **bold**"),
                new ChatMessage("user", "My **question**"),
                new ChatMessage("assistant", "The **answer**")
            };

            var result = MarkdownStripper.StripMessages(messages);

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0].Content, Is.EqualTo("System prompt bold"));
            Assert.That(result[1].Content, Is.EqualTo("My question"));
            Assert.That(result[2].Content, Is.EqualTo("The answer"));
            Assert.That(result[0].Role, Is.EqualTo("system"));
            Assert.That(result[1].Role, Is.EqualTo("user"));
            Assert.That(result[2].Role, Is.EqualTo("assistant"));
        }

        [Test]
        public void StripMessages_PreservesToolCalls()
        {
            var toolCalls = new List<ToolCall>
            {
                new ToolCall
                {
                    Id = "call1",
                    Type = "function",
                    Function = new FunctionCallDetails
                    {
                        Name = "test_tool",
                        Arguments = "{}"
                    }
                }
            };

            var messages = new List<ChatMessage>
            {
                new ChatMessage("assistant", null)
                {
                    ToolCalls = toolCalls
                }
            };

            var result = MarkdownStripper.StripMessages(messages);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].ToolCalls, Is.SameAs(toolCalls));
        }

        [Test]
        public void StripMessages_PreservesToolCallId()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage("tool", "result content", "call-123")
            };

            var result = MarkdownStripper.StripMessages(messages);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].ToolCallId, Is.EqualTo("call-123"));
        }

        [Test]
        public void StripMessages_NonStringContent_Unchanged()
        {
            var jsonContent = JObject.FromObject(new { key = "value" });

            var messages = new List<ChatMessage>
            {
                new ChatMessage("user", jsonContent)
            };

            var result = MarkdownStripper.StripMessages(messages);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Content, Is.SameAs(jsonContent));
        }
    }
}
