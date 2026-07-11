using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.WebView.Models;
using LMLocal.Models;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend settings logic.
    /// </summary>
    public interface ISettingsController
    {
        Task<string> GetSettingsAsync();
        Task<bool> UpdateSettingsAsync(string newSettingsJson);
        Task<string> TestConnectionAsync(string payload);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class SettingsController : ISettingsController
    {
        private readonly ISettingsManager _settingsManager;
        private readonly IModelsListService _modelsListService;

        internal SettingsController(ISettingsManager settingsManager, IModelsListService modelsListService)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _modelsListService = modelsListService ?? throw new ArgumentNullException(nameof(modelsListService));
        }

        public Task<string> GetSettingsAsync()
        {
            try
            {
                return Task.FromResult(_settingsManager.Current.ToJson());
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetSettingsAsync failed", ex);
                return Task.FromResult<string>(null);
            }
        }

        public async Task<bool> UpdateSettingsAsync(string newSettingsJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newSettingsJson))
                {
                    return false;
                }

                var newSettings = newSettingsJson.FromJson<AppSettings>();

                await _settingsManager.SaveAsync(newSettings).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateSettingsAsync failed", ex);
                return false;
            }
        }

        public async Task<string> TestConnectionAsync(string payload)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(payload))
                    return new { success = false, error = "Invalid parameters" }.ToJson();

                var request = payload.FromJson<TestConnectionRequest>();
                if (request == null || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Url))
                    return new { success = false, error = "Provider and URL are required" }.ToJson();

                var requestTimeout = _settingsManager.RequestTimeoutSeconds;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(requestTimeout)))
                {
                    bool success = await _modelsListService.TestConnectionAsync(
                        request.Url,
                        request.Provider,
                        request.ApiKey ?? string.Empty,
                        cts.Token
                    ).ConfigureAwait(false);

                    return new { success, error = success ? (string)null : "Failed to connect" }.ToJson();
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("TestConnectionAsync failed", ex);
                return new { success = false, error = ex.Message }.ToJson();
            }
        }
    }
}
