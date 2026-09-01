using System.Collections.Generic;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// All SubAgent settings for one run, resolved by the caller so the service itself does not re-read json.
    /// </summary>
    public class SubAgentRunRequest
    {
        public string AgentName { get; set; }
        public string Prompt { get; set; }
        public string ProviderType { get; set; }
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public string System { get; set; }
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public int? TimeoutSeconds { get; set; }
        public int? MaxRounds { get; set; }
        public List<string> AllowedTools { get; set; }
        public IReadOnlyCollection<string> ExcludedAgentNames { get; set; }
    }
}
