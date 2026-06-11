using System.Collections.Generic;
using LMLocal.Infrastructure.LlmApi.Converter;
using LMLocal.Infrastructure.Tooling;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ToolDefinitionConverterTests
    {
        [Test]
        public void ConvertToOpenAiFormat_NullOrEmpty_ReturnsEmptyList()
        {
            var resNull = ToolDefinitionConverter.ConvertToOpenAiFormat(null);
            Assert.That(resNull, Is.Not.Null);
            Assert.That(resNull.Count, Is.EqualTo(0));

            var resEmpty = ToolDefinitionConverter.ConvertToOpenAiFormat(new List<ToolDefinition>());
            Assert.That(resEmpty, Is.Not.Null);
            Assert.That(resEmpty.Count, Is.EqualTo(0));
        }

        [Test]
        public void ConvertToOpenAiFormat_MapsFieldsCorrectly()
        {
            var internalTool = new ToolDefinition
            {
                Name = "search",
                Description = "Search files",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "query", new ToolDetails { Type = "string", Description = "search query" } },
                        { "ext", new ToolDetails { Type = "string", Description = "extension" } }
                    },
                    Required = new List<string> { "query" }
                }
            };

            var result = ToolDefinitionConverter.ConvertToOpenAiFormat(new List<ToolDefinition> { internalTool });

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1));

            var tool = result[0];
            Assert.That(tool.Type, Is.EqualTo("function"));
            Assert.That(tool.Function, Is.Not.Null);
            Assert.That(tool.Function.Name, Is.EqualTo("search"));
            Assert.That(tool.Function.Description, Is.EqualTo("Search files"));
            Assert.That(tool.Function.Parameters, Is.Not.Null);
            Assert.That(tool.Function.Parameters.Properties.ContainsKey("query"), Is.True);
            var prop = tool.Function.Parameters.Properties["query"] as Dictionary<string, object>;
            Assert.That(prop["type"], Is.EqualTo("string"));
        }
    }
}
