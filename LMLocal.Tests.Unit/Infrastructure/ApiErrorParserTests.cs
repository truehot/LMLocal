using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ApiErrorParserTests
    {
        [Test]
        public void ParseErrorBody_ErrorObject_ExtractsMessageAndCode()
        {
            var json = "{\"error\":{\"message\":\"boom\",\"code\":500}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Message, Is.EqualTo("boom"));
            Assert.That(result.Code, Is.EqualTo(500));
        }

        [Test]
        public void ParseErrorBody_ErrorString_ExtractsMessage()
        {
            var json = "{\"error\":\"something went wrong\"}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Message, Is.EqualTo("something went wrong"));
        }

        [Test]
        public void ParseErrorBody_NestedError_AnthropicStyle()
        {
            var json = "{\"error\":{\"error\":{\"message\":\"invalid x-api-key\"}}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Message, Is.EqualTo("invalid x-api-key"));
        }

        [Test]
        public void ParseErrorBody_ErrorsArray_ExtractsFirstMessage()
        {
            var json = "{\"errors\":[{\"message\":\"field required\",\"code\":\"validation\"},{\"message\":\"second\"}]}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Message, Is.EqualTo("field required"));
        }

        [Test]
        public void ParseErrorBody_ErrorsArray_ExtractsDetailFallback()
        {
            var json = "{\"errors\":[{\"detail\":\"name is invalid\"}]}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Message, Is.EqualTo("name is invalid"));
        }

        [Test]
        public void ParseErrorBody_DetailField_FastApiStyle()
        {
            var json = "{\"detail\":\"not found\"}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Message, Is.EqualTo("not found"));
        }

        [Test]
        public void ParseErrorBody_DetailWinsOverErrorWhenErrorHasNoMessage()
        {
            var json = "{\"error\":{},\"detail\":\"quota exceeded\"}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Message, Is.EqualTo("quota exceeded"));
        }

        [Test]
        public void ParseErrorBody_ErrorTakesPriorityOverDetail()
        {
            var json = "{\"error\":{\"message\":\"auth failed\"},\"detail\":\"not found\"}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Message, Is.EqualTo("auth failed"));
        }

        [Test]
        public void ParseErrorBody_EmptyObject_ReturnsTruncatedJson()
        {
            var json = "{}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Message, Is.Not.Empty);
            Assert.That(result.RawBody, Is.EqualTo("{}"));
        }

        [Test]
        public void ParseErrorBody_NullResponse_ReturnsEmptyResponse()
        {
            var result = ApiErrorParser.ParseErrorBody(null);

            Assert.That(result.Message, Is.EqualTo("Empty response"));
        }

        [Test]
        public void ParseErrorBody_WhitespaceResponse_ReturnsEmptyResponse()
        {
            var result = ApiErrorParser.ParseErrorBody("   ");

            Assert.That(result.Message, Is.EqualTo("Empty response"));
        }

        [Test]
        public void ParseErrorBody_WithoutMessage_KeepsBodyOutOfMessage()
        {
            // The body must not become the text the user reads or the text the overflow detector matches on.
            var json = "{\"error\":{\"code\":\"context_length_exceeded\"}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Message, Does.Not.Contain("context_length_exceeded"));
            Assert.That(result.Message, Is.Not.Empty);
            Assert.That(result.RawBody, Does.Contain("context_length_exceeded"));
            Assert.That(result.RawCode, Is.EqualTo("context_length_exceeded"));
        }

        [Test]
        public void ParseErrorBody_InvalidJson_KeepsRawTextInRawBody()
        {
            var result = ApiErrorParser.ParseErrorBody("<html>Server Error</html>");

            Assert.That(result.Message, Is.Not.EqualTo("<html>Server Error</html>"));
            Assert.That(result.RawBody, Is.EqualTo("<html>Server Error</html>"));
        }

        [Test]
        public void ParseErrorBody_TruncatesLongInvalidBody()
        {
            var longText = new string('x', 500);

            var result = ApiErrorParser.ParseErrorBody(longText);

            Assert.That(result.RawBody.Length, Is.EqualTo(303)); // 300 + "..."
        }

        [Test]
        public void ParseErrorBody_TruncatesLongValidJsonWithNoMessage()
        {
            var longJson = "{\"x\":\"" + new string('y', 500) + "\"}";

            var result = ApiErrorParser.ParseErrorBody(longJson);

            Assert.That(result.RawBody.Length, Is.EqualTo(303)); // 300 + "..."
            Assert.That(result.RawBody.EndsWith("..."), Is.True);
        }

        // --- metadata / provider / retry_after ---

        [Test]
        public void ParseErrorBody_Metadata_ExtractsProvider()
        {
            var json = "{\"error\":{\"message\":\"overloaded\",\"metadata\":{\"provider_name\":\"OpenRouter\"}}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Provider, Is.EqualTo("OpenRouter"));
            Assert.That(result.RawMetadata, Does.Contain("provider_name"));
        }

        [Test]
        public void ParseErrorBody_Metadata_ExtractsRetryAfterSeconds()
        {
            var json = "{\"error\":{\"message\":\"rate limited\",\"metadata\":{\"retry_after_seconds\":30}}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.RetryAfterSeconds, Is.EqualTo(30.0));
        }

        [Test]
        public void ParseErrorBody_Metadata_ExtractsRetryAfterSecondsRaw()
        {
            var json = "{\"error\":{\"message\":\"rate limited\",\"metadata\":{\"retry_after_seconds_raw\":15.5}}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.RetryAfterSeconds, Is.EqualTo(15.5));
        }

        [Test]
        public void ParseErrorBody_RetryAfter_DirectInError()
        {
            var json = "{\"error\":{\"message\":\"wait\",\"retry_after\":10}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.RetryAfterSeconds, Is.EqualTo(10.0));
        }

        // --- structured context-overflow markers ---

        [Test]
        public void ParseErrorBody_StringCode_KeepsRawCode()
        {
            var json = "{\"error\":{\"message\":\"This model's maximum context length is 8192 tokens\",\"code\":\"context_length_exceeded\"}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.RawCode, Is.EqualTo("context_length_exceeded"));
            Assert.That(result.Code, Is.Null, "string code must not be parsed as a number");
        }

        [Test]
        public void ParseErrorBody_ErrorType_IsRead()
        {
            var json = "{\"error\":{\"message\":\"exceeds the available context size\",\"type\":\"exceed_context_size_error\"}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Type, Is.EqualTo("exceed_context_size_error"));
        }

        [Test]
        public void ParseErrorBody_NumericCode_StillFillsCodeAndLeavesRawCodeNull()
        {
            var json = "{\"error\":{\"message\":\"bad request\",\"code\":400}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Code, Is.EqualTo(400));
            Assert.That(result.RawCode, Is.Null);
        }

        [Test]
        public void ParseErrorBody_WithoutMessage_KeepsStructuredFieldsAndRawBody()
        {
            var json = "{\"error\":{\"type\":\"exceed_context_size_error\",\"code\":\"context_length_exceeded\"}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.Type, Is.EqualTo("exceed_context_size_error"));
            Assert.That(result.RawCode, Is.EqualTo("context_length_exceeded"));

            // The body is available for the log, but it must not masquerade as the user-facing message.
            Assert.That(result.RawBody, Does.Contain("context_length_exceeded"));
            Assert.That(result.Message, Does.Not.Contain("context_length_exceeded"));
        }

        // --- IsRateLimit ---

        [Test]
        public void IsRateLimit_True_WhenCode429()
        {
            var info = new ApiErrorInfo { Message = "slow down", Code = 429 };

            Assert.That(info.IsRateLimit, Is.True);
        }

        [Test]
        public void IsRateLimit_True_WhenRetryAfterPresent()
        {
            var info = new ApiErrorInfo { Message = "wait", RetryAfterSeconds = 5 };

            Assert.That(info.IsRateLimit, Is.True);
        }

        [Test]
        public void IsRateLimit_False_WhenCode400()
        {
            var info = new ApiErrorInfo { Message = "bad request", Code = 400 };

            Assert.That(info.IsRateLimit, Is.False);
        }

        [Test]
        public void IsRateLimit_False_WhenNoCodeNoRetry()
        {
            var info = new ApiErrorInfo { Message = "unknown" };

            Assert.That(info.IsRateLimit, Is.False);
        }

        // --- ToString ---

        [Test]
        public void ToString_IncludesProviderAndCode()
        {
            var info = new ApiErrorInfo { Message = "fail", Code = 500, Provider = "Venice" };

            var str = info.ToString();

            Assert.That(str, Does.Contain("fail"));
            Assert.That(str, Does.Contain("code=500"));
            Assert.That(str, Does.Contain("[Venice]"));
        }

        [Test]
        public void ToString_OmitsProviderAndCode_WhenAbsent()
        {
            var info = new ApiErrorInfo { Message = "boom" };

            var str = info.ToString();

            Assert.That(str, Is.EqualTo("boom"));
        }

        // --- retry-after hints (rate-limit handling) ---

        [Test]
        public void ParseErrorBody_NumericAndStringHints_AreCultureInvariant()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

                var numeric = ApiErrorParser.ParseErrorBody("{\"error\":{\"message\":\"slow\",\"metadata\":{\"retry_after_seconds\":5.5}}}");
                var textual = ApiErrorParser.ParseErrorBody("{\"error\":{\"message\":\"slow\",\"metadata\":{\"retry_after_seconds\":\"5.5\"}}}");

                Assert.That(numeric.RetryAfterSeconds, Is.EqualTo(5.5));
                Assert.That(textual.RetryAfterSeconds, Is.EqualTo(5.5));
                Assert.That(numeric.RetryAfterSource, Is.EqualTo(RetryAfterSource.Body));
                Assert.That(textual.RetryAfterSource, Is.EqualTo(RetryAfterSource.Body));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [Test]
        public void ParseErrorBody_StringHintWithCommaSeparator_IsRejected()
        {
            var json = "{\"error\":{\"message\":\"slow\",\"metadata\":{\"retry_after_seconds\":\"5,5\"}}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.RetryAfterSeconds, Is.Null, "'5,5' must not be read as 55");
            Assert.That(result.RetryAfterSource, Is.EqualTo(RetryAfterSource.None));
        }

        [Test]
        public void ParseErrorBody_MalformedOrNonFiniteHints_LeaveNull()
        {
            var bodies = new[]
            {
                "{\"error\":{\"message\":\"slow\",\"metadata\":{\"retry_after_seconds\":\"abc\"}}}",
                "{\"error\":{\"message\":\"slow\",\"metadata\":{\"retry_after_seconds\":\"NaN\"}}}",
                "{\"error\":{\"message\":\"slow\",\"metadata\":{\"retry_after_seconds\":\"Infinity\"}}}",
                "{\"error\":{\"message\":\"slow\",\"metadata\":{\"retry_after_seconds\":-5}}}"
            };

            foreach (var json in bodies)
            {
                var result = ApiErrorParser.ParseErrorBody(json);

                Assert.That(result.RetryAfterSeconds, Is.Null, json);
                Assert.That(result.RetryAfterSource, Is.EqualTo(RetryAfterSource.None), json);
            }
        }

        [Test]
        public void ParseErrorBody_InvalidFirstCandidate_DoesNotMaskValidSecond()
        {
            var json = "{\"error\":{\"message\":\"slow\",\"metadata\":{\"retry_after_seconds\":\"oops\",\"retry_after_seconds_raw\":30}}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.RetryAfterSeconds, Is.EqualTo(30.0));
        }

        [Test]
        public void ParseErrorBody_TakesMaxAcrossBodyCandidates()
        {
            var json = "{\"error\":{\"message\":\"slow\",\"metadata\":{\"retry_after_seconds\":5},\"retry_after\":30}}";

            var result = ApiErrorParser.ParseErrorBody(json);

            Assert.That(result.RetryAfterSeconds, Is.EqualTo(30.0));
            Assert.That(result.RetryAfterSource, Is.EqualTo(RetryAfterSource.Body));
        }

        [Test]
        public void ParseRetryAfterHeader_Delta_ReturnsSeconds()
        {
            using (var response = new HttpResponseMessage())
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

                Assert.That(ApiErrorParser.ParseRetryAfterHeader(response.Headers), Is.EqualTo(30.0));
            }
        }

        [Test]
        public void ParseRetryAfterHeader_HttpDate_UsesServerDateHeader()
        {
            var serverNow = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

            using (var response = new HttpResponseMessage())
            {
                response.Headers.Date = serverNow;
                response.Headers.Age = TimeSpan.FromSeconds(10);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(serverNow.AddSeconds(60));

                Assert.That(ApiErrorParser.ParseRetryAfterHeader(response.Headers), Is.EqualTo(50.0));
            }
        }

        [Test]
        public void ParseRetryAfterHeader_HttpDateInThePast_IsClampedToZero()
        {
            using (var response = new HttpResponseMessage())
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(-60));

                Assert.That(ApiErrorParser.ParseRetryAfterHeader(response.Headers), Is.EqualTo(0.0));
            }
        }

        [Test]
        public void ParseRetryAfterHeader_AbsentOrUnusable_ReturnsNull()
        {
            using (var response = new HttpResponseMessage())
            {
                Assert.That(ApiErrorParser.ParseRetryAfterHeader(response.Headers), Is.Null);
                Assert.That(ApiErrorParser.ParseRetryAfterHeader(null), Is.Null);

                response.Headers.TryAddWithoutValidation("Retry-After", "-30");

                Assert.That(ApiErrorParser.ParseRetryAfterHeader(response.Headers), Is.Null);
            }
        }
    }
}
