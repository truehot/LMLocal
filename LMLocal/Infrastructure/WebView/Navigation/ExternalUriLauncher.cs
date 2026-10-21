using System;
using System.ComponentModel;
using System.Diagnostics;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.WebView.Navigation
{
    /// <summary>
    /// Abstraction over "open this URI with the default system handler" so that navigation policy stays testable and free of process side effects.
    /// </summary>
    internal interface IExternalUriLauncher
    {
        /// <summary>
        /// Opens <paramref name="uri"/> with the default system handler (browser, mail client, ...).
        /// </summary>
        bool TryOpen(string uri);
    }

    /// <summary>
    /// Default <see cref="IExternalUriLauncher"/> backed by <see cref="Process.Start(ProcessStartInfo)"/> with <c>UseShellExecute = true</c>.
    /// </summary>
    internal sealed class ShellExternalUriLauncher : IExternalUriLauncher
    {
        public bool TryOpen(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return false;

            try
            {
                var startInfo = new ProcessStartInfo(uri)
                {
                    UseShellExecute = true
                };

                using (Process.Start(startInfo))
                {
                }

                return true;
            }
            catch (Win32Exception ex)
            {
                InternalLogger.Warn($"ExternalUriLauncher: no handler for '{uri}': {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ExternalUriLauncher: failed to open '{uri}': {ex.Message}");
                return false;
            }
        }
    }
}









