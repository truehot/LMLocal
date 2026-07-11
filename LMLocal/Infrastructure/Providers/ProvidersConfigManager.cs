using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.Api;

namespace LMLocal.Infrastructure.Providers
{
    /// <summary>
    /// Manager for custom providers stored in a local JSON file.
    /// </summary>
    public interface IProvidersConfigManager
    {
        Task<ProvidersConfigFile> GetAsync(CancellationToken cancellationToken = default);
        Task UpdateAsync(ProvidersConfigFile config, CancellationToken cancellationToken = default);
    }

    internal class ProvidersConfigManager : IProvidersConfigManager
    {
        private readonly string _filePath;
        private readonly IFileSystem _fileSystem;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Defines which ModelProvider enum values are default (built-in), with their stable IDs and default base URLs.
        /// </summary>
        private static readonly (int Id, ModelProvider Provider, string BaseUrl)[] DefaultProviderDefs =
        {
            (0, ModelProvider.LmStudio, "http://localhost:1234"),
            (1, ModelProvider.Ollama,   "http://localhost:11434"),
            (2, ModelProvider.Jan,      "http://localhost:1337"),
            (4, ModelProvider.LlamaCpp, "http://localhost:8080"),
            (3, ModelProvider.OpenAi,   string.Empty),
        };

        public ProvidersConfigManager(IFileSystem fileSystem, ISettingsManager settingsManager)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

            var filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    _settingsManager?.LocalAppDataFolder ?? "LMLocalChat",
                    "providers.json"
                );

            _fileSystem.ValidateFilePath(filePath);
            _fileSystem.EnsureDirectoryExistsForFile(filePath);
            _filePath = filePath;
        }

        internal static List<CustomProvider> BuildDefaultProviders()
        {
            var result = new List<CustomProvider>(DefaultProviderDefs.Length);
            foreach (var def in DefaultProviderDefs)
            {
                result.Add(new CustomProvider
                {
                    Id = def.Id,
                    ProviderName = ProviderResolver.GetDisplayName(def.Provider),
                    ProviderType = def.Provider.ToString().ToLowerInvariant(),
                    CustomBaseUrl = def.BaseUrl,
                    CustomApiKey = string.Empty
                });
            }
            return result;
        }

        private static ProvidersConfigFile GetDefaultProviders()
        {
            return new ProvidersConfigFile
            {
                DefaultProviders = BuildDefaultProviders(),
                Providers = new List<CustomProvider>()
            };
        }

        public async Task<ProvidersConfigFile> GetAsync(CancellationToken cancellationToken = default)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var defaultConfig = GetDefaultProviders();

                if (!_fileSystem.FileExists(_filePath))
                    return defaultConfig;

                var fileContent = await _fileSystem.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);

                try
                {
                    var configFile = fileContent.FromJson<ProvidersConfigFile>();
                    if (configFile != null)
                    {
                        configFile.DefaultProviders = defaultConfig.DefaultProviders;
                        return configFile;
                    }
                    return defaultConfig;
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"Error parsing providers config: {ex.Message}", ex);
                    return defaultConfig;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error reading providers config file: {ex.Message}", ex);
                return GetDefaultProviders();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task UpdateAsync(ProvidersConfigFile config, CancellationToken cancellationToken = default)
        {
            if (config == null)
            {
                config = new ProvidersConfigFile();
            }

            if (config.DefaultProviders == null || config.DefaultProviders.Count == 0)
            {
                config.DefaultProviders = GetDefaultProviders().DefaultProviders;
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
