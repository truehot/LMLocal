using System;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.ModelsConfig;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend models configuration logic.
    /// </summary>
    public interface IModelsConfigController
    {
        Task<string> GetModelsConfigAsync();
        Task<bool> UpdateModelsConfigAsync(string modelsConfigJson);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class ModelsConfigController : IModelsConfigController
    {
        private readonly IModelsConfigManager _modelsConfigManager;

        public ModelsConfigController(IModelsConfigManager modelsConfigManager)
        {
            _modelsConfigManager = modelsConfigManager ?? throw new ArgumentNullException(nameof(modelsConfigManager));
        }

        public async Task<string> GetModelsConfigAsync()
        {
            try
            {
                var config = await _modelsConfigManager.GetAsync().ConfigureAwait(false);
                return config.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetModelsConfigAsync failed", ex);
                return "{}";
            }
        }

        public async Task<bool> UpdateModelsConfigAsync(string modelsConfigJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelsConfigJson))
                {
                    return false;
                }

                var config = modelsConfigJson.FromJson<ModelsConfigFile>();
                if (config == null)
                {
                    return false;
                }

                await _modelsConfigManager.UpdateAsync(config).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateModelsConfigAsync failed", ex);
                return false;
            }
        }
    }
}
