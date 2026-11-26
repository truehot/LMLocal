using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.HttpWrapper;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    /// <summary>One line per HTTP request in the daily HTTP log: no bodies, no key headers, no query string.</summary>
    [TestFixture]
    public class HttpLogFileTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 25, 10, 20, 30, 456);
        private static readonly TimeSpan Elapsed = TimeSpan.FromMilliseconds(1234);

        [Test]
        public void Format_Response_HasTimeMethodAddressStatusAndDuration()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/v1/chat/completions");
            var response = new HttpResponseMessage(HttpStatusCode.OK);

            string line = HttpLogFile.Format(Now, request, response, Elapsed, null);

            Assert.That(line, Does.StartWith("[2026-11-25 10:20:30.456] [tid:"));
            Assert.That(line, Does.EndWith("] POST https://api.example.com/v1/chat/completions 200 1234ms"));
        }

        [Test]
        public void Format_Address_HasNoQueryNoUserInfoNoFragment()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://user:SECRET-PW@api.example.com:8443/v1/models?key=SECRET-KEY#part");

            string line = HttpLogFile.Format(Now, request, new HttpResponseMessage(HttpStatusCode.OK), Elapsed, null);

            Assert.That(line, Does.Contain(" GET https://api.example.com:8443/v1/models 200 "));
            Assert.That(line, Does.Not.Contain("SECRET"));
            Assert.That(line, Does.Not.Contain("#part"));
        }

        [Test]
        public void Format_RequestHeaders_AreNeverWritten()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "SECRET-TOKEN");
            request.Headers.Add("api-key", "SECRET-KEY");

            string line = HttpLogFile.Format(Now, request, new HttpResponseMessage((HttpStatusCode)429), Elapsed, null);

            Assert.That(line, Does.Not.Contain("SECRET"));
        }

        [Test]
        public void Format_429_HasRetryAfterAndRateLimitHeadersOnly()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/v1/chat/completions");
            var response = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("{\"error\":\"SECRET-BODY\"}") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(20));
            response.Headers.Add("x-ratelimit-remaining-requests", "0");
            response.Headers.Add("anthropic-ratelimit-tokens-reset", "2026-11-25T10:21:00Z");
            response.Headers.Add("Set-Cookie", "session=SECRET-COOKIE");

            string line = HttpLogFile.Format(Now, request, response, Elapsed, null);

            Assert.That(line, Does.Contain(" 429 1234ms Retry-After=20"));
            Assert.That(line, Does.Contain(" x-ratelimit-remaining-requests=0"));
            Assert.That(line, Does.Contain(" anthropic-ratelimit-tokens-reset=2026-11-25T10:21:00Z"));
            Assert.That(line, Does.Not.Contain("SECRET"));
        }

        [Test]
        public void Format_200_HasNoRateLimitHeaders()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/v1/chat/completions");
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("x-ratelimit-remaining-requests", "99");

            string line = HttpLogFile.Format(Now, request, response, Elapsed, null);

            Assert.That(line, Does.Not.Contain("ratelimit"));
        }

        [Test]
        public void Format_Failure_HasTypeAndOneLineMessage()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/v1/chat/completions");

            string line = HttpLogFile.Format(Now, request, null, Elapsed, new HttpRequestException("first\r\nsecond"));

            Assert.That(line, Does.EndWith(" FAILED 1234ms HttpRequestException: first  second"));
        }

        [Test]
        public void Format_Cancellation_IsMarkedWithoutTheException()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/v1/chat/completions");

            string line = HttpLogFile.Format(Now, request, null, Elapsed, new TaskCanceledException("A task was canceled."));

            Assert.That(line, Does.EndWith(" CANCELLED 1234ms"));
        }

#if DEBUG
        // Write is [Conditional("DEBUG")]: these calls are removed in Release.
        [Test]
        public void Write_AppendsToTheHttpFileOfTheDay()
        {
            string previous = DailyLogFile.DirectoryOverride;
            string folder = Path.Combine(Path.GetTempPath(), "lmlocal-http-log-" + Guid.NewGuid().ToString("N"));
            DailyLogFile.DirectoryOverride = folder;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/v1/models");
                HttpLogFile.Write(request, new HttpResponseMessage(HttpStatusCode.OK), Elapsed, null);
                HttpLogFile.Write(request, new HttpResponseMessage(HttpStatusCode.OK), Elapsed, null);

                string expectedName = "http-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log";
                string[] files = Directory.GetFiles(folder);
                Assert.That(files.Select(Path.GetFileName), Is.EquivalentTo(new[] { expectedName }));
                Assert.That(File.ReadAllLines(files[0]).Count(l => l.Contains(" GET https://api.example.com/v1/models 200 ")), Is.EqualTo(2));
            }
            finally
            {
                DailyLogFile.DirectoryOverride = previous;
                try
                {
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, true);
                }
                catch (IOException)
                {
                    // A leftover temp folder must not fail the test.
                }
            }
        }
#endif
    }
}
