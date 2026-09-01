using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Application.Abstractions.Ports;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend models logic.
    /// </summary>
    public interface IModelsController
    {
        Task<string> ListModelsAsync();
        Task<bool> SetActiveModelAsync(string modelId, int contextLength);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class ModelsController : IModelsController
    {
        private readonly ISettingsManager _settingsManager;
        private readonly IModelsListService _modelsListService;
        private readonly IActiveModelContext _activeModelContext;

        internal ModelsController(
            ISettingsManager settingsManager,
            IModelsListService modelsListService,
            IActiveModelContext activeModelContext)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _modelsListService = modelsListService ?? throw new ArgumentNullException(nameof(modelsListService));
            _activeModelContext = activeModelContext ?? throw new ArgumentNullException(nameof(activeModelContext));
        }

        /// <summary>
        /// Returns list of available models from different providers and activeModel if any.
        /// </summary>
        public async Task<string> ListModelsAsync()
        {
            var requestTimeout = _settingsManager.RequestTimeoutSeconds;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(requestTimeout)))
                {
                    var response = await _modelsListService.ListModelsAsync(_activeModelContext.CurrentModelId, cts.Token).ConfigureAwait(false);
                    return response == null ? "{}" : response.ToJson();
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ListModelsAsync failed", ex);
                return new { Error = "Failed to list models: " + ex.Message }.ToJson();
            }
        }

        /// <summary>
        /// Sets the active model and its max context length. If contextLength is not provided or &lt;= 0, defaults to 16384.
        /// </summary>
        public Task<bool> SetActiveModelAsync(string modelId, int contextLength)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelId)) return Task.FromResult(false);

                var maxContext = contextLength <= 0 ? 16384 : contextLength;
                _activeModelContext.SetActiveModel(modelId, maxContext);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("SetActiveModelAsync failed", ex);
                return Task.FromResult(false);
            }
        }
    }
}
