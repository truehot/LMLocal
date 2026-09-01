using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Manages application settings persisted to a local JSON file under
    /// the user's LocalApplicationData folder and provides cached access to those settings.
    /// Also provides access to default configuration values.
    /// </summary>
    public interface ISettingsManager
    {
        AppSettings Current { get; }
        Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
        Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
        Task SetAiToolsModeAsync(string mode, CancellationToken cancellationToken = default);
        Task SetSubAgentsEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
        event Action<AppSettings> SettingsChanged;

        // Default configuration values
        string ApplicationName { get; }
        string SettingsFileName { get; }
        string LocalAppDataFolder { get; }
        string LocalAppSettingFileName { get; }
        string LocalAppInstructionsFileName { get; }
        string LocalAppMcpFileName { get; }
        string WebViewUserDataFolder { get; }
        string ChatHistoryFolder { get; }
        string ChatHistoryFileLabel { get; }
        string HtmlResourcePath { get; }
        string VirtualHostName { get; }
        string SystemPrompt { get; }
        int BatchIntervalMs { get; }
        int WindowSeconds { get; }
        int RequestTimeoutSeconds { get; }
        string SnapshotFolder { get; }
        string LocalSnapshotsFileName { get; }
        string UserAgent { get; }
        string AssistantPlaceholder { get; }
    }
}
