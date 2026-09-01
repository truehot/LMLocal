using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Application.Abstractions.Ports;

namespace LMLocal.Infrastructure.ModelsConfig
{
    /// <summary>
    /// Manager for custom model profiles stored in a local JSON file.
    /// </summary>
    public interface IModelsConfigManager
    {
        Task<ModelsConfigFile> GetAsync(CancellationToken cancellationToken = default);
        Task UpdateAsync(ModelsConfigFile config, CancellationToken cancellationToken = default);
    }

    internal class ModelsConfigManager : IModelsConfigManager
    {
        private readonly string _filePath;
        private readonly IFileSystem _fileSystem;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        public ModelsConfigManager(IFileSystem fileSystem, ISettingsManager settingsManager)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

            var filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    _settingsManager?.LocalAppDataFolder ?? "LMLocalChat",
                    "models.config.json"
                );

            _fileSystem.ValidateFilePath(filePath);
            _fileSystem.EnsureDirectoryExistsForFile(filePath);
            _filePath = filePath;
        }

        private static ModelsConfigFile GetDefaultModels()
        {
            return new ModelsConfigFile
            {
                Models = new List<ModelDefinition>()
            };
        }

        public async Task<ModelsConfigFile> GetAsync(CancellationToken cancellationToken = default)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var defaultConfig = GetDefaultModels();

                if (!_fileSystem.FileExists(_filePath))
                    return defaultConfig;

                var fileContent = await _fileSystem.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);

                try
                {
                    var configFile = fileContent.FromJson<ModelsConfigFile>();
                    if (configFile != null)
                    {
                        if (configFile.Models == null)
                            configFile.Models = defaultConfig.Models;
                        return configFile;
                    }
                    return defaultConfig;
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"Error parsing models config: {ex.Message}", ex);
                    return defaultConfig;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error reading models config file: {ex.Message}", ex);
                return GetDefaultModels();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task UpdateAsync(ModelsConfigFile config, CancellationToken cancellationToken = default)
        {
            if (config == null)
            {
                config = new ModelsConfigFile();
            }

            if (config.Models == null)
            {
                config.Models = GetDefaultModels().Models;
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
