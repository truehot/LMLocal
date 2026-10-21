using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Tooling.Mcp.Client;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

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

        [Test]
        public async Task SendJsonAndWaitResponseAsync_RecoversSession_On404WithSessionId()
        {
            var calls = new List<string>();
            int initializeCount = 0;

            var wrapper = new FakeHttpClientWrapper(req =>
            {
                var body = ReadBody(req);
                var sessionId = GetSessionId(req);
                calls.Add((sessionId ?? "-") + ":" + body);

                if (body.Contains("notifications/initialized"))
                    return JsonResponse(HttpStatusCode.Accepted, "", sessionId);

                if (body.Contains("\"initialize\""))
                {
                    initializeCount++;
                    var newSessionId = initializeCount == 1 ? "sid-1" : "sid-2";
                    return JsonResponse(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", newSessionId);
                }

                if (body.Contains("tools/call"))
                {
                    if (sessionId == "sid-1")
                        return JsonResponse(HttpStatusCode.NotFound, "{\"error\":\"session not found\"}");

                    return JsonResponse(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"ok\":true}}", sessionId);
                }

                return JsonResponse(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", sessionId);
            });

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            await client.InitializeAsync(CancellationToken.None).ConfigureAwait(false);

            var result = await client
                .CallSendJsonAndWaitResponseAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\"}", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result, Does.Contain("\"ok\":true"));
            Assert.That(initializeCount, Is.EqualTo(2), "recovery must perform exactly one extra initialize");
            Assert.That(calls.Count, Is.EqualTo(6));

            // 0: initialize без session id (sid-1 приходит в ответе), 1: notifications/initialized с sid-1
            Assert.That(calls[1], Does.StartWith("sid-1:"));
            Assert.That(calls[1], Does.Contain("notifications/initialized"));
            // 2: tools/call со старым id -> 404
            Assert.That(calls[2], Does.StartWith("sid-1:"));
            Assert.That(calls[2], Does.Contain("tools/call"));
            // 3: повторный initialize без session id
            Assert.That(calls[3], Does.StartWith("-:"));
            Assert.That(calls[3], Does.Contain("\"initialize\""));
            // 4: notifications/initialized уже с новым id
            Assert.That(calls[4], Does.StartWith("sid-2:"));
            Assert.That(calls[4], Does.Contain("notifications/initialized"));
            // 5: единственный повтор исходного запроса с новым id
            Assert.That(calls[5], Does.StartWith("sid-2:"));
            Assert.That(calls[5], Does.Contain("tools/call"));
        }

        [Test]
        public void SendJsonAndWaitResponseAsync_Throws_WhenRetryAlsoReturns404()
        {
            int initializeCount = 0;
            int toolCallCount = 0;

            var wrapper = new FakeHttpClientWrapper(req =>
            {
                var body = ReadBody(req);

                if (body.Contains("notifications/initialized"))
                    return JsonResponse(HttpStatusCode.Accepted, "");

                if (body.Contains("\"initialize\""))
                {
                    initializeCount++;
                    return JsonResponse(
                        HttpStatusCode.OK,
                        "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}",
                        "sid-" + initializeCount);
                }

                if (body.Contains("tools/call"))
                {
                    toolCallCount++;
                    return JsonResponse(HttpStatusCode.NotFound, "{\"error\":\"session not found\"}");
                }

                return JsonResponse(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}");
            });

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            Assert.DoesNotThrowAsync(async () => await client.InitializeAsync(CancellationToken.None).ConfigureAwait(false));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await client
                    .CallSendJsonAndWaitResponseAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\"}", CancellationToken.None)
                    .ConfigureAwait(false);
            });

            Assert.That(ex.Message, Does.Contain("HTTP request failed"));
            Assert.That(ex.Message, Does.Contain("404"));
            Assert.That(initializeCount, Is.EqualTo(2), "recovery must not loop");
            Assert.That(toolCallCount, Is.EqualTo(2), "the original request is retried exactly once");
        }

        [Test]
        public void SendJsonAndWaitResponseAsync_DoesNotRecover_When404WithoutSessionId()
        {
            int initializeCount = 0;

            var wrapper = new FakeHttpClientWrapper(req =>
            {
                var body = ReadBody(req);
                if (body.Contains("\"initialize\""))
                    initializeCount++;

                return JsonResponse(HttpStatusCode.NotFound, "{\"error\":\"not found\"}");
            });

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await client
                    .CallSendJsonAndWaitResponseAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\"}", CancellationToken.None)
                    .ConfigureAwait(false);
            });

            Assert.That(initializeCount, Is.EqualTo(0), "a 404 without session id must not trigger recovery");
        }

        [Test]
        public async Task InitializeAsync_SendsNotificationsInitialized_BeforeFirstToolsList()
        {
            var calls = new List<string>();

            var wrapper = new FakeHttpClientWrapper(req =>
            {
                var body = ReadBody(req);
                calls.Add(body);

                if (body.Contains("notifications/initialized"))
                    return JsonResponse(HttpStatusCode.Accepted, "", "sid-1");

                return JsonResponse(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[]}}", "sid-1");
            });

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            await client.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
            await client.ListToolsAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(calls.Count, Is.EqualTo(3));
            Assert.That(calls[0], Does.Contain("\"initialize\""));
            Assert.That(calls[2], Does.Contain("tools/list"));

            var notificationJson = JObject.Parse(calls[1]);
            Assert.That(notificationJson["jsonrpc"]?.ToString(), Is.EqualTo("2.0"));
            Assert.That(notificationJson["method"]?.ToString(), Is.EqualTo("notifications/initialized"));
            Assert.That(notificationJson["id"], Is.Null, "a notification must not carry an id");
        }

        [Test]
        public async Task InitializeAsync_Succeeds_WhenNotificationsInitializedFails()
        {
            var wrapper = new FakeHttpClientWrapper(req =>
            {
                var body = ReadBody(req);

                if (body.Contains("notifications/initialized"))
                    return JsonResponse(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");

                return JsonResponse(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", "sid-1");
            });

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            // An error while sending notifications/initialized must not break the connection.
            await client.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task CloseAsync_DoesNotThrow_WhenDeleteReturns405()
        {
            var wrapper = new FakeHttpClientWrapper(req =>
            {
                if (req.Method == HttpMethod.Delete)
                    return JsonResponse(HttpStatusCode.MethodNotAllowed, "{}");

                return JsonResponse(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", "sid-1");
            });

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            await client.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
            await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task CloseAsync_IsIdempotent_AndDeletesSessionOnce()
        {
            int deleteCount = 0;

            var wrapper = new FakeHttpClientWrapper(req =>
            {
                if (req.Method == HttpMethod.Delete)
                {
                    deleteCount++;
                    return JsonResponse(HttpStatusCode.OK, "");
                }

                return JsonResponse(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", "sid-1");
            });

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            await client.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
            await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(deleteCount, Is.EqualTo(1), "the session must be deleted exactly once");
        }

        [Test]
        public void SendJsonAndWaitResponseAsync_Throws_WhenClientIsClosed()
        {
            var wrapper = new FakeHttpClientWrapper(req =>
                JsonResponse(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", "sid-1"));

            var client = new TestHttpMcpClient("https://example.local/api", wrapper, TimeSpan.FromSeconds(5));

            Assert.DoesNotThrowAsync(async () => await client.CloseAsync(CancellationToken.None).ConfigureAwait(false));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await client.CallSendJsonAndWaitResponseAsync("{}", CancellationToken.None).ConfigureAwait(false);
            });

            Assert.That(ex.Message, Does.Contain("Client is closed"));
        }

        private static string ReadBody(HttpRequestMessage request)
        {
            if (request.Content == null)
                return string.Empty;

            return request.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private static string GetSessionId(HttpRequestMessage request)
        {
            return request.Headers.TryGetValues("Mcp-Session-Id", out var values)
                ? values.FirstOrDefault()
                : null;
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body, string sessionId = null)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrEmpty(sessionId))
                response.Headers.Add("Mcp-Session-Id", sessionId);

            return response;
        }
    }
}
