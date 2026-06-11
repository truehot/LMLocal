using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Tooling.Mcp.Client;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Mcp
{
    [TestFixture]
    public class HttpMcpClientTests
    {
        private class FakeHttpClientWrapper : IHttpClientWrapper
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public FakeHttpClientWrapper(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder ?? throw new ArgumentNullException(nameof(responder));
            }

            public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default)
            {
                var resp = _responder(request);
                return Task.FromResult(resp);
            }

            public void Dispose() { }
        }

        private class TestHttpMcpClient : HttpMcpClient
        {
            public TestHttpMcpClient(string baseUrl, IHttpClientWrapper httpClientWrapper, TimeSpan? requestTimeout = null)
                : base(baseUrl, httpClientWrapper, null, null, requestTimeout)
            {
            }

            public Task<string> CallSendJsonAndWaitResponseAsync(string json, CancellationToken cancellationToken)
            {
                return base.SendJsonAndWaitResponseAsync(json, cancellationToken);
            }
        }

        [Test]
        public async Task SendJsonAndWaitResponseAsync_ReturnsJson_ForApplicationJson()
        {
            var jsonBody = "{\"ok\":true}";
            var wrapper = new FakeHttpClientWrapper(req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };
                return resp;
            });

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            var result = await client.CallSendJsonAndWaitResponseAsync("{}", CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(jsonBody));
        }

        [Test]
        public async Task SendJsonAndWaitResponseAsync_ReadsSseStream_ForEventStream()
        {
            var sseContent = "data: {\"a\":1}\n\ndata: {\"b\":2}\ndata: [DONE]\n";

            var wrapper = new FakeHttpClientWrapper(req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sseContent)))
                };
                resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                return resp;
            });

            var client = new TestHttpMcpClient("https://example.local/stream", wrapper, TimeSpan.FromSeconds(10));

            var result = await client.CallSendJsonAndWaitResponseAsync("{}", CancellationToken.None).ConfigureAwait(false);

            // SSE reader returns the first complete JSON message (per current implementation)
            Assert.That(result, Does.Contain("{\"b\":2}"));
            Assert.That(result, Does.Not.Contain("{\"a\":1}"));
        }

        [Test]
        public void SendJsonAndWaitResponseAsync_Throws_OnNonSuccessStatus()
        {
            var wrapper = new FakeHttpClientWrapper(req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"bad\"}", Encoding.UTF8, "application/json")
                };
                return resp;
            });

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await client.CallSendJsonAndWaitResponseAsync("{}", CancellationToken.None).ConfigureAwait(false);
            });

            Assert.That(ex.Message, Does.Contain("HTTP request failed"));
        }

        [Test]
        public async Task SendJsonAndWaitResponseAsync_StoresSessionId_FromHeaders()
        {
            var wrapper = new FakeHttpClientWrapper(req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"result\":{}}", Encoding.UTF8, "application/json")
                };
                resp.Headers.Add("Mcp-Session-Id", "sid-123");
                return resp;
            });

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            var result = await client.CallSendJsonAndWaitResponseAsync("{}", CancellationToken.None).ConfigureAwait(false);

            // Use reflection to verify private field _sessionId was set
            var fi = typeof(HttpMcpClient).GetField("_sessionId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var val = fi?.GetValue(client) as string;

            Assert.That(val, Is.EqualTo("sid-123"));
        }
    }
}
