using System;
using Microsoft.Web.WebView2.Core;

namespace LMLocal.Infrastructure.WebView.Navigation
{
    /// <summary>
    /// Pure guard rules for WebView2 navigation.
    /// </summary>
    internal static class WebView2NavigationGuard
    {
        /// <summary>
        /// Returns <c>true</c> when the navigation must be cancelled.
        /// </summary>
        public static bool ShouldCancel(CoreWebView2NavigationKind kind, bool isFrame)
            => isFrame || kind != CoreWebView2NavigationKind.NewDocument;
    }
}
