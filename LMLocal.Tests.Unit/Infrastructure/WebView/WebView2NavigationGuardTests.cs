using LMLocal.Infrastructure.WebView.Navigation;
using Microsoft.Web.WebView2.Core;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.WebView
{
    [TestFixture]
    public class WebView2NavigationGuardTests
    {
        private const string AppUri = "https://app.local/app.html";

        private static WebView2NavigationDecision Decide(
            CoreWebView2NavigationKind kind,
            string targetUri,
            bool isFrame = false)
            => WebView2NavigationGuard.Decide(kind, targetUri, isFrame, AppUri);

        private static WebView2NavigationDecision NewDocument(string targetUri)
            => Decide(CoreWebView2NavigationKind.NewDocument, targetUri);

        // --- Unchanged behaviour: the application page itself -------------------

        [Test]
        public void Decide_NewDocumentAppUri_ReturnsAllow()
        {
            Assert.That(NewDocument(AppUri), Is.EqualTo(WebView2NavigationDecision.Allow));
        }

        [Test]
        public void Decide_NewDocumentAppUriWithFragment_ReturnsAllow()
        {
            Assert.That(NewDocument("https://app.local/app.html#settings"), Is.EqualTo(WebView2NavigationDecision.Allow));
        }

        // --- Unchanged behaviour: reload, back/forward and frames stay blocked ---

        [Test]
        public void Decide_ReloadTopLevelAppUri_ReturnsCancelSilently()
        {
            Assert.That(
                Decide(CoreWebView2NavigationKind.Reload, AppUri),
                Is.EqualTo(WebView2NavigationDecision.CancelSilently));
        }

        [Test]
        public void Decide_BackOrForwardTopLevelAppUri_ReturnsCancelSilently()
        {
            Assert.That(
                Decide(CoreWebView2NavigationKind.BackOrForward, AppUri),
                Is.EqualTo(WebView2NavigationDecision.CancelSilently));
        }

        [Test]
        public void Decide_AnyFrame_ReturnsCancelSilently()
        {
            Assert.That(
                Decide(CoreWebView2NavigationKind.NewDocument, AppUri, isFrame: true),
                Is.EqualTo(WebView2NavigationDecision.CancelSilently));
            Assert.That(
                Decide(CoreWebView2NavigationKind.NewDocument, "https://example.com/frame", isFrame: true),
                Is.EqualTo(WebView2NavigationDecision.CancelSilently));
            Assert.That(
                Decide(CoreWebView2NavigationKind.Reload, AppUri, isFrame: true),
                Is.EqualTo(WebView2NavigationDecision.CancelSilently));
            Assert.That(
                Decide(CoreWebView2NavigationKind.BackOrForward, AppUri, isFrame: true),
                Is.EqualTo(WebView2NavigationDecision.CancelSilently));
        }

        [Test]
        public void Decide_ReloadExternalUri_ReturnsCancelSilentlyAndDoesNotOpenExternally()
        {
            Assert.That(
                Decide(CoreWebView2NavigationKind.Reload, "https://example.com"),
                Is.EqualTo(WebView2NavigationDecision.CancelSilently));
        }

        // --- New behaviour: non-app top-level documents are never loaded ---------

        [Test]
        public void Decide_NewDocumentExternalHttps_ReturnsCancelAndOpenExternally()
        {
            Assert.That(
                NewDocument("https://github.com/anthropics/claude-code"),
                Is.EqualTo(WebView2NavigationDecision.CancelAndOpenExternally));
        }

        [Test]
        public void Decide_NewDocumentExternalHttp_ReturnsCancelAndOpenExternally()
        {
            Assert.That(
                NewDocument("http://example.com/page"),
                Is.EqualTo(WebView2NavigationDecision.CancelAndOpenExternally));
        }

        [Test]
        public void Decide_NewDocumentAppHostOtherPath_ReturnsCancelSilently()
        {
            Assert.That(NewDocument("https://app.local/other.html"), Is.EqualTo(WebView2NavigationDecision.CancelSilently));
        }

        [Test]
        public void Decide_NewDocumentNonWebSchemes_ReturnsCancelSilently()
        {
            Assert.That(NewDocument("about:blank"), Is.EqualTo(WebView2NavigationDecision.CancelSilently));
            Assert.That(NewDocument("mailto:someone@example.com"), Is.EqualTo(WebView2NavigationDecision.CancelSilently));
            Assert.That(NewDocument("blob:https://app.local/1234"), Is.EqualTo(WebView2NavigationDecision.CancelSilently));
            Assert.That(NewDocument("data:text/html,<h1>x</h1>"), Is.EqualTo(WebView2NavigationDecision.CancelSilently));
            Assert.That(NewDocument("file:///C:/Windows/win.ini"), Is.EqualTo(WebView2NavigationDecision.CancelSilently));
        }

        [Test]
        public void Decide_NewDocumentEmptyUri_ReturnsCancelSilently()
        {
            Assert.That(NewDocument(null), Is.EqualTo(WebView2NavigationDecision.CancelSilently));
            Assert.That(NewDocument(string.Empty), Is.EqualTo(WebView2NavigationDecision.CancelSilently));
            Assert.That(NewDocument("   "), Is.EqualTo(WebView2NavigationDecision.CancelSilently));
        }

        // --- Helpers ------------------------------------------------------------

        [Test]
        public void ShouldOpenExternally_ExternalWebUri_ReturnsTrue()
        {
            Assert.That(WebView2NavigationGuard.ShouldOpenExternally("https://example.com", AppUri), Is.True);
            Assert.That(WebView2NavigationGuard.ShouldOpenExternally("http://example.com", AppUri), Is.True);
        }

        [Test]
        public void ShouldOpenExternally_AppHostOrNonWebUri_ReturnsFalse()
        {
            Assert.That(WebView2NavigationGuard.ShouldOpenExternally(AppUri, AppUri), Is.False);
            Assert.That(WebView2NavigationGuard.ShouldOpenExternally("https://app.local/other.html", AppUri), Is.False);
            Assert.That(WebView2NavigationGuard.ShouldOpenExternally("blob:https://app.local/1234", AppUri), Is.False);
            Assert.That(WebView2NavigationGuard.ShouldOpenExternally("data:image/png;base64,AAAA", AppUri), Is.False);
        }

        [Test]
        public void IsAppUri_ComparesHostAndPathOnly()
        {
            Assert.That(WebView2NavigationGuard.IsAppUri(AppUri, AppUri), Is.True);
            Assert.That(WebView2NavigationGuard.IsAppUri("HTTPS://APP.LOCAL/app.html", AppUri), Is.True);
            Assert.That(WebView2NavigationGuard.IsAppUri("https://app.local/", AppUri), Is.False);
            Assert.That(WebView2NavigationGuard.IsAppUri("https://app.local.evil.com/app.html", AppUri), Is.False);
        }

        [Test]
        public void IsAppUri_HostSpoofingAttempts_AreNotTheAppPage()
        {
            // userinfo trick: host is evil.example, "app.local" is only a user name
            Assert.That(WebView2NavigationGuard.IsAppUri("https://app.local@evil.example/app.html", AppUri), Is.False);
            // trailing dot is a different host string, so the navigation fails closed (cancelled)
            Assert.That(WebView2NavigationGuard.IsAppUri("https://app.local./app.html", AppUri), Is.False);
            // different port
            Assert.That(WebView2NavigationGuard.IsAppUri("https://app.local:8443/app.html", AppUri), Is.False);
            // different scheme
            Assert.That(WebView2NavigationGuard.IsAppUri("http://app.local/app.html", AppUri), Is.False);
        }
    }
}

















