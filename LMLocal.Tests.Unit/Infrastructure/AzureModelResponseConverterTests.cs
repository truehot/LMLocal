using LMLocal.Infrastructure.LlmApi.Converter;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class AzureModelResponseConverterTests
    {
        [Test]
        public void ConvertAzureResponseToUnified_ValidAzureResponse_ReturnsUnifiedModels()
        {
            // Arrange
            var azureJson = @"[
                {
                    ""id"": ""azureml://registries/azureml-cohere/models/Cohere-embed-v3-english/versions/3"",
                    ""name"": ""Cohere-embed-v3-english"",
                    ""friendly_name"": ""Cohere Embed v3 English"",
                    ""model_version"": 3,
                    ""task"": ""embeddings"",
                    ""description"": ""Embedding model"",
                    ""tags"": [""RAG""]
                },
                {
                    ""id"": ""azureml://registries/azureml-models/models/gpt-4/versions/1"",
                    ""name"": ""gpt-4"",
                    ""friendly_name"": ""GPT-4"",
                    ""task"": ""chat-completion"",
                    ""description"": ""Chat completion model""
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertAzureResponseToUnified(azureJson);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Models, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1), "Should only include chat-completion models");

            var model = result.Models[0];
            Assert.That(model.Id, Is.EqualTo("gpt-4"));
            Assert.That(model.Name, Is.EqualTo("GPT-4"));
            Assert.That(model.IsLoaded, Is.False);
            Assert.That(result.SupportsIsLoaded, Is.False);
        }

        [Test]
        public void ConvertAzureResponseToUnified_OnlyEmbeddingModels_ReturnsError()
        {
            // Arrange
            var azureJson = @"[
                {
                    ""id"": ""azureml://registries/azureml-cohere/models/Cohere-embed-v3-english/versions/3"",
                    ""name"": ""Cohere-embed-v3-english"",
                    ""friendly_name"": ""Cohere Embed v3 English"",
                    ""task"": ""embeddings"",
                    ""description"": ""Embedding model""
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertAzureResponseToUnified(azureJson);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(0));
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error, Does.Contain("No chat-completion models"));
        }

        [Test]
        public void ConvertAzureResponseToUnified_EmptyArray_ReturnsError()
        {
            // Arrange
            var azureJson = "[]";

            // Act
            var result = ModelResponseConverter.ConvertAzureResponseToUnified(azureJson);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(0));
            Assert.That(result.Error, Is.Not.Null);
        }

        [Test]
        public void ConvertAzureResponseToUnified_InvalidJson_ReturnsError()
        {
            // Arrange
            var invalidJson = "{ invalid json";

            // Act
            var result = ModelResponseConverter.ConvertAzureResponseToUnified(invalidJson);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error, Does.Contain("Failed to parse Azure response"));
        }

        [Test]
        public void ConvertAzureResponseToUnified_NullResponse_ReturnsError()
        {
            // Arrange
            var nullJson = "null";

            // Act
            var result = ModelResponseConverter.ConvertAzureResponseToUnified(nullJson);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Not.Null);
        }

        [Test]
        public void ConvertAzureResponseToUnified_UseFriendlyName_WhenAvailable()
        {
            // Arrange
            var azureJson = @"[
                {
                    ""id"": ""model-id"",
                    ""name"": ""model-name"",
                    ""friendly_name"": ""Model Display Name"",
                    ""task"": ""chat-completion""
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertAzureResponseToUnified(azureJson);

            // Assert
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var model = result.Models[0];
            Assert.That(model.Name, Is.EqualTo("Model Display Name"), "Should use friendly_name");
        }

        [Test]
        public void ConvertAzureResponseToUnified_UseName_WhenFriendlyNameEmpty()
        {
            // Arrange
            var azureJson = @"[
                {
                    ""id"": ""model-id"",
                    ""name"": ""model-name"",
                    ""friendly_name"": """",
                    ""task"": ""chat-completion""
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertAzureResponseToUnified(azureJson);

            // Assert
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var model = result.Models[0];
            Assert.That(model.Name, Is.EqualTo("model-name"), "Should fallback to name");
        }

        [Test]
        public void ConvertAzureResponseToUnified_MultipleModels_FiltersChatCompletion()
        {
            // Arrange
            var azureJson = @"[
                {
                    ""name"": ""embeddings-model"",
                    ""task"": ""embeddings""
                },
                {
                    ""name"": ""chat-model-1"",
                    ""task"": ""chat-completion""
                },
                {
                    ""name"": ""text-generation"",
                    ""task"": ""text-generation""
                },
                {
                    ""name"": ""chat-model-2"",
                    ""task"": ""chat-completion""
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertAzureResponseToUnified(azureJson);

            // Assert
            Assert.That(result.Models.Count, Is.EqualTo(2));
            Assert.That(result.Models[0].Id, Is.EqualTo("chat-model-1"));
            Assert.That(result.Models[1].Id, Is.EqualTo("chat-model-2"));
        }

        [Test]
        public void ConvertAzureResponseToUnified_KnownModel_GetsMaxTokens()
        {
            // Arrange
            var azureJson = @"[
                {
                    ""name"": ""deepseek-v4"",
                    ""friendly_name"": ""DeepSeek V4"",
                    ""task"": ""chat-completion""
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertAzureResponseToUnified(azureJson);

            // Assert
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var model = result.Models[0];
            Assert.That(model.Id, Is.EqualTo("deepseek-v4"));
            Assert.That(model.MaxTokens, Is.EqualTo(1_048_576));
            Assert.That(model.SupportsMaxTokens, Is.True);
        }

        [Test]
        public void ConvertAzureResponseToUnified_UnknownModel_NullMaxTokens()
        {
            // Arrange
            var azureJson = @"[
                {
                    ""name"": ""my-custom-model"",
                    ""task"": ""chat-completion""
                }
            ]";

            // Act
            var result = ModelResponseConverter.ConvertAzureResponseToUnified(azureJson);

            // Assert
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var model = result.Models[0];
            Assert.That(model.MaxTokens, Is.Null);
            Assert.That(model.SupportsMaxTokens, Is.False);
        }

    }
}
