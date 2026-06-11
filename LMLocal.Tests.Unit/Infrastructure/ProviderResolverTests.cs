using System;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.LlmApi.Provider;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ProviderResolverTests
    {
        [TestCase("http://localhost:1234", "LmStudio")]
        [TestCase("http://example.com:8080", "LmStudio")]
        [TestCase("not a url", "LmStudio")]
        [TestCase(null, "LmStudio")]
        public void ResolveProvider_ReturnsExpected_ForVariousInputs(string input, string expectedName)
        {
            var p = ProviderResolver.ResolveProvider(input);
            Enum.TryParse<ModelProvider>(expectedName, out var expected);
            Assert.That(p, Is.EqualTo(expected));
        }

        [TestCase("lmstudio", "LmStudio")]
        [TestCase("ollama", "Ollama")]
        [TestCase("openai", "OpenAi")]
        [TestCase("jan", "Jan")]
        public void ResolveProvider_ReturnsExpected_ForProviderNames(string providerName, string expectedName)
        {
            var p = ProviderResolver.ResolveProvider(providerName);
            Enum.TryParse<ModelProvider>(expectedName, out var expected);
            Assert.That(p, Is.EqualTo(expected));
        }
    }
}
