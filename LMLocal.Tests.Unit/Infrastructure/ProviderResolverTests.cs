using System;
using System.Linq;
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
        [TestCase("togetherai", "TogetherAi")]
        [TestCase("deepseek", "DeepSeek")]
        [TestCase("gemini", "Gemini")]
        [TestCase("githubmodelsazure", "GithubModelsAzure")]
        [TestCase("llamacpp", "LlamaCpp")]
        public void ResolveProvider_ReturnsExpected_ForProviderNames(string providerName, string expectedName)
        {
            var p = ProviderResolver.ResolveProvider(providerName);
            Enum.TryParse<ModelProvider>(expectedName, out var expected);
            Assert.That(p, Is.EqualTo(expected));
        }

        [Test]
        public void GetProviderTypes_ReturnsAllEnumValues()
        {
            var types = ProviderResolver.GetProviderTypes();

            Assert.That(types, Is.Not.Null);
            Assert.That(types.Count, Is.EqualTo(9));

            var enumValues = Enum.GetValues(typeof(ModelProvider)).Cast<ModelProvider>().ToList();
            Assert.That(types.Count, Is.EqualTo(enumValues.Count));

            // Each enum value must have a corresponding entry with a non-empty display name
            foreach (var ev in enumValues)
            {
                var expectedKey = ev.ToString().ToLowerInvariant();
                var match = types.Find(t => t.Key == expectedKey);
                Assert.That(match, Is.Not.Null, $"Missing ProviderTypeInfo for {ev}");
                Assert.That(match.DisplayName, Is.Not.Null.And.Not.Empty,
                    $"DisplayName must not be empty for {ev}");
            }
        }

        [Test]
        public void GetProviderTypes_DisplayNames_AreCorrect()
        {
            var types = ProviderResolver.GetProviderTypes();

            AssertDisplayName(types, "lmstudio", "LM Studio (local)");
            AssertDisplayName(types, "ollama", "Ollama (local)");
            AssertDisplayName(types, "jan", "Jan (local)");
            AssertDisplayName(types, "llamacpp", "Llama.cpp (local)");
            AssertDisplayName(types, "openai", "OpenAI compatible");
            AssertDisplayName(types, "togetherai", "Together AI (cloud)");
            AssertDisplayName(types, "deepseek", "DeepSeek (cloud)");
            AssertDisplayName(types, "gemini", "Gemini (cloud)");
            AssertDisplayName(types, "githubmodelsazure", "Github Models via Azure (cloud)");
        }

        [Test]
        public void GetProviderTypes_Keys_AreUnique()
        {
            var types = ProviderResolver.GetProviderTypes();
            var keys = types.Select(t => t.Key).ToList();
            Assert.That(keys.Count, Is.EqualTo(keys.Distinct().Count()));
        }

        private static void AssertDisplayName(System.Collections.Generic.List<LMLocal.Core.Models.ProviderTypeInfo> types,
            string key, string expectedDisplayName)
        {
            var match = types.Find(t => t.Key == key);
            Assert.That(match, Is.Not.Null, $"Missing type: {key}");
            Assert.That(match.DisplayName, Is.EqualTo(expectedDisplayName),
                $"Wrong display name for '{key}'");
        }

        public void GetDisplayName_ReturnsAttributeValue_ForAllEnumValues()
        {
            foreach (ModelProvider mp in Enum.GetValues(typeof(ModelProvider)))
            {
                var displayName = ProviderResolver.GetDisplayName(mp);
                Assert.That(displayName, Is.Not.Null.And.Not.Empty,
                    $"GetDisplayName returned null/empty for {mp}");
                Assert.That(displayName, Is.Not.EqualTo(mp.ToString()),
                    $"GetDisplayName should not fall back to enum name for {mp}");
            }
        }

        [TestCase("LmStudio", "LM Studio (local)")]
        [TestCase("Ollama", "Ollama (local)")]
        [TestCase("Jan", "Jan (local)")]
        [TestCase("LlamaCpp", "Llama.cpp (local)")]
        [TestCase("OpenAi", "OpenAI compatible")]
        [TestCase("TogetherAi", "Together AI (cloud)")]
        [TestCase("DeepSeek", "DeepSeek (cloud)")]
        [TestCase("Gemini", "Gemini (cloud)")]
        [TestCase("GithubModelsAzure", "Github Models via Azure (cloud)")]
        public void GetDisplayName_ReturnsCorrectValue(string mpName, string expected)
        {
            Enum.TryParse<ModelProvider>(mpName, out var mp);
            Assert.That(ProviderResolver.GetDisplayName(mp), Is.EqualTo(expected));
        }
    }
}
