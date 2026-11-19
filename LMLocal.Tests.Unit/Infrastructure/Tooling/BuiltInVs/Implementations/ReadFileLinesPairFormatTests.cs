using System.Collections.Generic;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// read_file_lines lines are serialized as ["60","text"] pairs; the old {"line_number":60,"text":"..."} form is still read.
    /// </summary>
    [TestFixture]
    public class ReadFileLinesPairFormatTests
    {
        private static ReadFileLines.FileLineEntry Line(int number, string text) =>
            new ReadFileLines.FileLineEntry { LineNumber = number, Text = text };

        [Test]
        public void Serialize_Line_IsPairOfStrings()
        {
            var json = JsonConvert.SerializeObject(Line(60, "    return x;"));

            Assert.That(json, Is.EqualTo("[\"60\",\"    return x;\"]"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\t\tint a = 1;")]
        [TestCase("var s = \"a\";")]
        [TestCase("x = arr[\"k\"];")]
        [TestCase("\"]")]
        [TestCase("path = \"C:\\\\dir\\\\file\";")]
        [TestCase("ends with backslash \\")]
        public void Serialize_ThenDeserialize_KeepsTextExactly(string text)
        {
            var json = JsonConvert.SerializeObject(Line(7, text));
            var back = JsonConvert.DeserializeObject<ReadFileLines.FileLineEntry>(json);

            Assert.That(back.LineNumber, Is.EqualTo(7));
            Assert.That(back.Text, Is.EqualTo(text));
            var pair = JArray.Parse(json);
            Assert.That(pair.Count, Is.EqualTo(2));
            Assert.That(pair[0].Type, Is.EqualTo(JTokenType.String));
            Assert.That(pair[1].Value<string>(), Is.EqualTo(text));
        }

        [Test]
        public void Deserialize_OldObjectForm_IsRead()
        {
            var back = JsonConvert.DeserializeObject<ReadFileLines.FileLineEntry>("{\"line_number\":12,\"text\":\"old\"}");

            Assert.That(back.LineNumber, Is.EqualTo(12));
            Assert.That(back.Text, Is.EqualTo("old"));
        }

        [Test]
        public void TryParse_UnknownShape_IsFalse()
        {
            Assert.That(ReadFileLines.FileLineEntryConverter.TryParse(JToken.Parse("[\"x\",\"text\"]"), out _), Is.False);
            Assert.That(ReadFileLines.FileLineEntryConverter.TryParse(JToken.Parse("[\"1\"]"), out _), Is.False);
            Assert.That(ReadFileLines.FileLineEntryConverter.TryParse(JToken.Parse("\"text\""), out _), Is.False);
        }

        [Test]
        public void Serialize_Response_ContentIsListOfPairs()
        {
            var response = new ReadFileLines.FileLinesResponse
            {
                FilePath = "a.txt",
                Content = new List<ReadFileLines.FileLineEntry> { Line(1, "a1"), Line(2, "") },
                Success = true
            };

            var obj = JObject.Parse(JsonConvert.SerializeObject(response));

            Assert.That(obj["content"].ToString(Formatting.None), Is.EqualTo("[[\"1\",\"a1\"],[\"2\",\"\"]]"));
        }

        [Test]
        public void Markdown_PairFormat_RendersNumberedLines()
        {
            var json = "{\"file_path\":\"a.txt\",\"content\":[[\"4\",\"a4\"],[\"5\",\"a5\"]],\"has_more_results\":false,\"success\":true}";

            var markdown = new ToolResultMarkdownFormatter().FormatToolResults(new[] { ("read_file_lines", json) });

            Assert.That(markdown, Does.Contain("4: a4\n5: a5"));
        }

        [Test]
        public void Markdown_OldObjectFormat_RendersNumberedLines()
        {
            var json = "{\"file_path\":\"a.txt\",\"content\":[{\"line_number\":4,\"text\":\"a4\"},{\"line_number\":5,\"text\":\"a5\"}],\"has_more_results\":false,\"success\":true}";

            var markdown = new ToolResultMarkdownFormatter().FormatToolResults(new[] { ("read_file_lines", json) });

            Assert.That(markdown, Does.Contain("4: a4\n5: a5"));
        }

        [Test]
        public void Markdown_FilesWithSeveralBlocks_RendersEachBlock()
        {
            var json = "{\"files\":[" +
                       "{\"file_path\":\"a.txt\",\"content\":[[\"1\",\"a1\"]],\"has_more_results\":true,\"success\":true}," +
                       "{\"file_path\":\"b.txt\",\"content\":[[\"3\",\"b3\"]],\"has_more_results\":false,\"success\":true}" +
                       "],\"success\":true}";

            var markdown = new ToolResultMarkdownFormatter().FormatToolResults(new[] { ("read_file_lines", json) });

            Assert.That(markdown, Does.Contain("1: a1").And.Contain("3: b3").And.Contain("a.txt").And.Contain("b.txt"));
        }
    }
}
