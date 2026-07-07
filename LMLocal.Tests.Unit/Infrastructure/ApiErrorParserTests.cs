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

            Assert.That(result.Message, Is.EqualTo("{}"));
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
        public void ParseErrorBody_InvalidJson_ReturnsTrimmedRawText()
        {
            var result = ApiErrorParser.ParseErrorBody("<html>Server Error</html>");

            Assert.That(result.Message, Is.EqualTo("<html>Server Error</html>"));
        }

        [Test]
        public void ParseErrorBody_TruncatesLongInvalidJson()
        {
            var longText = new string('x', 500);

            var result = ApiErrorParser.ParseErrorBody(longText);

            Assert.That(result.Message.Length, Is.EqualTo(303)); // 300 + "..."
        }

        [Test]
        public void ParseErrorBody_TruncatesLongValidJsonWithNoMessage()
        {
            var longJson = "{\"x\":\"" + new string('y', 500) + "\"}";

            var result = ApiErrorParser.ParseErrorBody(longJson);

            Assert.That(result.Message.Length, Is.EqualTo(303)); // 300 + "..."
            Assert.That(result.Message.EndsWith("..."));
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
    }
}
