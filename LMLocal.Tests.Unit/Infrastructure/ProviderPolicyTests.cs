using LMLocal.Infrastructure.LlmApi.Provider;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    /// <summary>Every provider name, including the unknown/empty/null fallback, answers as <see cref="ProviderResolver"/> resolves it.</summary>
    [TestFixture]
    public class ProviderPolicyTests
    {
        [TestCase("llamacpp", true)]
        [TestCase("LlamaCpp", true)]
        [TestCase("LLAMACPP", true)]
        [TestCase("lmstudio", false)]
        [TestCase("ollama", false)]
        [TestCase("openai", false)]
        [TestCase("jan", false)]
        [TestCase("togetherai", false)]
        [TestCase("deepseek", false)]
        [TestCase("gemini", false)]
        [TestCase("githubmodelsazure", false)]
        [TestCase("unsloth", false)]
        [TestCase("unknown-provider", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void RequiresStrictRoleAlternation_ResolvesLikeProviderResolver(string providerName, bool expected)
        {
            Assert.That(new ProviderPolicy().RequiresStrictRoleAlternation(providerName), Is.EqualTo(expected));
        }
    }
}
