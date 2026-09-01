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

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Manager for built-in tools configuration stored in a local JSON file.
    /// Provides cached access to tools configuration, similar to SettingsManager.
    /// </summary>
    public interface IToolsConfigManager
    {
        /// <summary>
        /// Gets the current cached tools configuration. Must call LoadAsync first.
        /// </summary>
        ToolsConfigFile Current { get; }

        /// <summary>
        /// Loads tools configuration from disk and caches it.
        /// </summary>
        Task<ToolsConfigFile> LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves the provided configuration to disk and updates the cache.
        /// </summary>
        Task SaveAsync(ToolsConfigFile config, CancellationToken cancellationToken = default);
    }

    internal class ToolsConfigManager : IToolsConfigManager, IDisposable
    {
        private readonly string _filePath;
        private readonly IFileSystem _fileSystem;
        private readonly SemaphoreSlim _saveSemaphore = new SemaphoreSlim(1, 1);
        private ToolsConfigFile _cachedConfig;
        private bool _isLoaded;
        private readonly object _lock = new object();
        private bool _disposed;

        public ToolsConfigManager(IFileSystem fileSystem, ISettingsManager settingsManager)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            if (settingsManager == null)
                throw new ArgumentNullException(nameof(settingsManager));

            var filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    settingsManager.LocalAppDataFolder ?? "LMLocalChat",
                    "tools.json"
            );

            _fileSystem.ValidateFilePath(filePath);
            _fileSystem.EnsureDirectoryExistsForFile(filePath);
            _filePath = filePath;
        }

        /// <summary>
        /// Gets the current cached tools configuration.
        /// Throws if LoadAsync has not been called.
        /// </summary>
        public ToolsConfigFile Current
        {
            get
            {
                ThrowIfDisposed();
                if (!_isLoaded)
                    throw new InvalidOperationException("Tools configuration not loaded. Call LoadAsync first.");
                return _cachedConfig;
            }
        }

        private static ToolsConfigFile GetDefaultTools()
        {
            return new ToolsConfigFile
            {
                Tools = new List<ToolConfig>()
            };
        }

        public async Task<ToolsConfigFile> LoadAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            try
            {
                var loaded = await ReadAndUpdateCacheAsync(cancellationToken).ConfigureAwait(false);
                return loaded;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"Failed to read tools configuration file; using defaults: {ex.Message}");
                lock (_lock)
                {
                    _cachedConfig = GetDefaultTools();
                    _isLoaded = true;
                }
                return _cachedConfig;
            }
        }

        private async Task<ToolsConfigFile> ReadAndUpdateCacheAsync(CancellationToken cancellationToken)
        {
            ToolsConfigFile loaded;
            var defaultConfig = GetDefaultTools();

            if (!_fileSystem.FileExists(_filePath))
            {
                loaded = defaultConfig;
            }
            else
            {
                var fileContent = await _fileSystem.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(fileContent))
                {
                    loaded = defaultConfig;
                }
                else
                {
                    try
                    {
                        loaded = fileContent.FromJson<ToolsConfigFile>() ?? defaultConfig;
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Warn($"Failed to parse tools.json: {ex.Message}");
                        loaded = defaultConfig;
                    }
                }
            }

            lock (_lock)
            {
                _cachedConfig = loaded;
                _isLoaded = true;
            }

            return loaded;
        }

        public async Task SaveAsync(ToolsConfigFile config, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            cancellationToken.ThrowIfCancellationRequested();

            await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var json = config.ToJson();
                byte[] data = Encoding.UTF8.GetBytes(json);
                await _fileSystem.WriteAllBytesAsync(_filePath, data, cancellationToken).ConfigureAwait(false);

                lock (_lock)
                {
                    _cachedConfig = config;
                    _isLoaded = true;
                }

                InternalLogger.Debug("Tools configuration saved successfully.");
            }
            finally
            {
                _saveSemaphore.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _saveSemaphore?.Dispose(); } catch { }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ToolsConfigManager));
        }
    }
}
