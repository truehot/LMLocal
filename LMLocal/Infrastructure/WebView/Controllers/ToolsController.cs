using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.WebView.Models;
using LMLocal.Models;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend tools logic.
    /// </summary>
    public interface IToolsController
    {
        Task<string> GetToolsAsync();
        Task<bool> UpdateToolsAsync(string toolsConfigJson);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class ToolsController : IToolsController
    {
        private readonly IToolsConfigManager _toolsConfigManager;
        private readonly IBuiltInVsToolProvider _builtInVsToolProvider;

        public ToolsController(IToolsConfigManager toolsConfigManager, IBuiltInVsToolProvider builtInVsToolProvider)
        {
            _toolsConfigManager = toolsConfigManager ?? throw new ArgumentNullException(nameof(toolsConfigManager));
            _builtInVsToolProvider = builtInVsToolProvider ?? throw new ArgumentNullException(nameof(builtInVsToolProvider));
        }

        public Task<string> GetToolsAsync()
        {
            try
            {
                var toolsConfig = _toolsConfigManager.Current;
                var allToolDefs = _builtInVsToolProvider.GetAllToolDefinitionsUnfiltered();

                var tools = new List<ToolResponse>();

                foreach (var toolDef in allToolDefs)
                {
                    var isEnabled = true;

                    if (toolsConfig?.Tools != null && toolsConfig.Tools.Count > 0)
                    {
                        var toolConfig = toolsConfig.Tools.Find(t => t.Id == toolDef.Name);
                        if (toolConfig != null)
                        {
                            isEnabled = toolConfig.Enabled;
                        }
                    }

                    tools.Add(new ToolResponse
                    {
                        Id = toolDef.Name,
                        Name = toolDef.Name,
                        Description = toolDef.Description ?? string.Empty,
                        Enabled = isEnabled
                    });
                }

                var response = new ToolsListResponse { Tools = tools };
                return Task.FromResult(response.ToJson());
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetToolsAsync failed", ex);
                return Task.FromResult("{}");
            }
        }

        public async Task<bool> UpdateToolsAsync(string toolsConfigJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(toolsConfigJson))
                {
                    return false;
                }

                var config = toolsConfigJson.FromJson<ToolsConfigFile>();
                if (config == null)
                {
                    return false;
                }

                await _toolsConfigManager.SaveAsync(config).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateToolsAsync failed", ex);
                return false;
            }
        }
    }
}
