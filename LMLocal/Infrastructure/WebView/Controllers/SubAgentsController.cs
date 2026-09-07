using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.WebView.Models;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and the SubAgents configuration.
    /// </summary>
    public interface ISubAgentsController
    {
        /// <summary>
        /// Returns the list of configured SubAgents (read-only details + enabled flag).
        /// </summary>
        Task<string> GetSubAgentsAsync();

        /// <summary>
        /// Merges the enabled/disabled flags from the dialog into the stored configuration.
        /// </summary>
        Task<string> UpdateSubAgentsAsync(string subAgentsConfigJson);

        /// <summary>
        /// Replaces the whole stored SubAgents configuration with the provided full json.
        /// </summary>
        Task<string> ReplaceSubAgentsConfigAsync(string subAgentsConfigJson);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class SubAgentsController : ISubAgentsController
    {
        private readonly ISubAgentsConfigManager _configManager;

        public SubAgentsController(ISubAgentsConfigManager configManager)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        }

        public async Task<string> GetSubAgentsAsync()
        {
            try
            {
                var config = await _configManager.GetAsync().ConfigureAwait(false);
                var response = new SubAgentsListResponse();

                foreach (var agent in config.Agents)
                {
                    response.Agents.Add(new SubAgentResponse
                    {
                        Id = agent.Id,
                        DisplayName = agent.DisplayName,
                        Description = agent.Description,
                        ProviderType = agent.ProviderType,
                        CustomBaseUrl = agent.CustomBaseUrl,
                        Model = agent.Model,
                        Temperature = agent.Temperature,
                        TimeoutSeconds = agent.TimeoutSeconds,
                        MaxRounds = agent.MaxRounds,
                        MaxTokens = agent.MaxTokens,
                        ReasoningEffort = agent.ReasoningEffort,

                        AllowedTools = agent.AllowedTools != null
                            ? new List<string>(agent.AllowedTools)
                            : new List<string>(),
                        Enabled = agent.Enabled
                    });
                }

                return response.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetSubAgentsAsync failed", ex);
                return new SubAgentsListResponse
                {
                    Success = false,
                    Error = new SubAgentsErrorResponse { Message = ex.Message }
                }.ToJson();
            }
        }

        public async Task<string> UpdateSubAgentsAsync(string subAgentsConfigJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(subAgentsConfigJson))
                {
                    return Failure("Update payload is empty.");
                }

                var request = subAgentsConfigJson.FromJson<SubAgentsUpdateRequest>();
                if (request?.Agents == null)
                {
                    return Failure("Update payload is invalid: 'agents' is required.");
                }

                IReadOnlyList<string> errors;
                if (HasRootDefaults(request))
                {
                    var defaults = new SubAgentDefaults
                    {
                        ProviderType = request.ProviderType,
                        CustomBaseUrl = request.CustomBaseUrl,
                        CustomApiKey = request.CustomApiKey,
                        Model = request.Model
                    };

                    errors = await _configManager.UpdateEnabledFlagsAsync(request.Agents, defaults: defaults).ConfigureAwait(false);
                }
                else
                {
                    errors = await _configManager.UpdateEnabledFlagsAsync(request.Agents).ConfigureAwait(false);
                }

                if (errors.Count > 0)
                {
                    InternalLogger.Warn(
                        $"SubAgentsController: rejected config update: {string.Join("; ", errors)}.");
                    return Failure("SubAgents configuration is invalid: " + string.Join("; ", errors));
                }

                return new SubAgentsUpdateResponse { Success = true }.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateSubAgentsAsync failed", ex);
                return Failure(ex.Message);
            }
        }

        public async Task<string> ReplaceSubAgentsConfigAsync(string subAgentsConfigJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(subAgentsConfigJson))
                {
                    return Failure("Replace payload is empty.");
                }

                var config = subAgentsConfigJson.FromJson<SubAgentsConfig>();
                if (config?.Agents == null || config.Agents.Count == 0)
                {
                    return Failure("Replace payload is invalid: at least one agent is required.");
                }

                await _configManager.SaveAsync(config).ConfigureAwait(false);
                return new SubAgentsUpdateResponse { Success = true }.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ReplaceSubAgentsConfigAsync failed", ex);
                return Failure(ex.Message);
            }
        }

        /// <summary>
        /// True when the "Use Active Model" root defaults were provided in the payload.
        /// </summary>
        private static bool HasRootDefaults(SubAgentsUpdateRequest request)
        {
            return request != null
                && (!string.IsNullOrWhiteSpace(request.ProviderType)
                    || !string.IsNullOrWhiteSpace(request.CustomBaseUrl)
                    || !string.IsNullOrWhiteSpace(request.CustomApiKey)
                    || !string.IsNullOrWhiteSpace(request.Model));
        }

        private static string Failure(string message)
        {
            return new SubAgentsUpdateResponse
            {
                Success = false,
                Error = new SubAgentsErrorResponse { Message = message }
            }.ToJson();
        }
    }
}
