using System.Reflection;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.Streaming;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class StreamProcessorFactoryTests
    {
        private static Mock<ISettingsManager> CreateSettings()
        {
            var settingsMock = new Mock<ISettingsManager>();
            settingsMock.Setup(s => s.WindowSeconds).Returns(5);
            settingsMock.Setup(s => s.Current).Returns(new AppSettings { StreamInactivityTimeoutSeconds = 20 });
            return settingsMock;
        }

        private static object GetField(object processor, string name)
        {
            var field = processor.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(processor);
        }

        [Test]
        public void Create_Returns_Processor_With_SettingsManager()
        {
            var settingsMock = CreateSettings();

            var factory = new StreamProcessorFactory(settingsMock.Object, new ToolCallArgumentsGuardFactory());
            var processor = factory.Create("google_gemma-4-e4b-it");

            Assert.That(processor, Is.Not.Null);
            Assert.That(processor, Is.InstanceOf<StreamProcessor>());
            Assert.That(GetField(processor, "_settingsManager"), Is.SameAs(settingsMock.Object));
        }

        [Test]
        public void Create_GemmaModel_ProcessorGetsCompositeGuard()
        {
            var factory = new StreamProcessorFactory(CreateSettings().Object, new ToolCallArgumentsGuardFactory());

            var processor = factory.Create("google_gemma-4-e4b-it");

            Assert.That(GetField(processor, "_argumentsGuard"), Is.InstanceOf<CompositeToolCallArgumentsGuard>());
        }

        [Test]
        public void Create_OtherModel_ProcessorGetsLengthGuard()
        {
            var factory = new StreamProcessorFactory(CreateSettings().Object, new ToolCallArgumentsGuardFactory());

            var processor = factory.Create("qwen/qwen3-coder-30b");

            Assert.That(GetField(processor, "_argumentsGuard"), Is.SameAs(ArgumentsLengthGuard.Instance));
        }
    }
}
