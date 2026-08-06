using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ModelsList;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.WebView.Controllers;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class SettingsControllerTests
    {
        private Mock<ISettingsManager> _settingsManagerMock;
        private Mock<IModelsListService> _modelsListServiceMock;
        private SettingsController _controller;

        [SetUp]
        public void SetUp()
        {
            _settingsManagerMock = new Mock<ISettingsManager>();
            _settingsManagerMock
                .Setup(m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _modelsListServiceMock = new Mock<IModelsListService>();
            _controller = new SettingsController(_settingsManagerMock.Object, _modelsListServiceMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _controller = null;
            _settingsManagerMock = null;
            _modelsListServiceMock = null;
        }

        // =========================================================================
        // SetAiToolsAsync
        // =========================================================================

        [Test]
        public async Task SetAiToolsAsync_ReadOnly_ReturnsTrueAndDelegatesMode()
        {
            var json = "{\"mode\":\"readonly\"}";
            var result = await _controller.SetAiToolsAsync(json);

            Assert.That(result, Is.True);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync("readonly", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task SetAiToolsAsync_ReadWrite_ReturnsTrueAndDelegatesMode()
        {
            var json = "{\"mode\":\"readwrite\"}";
            var result = await _controller.SetAiToolsAsync(json);

            Assert.That(result, Is.True);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync("readwrite", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task SetAiToolsAsync_NoTools_ReturnsTrueAndDelegatesMode()
        {
            var json = "{\"mode\":\"none\"}";
            var result = await _controller.SetAiToolsAsync(json);

            Assert.That(result, Is.True);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync("none", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task SetAiToolsAsync_UnrecognizedMode_StillDelegatesToManager()
        {
            var json = "{\"mode\":\"garbage\"}";
            var result = await _controller.SetAiToolsAsync(json);

            Assert.That(result, Is.True);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync("garbage", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task SetAiToolsAsync_WhenManagerThrows_ReturnsFalse()
        {
            _settingsManagerMock
                .Setup(m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("disk failure"));

            var json = "{\"mode\":\"readwrite\"}";
            var result = await _controller.SetAiToolsAsync(json);

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task SetAiToolsAsync_NullJson_ReturnsFalse()
        {
            var result = await _controller.SetAiToolsAsync(null);

            Assert.That(result, Is.False);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task SetAiToolsAsync_EmptyJson_ReturnsFalse()
        {
            var result = await _controller.SetAiToolsAsync("");

            Assert.That(result, Is.False);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task SetAiToolsAsync_InvalidJson_ReturnsFalse()
        {
            var result = await _controller.SetAiToolsAsync("{not valid}");

            Assert.That(result, Is.False);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task SetAiToolsAsync_MissingMode_ReturnsFalse()
        {
            var result = await _controller.SetAiToolsAsync("{}");

            Assert.That(result, Is.False);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
