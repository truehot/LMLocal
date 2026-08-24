using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Js
{
    /// <summary>
    /// Represents the resolved JS/TS project configuration: baseUrl and path aliases.
    /// </summary>
    internal sealed class JsConfig
    {
        /// <summary>
        /// Base directory (absolute) against which relative imports and aliases are resolved.
        /// </summary>
        public string BaseUrl { get; set; }

        /// <summary>
        /// Path aliases from compilerOptions.paths. Key is the alias pattern (may contain '*'), value is the first target pattern (may contain '*').
        /// </summary>
        public Dictionary<string, string> PathMappings { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Absolute path of the config file that was loaded (jsconfig.json or tsconfig.json).
        /// </summary>
        public string ConfigFilePath { get; set; }

        /// <summary>
        /// Stable hash of the config content used for cache invalidation.
        /// </summary>
        public string ConfigHash { get; set; }
    }

    /// <summary>
    /// Loads and resolves JavaScript/TypeScript project configuration (jsconfig.json / tsconfig.json).
    /// </summary>
    internal interface IJsConfigResolver
    {
        /// <summary>
        /// Loads the JS/TS config from the solution directory (and optional extra search roots,
        /// e.g. project roots / JS file directories).
        /// </summary>
        JsConfig Load(string solutionDir, IReadOnlyCollection<string> additionalSearchRoots = null);

        /// <summary>
        /// Resolves a module specifier (import source) to an absolute file path.
        /// </summary>
        string ResolveModule(string source, string fromFile, JsConfig config);
    }

    internal sealed class JsConfigResolver : IJsConfigResolver
    {
        private const string JsConfigFileName = "jsconfig.json";
        private const string TsConfigFileName = "tsconfig.json";

        private readonly IFileSystem _fileSystem;

        public JsConfigResolver(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        /// <summary>
        /// Loads the JS/TS config. Search order:
        ///   1. solutionDir itself
        ///   2. each additional search root (project roots, JS file directories), walking up to solutionDir
        /// </summary>
        public JsConfig Load(string solutionDir, IReadOnlyCollection<string> additionalSearchRoots = null)
        {
            if (string.IsNullOrEmpty(solutionDir))
                return null;

            var searchRoots = new List<string>();
            if (!string.IsNullOrEmpty(solutionDir))
                searchRoots.Add(solutionDir);
            if (additionalSearchRoots != null)
            {
                foreach (var root in additionalSearchRoots)
                {
                    if (string.IsNullOrEmpty(root))
                        continue;
                    string abs;
                    try
                    {
                        abs = Path.GetFullPath(root);
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Warn($"JsConfigResolver: invalid search root '{root}': {ex.Message}");
                        continue;
                    }
                    if (!searchRoots.Any(r => string.Equals(r, abs, StringComparison.OrdinalIgnoreCase)))
                        searchRoots.Add(abs);
                }
            }

            foreach (var root in searchRoots)
            {
                foreach (var dir in WalkUpToSolution(root, solutionDir))
                {
                    string jsConfigPath = Path.Combine(dir, JsConfigFileName);
                    if (_fileSystem.FileExists(jsConfigPath))
                    {
                        var cfg = TryLoadFromFile(jsConfigPath, solutionDir);
                        if (cfg != null)
                            return cfg;
                    }

                    string tsConfigPath = Path.Combine(dir, TsConfigFileName);
                    if (_fileSystem.FileExists(tsConfigPath))
                    {
                        var cfg = TryLoadFromFile(tsConfigPath, solutionDir);
                        if (cfg != null)
                            return cfg;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves a module specifier (import source) to an absolute file path.
        /// </summary>
        public string ResolveModule(string source, string fromFile, JsConfig config)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrEmpty(fromFile))
                return null;

            string resolvedBase = null;

            // 1. Path alias (compilerOptions.paths), e.g. "@app/*" -> "Resources/js/*"
            if (config != null && config.PathMappings.Count > 0)
            {
                resolvedBase = TryResolveAlias(source, config);
            }

            // 2. Relative import (./ or ../) — resolve against the directory of the importing file
            if (resolvedBase == null && (source.StartsWith("./", StringComparison.Ordinal) || source.StartsWith("../", StringComparison.Ordinal)))
            {
                string fromDir = Path.GetDirectoryName(fromFile);
                if (!string.IsNullOrEmpty(fromDir))
                    resolvedBase = Path.GetFullPath(Path.Combine(fromDir, source));
            }

            // 3. Absolute path
            if (resolvedBase == null && Path.IsPathRooted(source))
            {
                resolvedBase = Path.GetFullPath(source);
            }

            // 4. Bare specifier (react, lodash) — external, not resolvable
            if (resolvedBase == null)
                return null;

            // Try with extension resolution
            return ResolveWithExtensions(resolvedBase);
        }

        private static IEnumerable<string> WalkUpToSolution(string startDir, string solutionDir)
        {
            string normalizedSolution;
            try
            {
                normalizedSolution = Path.GetFullPath(solutionDir).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch (Exception)
            {
                yield break;
            }

            string current = startDir;
            while (!string.IsNullOrEmpty(current))
            {
                string norm;
                try
                {
                    norm = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar);
                }
                catch (Exception)
                {
                    yield break;
                }

                if (!IsUnderOrEqual(norm, normalizedSolution))
                    yield break;

                yield return current;

                if (string.Equals(norm, normalizedSolution, StringComparison.OrdinalIgnoreCase))
                    yield break;

                string parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    yield break;

                current = parent;
            }
        }

        private static bool IsUnderOrEqual(string path, string root)
        {
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                return true;
            return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private JsConfig TryLoadFromFile(string configPath, string solutionDir)
        {
            try
            {
                string content = _fileSystem.ReadAllText(configPath);
                var root = JObject.Parse(content);

                var config = new JsConfig
                {
                    ConfigFilePath = configPath,
                    ConfigHash = ComputeHash(content)
                };

                if (root["compilerOptions"] is JObject compilerOptions)
                {
                    string baseUrl = compilerOptions["baseUrl"]?.Value<string>();
                    config.BaseUrl = ResolveBaseUrl(baseUrl, solutionDir, configPath);

                    if (compilerOptions["paths"] is JObject paths)
                    {
                        foreach (var prop in paths.Properties())
                        {
                            if (!(prop.Value is JArray targets) || targets.Count == 0)
                                continue;
                            string firstTarget = targets[0]?.Value<string>();
                            if (!string.IsNullOrEmpty(firstTarget))
                                config.PathMappings[prop.Name] = firstTarget;
                        }
                    }
                }

                InternalLogger.Info($"JsConfigResolver: loaded config '{configPath}'");
                return config;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"JsConfigResolver: failed to load config '{configPath}': {ex.Message}");
                return null;
            }
        }

        private static string TryResolveAlias(string source, JsConfig config)
        {
            foreach (var kvp in config.PathMappings)
            {
                string aliasPattern = kvp.Key;
                string targetPattern = kvp.Value;

                int starIndex = aliasPattern.IndexOf('*');
                if (starIndex >= 0)
                {
                    string prefix = aliasPattern.Substring(0, starIndex);
                    string suffix = aliasPattern.Substring(starIndex + 1);

                    if (source.StartsWith(prefix, StringComparison.Ordinal) && source.EndsWith(suffix, StringComparison.Ordinal) && source.Length >= prefix.Length + suffix.Length)
                    {
                        string starValue = source.Substring(prefix.Length, source.Length - prefix.Length - suffix.Length);
                        string target = targetPattern.Replace("*", starValue);
                        return CombineWithBaseUrl(target, config);
                    }
                }
                else if (string.Equals(source, aliasPattern, StringComparison.Ordinal))
                {
                    return CombineWithBaseUrl(targetPattern, config);
                }
            }

            return null;
        }

        private static string CombineWithBaseUrl(string relativePath, JsConfig config)
        {
            if (Path.IsPathRooted(relativePath))
                return Path.GetFullPath(relativePath);

            string baseUrl = config.BaseUrl ?? string.Empty;
            if (string.IsNullOrEmpty(baseUrl))
                return Path.GetFullPath(relativePath);

            return Path.GetFullPath(Path.Combine(baseUrl, relativePath));
        }

        private static string ResolveBaseUrl(string baseUrl, string solutionDir, string configPath)
        {
            string configDir = Path.GetDirectoryName(configPath) ?? solutionDir;
            if (string.IsNullOrEmpty(baseUrl))
                return configDir;

            if (Path.IsPathRooted(baseUrl))
                return Path.GetFullPath(baseUrl);

            return Path.GetFullPath(Path.Combine(configDir, baseUrl));
        }

        private string ResolveWithExtensions(string basePath)
        {
            var candidates = new List<string>
            {
                basePath,
                basePath + ".js",
                basePath + ".mjs",
                basePath + ".cjs",
                Path.Combine(basePath, "index.js"),
                Path.Combine(basePath, "index.mjs"),
                Path.Combine(basePath, "index.cjs")
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    if (_fileSystem.FileExists(candidate))
                        return candidate;
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"JsConfigResolver: FileExists failed for '{candidate}': {ex.Message}");
                }
            }

            return null;
        }

        private static string ComputeHash(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            unchecked
            {
                int hash = 17;
                foreach (char c in content)
                    hash = hash * 31 + c;
                return hash.ToString("x");
            }
        }
    }
}
