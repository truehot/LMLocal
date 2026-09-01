using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.WebView.Navigation;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.WebView
{
    [TestFixture]
    public class WebViewNavigatorTests
    {
        private static Mock<IWebView2Page> CreatePage(bool success = true)
        {
            var page = new Mock<IWebView2Page>();
            page.Setup(p => p.NavigateAndWaitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(success);
            return page;
        }

        [Test]
        public async Task LoadAsync_Success_NavigatesAndFiresLmInit()
        {
            var page = CreatePage();
            var navigator = new WebViewNavigator();

            await navigator.LoadAsync(page.Object, "https://app.local/app.html", CancellationToken.None);

            page.Verify(p => p.NavigateAndWaitAsync("https://app.local/app.html", CancellationToken.None), Times.Once);
            page.Verify(p => p.ExecuteScriptAsync("window.lmInit()"), Times.Once);
        }

        [Test]
        public void LoadAsync_FailedNavigation_ThrowsAndDoesNotFireLmInit()
        {
            var page = CreatePage(success: false);
            var navigator = new WebViewNavigator();

            Assert.That(
                async () => await navigator.LoadAsync(page.Object, "https://app.local/app.html", CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>());

            page.Verify(p => p.ExecuteScriptAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void LoadAsync_CancelledWhileLoading_PropagatesAndDoesNotFireLmInit()
        {
            var page = new Mock<IWebView2Page>();
            page.Setup(p => p.NavigateAndWaitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            var navigator = new WebViewNavigator();

            Assert.That(
                async () => await navigator.LoadAsync(page.Object, "https://app.local/app.html", CancellationToken.None),
                Throws.TypeOf<OperationCanceledException>());

            page.Verify(p => p.ExecuteScriptAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void LoadAsync_PreCancelledToken_ThrowsWithoutNavigating()
        {
            var page = CreatePage();
            var navigator = new WebViewNavigator();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                Assert.That(
                    async () => await navigator.LoadAsync(page.Object, "https://app.local/app.html", cts.Token),
                    Throws.TypeOf<OperationCanceledException>());
            }

            page.Verify(p => p.NavigateAndWaitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            page.Verify(p => p.ExecuteScriptAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void LoadAsync_NullPage_Throws()
        {
            var navigator = new WebViewNavigator();

            Assert.That(
                async () => await navigator.LoadAsync(null, "https://app.local/app.html", CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void LoadAsync_EmptyUrl_Throws()
        {
            var page = CreatePage();
            var navigator = new WebViewNavigator();

            Assert.That(
                async () => await navigator.LoadAsync(page.Object, "  ", CancellationToken.None),
                Throws.TypeOf<ArgumentException>());

            page.Verify(p => p.NavigateAndWaitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task LoadAsync_LmInitScriptThrows_IsSwallowed()
        {
            var page = CreatePage();
            page.Setup(p => p.ExecuteScriptAsync(It.IsAny<string>()))
                .Throws(new InvalidOperationException("script boom"));
            var navigator = new WebViewNavigator();

            // Must not throw even though the lmInit script failed.
            await navigator.LoadAsync(page.Object, "https://app.local/app.html", CancellationToken.None);
        }
    }
}
