using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using Microsoft.Web.WebView2.Core;

namespace LMLocal.Infrastructure.WebView.Environment
{
    /// <summary>
    /// Caches the process-wide <see cref="CoreWebView2Environment"/> and creates it only once.
    /// </summary>
    internal class WebViewEnvironmentProvider : IWebViewEnvironmentProvider
    {
        private readonly ISettingsManager _settingsManager;
        private readonly ICoreWebView2EnvironmentFactory _environmentFactory;

        private readonly SemaphoreSlim _envLock = new SemaphoreSlim(1, 1);
        private CoreWebView2Environment _sharedEnvironment;
        private bool _environmentCreated;

        public WebViewEnvironmentProvider(
            ISettingsManager settingsManager,
            ICoreWebView2EnvironmentFactory environmentFactory)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _environmentFactory = environmentFactory ?? throw new ArgumentNullException(nameof(environmentFactory));
        }

        public async Task<CoreWebView2Environment> GetEnvironmentAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            await _envLock.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();

                if (!_environmentCreated)
                {
                    string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
                    string userDataFolder = Path.Combine(
                        localAppData,
                        _settingsManager.LocalAppDataFolder,
                        _settingsManager.WebViewUserDataFolder);

                    Directory.CreateDirectory(userDataFolder);

                    _sharedEnvironment = await _environmentFactory.CreateAsync(userDataFolder, ct);
                    _environmentCreated = true;
                }

                return _sharedEnvironment;
            }
            finally
            {
                _envLock.Release();
            }
        }
    }
}
