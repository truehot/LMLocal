using System.Collections.Generic;
using LMLocal.Infrastructure.LlmApi.Converter;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Models;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ModelResponseConverterTests
    {
        [Test]
        public void ConvertLmStudioResponseToUnified_SuccessfulConversion()
        {
            var resp = new LmStudioModelsResponse
            {
                Models = new List<LmStudioModelInfo>
                {
                    new LmStudioModelInfo
                    {
                        Type = "llm",
                        Key = "model_key",
                        DisplayName = "Model Name",
                        MaxContextLength = 2048,
                        Vision = true,
                        Capabilities = new ModelCapabilities { TrainedForToolUse = true },
                        LoadedInstances = new List<LoadedInstance>
                        {
                            new LoadedInstance { Id = "inst1", Config = new InstanceConfig { ContextLength = 4096 } }
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(resp);

            var result = ModelResponseConverter.ConvertLmStudioResponseToUnified(json);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));

            var m = result.Models[0];
            Assert.That(m.Id, Is.EqualTo("inst1"));
            Assert.That(m.Name, Is.EqualTo("Model Name"));
            Assert.That(m.IsLoaded, Is.True);
            Assert.That(m.SupportsToolUse, Is.True);
            Assert.That(m.SupportsVision, Is.True);
            Assert.That(m.MaxTokens, Is.EqualTo(4096));
            Assert.That(result.SupportsIsLoaded, Is.True);
        }

        [Test]
        public void ConvertLmStudioResponseToUnified_InvalidJson_ReturnsError()
        {
            var result = ModelResponseConverter.ConvertLmStudioResponseToUnified("{ invalid json");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Does.Contain("Failed to parse LM Studio response"));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_SuccessfulConversion()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo { Id = "m1" }
                }
            };

            var json = JsonConvert.SerializeObject(resp);
            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            Assert.That(result.Models[0].Id, Is.EqualTo("m1"));
            Assert.That(result.SupportsIsLoaded, Is.False);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_InvalidJson_ReturnsError()
        {
            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified("not json");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Does.Contain("Failed to parse OpenAI-compatible response"));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_KnownModel_GetsMaxTokens()
        {
            // Arrange
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo { Id = "deepseek-v4" }
                }
            };

            var json = JsonConvert.SerializeObject(resp);

            // Act
            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var model = result.Models[0];
            Assert.That(model.Id, Is.EqualTo("deepseek-v4"));
            Assert.That(model.MaxTokens, Is.EqualTo(1_048_576));
            Assert.That(model.SupportsMaxTokens, Is.True);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_UnknownModel_NullMaxTokens()
        {
            // Arrange
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo { Id = "some-random-model-v1" }
                }
            };

            var json = JsonConvert.SerializeObject(resp);

            // Act
            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var model = result.Models[0];
            Assert.That(model.MaxTokens, Is.Null);
            Assert.That(model.SupportsMaxTokens, Is.False);
        }

        [Test]
        public void ConvertOllamaResponseToUnified_VisionModel_WhenFamiliesContainClip()
        {
            var resp = new OllamaPsResponse
            {
                Models = new List<OllamaRunningModel>
                {
                    new OllamaRunningModel
                    {
                        Name = "llava:latest",
                        Model = "llava:latest",
                        Details = new OllamaModelDetails { Families = new List<string> { "llama", "clip" } }
                    }
                }
            };

            var result = ModelResponseConverter.ConvertOllamaResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].SupportsVision, Is.True);
        }

        [Test]
        public void ConvertOllamaResponseToUnified_NoClipFamily_SupportsVisionFalse()
        {
            var resp = new OllamaPsResponse
            {
                Models = new List<OllamaRunningModel>
                {
                    new OllamaRunningModel
                    {
                        Name = "llama3:latest",
                        Model = "llama3:latest",
                        Details = new OllamaModelDetails { Families = new List<string> { "llama" } }
                    }
                }
            };

            var result = ModelResponseConverter.ConvertOllamaResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].SupportsVision, Is.False);
        }

        [Test]
        public void ConvertOllamaResponseToUnified_NoDetails_SupportsVisionNull()
        {
            var resp = new OllamaPsResponse
            {
                Models = new List<OllamaRunningModel>
                {
                    new OllamaRunningModel { Name = "llama3:latest", Model = "llama3:latest" }
                }
            };

            var result = ModelResponseConverter.ConvertOllamaResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].SupportsVision, Is.Null);
        }

        [Test]
        public void ConvertJanResponseToUnified_VisionModel_WhenMmprojSet()
        {
            var resp = new JanModelsResponse
            {
                Data = new List<JanModel>
                {
                    new JanModel { Id = "llava-v1.5-7b", Name = "Llava", Mmproj = "mmproj-model-f16.gguf" }
                }
            };

            var result = ModelResponseConverter.ConvertJanResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].SupportsVision, Is.True);
        }

        [Test]
        public void ConvertJanResponseToUnified_NoMmproj_SupportsVisionFalse()
        {
            var resp = new JanModelsResponse
            {
                Data = new List<JanModel>
                {
                    new JanModel { Id = "qwen2.5-7b", Name = "Qwen" }
                }
            };

            var result = ModelResponseConverter.ConvertJanResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].SupportsVision, Is.False);
        }

    }
}
