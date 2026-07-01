using System.Threading;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class StreamProcessorFactoryTests
    {
        [Test]
        public void Create_Returns_Processor_With_SettingsManager()
        {
            var settingsMock = new Mock<ISettingsManager>();
            settingsMock.Setup(s => s.WindowSeconds).Returns(5);
            var app = new AppSettings { StreamInactivityTimeoutSeconds = 20 };
            settingsMock.Setup(s => s.Current).Returns(app);

            var factory = new StreamProcessorFactory(settingsMock.Object);
            var cts = new CancellationTokenSource();
            var processor = factory.Create(cts);

            Assert.That(processor, Is.Not.Null);
            Assert.That(processor, Is.InstanceOf<StreamProcessor>());

            // Verify settings manager was passed through
            var field = processor.GetType().GetField("_settingsManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            var settings = field.GetValue(processor);
            Assert.That(settings, Is.SameAs(settingsMock.Object));
        }
    }
}
