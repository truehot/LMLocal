using System;
using System.ComponentModel.DataAnnotations;
using LMLocal.Services;

namespace LMLocal.Models
{
    public class AppSettings : IEquatable<AppSettings>
    {
        /// <summary>
        /// Base URL of the LLM backend. Used to build HTTP requests.
        /// </summary>
        [Required(ErrorMessage = "LmStudioBaseUrl is required.")]
        [Url(ErrorMessage = "LmStudioBaseUrl must be a valid absolute URL.")]
        public string LmStudioBaseUrl { get; set; } = "http://localhost:1234";

        /// <summary>
        /// When true, the application will attempt to connect to LM Studio on startup.
        /// </summary>
        public bool AutoLoadOnStartup { get; set; } = true;

        /// <summary>
        /// When true, message history is cleaned of markdown and trimmed to reduce token usage.
        /// </summary>
        public bool EnableHistoryCompression { get; set; } = false;

        /// <summary>
        /// When true, older conversation history is summarized into a concise summary as context limits are approached.
        /// </summary>
        public bool EnableHistoryCompaction { get; set; } = false;

        /// <summary>
        /// UI theme preference for the application.
        /// </summary>
        [Required(ErrorMessage = "Theme is required.")]
        [EnumDataType(typeof(AppTheme), ErrorMessage = "Theme contains an invalid value.")]
        public AppTheme Theme { get; set; } = AppTheme.Dark;

        /// <summary>
        /// Stream inactivity timeout in seconds. 0 = disabled (infinite timeout). Default = 20 seconds.
        /// </summary>
        [Range(0, 100, ErrorMessage = "StreamInactivityTimeoutSeconds must be between 0 and 100.")]
        public int StreamInactivityTimeoutSeconds { get; set; } = 20;

        /// <summary>
        /// When true, chat history is saved to disk in ChatHistory folder.
        /// </summary>
        public bool EnableChatLogging { get; set; } = false;

        /// <summary>
        /// API key for authenticating to remote services. Optional for most local servers.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// When true, AI Tools are enabled (allows the AI to inspect the open solution/files).
        /// </summary>
        public bool EnableAiTools { get; set; } = false;

        /// <summary>
        /// When true, large code blocks are collapsed to limit height and keep chat history clean.
        /// </summary>
        public bool EnableCodeCollapse { get; set; } = false;

        /// <summary>
        /// AI provider backend: "lmstudio" (local), "ollama" (local), or "openai" (custom compatible).
        /// </summary>
        [Required(ErrorMessage = "Provider is required.")]
        public string Provider { get; set; } = "lmstudio";

        public bool Equals(AppSettings other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return string.Equals(LmStudioBaseUrl, other.LmStudioBaseUrl, StringComparison.OrdinalIgnoreCase)
                && AutoLoadOnStartup == other.AutoLoadOnStartup
                && EnableHistoryCompression == other.EnableHistoryCompression
                && EnableHistoryCompaction == other.EnableHistoryCompaction
                && Theme == other.Theme
                && StreamInactivityTimeoutSeconds == other.StreamInactivityTimeoutSeconds
                && EnableChatLogging == other.EnableChatLogging
                && EnableAiTools == other.EnableAiTools
                && EnableCodeCollapse == other.EnableCodeCollapse
                && string.Equals(ApiKey, other.ApiKey, StringComparison.Ordinal)
                && string.Equals(Provider, other.Provider, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => Equals(obj as AppSettings);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (LmStudioBaseUrl != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(LmStudioBaseUrl) : 0);
                hash = hash * 23 + AutoLoadOnStartup.GetHashCode();
                hash = hash * 23 + EnableHistoryCompression.GetHashCode();
                hash = hash * 23 + EnableHistoryCompaction.GetHashCode();
                hash = hash * 23 + Theme.GetHashCode();
                hash = hash * 23 + StreamInactivityTimeoutSeconds.GetHashCode();
                hash = hash * 23 + EnableChatLogging.GetHashCode();
                hash = hash * 23 + EnableAiTools.GetHashCode();
                hash = hash * 23 + EnableCodeCollapse.GetHashCode();
                hash = hash * 23 + (ApiKey != null ? StringComparer.Ordinal.GetHashCode(ApiKey) : 0);
                hash = hash * 23 + (Provider != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(Provider) : 0);
                return hash;
            }
        }
    }
}
