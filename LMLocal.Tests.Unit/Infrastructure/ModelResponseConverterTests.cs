using System.Collections.Generic;
using LMLocal.Infrastructure.LlmApi.Converter;
using LMLocal.Infrastructure.LlmApi.Responses;
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

        [Test]
        public void ConvertOpenAiResponseToUnified_ParsesExtendedFields()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo
                    {
                        Id = "deepseek/deepseek-v4-flash-vision-exp",
                        Name = "DeepSeek: DeepSeek V4 Flash Vision Exp",
                        ContextLength = 1048576,
                        Pricing = new OpenAiPricing
                        {
                            Prompt = "0.00000022",
                            Completion = "0.00000066",
                            InputCacheRead = "0.000000007",
                            InputCacheWrite = "0.00000002"
                        },
                        Architecture = new OpenAiArchitecture
                        {
                            Modality = "text+image->text",
                            InputModalities = new List<string> { "text", "image" },
                            OutputModalities = new List<string> { "text" }
                        },
                        SupportedParameters = new List<string> { "temperature", "tools", "tool_choice", "top_p" }
                    }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));

            var m = result.Models[0];
            Assert.That(m.Id, Is.EqualTo("deepseek/deepseek-v4-flash-vision-exp"));
            Assert.That(m.Name, Is.EqualTo("DeepSeek: DeepSeek V4 Flash Vision Exp"));
            Assert.That(m.MaxTokens, Is.EqualTo(1048576));
            Assert.That(m.SupportsMaxTokens, Is.True);
            Assert.That(m.SupportsVision, Is.True);
            Assert.That(m.SupportsToolUse, Is.True);
            Assert.That(m.InputPricePerMillion, Is.EqualTo(0.22m));
            Assert.That(m.OutputPricePerMillion, Is.EqualTo(0.66m));
            Assert.That(m.CacheReadPricePerMillion, Is.EqualTo(0.007m));
            Assert.That(m.CacheWritePricePerMillion, Is.EqualTo(0.02m));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_TextOnlyModel()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo
                    {
                        Id = "text-only-model",
                        Architecture = new OpenAiArchitecture
                        {
                            Modality = "text->text",
                            InputModalities = new List<string> { "text" },
                            OutputModalities = new List<string> { "text" }
                        },
                        SupportedParameters = new List<string> { "temperature", "max_tokens" }
                    }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].SupportsVision, Is.False);
            Assert.That(result.Models[0].SupportsToolUse, Is.False);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_ImageGenerationModel_SupportsVisionFalse()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo
                    {
                        Id = "image-gen-model",
                        Architecture = new OpenAiArchitecture
                        {
                            Modality = "text+image->text+image",
                            InputModalities = new List<string> { "text", "image" },
                            OutputModalities = new List<string> { "text", "image" }
                        }
                    }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].SupportsVision, Is.False);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_NoArchitecture_SupportsVisionAndToolUseNull()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo { Id = "plain-openai" }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].SupportsVision, Is.Null);
            Assert.That(result.Models[0].SupportsToolUse, Is.Null);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_NoPricing_ReturnsNullPrices()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo { Id = "no-pricing", Pricing = new OpenAiPricing() }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].InputPricePerMillion, Is.Null);
            Assert.That(result.Models[0].OutputPricePerMillion, Is.Null);
            Assert.That(result.Models[0].CacheReadPricePerMillion, Is.Null);
            Assert.That(result.Models[0].CacheWritePricePerMillion, Is.Null);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_InvalidPriceString_ReturnsNull()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo
                    {
                        Id = "bad-price",
                        Pricing = new OpenAiPricing { Prompt = "not-a-number" }
                    }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].InputPricePerMillion, Is.Null);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_GeminiDisplayName_UsedAsName()
        {
            // Gemini OpenAI-compatible endpoint returns "displayName" (camelCase).
            const string json = @"{""object"":""list"",""data"":[{""id"":""gemini-3.1-pro"",""object"":""model"",""created"":0,""owned_by"":""google"",""displayName"":""Gemini 3.1 Pro""}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            Assert.That(result.Models[0].Id, Is.EqualTo("gemini-3.1-pro"));
            Assert.That(result.Models[0].Name, Is.EqualTo("Gemini 3.1 Pro"));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_GeminiDisplayNameSnakeCase_UsedAsName()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo { Id = "gemini-3-flash", DisplayNameSnakeCase = "Gemini 3 Flash" }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].Name, Is.EqualTo("Gemini 3 Flash"));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_OpenAiName_PrecedesGeminiDisplayName()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo
                    {
                        Id = "both-names",
                        Name = "OpenRouter Name",
                        DisplayName = "Gemini Name"
                    }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].Name, Is.EqualTo("OpenRouter Name"));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_GeminiDisplayName_CaseInsensitiveJsonKey()
        {
            // Newtonsoft matches JSON keys case-insensitively: "displayname" still maps to "displayName".
            const string json = @"{""object"":""list"",""data"":[{""id"":""gemini-2.0-flash"",""displayname"":""Gemini 2.0 Flash""}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result.Models[0].Name, Is.EqualTo("Gemini 2.0 Flash"));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_ContextWindow_UsedWhenContextLengthAbsent()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo { Id = "ctx-window-model", ContextWindow = 32768 }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].MaxTokens, Is.EqualTo(32768));
            Assert.That(result.Models[0].SupportsMaxTokens, Is.True);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_MaxContextLength_UsedWhenContextLengthAbsent()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo { Id = "max-ctx-model", MaxContextLength = 65536 }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].MaxTokens, Is.EqualTo(65536));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_MaxModelLen_UsedWhenOtherFieldsAbsent()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo { Id = "vllm-model", MaxModelLen = 131072 }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].MaxTokens, Is.EqualTo(131072));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_ContextLength_PrecedesAliases()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo
                    {
                        Id = "all-context-fields",
                        ContextLength = 128000,
                        ContextWindow = 1000,
                        MaxContextLength = 2000,
                        MaxModelLen = 3000
                    }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            Assert.That(result.Models[0].MaxTokens, Is.EqualTo(128000));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_ZeroContextAliases_FallThroughToKnownContext()
        {
            var resp = new ListModelsResponse
            {
                Object = "list",
                Data = new List<OpenAiModelInfo>
                {
                    new OpenAiModelInfo
                    {
                        Id = "deepseek-v4",
                        ContextWindow = 0,
                        MaxContextLength = -1,
                        MaxModelLen = 0
                    }
                }
            };

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(JsonConvert.SerializeObject(resp));

            // None of the aliases are positive -> falls back to the known context for deepseek-v4.
            Assert.That(result.Models[0].MaxTokens, Is.EqualTo(1_048_576));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_TieredPricingArray_DoesNotFailAndParsesPrices()
        {
            // Real response from an OpenAI-compatible (ProviderType="openai") provider whose per-model
            // "pricing" is an ARRAY of tiers with flat numeric prices. Previously this threw and failed the WHOLE list.
            const string json = @"{""object"":""list"",""data"":[{""api"":""chat"",""id"":""azure/decision-1"",""object"":""model"",""created"":1791504000,""updated"":1791708608,""owned_by"":""system"",""input_price"":4.2e-8,""cached_price"":0,""output_price"":0,""pricing"":[{""prompt_tokens_threshold"":0,""input_price"":4.2e-8,""cached_price"":0,""output_price"":0}],""max_output_tokens"":1024,""context_window"":32768,""supports_caching"":false,""supports_vision"":false,""supports_computer_use"":false,""supports_reasoning"":false,""supports_image_generation"":false,""supports_tool_calling"":false,""supports_role_developer"":false,""supports_web_search"":false,""supports_output_json_object"":false,""supports_output_json_schema"":false,""supports_system_mid_conversation"":true,""description"":""Microsoft-Decision-1 is a small decision model post trained from Qwen3.5-9B."",""data_retention"":false,""data_retention_days"":0,""data_used_for_training"":false,""privacy_comments"":""N/A"",""geolocation"":""us"",""open_weights"":false,""model_lab"":""microsoft"",""model_canonical_name"":""microsoft-decision-1""}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            // The pricing array must NOT fail the list anymore.
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));

            var m = result.Models[0];
            Assert.That(m.Id, Is.EqualTo("azure/decision-1"));
            // context_window already maps today (no change needed).
            Assert.That(m.MaxTokens, Is.EqualTo(32768));
            Assert.That(m.SupportsMaxTokens, Is.True);
            // Prices parsed from the tier: 4.2e-8 per token -> 0.042 per 1M.
            Assert.That(m.InputPricePerMillion, Is.EqualTo(0.042m));
            Assert.That(m.OutputPricePerMillion, Is.EqualTo(0m));
            Assert.That(m.CacheReadPricePerMillion, Is.EqualTo(0m));
            // The provider reports no input_cache_write, so cache-write stays unknown (null).
            Assert.That(m.CacheWritePricePerMillion, Is.Null);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_ObjectPricing_OpenRouterShape_StillMapsExactly()
        {
            // Regression: the OpenRouter object-pricing shape must keep working unchanged.
            const string json = @"{""object"":""list"",""data"":[{""id"":""openrouter/model"",""pricing"":{""prompt"":""0.00000022"",""completion"":""0.00000066"",""input_cache_read"":""0.000000007"",""input_cache_write"":""0.00000002""}}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var m = result.Models[0];
            Assert.That(m.InputPricePerMillion, Is.EqualTo(0.22m));
            Assert.That(m.OutputPricePerMillion, Is.EqualTo(0.66m));
            Assert.That(m.CacheReadPricePerMillion, Is.EqualTo(0.007m));
            Assert.That(m.CacheWritePricePerMillion, Is.EqualTo(0.02m));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_CanonicalPriceField_PrecedesFlatField()
        {
            // Precedence: the canonical "prompt"/"completion" fields win over the flat LiteLLM fields.
            const string json = @"{""object"":""list"",""data"":[{""id"":""precedence"",""pricing"":{""prompt"":""0.000001"",""input_price"":""0.000009""}}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result.Models[0].InputPricePerMillion, Is.EqualTo(1m));
        }

        [TestCase(@"{""object"":""list"",""data"":[{""id"":""missing-pricing""}]}")]
        [TestCase(@"{""object"":""list"",""data"":[{""id"":""empty-pricing"",""pricing"":[]}]}")]
        [TestCase(@"{""object"":""list"",""data"":[{""id"":""null-pricing"",""pricing"":null}]}")]
        [TestCase(@"{""object"":""list"",""data"":[{""id"":""weird-pricing"",""pricing"":""unexpected""}]}")]
        public void ConvertOpenAiResponseToUnified_MissingOrUnusualPricing_DoesNotFailAndLeavesPricesNull(string json)
        {
            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var m = result.Models[0];
            Assert.That(m.InputPricePerMillion, Is.Null);
            Assert.That(m.OutputPricePerMillion, Is.Null);
            Assert.That(m.CacheReadPricePerMillion, Is.Null);
            Assert.That(m.CacheWritePricePerMillion, Is.Null);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_NovitaObjectValuedPricing_MapsPricesPerMillion()
        {
            // Real response from a Novita-style OpenAI-compatible provider: each "pricing" entry is an
            // OBJECT reporting USD per 1M tokens. Previously Newtonsoft threw on "pricing.prompt" and the
            // WHOLE list broke. Now the nested prices must map to the provider's own values ($0.15 / $0.5 / $0.03).
            const string json = @"{""object"":""list"",""data"":[{""id"":""zai-org/glm-5.3-flash"",""object"":""model"",""owned_by"":""novita"",""context_size"":1048576,""display_name"":""GLM-5.3-Flash"",""input_token_price_per_m"":1500,""output_token_price_per_m"":5000,""max_output_tokens"":65536,""pricing"":{""prompt"":{""origin_price_per_m"":1500,""price_per_m"":1500,""origin_price_per_m_decimal"":""0.15"",""price_per_m_decimal"":""0.15""},""completion"":{""origin_price_per_m"":5000,""price_per_m"":5000,""origin_price_per_m_decimal"":""0.5"",""price_per_m_decimal"":""0.5""},""input_cache_read"":{""origin_price_per_m"":300,""price_per_m"":300,""origin_price_per_m_decimal"":""0.03"",""price_per_m_decimal"":""0.03""}}}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var m = result.Models[0];
            Assert.That(m.Id, Is.EqualTo("zai-org/glm-5.3-flash"));
            Assert.That(m.InputPricePerMillion, Is.EqualTo(0.15m));
            Assert.That(m.OutputPricePerMillion, Is.EqualTo(0.5m));
            Assert.That(m.CacheReadPricePerMillion, Is.EqualTo(0.03m));
            // The provider reports no cache-write price, so it stays unknown.
            Assert.That(m.CacheWritePricePerMillion, Is.Null);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_ObjectValuedPricing_DecimalField_MapsDirectly()
        {
            // "price_per_m_decimal" is already USD per 1M tokens.
            const string json = @"{""object"":""list"",""data"":[{""id"":""novita"",""pricing"":{""prompt"":{""price_per_m_decimal"":""0.15""},""completion"":{""price_per_m_decimal"":""0.5""},""input_cache_read"":{""price_per_m_decimal"":""0.03""}}}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var m = result.Models[0];
            Assert.That(m.InputPricePerMillion, Is.EqualTo(0.15m));
            Assert.That(m.OutputPricePerMillion, Is.EqualTo(0.5m));
            Assert.That(m.CacheReadPricePerMillion, Is.EqualTo(0.03m));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_ObjectValuedPricing_FixedPointField_DividedByTenThousand()
        {
            // "price_per_m" is USD per 1M tokens in 4-decimal fixed point (1500 -> 0.15).
            const string json = @"{""object"":""list"",""data"":[{""id"":""novita-fixed"",""pricing"":{""prompt"":{""price_per_m"":1500},""completion"":{""price_per_m"":5000}}}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var m = result.Models[0];
            Assert.That(m.InputPricePerMillion, Is.EqualTo(0.15m));
            Assert.That(m.OutputPricePerMillion, Is.EqualTo(0.5m));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_ObjectValuedPricing_WithoutPriceKey_LeavesPricesNull()
        {
            // A nested object that carries no recognizable price (only "origin_price_per_m") yields nothing.
            const string json = @"{""object"":""list"",""data"":[{""id"":""novita-bare"",""pricing"":{""prompt"":{""origin_price_per_m"":1500}}}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var m = result.Models[0];
            Assert.That(m.InputPricePerMillion, Is.Null);
            Assert.That(m.OutputPricePerMillion, Is.Null);
            Assert.That(m.CacheReadPricePerMillion, Is.Null);
            Assert.That(m.CacheWritePricePerMillion, Is.Null);
        }

        [TestCase(@"{""object"":""list"",""data"":[{""id"":""bool-price"",""pricing"":{""prompt"":{""price_per_m"":true}}}]}")]
        [TestCase(@"{""object"":""list"",""data"":[{""id"":""text-decimal"",""pricing"":{""prompt"":{""price_per_m_decimal"":""not-a-number""}}}]}")]
        [TestCase(@"{""object"":""list"",""data"":[{""id"":""string-fixed"",""pricing"":{""prompt"":{""price_per_m"":""1500""}}}]}")]
        [TestCase(@"{""object"":""list"",""data"":[{""id"":""huge"",""pricing"":{""prompt"":{""price_per_m"":1e308}}}]}")]
        public void ConvertOpenAiResponseToUnified_ObjectValuedPricing_NonNumericValue_DoesNotFailAndLeavesPricesNull(string json)
        {
            // A non-numeric (or out-of-range) value for a known price key must not throw and must not fail the list.
            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var m = result.Models[0];
            Assert.That(m.InputPricePerMillion, Is.Null);
            Assert.That(m.OutputPricePerMillion, Is.Null);
            Assert.That(m.CacheReadPricePerMillion, Is.Null);
            Assert.That(m.CacheWritePricePerMillion, Is.Null);
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_NumericPricingValue_IsAccepted()
        {
            // Some providers report prices as JSON numbers rather than strings.
            const string json = @"{""object"":""list"",""data"":[{""id"":""numeric"",""pricing"":{""prompt"":0.00000022,""completion"":0.00000066}}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            var m = result.Models[0];
            Assert.That(m.InputPricePerMillion, Is.EqualTo(0.22m));
            Assert.That(m.OutputPricePerMillion, Is.EqualTo(0.66m));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_ObjectValuedPricing_DoesNotBreakFollowingModels()
        {
            // Reader-balance guard: an object-valued "pricing" on the FIRST model must not corrupt the
            // reader for the SECOND model (previously it cascaded and the whole list was lost).
            const string json = @"{""object"":""list"",""data"":[{""id"":""novita/first"",""pricing"":{""prompt"":{""price_per_m_decimal"":""0.15""},""completion"":{""price_per_m_decimal"":""0.5""}}},{""id"":""openrouter/second"",""pricing"":{""prompt"":""0.00000022"",""completion"":""0.00000066""}}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(2));
            Assert.That(result.Models[0].InputPricePerMillion, Is.EqualTo(0.15m));
            Assert.That(result.Models[1].InputPricePerMillion, Is.EqualTo(0.22m));
            Assert.That(result.Models[1].OutputPricePerMillion, Is.EqualTo(0.66m));
        }

        [Test]
        public void ConvertOpenAiResponseToUnified_UnknownPricingKeyWithObjectValue_DoesNotBreakFollowingFields()
        {
            // An unknown key whose value is an object must be consumed balanced, so later known keys in the
            // SAME pricing object, and any following model, still parse.
            const string json = @"{""object"":""list"",""data"":[{""id"":""mixed"",""pricing"":{""weird"":{""nested"":{""deep"":1}},""prompt"":""0.000001""}},{""id"":""after"",""pricing"":{""prompt"":""0.000002""}}]}";

            var result = ModelResponseConverter.ConvertOpenAiResponseToUnified(json);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models.Count, Is.EqualTo(2));
            Assert.That(result.Models[0].InputPricePerMillion, Is.EqualTo(1m));
            Assert.That(result.Models[1].InputPricePerMillion, Is.EqualTo(2m));
        }

    }
}
