using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models.Instructions;
using LMLocal.Infrastructure.Persistence;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Instructions
{
    /// <summary>
    /// Reads the built-in instruction tabs from the static JSON resource that ships with the extension (<c>Resources/json/instruction-tabs.json</c>).
    /// </summary>
    internal sealed class FileInstructionDefaultsProvider : IInstructionDefaultsProvider
    {
        private readonly string _filePath;
        private readonly IFileSystem _fileSystem;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private IReadOnlyList<InstructionDefaultTab> _cache;

        public FileInstructionDefaultsProvider(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            _filePath = Path.Combine(assemblyDir, "Resources", "json", "instruction-tabs.json");
        }

        public async Task<IReadOnlyList<InstructionDefaultTab>> GetDefaultTabsAsync(CancellationToken cancellationToken = default)
        {
            if (_cache != null)
                return _cache;

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cache != null)
                    return _cache;

                _cache = LoadDefaults();
                return _cache;
            }
            finally
            {
                _lock.Release();
            }
        }

        private IReadOnlyList<InstructionDefaultTab> LoadDefaults()
        {
            var result = new List<InstructionDefaultTab>();
            try
            {
                if (!_fileSystem.FileExists(_filePath))
                {
                    InternalLogger.Warn("Instruction defaults file not found: " + _filePath);
                    return result;
                }

                string raw = _fileSystem.ReadAllText(_filePath);
                JObject doc = JObject.Parse(raw);
                if (!(doc["tabs"] is JArray tabs))
                    return result;

                foreach (JToken token in tabs)
                {
                    if (!(token is JObject tab))
                        continue;

                    int id = tab["id"]?.Value<int>() ?? 0;
                    string name = tab["displayName"]?.Value<string>();
                    if (id <= 0 || string.IsNullOrWhiteSpace(name))
                        continue;

                    bool enabled = tab["enabled"]?.Value<bool>() ?? true;
                    double temperature = tab["temperature"]?.Value<double>() ?? 0.5;
                    string prompt = tab["prompt"]?.Value<string>() ?? string.Empty;

                    result.Add(new InstructionDefaultTab(id, name, enabled, temperature, prompt));
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("Error loading instruction defaults: " + ex.Message);
                return new List<InstructionDefaultTab>();
            }

            return result;
        }
    }
}
