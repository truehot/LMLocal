using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.RecentModels;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend "recent models" logic.
    /// </summary>
    public interface IRecentModelsController
    {
        /// <summary>
        /// JSON: { "entries": [ { providerType, providerId, modelId, modelName, lastUsedUtc }, ... ] }
        /// </summary>
        Task<string> GetRecentModelsAsync();

        /// <summary>
        /// payload: { providerType, providerId, modelId, modelName }. Always true (errors are logged).
        /// </summary>
        Task<bool> RecordModelUsageAsync(string payloadJson);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class RecentModelsController : IRecentModelsController
    {
        private readonly IRecentModelsManager _recentModelsManager;

        internal RecentModelsController(IRecentModelsManager recentModelsManager)
        {
            _recentModelsManager = recentModelsManager ?? throw new ArgumentNullException(nameof(recentModelsManager));
        }

        public async Task<string> GetRecentModelsAsync()
        {
            try
            {
                var entries = await _recentModelsManager.GetForCurrentProviderAsync().ConfigureAwait(false);
                var file = new RecentModelsFile
                {
                    Entries = entries as List<RecentModelEntry> ?? new List<RecentModelEntry>(entries)
                };
                return file.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetRecentModelsAsync failed", ex);
                return new RecentModelsFile().ToJson();
            }
        }

        public async Task<bool> RecordModelUsageAsync(string payloadJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(payloadJson))
                    return true;

                var request = payloadJson.FromJson<RecentModelUsageRequest>();
                if (request == null || string.IsNullOrWhiteSpace(request.ModelId))
                    return true;

                await _recentModelsManager.RecordUsageAsync(
                    request.ProviderType,
                    request.ProviderId,
                    request.ModelId,
                    request.ModelName).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("RecordModelUsageAsync failed", ex);
                return true;
            }
        }
    }
}
