using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;


namespace LMLocal.Infrastructure.Mcp
{
    /// <summary>
    /// Manager for MCP (Model Context Protocol) configurations stored in a local JSON file.
    /// </summary>
    internal interface IMcpConfigManager
    {
        Task<McpConfigFile> GetAsync(CancellationToken cancellationToken = default);
        Task UpdateAsync(McpConfigFile config, CancellationToken cancellationToken = default);
    }

    internal class McpConfigManager : IMcpConfigManager
    {
        private readonly string _filePath;
        private readonly IFileSystem _fileSystem;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        public McpConfigManager(IFileSystem fileSystem, ISettingsManager settingsManager)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

            var filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    _settingsManager?.LocalAppDataFolder,
                    _settingsManager?.LocalAppMcpFileName
                );

            _fileSystem.ValidateFilePath(filePath);
            _fileSystem.EnsureDirectoryExistsForFile(filePath);
            _filePath = filePath;
        }

        public async Task<McpConfigFile> GetAsync(CancellationToken cancellationToken = default)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_fileSystem.FileExists(_filePath))
                    return new McpConfigFile();

                var fileContent = await _fileSystem.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);

                try
                {
                    var configFile = fileContent.FromJson<McpConfigFile>();
                    return configFile ?? new McpConfigFile();
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"Error parsing MCP config: {ex.Message}", ex);
                    return new McpConfigFile();
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error reading MCP config file: {ex.Message}", ex);
                return new McpConfigFile();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task UpdateAsync(McpConfigFile config, CancellationToken cancellationToken = default)
        {
            if (config == null)
                config = new McpConfigFile();

            try
            {
                var serversConfig = config.GetServersConfig();
                if (serversConfig?.Servers != null)
                {
                    foreach (var kvp in serversConfig.Servers)
                    {
                        var validationError = kvp.Value.Validate();
                        if (!string.IsNullOrEmpty(validationError))
                        {
                            throw new InvalidOperationException(
                                $"Invalid server configuration for '{kvp.Key}': {validationError}"
                            );
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                throw new InvalidOperationException($"Failed to validate MCP configuration: {ex.Message}", ex);
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
