using LMLocal.Infrastructure.LlmApi.Converter;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class TogetherAiModelResponseConverterTests
    {
        [Test]
        public void ConvertTogetherAiResponseToUnified_ValidResponse_ReturnsUnifiedModels()
        {
            // Arrange
            var json = @"[
                {
                    ""id"": ""zai-org/GLM-5.2"",
                    ""object"": ""model"",
                    ""created"": 0,
                    ""display_name"": ""GLM 5.2"",
                    ""context_length"": 262144,
                    ""config"": {
                        ""chat_template"": ""{% for message in messages %}{{ message.role }} {{ message.content }} {{ tools }} {% endfor %}""
                    }
                },
                {
                    ""id"": ""mistralai/Mixtral-8x7B"",
                    ""object"": ""model"",
                    ""created"": 0,
                    ""display_name"": ""Mixtral 8x7B"",
                    ""context_length"": 32768,
                    ""config"": {
                        ""chat_template"": ""{{ messages }}{% if tool_calls %}...{% endif %}""
                    }
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertTogetherAiResponseToUnified(json);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(2));

            // Model 1
            var m1 = result.Models[0];
            Assert.That(m1.Id, Is.EqualTo("zai-org/GLM-5.2"));
            Assert.That(m1.Name, Is.EqualTo("GLM 5.2"));
            Assert.That(m1.MaxTokens, Is.EqualTo(262144));
            Assert.That(m1.SupportsMaxTokens, Is.True);
            Assert.That(m1.SupportsToolUse, Is.True);
            Assert.That(m1.IsLoaded, Is.False);

            // Model 2 — tool_calls detection
            var m2 = result.Models[1];
            Assert.That(m2.Id, Is.EqualTo("mistralai/Mixtral-8x7B"));
            Assert.That(m2.Name, Is.EqualTo("Mixtral 8x7B"));
            Assert.That(m2.MaxTokens, Is.EqualTo(32768));
            Assert.That(m2.SupportsMaxTokens, Is.True);
            Assert.That(m2.SupportsToolUse, Is.True);
            Assert.That(m2.IsLoaded, Is.False);
        }

        [Test]
        public void ConvertTogetherAiResponseToUnified_EmptyConfig_ReturnsNullToolSupport()
        {
            // Arrange — chat_template is null, config is present but empty
            var json = @"[
                {
                    ""id"": ""model-1"",
                    ""object"": ""model"",
                    ""created"": 0,
                    ""display_name"": ""Model 1"",
                    ""context_length"": 4096,
                    ""config"": {}
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertTogetherAiResponseToUnified(json);

            // Assert
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var model = result.Models[0];
            Assert.That(model.SupportsToolUse, Is.Null, "Should be null when chat_template is missing");
            Assert.That(model.MaxTokens, Is.EqualTo(4096));
        }

        [Test]
        public void ConvertTogetherAiResponseToUnified_NoChatTemplate_ReturnsNullToolSupport()
        {
            // Arrange — config is null entirely
            var json = @"[
                {
                    ""id"": ""model-1"",
                    ""object"": ""model"",
                    ""created"": 0,
                    ""display_name"": ""Model 1"",
                    ""context_length"": 8192,
                    ""config"": null
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertTogetherAiResponseToUnified(json);

            // Assert
            Assert.That(result.Models.Count, Is.EqualTo(1));
            Assert.That(result.Models[0].SupportsToolUse, Is.Null, "Should be null when config is null");
        }

        [Test]
        public void ConvertTogetherAiResponseToUnified_ChatTemplateWithoutTools_ReturnsFalse()
        {
            // Arrange — chat_template exists but contains no tool keywords
            var json = @"[
                {
                    ""id"": ""model-1"",
                    ""object"": ""model"",
                    ""display_name"": ""Model 1"",
                    ""context_length"": 4096,
                    ""config"": {
                        ""chat_template"": ""{{ messages }}{% if system %}...{% endif %}""
                    }
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertTogetherAiResponseToUnified(json);

            // Assert
            Assert.That(result.Models.Count, Is.EqualTo(1));
            Assert.That(result.Models[0].SupportsToolUse, Is.False, "Should be false when template lacks tool tokens");
        }

        [Test]
        public void ConvertTogetherAiResponseToUnified_ZeroContextLength_ReturnsNullMaxTokens()
        {
            // Arrange — context_length is 0
            var json = @"[
                {
                    ""id"": ""model-1"",
                    ""object"": ""model"",
                    ""display_name"": ""Model 1"",
                    ""context_length"": 0
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertTogetherAiResponseToUnified(json);

            // Assert
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var model = result.Models[0];
            Assert.That(model.MaxTokens, Is.Null);
            Assert.That(model.SupportsMaxTokens, Is.False);
        }

        [Test]
        public void ConvertTogetherAiResponseToUnified_EmptyArray_ReturnsError()
        {
            // Arrange
            var json = "[]";

            // Act
            var result = ModelResponseConverter.ConvertTogetherAiResponseToUnified(json);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(0));
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error, Does.Contain("No models returned from Together AI"));
        }

        [Test]
        public void ConvertTogetherAiResponseToUnified_InvalidJson_ReturnsError()
        {
            // Arrange
            var invalidJson = "{ invalid json";

            // Act
            var result = ModelResponseConverter.ConvertTogetherAiResponseToUnified(invalidJson);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error, Does.Contain("Failed to parse Together AI response"));
        }

        [Test]
        public void ConvertTogetherAiResponseToUnified_NullResponse_ReturnsError()
        {
            // Arrange
            var nullJson = "null";

            // Act
            var result = ModelResponseConverter.ConvertTogetherAiResponseToUnified(nullJson);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Not.Null);
        }

        [Test]
        public void ConvertTogetherAiResponseToUnified_EmptyString_ReturnsError()
        {
            // Arrange
            var emptyJson = "";

            // Act
            var result = ModelResponseConverter.ConvertTogetherAiResponseToUnified(emptyJson);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Not.Null);
        }
    }
}
