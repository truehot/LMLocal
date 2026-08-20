using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Security;
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
        Task<string> TestCertificateAsync(string payload);
        Task<bool> SetAiToolsAsync(string json);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class SettingsController : ISettingsController
    {
        private readonly ISettingsManager _settingsManager;
        private readonly ITestConnectionService _testConnectionService;
        private readonly ICertificatePathValidator _certificatePathValidator;

        internal SettingsController(ISettingsManager settingsManager, ITestConnectionService testConnectionService, ICertificatePathValidator certificatePathValidator)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _testConnectionService = testConnectionService ?? throw new ArgumentNullException(nameof(testConnectionService));
            _certificatePathValidator = certificatePathValidator ?? throw new ArgumentNullException(nameof(certificatePathValidator));
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

        /// <summary>
        /// Updates only the AI Tools settings (EnableAiTools / EnableAiWriteTools).
        /// Expects JSON: { "mode": "none" | "readonly" | "readwrite" }.
        /// </summary>
        public async Task<bool> SetAiToolsAsync(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                var request = json.FromJson<SetAiToolsRequest>();
                if (request == null || string.IsNullOrWhiteSpace(request.Mode))
                    return false;

                await _settingsManager.SetAiToolsModeAsync(request.Mode).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("SetAiToolsAsync failed", ex);
                return false;
            }
        }

        public async Task<string> TestConnectionAsync(string payload)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(payload))
                    return ErrorResponse("Invalid parameters");

                var request = payload.FromJson<TestConnectionRequest>();
                if (request == null || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Url))
                    return ErrorResponse("Provider and URL are required");

                var requestTimeout = _settingsManager.RequestTimeoutSeconds;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(requestTimeout)))
                {
                    TestConnectionResult result = await _testConnectionService.TestAsync(
                        request.Provider,
                        request.Url,
                        request.ApiKey ?? string.Empty,
                        request.CertificatePath,
                        cts.Token
                    ).ConfigureAwait(false);

                    return new TestConnectionResponse
                    {
                        Success = result.Success,
                        Error = result.Error == null ? null : new ErrorInfo { Message = result.Error }
                    }.ToJson();
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("TestConnectionAsync failed", ex);
                return ErrorResponse(ex.Message);
            }
        }

        /// <summary>
        /// Validates that the configured certificate file can be loaded and reports its thumbprint.
        /// </summary>
        public Task<string> TestCertificateAsync(string payload)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(payload))
                    return Task.FromResult(ErrorResponse("Certificate path is required"));

                var request = payload.FromJson<TestCertificateRequest>();
                if (request == null || string.IsNullOrWhiteSpace(request.Path))
                    return Task.FromResult(ErrorResponse("Certificate path is required"));

                CertificateInfo info = _certificatePathValidator.ValidateOrThrow(request.Path);

                return Task.FromResult(new TestCertificateResponse
                {
                    Success = true,
                    Thumbprint = info.Thumbprint
                }.ToJson());
            }
            catch (Exception ex)
            {
                InternalLogger.Error("TestCertificateAsync failed", ex);
                return Task.FromResult(ErrorResponse(ex.Message));
            }
        }

        /// <summary>
        /// Builds a failure response matching the frontend contract: error is an object with a "message" property (the toast in settings.dialog.js reads result.error.message).
        /// </summary>
        private static string ErrorResponse(string message)
        {
            return new TestConnectionResponse
            {
                Success = false,
                Error = new ErrorInfo { Message = message }
            }.ToJson();
        }
    }
}
