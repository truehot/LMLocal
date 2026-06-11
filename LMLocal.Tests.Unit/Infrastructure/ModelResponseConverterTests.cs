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
            Assert.That(m.MaxTokens, Is.EqualTo(4096));
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
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_InvalidJson_ReturnsError()
        {
            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified("not json");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Does.Contain("Failed to parse OpenAI-compatible response"));
        }
    }
}
