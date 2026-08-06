using System.Collections.Generic;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class ChatHistoryNormalizerTests
    {
        // ================ Normalize() ================

        [Test]
        public void Normalize_PreservesContent_OnlyNormalizesWhitespace()
        {
            // Markdown is intentionally NOT stripped - only safe whitespace normalization happens.
            var input = "# Header\n**bold** *italic* [link](url)\n- item";

            var result = ChatHistoryNormalizer.Normalize(input);

            Assert.That(result, Is.EqualTo(input));
        }

        [Test]
        public void Normalize_EmptyOrNull_ReturnsInput()
        {
            Assert.That(ChatHistoryNormalizer.Normalize(null), Is.Null);
            Assert.That(ChatHistoryNormalizer.Normalize(""), Is.EqualTo(""));
            Assert.That(ChatHistoryNormalizer.Normalize("   "), Is.EqualTo(""));
        }

        [Test]
        public void Normalize_PreservesUnderlines()
        {
            // Underscores are plain text, not markdown - content must be preserved.
            var input = "Use __init__ and _count in Python";
            var result = ChatHistoryNormalizer.Normalize(input);

            Assert.That(result, Does.Contain("__init__"));
            Assert.That(result, Does.Contain("_count"));
        }

        [Test]
        public void Normalize_PreservesInlineCode()
        {
            // Inline code backticks are preserved - only whitespace is normalized.
            var input = "Use `some_code` in Python";
            var result = ChatHistoryNormalizer.Normalize(input);

            Assert.That(result, Does.Contain("`some_code`"));
            Assert.That(result, Does.Contain("`"));
        }

        [Test]
        public void Normalize_EdgeCases()
        {
            Assert.That(ChatHistoryNormalizer.Normalize("a\n\n\n\n\nb"), Is.EqualTo("a\n\nb"));
            Assert.That(ChatHistoryNormalizer.Normalize(""), Is.EqualTo(""));
            Assert.That(ChatHistoryNormalizer.Normalize("   "), Is.EqualTo(""));
        }

        [Test]
        public void Normalize_NullInput_ReturnsNull()
        {
            Assert.That(ChatHistoryNormalizer.Normalize(null), Is.Null);
        }

        [Test]
        public void Normalize_PreservesMarkdownFormatting()
        {
            // Formatting is deliberately preserved - content must not change.
            var input = "# Title\n**bold** *italic* `code`\n- list\n1. ordered\n> quote\n\n---\n\nend";
            var result = ChatHistoryNormalizer.Normalize(input);

            Assert.That(result, Is.EqualTo(input));
            Assert.That(result, Does.Contain("# Title"));
            Assert.That(result, Does.Contain("**bold**"));
            Assert.That(result, Does.Contain("`code`"));
            Assert.That(result, Does.Contain("- list"));
            Assert.That(result, Does.Contain("1. ordered"));
            Assert.That(result, Does.Contain("> quote"));
            Assert.That(result, Does.Contain("---"));
            Assert.That(result, Does.Contain("end"));
        }

        [Test]
        public void Normalize_PreservesContentInsideFencedCodeBlock()
        {
            var input = "before\n```\n**bold** *italic* inside\n```\nafter";
            var result = ChatHistoryNormalizer.Normalize(input);

            Assert.That(result, Does.Contain("before"));
            Assert.That(result, Does.Contain("after"));
            Assert.That(result, Does.Contain("**bold** *italic* inside"));
        }

        [Test]
        public void Normalize_PreservesFormattingAroundCodeBlocks()
        {
            // Fence markers are removed but formatting outside and inside is preserved.
            var input = "**outside**\n```\ncode\n```\n*outside*";
            var result = ChatHistoryNormalizer.Normalize(input);

            Assert.That(result, Does.Contain("**outside**"));
            Assert.That(result, Does.Contain("*outside*"));
            Assert.That(result, Does.Contain("code"));
            Assert.That(result, Does.Not.Contain("```"));
        }

        // ================ NormalizeMessages() ================

        [Test]
        public void NormalizeMessages_NullOrEmptyList_ReturnsEmptyList()
        {
            Assert.That(ChatHistoryNormalizer.NormalizeMessages(null), Is.Empty);
            Assert.That(ChatHistoryNormalizer.NormalizeMessages(new List<ChatMessage>()), Is.Empty);
        }

        [Test]
        public void NormalizeMessages_PreservesContent()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage("system", "# System prompt **bold**"),
                new ChatMessage("user", "My **question**"),
                new ChatMessage("assistant", "The **answer**")
            };

            var result = ChatHistoryNormalizer.NormalizeMessages(messages);

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0].Content, Is.EqualTo("# System prompt **bold**"));
            Assert.That(result[1].Content, Is.EqualTo("My **question**"));
            Assert.That(result[2].Content, Is.EqualTo("The **answer**"));
            Assert.That(result[0].Role, Is.EqualTo("system"));
            Assert.That(result[1].Role, Is.EqualTo("user"));
            Assert.That(result[2].Role, Is.EqualTo("assistant"));
        }

        [Test]
        public void NormalizeMessages_PreservesToolCalls()
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

            var result = ChatHistoryNormalizer.NormalizeMessages(messages);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].ToolCalls, Is.SameAs(toolCalls));
        }

        [Test]
        public void NormalizeMessages_PreservesToolCallId()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage("tool", "result content", "call-123")
            };

            var result = ChatHistoryNormalizer.NormalizeMessages(messages);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].ToolCallId, Is.EqualTo("call-123"));
        }

        [Test]
        public void NormalizeMessages_NonStringContent_Unchanged()
        {
            var jsonContent = JObject.FromObject(new { key = "value" });

            var messages = new List<ChatMessage>
            {
                new ChatMessage("user", jsonContent)
            };

            var result = ChatHistoryNormalizer.NormalizeMessages(messages);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Content, Is.SameAs(jsonContent));
        }
    }
}
