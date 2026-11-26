using LMLocal.Core.Models;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Core.Models
{
    /// <summary>
    /// Equality and hashing contract for <see cref="ModelDefinition"/>, with a focus on the
    /// optional <see cref="ModelDefinition.InstructionTabId"/> field (model ↔ instruction binding).
    /// </summary>
    [TestFixture]
    public class ModelDefinitionTests
    {
        private static ModelDefinition BaseModel()
        {
            return new ModelDefinition
            {
                Id = 5,
                ModelId = "qwen2.5-coder-32b-instruct",
                ProviderType = "lmstudio",
                ProviderId = 3,
                DisplayName = "Qwen Coder",
                ContextLength = 131072,
                MaxTokens = 8192,
                ReasoningEffort = "medium",
                InstructionTabId = 4,
                IsCustom = false,
                Enabled = true
            };
        }

        [Test]
        public void InstructionTabId_DefaultsToNull()
        {
            var model = new ModelDefinition();

            Assert.That(model.InstructionTabId, Is.Null);
        }

        [Test]
        public void Equals_ReturnsTrue_WhenAllFieldsMatch()
        {
            var a = BaseModel();
            var b = BaseModel();

            Assert.That(a.Equals(b), Is.True);
            Assert.That(b.Equals(a), Is.True);
        }

        [Test]
        public void Equals_ReturnsTrue_WhenBothInstructionTabIdNull()
        {
            var a = BaseModel();
            var b = BaseModel();
            a.InstructionTabId = null;
            b.InstructionTabId = null;

            Assert.That(a.Equals(b), Is.True);
        }

        [Test]
        public void Equals_ReturnsFalse_WhenInstructionTabIdDiffers()
        {
            var a = BaseModel();
            var b = BaseModel();
            b.InstructionTabId = 7;

            Assert.That(a.Equals(b), Is.False);
        }

        [Test]
        public void Equals_ReturnsFalse_WhenNullVersusValue()
        {
            var a = BaseModel();
            var b = BaseModel();
            a.InstructionTabId = null;
            b.InstructionTabId = 4;

            Assert.That(a.Equals(b), Is.False);
            Assert.That(b.Equals(a), Is.False);
        }

        [Test]
        public void Equals_ReturnsFalse_WhenOtherFieldsDiffer()
        {
            var a = BaseModel();
            var b = BaseModel();
            b.MaxTokens = 4096;

            Assert.That(a.Equals(b), Is.False);
        }

        [Test]
        public void Equals_ReturnsTrue_ForSameReference()
        {
            var model = BaseModel();

            Assert.That(model.Equals(model), Is.True);
        }

        [Test]
        public void Equals_ReturnsFalse_WhenOtherIsNull()
        {
            var model = BaseModel();

            Assert.That(model.Equals((ModelDefinition)null), Is.False);
        }

        [Test]
        public void GetHashCode_Equal_ForEqualInstances()
        {
            var a = BaseModel();
            var b = BaseModel();

            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void GetHashCode_Differs_WhenInstructionTabIdDiffers()
        {
            var a = BaseModel();
            var b = BaseModel();
            b.InstructionTabId = 99;

            // Not a hard contract, but a regression signal for a missing hash contribution.
            Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void DisableReasoningEcho_DefaultsToFalse()
        {
            Assert.That(new ModelDefinition().DisableReasoningEcho, Is.False, "An existing profile without the field keeps echoing reasoning.");
        }

        [Test]
        public void Equals_ReturnsFalse_WhenDisableReasoningEchoDiffers()
        {
            var a = BaseModel();
            var b = BaseModel();
            b.DisableReasoningEcho = true;

            Assert.That(a.Equals(b), Is.False);
            Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void Json_WithoutDisableReasoningEcho_ReadsFalse()
        {
            var file = JsonConvert.DeserializeObject<ModelsConfigFile>(
                "{\"models\":[{\"id\":1,\"modelId\":\"deepseek-chat\",\"providerType\":\"deepseek\",\"enabled\":true}]}");

            Assert.That(file.Models[0].DisableReasoningEcho, Is.False);
        }

        [Test]
        public void Json_WithDisableReasoningEcho_ReadsTrue()
        {
            var file = JsonConvert.DeserializeObject<ModelsConfigFile>(
                "{\"models\":[{\"id\":1,\"modelId\":\"deepseek-chat\",\"providerType\":\"deepseek\",\"disableReasoningEcho\":true}]}");

            Assert.That(file.Models[0].DisableReasoningEcho, Is.True);
        }

        [Test]
        public void ModelsConfigFile_Equals_UsesInstructionTabId()
        {
            var a = new ModelsConfigFile { Models = { BaseModel() } };
            var b = new ModelsConfigFile { Models = { BaseModel() } };
            var c = new ModelsConfigFile { Models = { BaseModel() } };
            c.Models[0].InstructionTabId = 1;

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals(c), Is.False);
        }
    }
}
