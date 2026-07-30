using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class KnownModelContextsTests
    {
        [Test]
        public void TryGet_ExactMatch_ReturnsContextLength()
        {
            // Arrange
            var modelId = "deepseek-v4";

            // Act
            var result = KnownModelContexts.TryGet(modelId);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(1_048_576));
        }

        [Test]
        public void TryGet_CaseInsensitiveMatch_ReturnsContextLength()
        {
            // Arrange
            var modelId = "DeepSeek-V4";

            // Act
            var result = KnownModelContexts.TryGet(modelId);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(1_048_576));
        }

        [Test]
        public void TryGet_OpenRouterFormat_ReturnsContextLength()
        {
            // Arrange
            var modelId = "deepseek/deepseek-v4";

            // Act
            var result = KnownModelContexts.TryGet(modelId);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(1_048_576));
        }

        [Test]
        public void TryGet_KimiK2_5_ReturnsContextLength()
        {
            // Arrange
            var modelId = "kimi-k2.5";

            // Act
            var result = KnownModelContexts.TryGet(modelId);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(256_000));
        }

        [Test]
        public void TryGet_Gemini31Pro_ReturnsContextLength()
        {
            // Arrange
            var modelId = "gemini-3.1-pro";

            // Act
            var result = KnownModelContexts.TryGet(modelId);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(2_000_000));
        }

        [Test]
        public void TryGet_Gemini31Flash_ReturnsContextLength()
        {
            // Arrange
            var modelId = "gemini-3.1-flash";

            // Act
            var result = KnownModelContexts.TryGet(modelId);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(1_048_576));
        }

        [Test]
        public void TryGet_UnknownModel_ReturnsNull()
        {
            // Arrange
            var modelId = "some-unknown-model-123";

            // Act
            var result = KnownModelContexts.TryGet(modelId);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryGet_NullOrEmpty_ReturnsNull()
        {
            Assert.That(KnownModelContexts.TryGet(null), Is.Null);
            Assert.That(KnownModelContexts.TryGet(string.Empty), Is.Null);
        }

        [Test]
        public void TryGet_PrefixDoesNotMatch_ReturnsNull()
        {
            // Should NOT match by prefix, only exact match
            var modelId = "deepseek-v4-extra";

            var result = KnownModelContexts.TryGet(modelId);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryGet_Glm4_5_ReturnsContextLength()
        {
            // glm-4.5 has 128k context
            var result = KnownModelContexts.TryGet("glm-4.5");

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(128_000));
        }
    }
}
