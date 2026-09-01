using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Application.Abstractions.Ports;

namespace LMLocal.Infrastructure.RecentModels
{
    /// <summary>
    /// Manager for "recently used models" (provider + model pairs) stored in a local JSON file.
    /// </summary>
    public interface IRecentModelsManager
    {
        /// <summary>
        /// Returns entries for the currently selected provider only, newest first.
        /// </summary>
        Task<IReadOnlyList<RecentModelEntry>> GetForCurrentProviderAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically creates or moves-to-top the entry for the given provider/model pair, pdating lastUsedUtc and modelName, then trims the list to the cap.
        /// </summary>
        Task RecordUsageAsync(string providerType, int? providerId, string modelId, string modelName, CancellationToken cancellationToken = default);
    }

    internal class RecentModelsManager : IRecentModelsManager
    {
        private const int MaxEntries = 50;

        private readonly string _filePath;
        private readonly IFileSystem _fileSystem;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        public RecentModelsManager(IFileSystem fileSystem, ISettingsManager settingsManager)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

            var filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    _settingsManager?.LocalAppDataFolder ?? "LMLocalChat",
                    "recent-models.json"
                );

            _fileSystem.ValidateFilePath(filePath);
            _fileSystem.EnsureDirectoryExistsForFile(filePath);
            _filePath = filePath;
        }

        public async Task<IReadOnlyList<RecentModelEntry>> GetForCurrentProviderAsync(CancellationToken cancellationToken = default)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var file = await ReadFileAsync(cancellationToken).ConfigureAwait(false);
                if (file == null || file.Entries == null || file.Entries.Count == 0)
                    return new List<RecentModelEntry>();

                var currentType = _settingsManager?.Current?.Provider;
                var currentId = _settingsManager?.Current?.ProviderId;

                var result = file.Entries
                    .Where(e => e != null
                                && string.Equals(e.ProviderType, currentType, StringComparison.OrdinalIgnoreCase)
                                && Nullable.Equals(e.ProviderId, currentId))
                    .OrderByDescending(e => e.LastUsedUtc)
                    .ToList();

                return result;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Error reading recent models: " + ex.Message, ex);
                return new List<RecentModelEntry>();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task RecordUsageAsync(string providerType, int? providerId, string modelId, string modelName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(providerType) || string.IsNullOrWhiteSpace(modelId))
                return;

            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var file = await ReadFileAsync(cancellationToken).ConfigureAwait(false) ?? new RecentModelsFile();
                if (file.Entries == null)
                    file.Entries = new List<RecentModelEntry>();

                var existing = file.Entries.FirstOrDefault(e => e != null
                    && string.Equals(e.ProviderType, providerType, StringComparison.OrdinalIgnoreCase)
                    && Nullable.Equals(e.ProviderId, providerId)
                    && string.Equals(e.ModelId, modelId, StringComparison.Ordinal));

                if (existing != null)
                {
                    file.Entries.Remove(existing);
                }
                else
                {
                    existing = new RecentModelEntry
                    {
                        ProviderType = providerType,
                        ProviderId = providerId,
                        ModelId = modelId
                    };
                }

                existing.ModelName = string.IsNullOrWhiteSpace(modelName) ? modelId : modelName;
                existing.LastUsedUtc = DateTimeOffset.UtcNow;

                file.Entries.Insert(0, existing);

                if (file.Entries.Count > MaxEntries)
                {
                    file.Entries = file.Entries.Take(MaxEntries).ToList();
                }

                var json = file.ToJson();
                var data = Encoding.UTF8.GetBytes(json);
                await _fileSystem.WriteAllBytesAsync(_filePath, data, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Error recording recent model usage: " + ex.Message, ex);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <summary>
        /// Reads and parses recent-models.json. Missing or corrupt file yields an empty document, never throws.
        /// </summary>
        private async Task<RecentModelsFile> ReadFileAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_fileSystem.FileExists(_filePath))
                    return null;

                var content = await _fileSystem.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(content))
                    return null;

                var file = content.FromJson<RecentModelsFile>();
                return file ?? new RecentModelsFile();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Error parsing recent models file: " + ex.Message, ex);
                return new RecentModelsFile();
            }
        }
    }
}
