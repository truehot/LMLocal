using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure;

namespace LMLocal.Infrastructure.Settings
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
        string ChatHistoryFilePrefix { get; }
        string HtmlResourcePath { get; }
        string VirtualHostName { get; }
        string SystemPrompt { get; }
        int BatchIntervalMs { get; }
        int WindowSeconds { get; }
        int RequestTimeoutSeconds { get; }
    }

    internal class SettingsManager : ISettingsManager, IDisposable
    {
        private readonly string _filePath;
        private readonly IFileSystem _fileSystem;
        private AppSettings _cachedSettings;
        private readonly object _lock = new object();
        private bool _isLoaded;
        private readonly SemaphoreSlim _saveSemaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        // Default configuration constants
        private const string DefaultApplicationName = "LMLocalChat";
        private const string DefaultSettingsFileName = "settings.json";
        private const string DefaultLocalAppDataFolder = "LMLocalChat";
        private const string DefaultLocalAppSettingFileName = "settings.json";
        private const string DefaultLocalAppInstructionsFileName = "instructions.json";
        private const string DefaultLocalAppMcpFileName = "mcp.json";
        private const string DefaultWebViewUserDataFolder = "WebViewData";
        private const string DefaultChatHistoryFolder = "ChatHistory";
        private const string DefaultChatHistoryFilePrefix = "chat_";
        private const string DefaultHtmlResourcePath = "Resources/app.html";
        private const string DefaultVirtualHostName = "app.local";
        private const string DefaultSystemPrompt = "You are an expert Senior Software Engineer and Architect. You are integrated as a plugin into Visual Studio. When calling tools, you MUST extract relevant values from the user's input and fill all 'required' parameters.";
        private const int DefaultBatchIntervalMs = 100;
        private const int DefaultWindowSeconds = 5;
        private const int DefaultRequestTimeoutSeconds = 105;
        public event Action<AppSettings> SettingsChanged;

        public SettingsManager()
            : this(
                  Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    DefaultLocalAppDataFolder,
                    DefaultLocalAppSettingFileName
                  ),
                  null
              )
        {
        }

        public SettingsManager(string filePath)
            : this(filePath, null)
        {
        }

        public SettingsManager(string filePath, IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? new DefaultFileSystem();
            _fileSystem.ValidateFilePath(filePath);
            _fileSystem.EnsureDirectoryExistsForFile(filePath);
            _filePath = filePath;
        }

        /// <summary>
        /// Gets the current application settings.
        /// </summary>
        public AppSettings Current
        {
            get
            {
                ThrowIfDisposed();
                if (!_isLoaded)
                    throw new InvalidOperationException("Settings not loaded. Call LoadAsync first.");
                return _cachedSettings;
            }
        }

        /// <summary>
        /// Default configuration values (implementing default constants).
        /// </summary>
        public string ApplicationName => DefaultApplicationName;
        public string SettingsFileName => DefaultSettingsFileName;
        public string LocalAppDataFolder => DefaultLocalAppDataFolder;
        public string LocalAppSettingFileName => DefaultLocalAppSettingFileName;
        public string LocalAppInstructionsFileName => DefaultLocalAppInstructionsFileName;
        public string LocalAppMcpFileName => DefaultLocalAppMcpFileName;
        public string WebViewUserDataFolder => DefaultWebViewUserDataFolder;
        public string ChatHistoryFolder => DefaultChatHistoryFolder;
        public string ChatHistoryFilePrefix => DefaultChatHistoryFilePrefix;
        public string HtmlResourcePath => DefaultHtmlResourcePath;
        public string VirtualHostName => DefaultVirtualHostName;
        public string SystemPrompt => DefaultSystemPrompt;
        public int BatchIntervalMs => DefaultBatchIntervalMs;
        public int WindowSeconds => DefaultWindowSeconds;
        public int RequestTimeoutSeconds => DefaultRequestTimeoutSeconds;

        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (settings == null) throw new ArgumentNullException(nameof(settings));

            EnsureValidSettings(settings);

            if (!TryValidateSettings(settings, out var _errors))
            {
                var msg = "Settings validation failed: " + string.Join("; ", _errors);
                InternalLogger.Error(msg);
                throw new ValidationException(msg);
            }

            await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            string tempPath = null;
            try
            {
                string json = settings.ToJsonIndented();
                tempPath = _filePath + ".tmp";
                byte[] data = Encoding.UTF8.GetBytes(json);

                await _fileSystem.WriteAllBytesAsync(tempPath, data, cancellationToken).ConfigureAwait(false);

                bool replaced = false;
                if (_fileSystem.FileExists(_filePath))
                {
                    try
                    {
                        _fileSystem.Replace(tempPath, _filePath);
                        replaced = true;
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Error("Replace failed, will attempt backup-and-move.", ex);
                    }
                }

                if (!replaced)
                {
                    string backupPath = _filePath + ".bak";
                    bool hasBackup = false;
                    try
                    {
                        if (_fileSystem.FileExists(_filePath))
                        {
                            _fileSystem.Move(_filePath, backupPath);
                            hasBackup = true;
                        }

                        _fileSystem.Move(tempPath, _filePath);

                        if (hasBackup)
                        {
                            try { _fileSystem.Delete(backupPath); }
                            catch (Exception ex) { InternalLogger.Error("Failed to delete backup file.", ex); }
                        }
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Error("Fallback backup-and-move failed while saving settings.", ex);

                        try
                        {
                            if (_fileSystem.FileExists(backupPath) && !_fileSystem.FileExists(_filePath))
                            {
                                _fileSystem.Move(backupPath, _filePath);
                            }
                        }
                        catch (Exception ex2)
                        {
                            InternalLogger.Error("Failed to restore backup after failed save.", ex2);
                        }

                        throw;
                    }
                }

                AppSettings previous;
                lock (_lock)
                {
                    previous = _cachedSettings;
                    _cachedSettings = settings;
                    _isLoaded = true;
                }

                if (!Equals(previous, settings))
                {
                    SettingsChanged?.Invoke(settings);
                }

                InternalLogger.Debug("Settings saved successfully.");
            }
            finally
            {
                try { if (!string.IsNullOrEmpty(tempPath) && _fileSystem.FileExists(tempPath)) _fileSystem.Delete(tempPath); } catch { }
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
            if (_disposed) throw new ObjectDisposedException(nameof(SettingsManager));
        }


        public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            try
            {
                var (previous, loaded) = await ReadAndUpdateCacheAsync(cancellationToken).ConfigureAwait(false);

                if (!Equals(previous, loaded))
                {
                    SettingsChanged?.Invoke(loaded);
                }

                return loaded;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to read settings file during LoadAsync; falling back to defaults.", ex);
                lock (_lock)
                {
                    _cachedSettings = new AppSettings();
                    _isLoaded = true;
                }
                SettingsChanged?.Invoke(_cachedSettings);
                return _cachedSettings;
            }
        }

        private async Task<(AppSettings previous, AppSettings loaded)> ReadAndUpdateCacheAsync(CancellationToken cancellationToken)
        {
            AppSettings previous = null;
            AppSettings loaded;

            var snapshot = _cachedSettings;

            if (!_fileSystem.FileExists(_filePath))
            {
                loaded = new AppSettings();
            }
            else
            {
                try
                {
                    var content = await _fileSystem.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
                    loaded = DeserializeAndValidate(content);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    InternalLogger.Error("Failed to read or deserialize settings file; falling back to defaults.", ex);
                    loaded = new AppSettings();
                }
            }

            lock (_lock)
            {
                previous = _cachedSettings;

                if (!ReferenceEquals(previous, snapshot))
                {
                    loaded = previous;
                }
                else
                {
                    _cachedSettings = loaded;
                    _isLoaded = true;
                }
            }

            return (previous, loaded);
        }

        private AppSettings DeserializeAndValidate(string content)
        {
            if (string.IsNullOrEmpty(content)) return new AppSettings();

            try
            {
                var loaded = content.FromJson<AppSettings>();
                if (loaded == null) return new AppSettings();

                EnsureValidSettings(loaded);

                if (!TryValidateSettings(loaded, out var _errors))
                {
                    InternalLogger.Error("Settings validation failed during load; falling back to defaults: " + string.Join("; ", _errors));
                    return new AppSettings();
                }

                return loaded;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to deserialize settings content; falling back to defaults.", ex);
                return new AppSettings();
            }
        }

        private static void EnsureValidSettings(AppSettings settings)
        {
            if (settings != null && !Enum.IsDefined(typeof(AppTheme), settings.Theme))
            {
                settings.Theme = AppTheme.Dark;
            }
        }

        private bool TryValidateSettings(AppSettings settings, out System.Collections.Generic.List<string> errors)
        {
            errors = new System.Collections.Generic.List<string>();
            if (settings == null)
            {
                errors.Add("Settings instance is null.");
                return false;
            }

            var context = new ValidationContext(settings);
            var results = new System.Collections.Generic.List<ValidationResult>();
            bool valid = Validator.TryValidateObject(settings, context, results, validateAllProperties: true);

            foreach (var r in results)
            {
                if (!string.IsNullOrEmpty(r.ErrorMessage)) errors.Add(r.ErrorMessage);
            }

            return valid && errors.Count == 0;
        }
    }
}
