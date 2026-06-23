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

        [Test]
        public void ConvertToOpenAiFormat_ArrayParameterWithItems_IncludesItemsSchema()
        {
            // Arrange
            var internalTool = new ToolDefinition
            {
                Name = "read_file_ranges",
                Description = "Reads multiple line ranges.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["file_path"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Path to the source file."
                        },
                        ["ranges"] = new ToolDetails
                        {
                            Type = "array",
                            Description = "List of line ranges.",
                            Items = new ToolDetails
                            {
                                Type = "object",
                                Properties = new Dictionary<string, ToolDetails>
                                {
                                    ["start_line"] = new ToolDetails
                                    {
                                        Type = "integer",
                                        Description = "The starting line number (>= 1)."
                                    },
                                    ["end_line"] = new ToolDetails
                                    {
                                        Type = "integer",
                                        Description = "The ending line number (>= start_line)."
                                    }
                                },
                                Required = new List<string> { "start_line", "end_line" }
                            }
                        }
                    },
                    Required = new List<string> { "file_path", "ranges" }
                }
            };

            // Act
            var result = ToolDefinitionConverter.ConvertToOpenAiFormat(
                new List<ToolDefinition> { internalTool });

            // Assert
            var props = result[0].Function.Parameters.Properties;
            Assert.That(props, Contains.Key("ranges"));

            var ranges = props["ranges"] as Dictionary<string, object>;
            Assert.That(ranges, Is.Not.Null);
            Assert.That(ranges["type"], Is.EqualTo("array"));

            var items = ranges["items"] as Dictionary<string, object>;
            Assert.That(items, Is.Not.Null);
            Assert.That(items["type"], Is.EqualTo("object"));

            var nestedProps = items["properties"] as Dictionary<string, object>;
            Assert.That(nestedProps, Is.Not.Null);
            Assert.That(nestedProps, Contains.Key("start_line"));
            Assert.That(nestedProps, Contains.Key("end_line"));

            var startLine = nestedProps["start_line"] as Dictionary<string, object>;
            Assert.That(startLine["type"], Is.EqualTo("integer"));

            var endLine = nestedProps["end_line"] as Dictionary<string, object>;
            Assert.That(endLine["type"], Is.EqualTo("integer"));

            var nestedRequired = items["required"] as List<string>;
            Assert.That(nestedRequired, Is.Not.Null);
            Assert.That(nestedRequired, Is.EquivalentTo(new[] { "start_line", "end_line" }));
        }

        [Test]
        public void ConvertToOpenAiFormat_ObjectParameterWithNestedProperties_IncludesNestedPropertiesAndRequired()
        {
            // Arrange
            var internalTool = new ToolDefinition
            {
                Name = "configure",
                Description = "Configures a setting.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["options"] = new ToolDetails
                        {
                            Type = "object",
                            Description = "Configuration options.",
                            Properties = new Dictionary<string, ToolDetails>
                            {
                                ["timeout"] = new ToolDetails
                                {
                                    Type = "integer",
                                    Description = "Timeout in seconds."
                                },
                                ["retry"] = new ToolDetails
                                {
                                    Type = "boolean",
                                    Description = "Whether to retry on failure."
                                }
                            },
                            Required = new List<string> { "timeout" }
                        }
                    },
                    Required = new List<string> { "options" }
                }
            };

            // Act
            var result = ToolDefinitionConverter.ConvertToOpenAiFormat(
                new List<ToolDefinition> { internalTool });

            // Assert
            var props = result[0].Function.Parameters.Properties;
            Assert.That(props, Contains.Key("options"));

            var options = props["options"] as Dictionary<string, object>;
            Assert.That(options, Is.Not.Null);
            Assert.That(options["type"], Is.EqualTo("object"));
            Assert.That(options["description"], Is.EqualTo("Configuration options."));

            var nestedProps = options["properties"] as Dictionary<string, object>;
            Assert.That(nestedProps, Is.Not.Null);
            Assert.That(nestedProps, Contains.Key("timeout"));
            Assert.That(nestedProps, Contains.Key("retry"));

            var timeout = nestedProps["timeout"] as Dictionary<string, object>;
            Assert.That(timeout["type"], Is.EqualTo("integer"));

            var retry = nestedProps["retry"] as Dictionary<string, object>;
            Assert.That(retry["type"], Is.EqualTo("boolean"));

            var nestedRequired = options["required"] as List<string>;
            Assert.That(nestedRequired, Is.Not.Null);
            Assert.That(nestedRequired, Is.EquivalentTo(new[] { "timeout" }));
        }

        [Test]
        public void ConvertToOpenAiFormat_StringParameterWithoutItems_OmitsItemsAndProperties()
        {
            // Arrange
            var internalTool = new ToolDefinition
            {
                Name = "read_file",
                Description = "Reads a file.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["file_path"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Path to file."
                        },
                        ["line_count"] = new ToolDetails
                        {
                            Type = "integer",
                            Description = "Number of lines."
                        }
                    },
                    Required = new List<string> { "file_path" }
                }
            };

            // Act
            var result = ToolDefinitionConverter.ConvertToOpenAiFormat(
                new List<ToolDefinition> { internalTool });

            // Assert
            var props = result[0].Function.Parameters.Properties;

            var filePath = props["file_path"] as Dictionary<string, object>;
            Assert.That(filePath["type"], Is.EqualTo("string"));
            Assert.That(filePath, Does.Not.ContainKey("items"));
            Assert.That(filePath, Does.Not.ContainKey("properties"));
            Assert.That(filePath, Does.Not.ContainKey("required"));

            var lineCount = props["line_count"] as Dictionary<string, object>;
            Assert.That(lineCount["type"], Is.EqualTo("integer"));
            Assert.That(lineCount, Does.Not.ContainKey("items"));
            Assert.That(lineCount, Does.Not.ContainKey("properties"));
            Assert.That(lineCount, Does.Not.ContainKey("required"));
        }

        [Test]
        public void ConvertToOpenAiFormat_NullItemsProperty_OmitsItemsFromOutput()
        {
            // Arrange
            var internalTool = new ToolDefinition
            {
                Name = "list",
                Description = "Lists items.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["tags"] = new ToolDetails
                        {
                            Type = "array",
                            Description = "A list of tags.",
                            Items = null // explicitly null
                        }
                    }
                }
            };

            // Act
            var result = ToolDefinitionConverter.ConvertToOpenAiFormat(
                new List<ToolDefinition> { internalTool });

            // Assert
            var tags = result[0].Function.Parameters.Properties["tags"] as Dictionary<string, object>;
            Assert.That(tags["type"], Is.EqualTo("array"));
            Assert.That(tags, Does.Not.ContainKey("items"));
        }

        [Test]
        public void ConvertToOpenAiFormat_EmptyPropertiesDictionary_OmitsPropertiesFromOutput()
        {
            // Arrange
            var internalTool = new ToolDefinition
            {
                Name = "empty_obj",
                Description = "Has an object with no fields.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["wrapped"] = new ToolDetails
                        {
                            Type = "object",
                            Description = "An empty nested object.",
                            Properties = new Dictionary<string, ToolDetails>() // empty
                        }
                    }
                }
            };

            // Act
            var result = ToolDefinitionConverter.ConvertToOpenAiFormat(
                new List<ToolDefinition> { internalTool });

            // Assert
            var wrapped = result[0].Function.Parameters.Properties["wrapped"] as Dictionary<string, object>;
            Assert.That(wrapped["type"], Is.EqualTo("object"));
            Assert.That(wrapped, Does.Not.ContainKey("properties"));
        }

        [Test]
        public void ConvertToOpenAiFormat_EmptyRequiredList_OmitsRequiredFromOutput()
        {
            // Arrange
            var internalTool = new ToolDefinition
            {
                Name = "optional_obj",
                Description = "Has an object with no required fields.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["config"] = new ToolDetails
                        {
                            Type = "object",
                            Description = "All-optional config.",
                            Properties = new Dictionary<string, ToolDetails>
                            {
                                ["flag"] = new ToolDetails
                                {
                                    Type = "boolean",
                                    Description = "An optional flag."
                                }
                            },
                            Required = new List<string>() // empty
                        }
                    }
                }
            };

            // Act
            var result = ToolDefinitionConverter.ConvertToOpenAiFormat(
                new List<ToolDefinition> { internalTool });

            // Assert
            var config = result[0].Function.Parameters.Properties["config"] as Dictionary<string, object>;
            Assert.That(config["type"], Is.EqualTo("object"));
            Assert.That(config, Does.Not.ContainKey("required"));

            var nestedProps = config["properties"] as Dictionary<string, object>;
            Assert.That(nestedProps, Is.Not.Null);
            Assert.That(nestedProps, Contains.Key("flag"));
        }

        [Test]
        public void ConvertToOpenAiFormat_TopLevelParametersRequired_PreservedInOutput()
        {
            // Arrange
            var internalTool = new ToolDefinition
            {
                Name = "delete",
                Description = "Deletes a file.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["file_path"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Path to the file."
                        }
                    },
                    Required = new List<string> { "file_path" }
                }
            };

            // Act
            var result = ToolDefinitionConverter.ConvertToOpenAiFormat(
                new List<ToolDefinition> { internalTool });

            // Assert
            var parameters = result[0].Function.Parameters;
            Assert.That(parameters.Required, Is.Not.Null);
            Assert.That(parameters.Required, Is.EquivalentTo(new[] { "file_path" }));
        }

        [Test]
        public void ConvertToOpenAiFormat_DeeplyNestedSchema_ProducesCorrectOutput()
        {
            // Arrange
            var internalTool = new ToolDefinition
            {
                Name = "complex",
                Description = "Tool with deep nesting.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["matrix"] = new ToolDetails
                        {
                            Type = "array",
                            Description = "A matrix of options.",
                            Items = new ToolDetails
                            {
                                Type = "array",
                                Description = "A row of options.",
                                Items = new ToolDetails
                                {
                                    Type = "object",
                                    Description = "A single cell.",
                                    Properties = new Dictionary<string, ToolDetails>
                                    {
                                        ["value"] = new ToolDetails
                                        {
                                            Type = "string",
                                            Description = "The cell value."
                                        }
                                    },
                                    Required = new List<string> { "value" }
                                }
                            }
                        }
                    },
                    Required = new List<string> { "matrix" }
                }
            };

            // Act
            var result = ToolDefinitionConverter.ConvertToOpenAiFormat(
                new List<ToolDefinition> { internalTool });

            // Assert
            var matrix = result[0].Function.Parameters.Properties["matrix"] as Dictionary<string, object>;
            Assert.That(matrix["type"], Is.EqualTo("array"));

            var outerItems = matrix["items"] as Dictionary<string, object>;
            Assert.That(outerItems["type"], Is.EqualTo("array"));

            var innerItems = outerItems["items"] as Dictionary<string, object>;
            Assert.That(innerItems["type"], Is.EqualTo("object"));

            var cellProps = innerItems["properties"] as Dictionary<string, object>;
            Assert.That(cellProps, Contains.Key("value"));

            var valueField = cellProps["value"] as Dictionary<string, object>;
            Assert.That(valueField["type"], Is.EqualTo("string"));

            var cellRequired = innerItems["required"] as List<string>;
            Assert.That(cellRequired, Is.Not.Null);
            Assert.That(cellRequired, Contains.Item("value"));
        }
    }
}
