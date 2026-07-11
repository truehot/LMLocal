using System;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Providers;
using LMLocal.Models;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend providers logic.
    /// </summary>
    public interface IProvidersController
    {
        Task<string> GetProvidersAsync();
        Task<bool> UpdateProvidersAsync(string providersConfigJson);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class ProvidersController : IProvidersController
    {
        private readonly IProvidersConfigManager _providersConfigManager;

        public ProvidersController(IProvidersConfigManager providersConfigManager)
        {
            _providersConfigManager = providersConfigManager ?? throw new ArgumentNullException(nameof(providersConfigManager));
        }

        public async Task<string> GetProvidersAsync()
        {
            try
            {
                var config = await _providersConfigManager.GetAsync().ConfigureAwait(false);
                var response = new GetProvidersResponse
                {
                    DefaultProviders = config.DefaultProviders,
                    Providers = config.Providers,
                    ProviderTypes = Infrastructure.Api.ProviderResolver.GetProviderTypes()
                };
                return response.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetProvidersAsync failed", ex);
                return "{}";
            }
        }

        public async Task<bool> UpdateProvidersAsync(string providersConfigJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(providersConfigJson))
                {
                    return false;
                }

                var config = providersConfigJson.FromJson<ProvidersConfigFile>();
                if (config == null)
                {
                    return false;
                }

                await _providersConfigManager.UpdateAsync(config).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateProvidersAsync failed", ex);
                return false;
            }
        }
    }
}
