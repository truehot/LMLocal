using System;
using Microsoft.Web.WebView2.Core;

namespace LMLocal.Infrastructure.WebView.Navigation
{
    /// <summary>
    /// Outcome of evaluating the WebView2 navigation policy.
    /// </summary>
    internal enum WebView2NavigationDecision
    {
        /// <summary>
        /// The navigation targets the application page itself and must proceed.
        /// </summary>
        Allow,

        /// <summary>
        /// The navigation must be cancelled and nothing else must happen.
        /// </summary>
        CancelSilently,

        /// <summary>
        /// The navigation must be cancelled and the target must be opened by the default browser.
        /// </summary>
        CancelAndOpenExternally
    }

    /// <summary>
    /// Pure navigation policy for the chat WebView2.
    /// </summary>
    internal static class WebView2NavigationGuard
    {
        /// <summary>
        /// Evaluates a single navigation attempt.
        /// </summary>
        public static WebView2NavigationDecision Decide(
            CoreWebView2NavigationKind kind,
            string targetUri,
            bool isFrame,
            string appUri)
        {
            if (isFrame)
                return WebView2NavigationDecision.CancelSilently;

            if (kind != CoreWebView2NavigationKind.NewDocument)
                return WebView2NavigationDecision.CancelSilently;

            if (IsAppUri(targetUri, appUri))
                return WebView2NavigationDecision.Allow;

            if (ShouldOpenExternally(targetUri, appUri))
                return WebView2NavigationDecision.CancelAndOpenExternally;

            return WebView2NavigationDecision.CancelSilently;
        }

        /// <summary>
        /// Returns <c>true</c> for absolute http/https URIs that do not point at the application host.
        /// </summary>
        public static bool ShouldOpenExternally(string targetUri, string appUri)
            => IsWebUri(targetUri) && !IsAppHost(targetUri, appUri);

        /// <summary>
        /// Returns <c>true</c> when <paramref name="targetUri"/> is the application page itself.
        /// </summary>
        public static bool IsAppUri(string targetUri, string appUri)
        {
            var target = ParseAbsolute(targetUri);
            var app = ParseAbsolute(appUri);
            if (target == null || app == null)
                return false;

            if (!IsSameOrigin(target, app))
                return false;

            return string.Equals(target.AbsolutePath, app.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAppHost(string targetUri, string appUri)
        {
            var target = ParseAbsolute(targetUri);
            var app = ParseAbsolute(appUri);
            if (target == null || app == null)
                return false;

            return IsSameOrigin(target, app);
        }

        private static bool IsSameOrigin(Uri left, Uri right)
            => string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
               && left.Port == right.Port;

        private static bool IsWebUri(string uri)
        {
            var parsed = ParseAbsolute(uri);
            if (parsed == null)
                return false;

            return string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static Uri ParseAbsolute(string uri)
        {
            return Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed) ? parsed : null;
        }
    }
}
