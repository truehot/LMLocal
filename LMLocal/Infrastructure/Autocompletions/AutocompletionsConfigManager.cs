using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;

namespace LMLocal.Infrastructure.Autocompletions
{
    /// <summary>
    /// Manager for autocomplete configuration stored in a local JSON file.
    /// </summary>
    public interface IAutocompletionsConfigManager
    {
        Task<AutocompletionsConfig> GetAsync(CancellationToken cancellationToken = default);
        Task UpdateAsync(AutocompletionsConfig config, CancellationToken cancellationToken = default);
    }

    internal class AutocompletionsConfigManager : IAutocompletionsConfigManager
    {
        private readonly string _filePath;
        private readonly IFileSystem _fileSystem;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        private static readonly AutocompletionsConfig DefaultConfig = new AutocompletionsConfig
        {
            Enabled = false,
            ProviderId = 0,
            ProviderType = "lmstudio",
            ModelId = string.Empty,
            DebounceDelayMs = 300
        };

        public AutocompletionsConfigManager(IFileSystem fileSystem, ISettingsManager settingsManager)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

            var filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    _settingsManager?.LocalAppDataFolder ?? "LMLocalChat",
                    "autocompletions.json"
                );

            _fileSystem.ValidateFilePath(filePath);
            _fileSystem.EnsureDirectoryExistsForFile(filePath);
            _filePath = filePath;
        }

        public async Task<AutocompletionsConfig> GetAsync(CancellationToken cancellationToken = default)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_fileSystem.FileExists(_filePath))
                    return DefaultConfig;

                var fileContent = await _fileSystem.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);

                try
                {
                    var config = fileContent.FromJson<AutocompletionsConfig>();
                    if (config != null)
                    {
                        return config;
                    }
                    return DefaultConfig;
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"Error parsing autocompletions config: {ex.Message}", ex);
                    return DefaultConfig;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error reading autocompletions config file: {ex.Message}", ex);
                return DefaultConfig;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task UpdateAsync(AutocompletionsConfig config, CancellationToken cancellationToken = default)
        {
            if (config == null)
            {
                config = DefaultConfig;
            }

            var json = config.ToJson();
            byte[] data = Encoding.UTF8.GetBytes(json);

            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _fileSystem.WriteAllBytesAsync(_filePath, data, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _fileLock.Release();
            }
        }
    }
}
