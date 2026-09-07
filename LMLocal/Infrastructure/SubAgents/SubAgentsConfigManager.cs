using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.SubAgents
{
    /// <summary>
    /// Manager for the SubAgent configuration stored in a local JSON file.
    /// </summary>
    public interface ISubAgentsConfigManager
    {
        /// <summary>
        /// Reads, parses and validates json.
        /// </summary>
        Task<SubAgentsConfig> GetAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Last validation errors from GetAsync or RefreshAsync call.
        /// </summary>
        IReadOnlyList<string> LastErrors { get; }

        /// <summary>
        /// Re-reads json and replaces the in-memory snapshot used by the runtime.
        /// </summary>
        Task RefreshAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the current in-memory snapshot, or an empty config when no snapshot has been built yet.
        /// </summary>
        SubAgentsConfig TryGetSnapshot();

        /// <summary>
        /// Validates the whole configuration: model-level rules plus tool-name collisions and allowedTools references against the registered built-in tools.
        /// </summary>
        IReadOnlyList<string> Validate(SubAgentsConfig config);

        /// <summary>
        /// Merges enabled flags into the stored configuration and optionally applies the "Use Active Model" top-level defaults, validates the whole result and saves it.
        /// </summary>
        Task<IReadOnlyList<string>> UpdateEnabledFlagsAsync(
            IReadOnlyList<SubAgentEnabledFlag> flags,
            CancellationToken cancellationToken = default,
            SubAgentDefaults defaults = null);

        /// <summary>
        /// Serializes the provided configuration to json and invalidates the snapshot so it is re-read on the next session.
        /// </summary>
        Task SaveAsync(SubAgentsConfig config, CancellationToken cancellationToken = default);
    }

    internal class SubAgentsConfigManager : ISubAgentsConfigManager, ISubAgentsCatalog
    {
        private readonly string _filePath;
        private readonly IFileSystem _fileSystem;
        private readonly IBuiltInVsToolProvider _builtInTools;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
        private readonly object _snapshotLock = new object();
        private SubAgentsConfig _snapshot = new SubAgentsConfig();
        private IReadOnlyList<string> _lastErrors = new List<string>();

        private static readonly JsonSerializerSettings SubAgentsJsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public SubAgentsConfigManager(
            IFileSystem fileSystem,
            ISettingsManager settingsManager,
            IBuiltInVsToolProvider builtInTools)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            if (settingsManager == null)
                throw new ArgumentNullException(nameof(settingsManager));
            _builtInTools = builtInTools ?? throw new ArgumentNullException(nameof(builtInTools));

            var filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                settingsManager.LocalAppDataFolder ?? "LMLocalChat",
                "subagents.json");

            _fileSystem.ValidateFilePath(filePath);
            _fileSystem.EnsureDirectoryExistsForFile(filePath);
            _filePath = filePath;
        }

        public IReadOnlyList<string> LastErrors
        {
            get
            {
                lock (_snapshotLock)
                {
                    return _lastErrors;
                }
            }
        }

        public SubAgentsConfig TryGetSnapshot()
        {
            lock (_snapshotLock)
            {
                return _snapshot;
            }
        }

        /// <summary>
        /// Enabled agents with a non-empty name, in file order.
        /// </summary>
        public IReadOnlyList<SubAgentDefinition> GetEnabledAgents()
        {
            var config = TryGetSnapshot();
            if (config == null || config.Agents == null)
                return Array.Empty<SubAgentDefinition>();

            return config.Agents
                .Where(a => a != null && a.Enabled && !string.IsNullOrWhiteSpace(a.Id))
                .ToList();
        }

        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            var config = await ReadConfigCoreAsync(cancellationToken).ConfigureAwait(false);

            lock (_snapshotLock)
            {
                _snapshot = config;
                _lastErrors = config.Errors ?? new List<string>();
            }
        }

        public async Task<SubAgentsConfig> GetAsync(CancellationToken cancellationToken = default)
        {
            var config = await ReadConfigCoreAsync(cancellationToken).ConfigureAwait(false);

            lock (_snapshotLock)
            {
                _lastErrors = config.Errors ?? new List<string>();
            }

            return config;
        }

        public IReadOnlyList<string> Validate(SubAgentsConfig config)
        {
            if (config == null)
                return new List<string> { "SubAgents configuration is required." };

            var errors = new List<string>(config.Validate());

            var toolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unfiltered = _builtInTools.GetAllToolDefinitionsUnfiltered();
            if (unfiltered != null)
            {
                foreach (var def in unfiltered)
                {
                    if (!string.IsNullOrEmpty(def.Name))
                        toolNames.Add(def.Name);
                }
            }

            var allAgentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var agent in config.Agents)
            {
                if (agent == null)
                    continue;

                var name = agent.Id?.Trim();
                if (!string.IsNullOrEmpty(name))
                    allAgentNames.Add(name);
            }

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var agent in config.Agents)
            {
                if (agent == null)
                    continue;

                var name = agent.Id?.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                if (!seenNames.Add(name))
                {
                    errors.Add($"agent id '{name}' is not unique (used by another SubAgent)");
                }

                if (toolNames.Contains(name))
                {
                    errors.Add($"agent id '{name}' collides with a built-in tool name");
                }

                if (agent.AllowedTools == null)
                    continue;

                foreach (var allowed in agent.AllowedTools)
                {
                    var tool = allowed?.Trim();
                    if (string.IsNullOrEmpty(tool))
                        continue;

                    if (allAgentNames.Contains(tool))
                    {
                        errors.Add($"agent '{name}': allowedTools references another SubAgent '{tool}' (recursion is not allowed)");
                    }
                    else if (!toolNames.Contains(tool))
                    {
                        errors.Add($"agent '{name}': allowedTools references unknown tool '{tool}'");
                    }
                }
            }

            return errors;
        }

        public async Task<IReadOnlyList<string>> UpdateEnabledFlagsAsync(
            IReadOnlyList<SubAgentEnabledFlag> flags,
            CancellationToken cancellationToken = default,
            SubAgentDefaults defaults = null)
        {

            var raw = await ReadConfigCoreAsync(cancellationToken, applyDefaults: false).ConfigureAwait(false);

            if (flags != null)
            {
                foreach (var flag in flags)
                {
                    SubAgentDefinition target = null;

                    if (!string.IsNullOrWhiteSpace(flag.Id))
                    {
                        target = raw.Agents.FirstOrDefault(a =>
                            string.Equals(a.Id, flag.Id, StringComparison.OrdinalIgnoreCase));
                    }

                    if (target == null)
                    {
                        int index = flag.Index ?? -1;
                        if (index >= 0 && index < raw.Agents.Count)
                        {
                            target = raw.Agents[index];
                        }
                    }

                    if (target != null)
                    {
                        target.Enabled = flag.Enabled;
                    }
                }
            }

            if (defaults != null)
            {
                ApplyActiveModelDefaults(raw, defaults);
            }

            var effective = raw.Clone();
            effective.ApplyDefaults();

            var errors = Validate(effective);
            if (errors.Count > 0)
            {
                return errors;
            }

            await WriteCoreAsync(raw, cancellationToken).ConfigureAwait(false);

            lock (_snapshotLock)
            {
                _snapshot = effective;
                _lastErrors = new List<string>();
            }

            return new List<string>();
        }

        /// <summary>
        /// Applies the "Use Active Model" top-level defaults to the raw config.
        /// </summary>
        private static void ApplyActiveModelDefaults(SubAgentsConfig config, SubAgentDefaults defaults)
        {
            if (config == null || defaults == null)
                return;

            if (!string.IsNullOrWhiteSpace(defaults.ProviderType))
                config.ProviderType = defaults.ProviderType;
            if (!string.IsNullOrWhiteSpace(defaults.CustomBaseUrl))
                config.CustomBaseUrl = defaults.CustomBaseUrl;
            if (!string.IsNullOrWhiteSpace(defaults.CustomApiKey))
                config.CustomApiKey = defaults.CustomApiKey;
            if (!string.IsNullOrWhiteSpace(defaults.Model))
                config.Model = defaults.Model;

            if (config.Agents == null)
                return;

            foreach (var agent in config.Agents)
            {
                if (agent == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(agent.ProviderType) && !string.IsNullOrWhiteSpace(defaults.ProviderType))
                    agent.ProviderType = defaults.ProviderType;
                if (!string.IsNullOrWhiteSpace(agent.CustomBaseUrl) && !string.IsNullOrWhiteSpace(defaults.CustomBaseUrl))
                    agent.CustomBaseUrl = defaults.CustomBaseUrl;
                if (!string.IsNullOrWhiteSpace(agent.CustomApiKey) && !string.IsNullOrWhiteSpace(defaults.CustomApiKey))
                    agent.CustomApiKey = defaults.CustomApiKey;
                if (!string.IsNullOrWhiteSpace(agent.Model) && !string.IsNullOrWhiteSpace(defaults.Model))
                    agent.Model = defaults.Model;
            }
        }

        public async Task SaveAsync(SubAgentsConfig config, CancellationToken cancellationToken = default)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var effective = config.Clone();
            effective.ApplyDefaults();

            var errors = Validate(effective);
            if (errors.Count > 0)
                throw new ArgumentException("SubAgents configuration is invalid: " + string.Join("; ", errors));

            cancellationToken.ThrowIfCancellationRequested();

            await WriteCoreAsync(config, cancellationToken).ConfigureAwait(false);

            lock (_snapshotLock)
            {
                _snapshot = effective;
                _lastErrors = new List<string>();
            }
        }

        /// <summary>
        /// Serializes the provided config (as given) and writes it to disk. 
        /// </summary>
        private async Task WriteCoreAsync(SubAgentsConfig config, CancellationToken cancellationToken)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string json = JsonConvert.SerializeObject(config, SubAgentsJsonSettings);
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
                await _fileSystem.WriteAllBytesAsync(_filePath, bytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                InternalLogger.Error($"SubAgentsConfigManager: failed to save subagents config: {ex.Message}", ex);
                throw;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <summary>
        /// Reads the file and parses it.
        private async Task<SubAgentsConfig> ReadConfigCoreAsync(CancellationToken cancellationToken, bool applyDefaults = true)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            var config = new SubAgentsConfig();
            try
            {
                if (!_fileSystem.FileExists(_filePath))
                {
                    return config;
                }

                string fileContent = await _fileSystem.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
                var parsed = ParseConfig(fileContent);

                if (!applyDefaults)
                {
                    return parsed;
                }

                parsed.ApplyDefaults();

                var errors = parsed.Validate();
                config = parsed;

                if (errors.Count > 0)
                {
                    InternalLogger.Warn($"SubAgentsConfigManager: invalid subagents.json ({_filePath}): {string.Join("; ", errors)}.");
                    config.Errors = errors;
                }

                return config;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                InternalLogger.Error($"SubAgentsConfigManager: failed to read subagents config: {ex.Message}", ex);
                config.Errors = new System.Collections.Generic.List<string> { ex.Message };
                return config;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <summary>
        /// Deserializes json content.
        /// </summary>
        private static SubAgentsConfig ParseConfig(string fileContent)
        {
            if (string.IsNullOrWhiteSpace(fileContent))
            {
                return new SubAgentsConfig();
            }

            return fileContent.FromJson<SubAgentsConfig>() ?? new SubAgentsConfig();
        }
    }
}
