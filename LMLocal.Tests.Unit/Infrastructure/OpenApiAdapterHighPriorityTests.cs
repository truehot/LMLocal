using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Core.Exceptions;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Converter;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.Security;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.Tooling;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class OpenApiAdapterHighPriorityTests
    {
        // Helper handler that writes content in several chunks to simulate a streaming/chunked response
        private class ChunkedContent : HttpContent
        {
            private readonly string[] _chunks;

            public ChunkedContent(params string[] chunks)
            {
                _chunks = chunks ?? Array.Empty<string>();
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                foreach (var c in _chunks)
                {
                    var bytes = Encoding.UTF8.GetBytes(c);
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }

                // do not close the stream here; caller owns it
            }

            protected override bool TryComputeLength(out long length)
            {
                length = -1;
                return false;
            }
        }

        private class FakeHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;
            public FakeHandler(HttpResponseMessage response) { _response = response; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_response);
            }
        }

        private static Mock<IToolQueueProvider> EmptyQueueProviderMock()
        {
            var mock = new Mock<IToolQueueProvider>();
            mock.Setup(p => p.GetMainQueue()).Returns(ToolQueue.Main(new System.Collections.Generic.List<ToolDefinition>()));
            return mock;
        }

        [Test]
        public async Task StreamingResponse_ParsesChunkedStream()
        {
            var sb = new StringBuilder();
            sb.AppendLine("data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}");
            sb.AppendLine("data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}");
            sb.AppendLine("data: [DONE]");

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ChunkedContent(sb.ToString())
            };

            var client = new HttpClient(new FakeHandler(response));
            var wrapper = new TestHttpClientWrapper(client);

            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings());
            var toolQueueProvider = EmptyQueueProviderMock().Object;

            var adapter = new OpenApiAdapter(wrapper, mockSettings.Object, new ApiRequestBuilder(mockSettings.Object, toolQueueProvider), new Mock<ITemporaryHttpClientFactory>().Object);

            var messageContext = new MessageContext(new ChatMessage[0]);
            var modelContext = new ModelContext("test-model");

            using (var streaming = await adapter.SendChatStreamingAsync(messageContext, modelContext, CancellationToken.None))
            {
                using (var reader = new StreamReader(streaming.Stream, Encoding.UTF8))
                {
                    var text = await reader.ReadToEndAsync().ConfigureAwait(false);
                    Assert.That(text, Does.Contain("hello"));
                    Assert.That(text, Does.Contain("world"));
                    Assert.That(text, Does.Contain("[DONE]").Or.Contain("DONE"));
                }
            }
        }

        [Test]
        public void StreamingResponse_ErrorDuringStream_ThrowsOrReports()
        {
            var json = "{ \"error\": { \"message\": \"Bad things\" } }";
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var client = new HttpClient(new FakeHandler(response));
            var wrapper = new TestHttpClientWrapper(client);

            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings());
            var toolQueueProvider = EmptyQueueProviderMock().Object;

            var adapter = new OpenApiAdapter(wrapper, mockSettings.Object, new ApiRequestBuilder(mockSettings.Object, toolQueueProvider), new Mock<ITemporaryHttpClientFactory>().Object);

            var messageContext = new MessageContext(new ChatMessage[0]);
            var modelContext = new ModelContext("test-model");

            var ex = Assert.ThrowsAsync<ApiException>(async () => await adapter.SendChatStreamingAsync(messageContext, modelContext, CancellationToken.None));
            Assert.That(ex.ErrorInfo.Message, Is.EqualTo("Bad things"));
        }

        [Test]
        public async Task BuildsCorrectEndpoint_ForProvider()
        {
            var captured = (HttpRequestMessage)null;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };

            var mockWrapper = new Mock<IHttpClientWrapper>();
            mockWrapper
                .Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<HttpCompletionOption>(), It.IsAny<CancellationToken>()))
                .Callback<HttpRequestMessage, HttpCompletionOption, CancellationToken>((req, opt, ct) => captured = req)
                .ReturnsAsync(response);

            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { LmStudioBaseUrl = "http://example.com:8080/" });
            var toolQueueProvider = EmptyQueueProviderMock().Object;

            var adapter = new OpenApiAdapter(mockWrapper.Object, mockSettings.Object, new ApiRequestBuilder(mockSettings.Object, toolQueueProvider), new Mock<ITemporaryHttpClientFactory>().Object);

            var messageContext = new MessageContext(new ChatMessage[0]);
            var modelContext = new ModelContext("mymodel");

            await adapter.SendChatAsync(messageContext, modelContext, CancellationToken.None);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.RequestUri.ToString(), Is.EqualTo("http://example.com:8080/v1/chat/completions"));
        }

        [Test]
        public async Task ListModelsRawAsync_WithCertificatePath_UsesTemporaryClientFromFactory()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"models\":[]}", Encoding.UTF8, "application/json")
            };
            var temporaryClient = new HttpClient(new FakeHandler(response));

            var factory = new Mock<ITemporaryHttpClientFactory>();
            factory.Setup(f => f.Create(It.IsAny<string>())).Returns(temporaryClient);

            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings());
            var toolQueueProvider = EmptyQueueProviderMock().Object;

            var adapter = new OpenApiAdapter(
                Mock.Of<IHttpClientWrapper>(),
                mockSettings.Object,
                new ApiRequestBuilder(mockSettings.Object, toolQueueProvider),
                factory.Object);

            var json = await adapter.ListModelsRawAsync(
                "/v1/models", "https://localhost:8443", "sk", CancellationToken.None, "C:\\certs\\server.cer");

            Assert.That(json, Does.Contain("models"));
            factory.Verify(f => f.Create("C:\\certs\\server.cer"), Times.Once);
        }

        [Test]
        public async Task ListModelsRawAsync_WithoutCertificatePath_DoesNotUseTemporaryFactory()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"models\":[]}", Encoding.UTF8, "application/json")
            };

            var mockWrapper = new Mock<IHttpClientWrapper>();
            mockWrapper
                .Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<HttpCompletionOption>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var factory = new Mock<ITemporaryHttpClientFactory>();

            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings());
            var toolQueueProvider = EmptyQueueProviderMock().Object;

            var adapter = new OpenApiAdapter(
                mockWrapper.Object,
                mockSettings.Object,
                new ApiRequestBuilder(mockSettings.Object, toolQueueProvider),
                factory.Object);

            var json = await adapter.ListModelsRawAsync("/v1/models", "https://api.openai.com", "sk", CancellationToken.None);

            Assert.That(json, Does.Contain("models"));
            mockWrapper.Verify(
                x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<HttpCompletionOption>(), It.IsAny<CancellationToken>()),
                Times.Once);
            factory.Verify(f => f.Create(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void ConvertJanModelsToUnified()
        {
            var jan = new JanModelsResponse
            {
                Data = new System.Collections.Generic.List<JanModel>
                {
                    new JanModel
                    {
                        Id = "jan-1",
                        Name = "Jan One",
                        Size = 12345,
                        Mmproj = "mmproj-model-f16.gguf",
                        Settings = new JanModelSettings { ContextLength = 4096 },
                        Parameters = new JanModelParameters { MaxTokens = 2048 }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(jan);
            var result = ModelResponseConverter.ConvertJanResponseToUnified(json);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Models, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            Assert.That(result.SupportsIsLoaded, Is.True);
            var m = result.Models[0];
            Assert.That(m.Id, Is.EqualTo("jan-1"));
            Assert.That(m.Name, Is.EqualTo("Jan One"));
            Assert.That(m.MaxTokens, Is.EqualTo(4096));
            Assert.That(m.IsLoaded, Is.True);
            Assert.That(m.SizeInBytes, Is.EqualTo(12345));
            Assert.That(m.SupportsVision, Is.True);
        }

        [Test]
        public void ConvertOllamaPsToUnified()
        {
            var ollama = new OllamaPsResponse
            {
                Models = new System.Collections.Generic.List<OllamaRunningModel>
                {
                    new OllamaRunningModel
                    {
                        Name = "ollama-1",
                        Model = "ollama-1",
                        ContextLength = 8192,
                        Size = 9999,
                        Details = new OllamaModelDetails
                        {
                            Families = new System.Collections.Generic.List<string> { "llama", "clip" }
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(ollama);
            var result = ModelResponseConverter.ConvertOllamaResponseToUnified(json);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Models, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(1));
            Assert.That(result.SupportsIsLoaded, Is.True);
            var m = result.Models[0];
            Assert.That(m.Id, Is.EqualTo("ollama-1"));
            Assert.That(m.MaxTokens, Is.EqualTo(8192));
            Assert.That(m.IsLoaded, Is.True);
            Assert.That(m.SizeInBytes, Is.EqualTo(9999));
            Assert.That(m.SupportsVision, Is.True);
        }

        [Test]
        public async Task FunctionCallHandling_WhenToolInvocationReturned_IncludesToolsInRequest()
        {
            string capturedBody = null;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };

            var mockWrapper = new Mock<IHttpClientWrapper>();
            mockWrapper
                .Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<HttpCompletionOption>(), It.IsAny<CancellationToken>()))
                .Callback<HttpRequestMessage, HttpCompletionOption, CancellationToken>((req, opt, ct) =>
                {
                    // capture request body synchronously to avoid reading a disposed content later
                    try { capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult(); } catch { capturedBody = null; }
                })
                .ReturnsAsync(response);

            var settings = new AppSettings { EnableAiTools = true, LmStudioBaseUrl = "http://localhost:1234" };
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.Current).Returns(settings);

            var toolDef = new ToolDefinition
            {
                Name = "search",
                Description = "Search files",
                Parameters = new ToolParameters { Type = "object" }
            };

            var mockToolQueueProvider = new Mock<IToolQueueProvider>();
            mockToolQueueProvider
                .Setup(p => p.GetMainQueue())
                .Returns(ToolQueue.Main(new System.Collections.Generic.List<ToolDefinition> { toolDef }));

            var adapter = new OpenApiAdapter(mockWrapper.Object, mockSettings.Object, new ApiRequestBuilder(mockSettings.Object, mockToolQueueProvider.Object), new Mock<ITemporaryHttpClientFactory>().Object);

            var messageContext = new MessageContext(new ChatMessage[0]);
            var modelContext = new ModelContext("mymodel");

            // use streaming call which passes useTools = true in BuildRequest
            using (var streaming = await adapter.SendChatStreamingAsync(messageContext, modelContext, CancellationToken.None))
            {
                // dispose immediately; body already captured in mock
            }

            Assert.That(capturedBody, Is.Not.Null.And.Not.Empty);
            Assert.That(capturedBody, Does.Contain("\"tools\""));
            Assert.That(capturedBody, Does.Contain("search"));
        }
    }
}
