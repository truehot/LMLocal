using LMLocal.Infrastructure.WebView.Navigation;
using Microsoft.Web.WebView2.Core;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.WebView
{
    [TestFixture]
    public class WebView2NavigationGuardTests
    {
        [Test]
        public void ShouldCancel_NewDocumentTopLevel_ReturnsFalse()
        {
            Assert.That(WebView2NavigationGuard.ShouldCancel(CoreWebView2NavigationKind.NewDocument, isFrame: false), Is.False);
        }

        [Test]
        public void ShouldCancel_ReloadTopLevel_ReturnsTrue()
        {
            Assert.That(WebView2NavigationGuard.ShouldCancel(CoreWebView2NavigationKind.Reload, isFrame: false), Is.True);
        }

        [Test]
        public void ShouldCancel_BackOrForwardTopLevel_ReturnsTrue()
        {
            Assert.That(WebView2NavigationGuard.ShouldCancel(CoreWebView2NavigationKind.BackOrForward, isFrame: false), Is.True);
        }

        [Test]
        public void ShouldCancel_AnyFrame_ReturnsTrue()
        {
            Assert.That(WebView2NavigationGuard.ShouldCancel(CoreWebView2NavigationKind.NewDocument, isFrame: true), Is.True);
            Assert.That(WebView2NavigationGuard.ShouldCancel(CoreWebView2NavigationKind.Reload, isFrame: true), Is.True);
            Assert.That(WebView2NavigationGuard.ShouldCancel(CoreWebView2NavigationKind.BackOrForward, isFrame: true), Is.True);
        }
    }
}
