using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.WebView.Environment;
using Microsoft.Web.WebView2.Core;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.WebView
{
    [TestFixture]
    public class WebViewEnvironmentProviderTests
    {
        private static Mock<ISettingsManager> CreateSettings()
        {
            var mock = new Mock<ISettingsManager>();
            mock.Setup(s => s.LocalAppDataFolder).Returns("LMLocalChat");
            mock.Setup(s => s.WebViewUserDataFolder).Returns("WebViewData");
            return mock;
        }

        /// <summary>
        /// Creates a factory mock that records the number of invocations.
        /// The mock returns null — the provider must still treat the environment as created once.
        /// </summary>
        private static Mock<ICoreWebView2EnvironmentFactory> CreateFactory()
        {
            var mock = new Mock<ICoreWebView2EnvironmentFactory>();
            mock.Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CoreWebView2Environment)null);
            return mock;
        }

        [Test]
        public async Task GetEnvironmentAsync_RepeatedCalls_CreatesEnvironmentOnlyOnce()
        {
            var factory = CreateFactory();
            var provider = new WebViewEnvironmentProvider(CreateSettings().Object, factory.Object);

            await provider.GetEnvironmentAsync(CancellationToken.None);
            await provider.GetEnvironmentAsync(CancellationToken.None);
            await provider.GetEnvironmentAsync(CancellationToken.None);

            factory.Verify(
                f => f.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once,
                "The shared environment must be created exactly once per process.");
        }

        [Test]
        public async Task GetEnvironmentAsync_UserDataFolder_IsLocalAppDataPlusConfiguredFolders()
        {
            string expected = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "LMLocalChat",
                "WebViewData");

            string capturedFolder = null;
            var factory = new Mock<ICoreWebView2EnvironmentFactory>();
            factory.Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CoreWebView2Environment)null)
                .Callback<string, CancellationToken>((folder, ct) => capturedFolder = folder);

            var provider = new WebViewEnvironmentProvider(CreateSettings().Object, factory.Object);

            await provider.GetEnvironmentAsync(CancellationToken.None);

            Assert.That(capturedFolder, Is.EqualTo(expected));
        }

        [Test]
        public void GetEnvironmentAsync_CancelledToken_ThrowsAndDoesNotCreate()
        {
            var factory = CreateFactory();
            var provider = new WebViewEnvironmentProvider(CreateSettings().Object, factory.Object);

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                Assert.That(
                    async () => await provider.GetEnvironmentAsync(cts.Token),
                    Throws.TypeOf<OperationCanceledException>());
            }

            factory.Verify(
                f => f.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "No environment creation should happen when the token is already cancelled.");
        }

        [Test]
        public async Task GetEnvironmentAsync_ParallelCalls_CreatesEnvironmentOnlyOnce()
        {
            int createCount = 0;
            var factory = new Mock<ICoreWebView2EnvironmentFactory>();
            factory.Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string folder, CancellationToken ct) =>
                {
                    Interlocked.Increment(ref createCount);
                    await Task.Delay(50); // force concurrent overlap
                    return null;
                });

            var provider = new WebViewEnvironmentProvider(CreateSettings().Object, factory.Object);

            var results = await Task.WhenAll(
                provider.GetEnvironmentAsync(CancellationToken.None),
                provider.GetEnvironmentAsync(CancellationToken.None),
                provider.GetEnvironmentAsync(CancellationToken.None));

            Assert.That(createCount, Is.EqualTo(1),
                "Concurrent callers must share a single environment creation.");
            Assert.That(results, Is.Not.Null);
            Assert.That(results, Has.Length.EqualTo(3));
        }

        [Test]
        public void Constructor_NullSettings_Throws()
        {
            Assert.That(
                () => new WebViewEnvironmentProvider(null, CreateFactory().Object),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_NullEnvironmentFactory_Throws()
        {
            Assert.That(
                () => new WebViewEnvironmentProvider(CreateSettings().Object, null),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
