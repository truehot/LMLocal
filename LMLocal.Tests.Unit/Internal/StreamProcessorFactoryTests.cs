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
        public void Create_Uses_NoopWatcher_WhenTimeoutZero()
        {
            var settingsMock = new Mock<ISettingsManager>();
            settingsMock.Setup(s => s.WindowSeconds).Returns(5);
            var app = new AppSettings { StreamInactivityTimeoutSeconds = 0 };
            settingsMock.Setup(s => s.Current).Returns(app);

            var factory = new StreamProcessorFactory(settingsMock.Object);
            var cts = new CancellationTokenSource();
            var processor = factory.Create(cts);

            Assert.That(processor, Is.Not.Null);
            // we can further assert by reflection that watcher type is NoopStreamInactivityWatcher
            var field = processor.GetType().GetField("_inactivityWatcher", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            var watcher = field.GetValue(processor);
            Assert.That(watcher.GetType().Name, Is.EqualTo("NoopStreamInactivityWatcher"));
        }

        [Test]
        public void Create_Uses_StreamInactivityWatcher_WhenTimeoutPositive()
        {
            var settingsMock = new Mock<ISettingsManager>();
            settingsMock.Setup(s => s.WindowSeconds).Returns(5);
            var app = new AppSettings { StreamInactivityTimeoutSeconds = 3 };
            settingsMock.Setup(s => s.Current).Returns(app);

            var factory = new StreamProcessorFactory(settingsMock.Object);
            var cts = new CancellationTokenSource();
            var processor = factory.Create(cts);

            Assert.That(processor, Is.Not.Null);
            var field = processor.GetType().GetField("_inactivityWatcher", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            var watcher = field.GetValue(processor);
            Assert.That(watcher.GetType().Name, Is.EqualTo("StreamInactivityWatcher"));
        }
    }
}
