using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Settings;
using Newtonsoft.Json.Linq;


namespace LMLocal.Infrastructure.Instructions
{
    /// <summary>
    /// Simple manager for instructions stored in a local JSON file.
    /// </summary>
    public interface IInstructionsManager
    {
        Task<string> GetAsync(CancellationToken cancellationToken = default);
        Task UpdateAsync(string jsonInstructions, CancellationToken cancellationToken = default);
        Task UpdateSelectedTabAsync(string selectedTabId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds the tab id (as string) of the first enabled instruction tab whose displayName matches the specified name (case-insensitive). Returns null if not found or disabled.
        /// </summary>
        Task<string> GetInstructionTabIdByDisplayNameAsync(string displayName, CancellationToken cancellationToken = default);
    }

    internal class InstructionsManager : IInstructionsManager
    {
        private readonly string _filePath;
        private readonly IFileSystem _fileSystem;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        public InstructionsManager(IFileSystem fileSystem, ISettingsManager settingsManager)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

            var filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    _settingsManager?.LocalAppDataFolder ?? "LMLocalChat",
                    _settingsManager?.LocalAppInstructionsFileName ?? "instructions.json"
                );


            _fileSystem.ValidateFilePath(filePath);
            _fileSystem.EnsureDirectoryExistsForFile(filePath);
            _filePath = filePath;
        }

        public async Task<string> GetAsync(CancellationToken cancellationToken = default)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_fileSystem.FileExists(_filePath))
                    return "{}";

                return await _fileSystem.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("Error reading instructions: " + ex.Message);
                return "{}";
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task UpdateAsync(string jsonInstructions, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(jsonInstructions))
                jsonInstructions = "{}";

            try
            {
                JObject.Parse(jsonInstructions);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid JSON format: {ex.Message}", ex);
            }

            byte[] data = Encoding.UTF8.GetBytes(jsonInstructions);

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

        public async Task UpdateSelectedTabAsync(string selectedTabId, CancellationToken cancellationToken = default)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string jsonContent = "{}";
                if (_fileSystem.FileExists(_filePath))
                {
                    jsonContent = await _fileSystem.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
                }

                JObject jObject = JObject.Parse(jsonContent);
                jObject["selectedTabId"] = selectedTabId;

                string updatedJson = jObject.ToString();
                byte[] data = Encoding.UTF8.GetBytes(updatedJson);

                await _fileSystem.WriteAllBytesAsync(_filePath, data, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<string> GetInstructionTabIdByDisplayNameAsync(string displayName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return null;

            string json = await GetAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                JObject jObject = JObject.Parse(json);
                if (!(jObject["tabs"] is JArray tabs))
                    return null;

                foreach (JToken tab in tabs)
                {
                    string name = tab["displayName"]?.Value<string>();
                    bool enabled = tab["enabled"]?.Value<bool>() ?? false;
                    string id = tab["id"]?.Value<string>();

                    if (enabled &&
                        !string.IsNullOrWhiteSpace(name) &&
                        !string.IsNullOrWhiteSpace(id) &&
                        string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase))
                    {
                        return id;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("Error searching instructions by displayName: " + ex.Message);
                return null;
            }
        }
    }
}
