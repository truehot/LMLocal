using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Infrastructure.Vs.Abstractions;
using LMLocal.Infrastructure.Vs.Implementations;

namespace LMLocal.Infrastructure.Vs
{
    public interface IVsToolFactory
    {
        /// <summary>
        /// Returns all registered tool definitions for the LLM.
        /// </summary>
        IReadOnlyList<ToolDefinition> GetAllToolDefinitions();

        /// <summary>
        /// Checks whether a tool with the specified name is registered.
        /// </summary>
        bool ToolExists(string toolName);

        /// <summary>
        /// Resolves a tool by its name.
        /// </summary>
        IVsTool GetTool(string toolName);

        /// <summary>
        /// Executes a tool with the given parameters from LLM response.
        /// </summary>
        Task<object> ExecuteAsync(
            string toolName,
            IServiceProvider sp,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets processing message for a tool based on its parameters.
        /// </summary>
        string GetProcessingMessage(string toolName, Dictionary<string, object> parameters);

        /// <summary>
        /// Gets completion message for a tool based on its execution result.
        /// </summary>
        string GetCompletionMessage(string toolName, object result);
    }


    internal class VsToolFactory : IVsToolFactory
    {
        private readonly ISolutionSearchTool _searchTool;
        private readonly IActiveDocumentTool _activeDocTool;
        private readonly IFileLinesReaderTool _fileLinesReaderTool;
        private readonly IFindFilesByNameTool _findFilesByNameTool;
        private readonly IGetSolutionOverviewTool _solutionOverviewTool;
        private readonly IFindSymbolReferencesTool _findSymbolReferencesTool;

        private readonly IReadOnlyList<ToolDefinition> _allToolDefinitions;
        private readonly Dictionary<string, IVsTool> _toolsByName;

        public VsToolFactory(
            ISolutionSearchTool searchTool,
            IActiveDocumentTool activeDocTool,
            IFileLinesReaderTool fileLinesReaderTool,
            IFindFilesByNameTool findFilesByNameTool,
            IGetSolutionOverviewTool solutionOverviewTool,
            IFindSymbolReferencesTool findSymbolReferencesTool)
        {
            _searchTool = searchTool ?? throw new ArgumentNullException(nameof(searchTool));
            _activeDocTool = activeDocTool ?? throw new ArgumentNullException(nameof(activeDocTool));
            _fileLinesReaderTool = fileLinesReaderTool ?? throw new ArgumentNullException(nameof(fileLinesReaderTool));
            _findFilesByNameTool = findFilesByNameTool ?? throw new ArgumentNullException(nameof(findFilesByNameTool));
            _solutionOverviewTool = solutionOverviewTool ?? throw new ArgumentNullException(nameof(solutionOverviewTool));
            _findSymbolReferencesTool = findSymbolReferencesTool ?? throw new ArgumentNullException(nameof(findSymbolReferencesTool));

            _allToolDefinitions = new List<ToolDefinition>
            {
                _searchTool.GetToolInfo(),
                _activeDocTool.GetToolInfo(),
                _fileLinesReaderTool.GetToolInfo(),
                _findFilesByNameTool.GetToolInfo(),
                _solutionOverviewTool.GetToolInfo(),
                _findSymbolReferencesTool.GetToolInfo()
            }.AsReadOnly();

            _toolsByName = new Dictionary<string, IVsTool>(StringComparer.OrdinalIgnoreCase)
            {
                { _searchTool.ToolName, _searchTool },
                { _activeDocTool.ToolName, _activeDocTool },
                { _fileLinesReaderTool.ToolName, _fileLinesReaderTool },
                { _findFilesByNameTool.ToolName, _findFilesByNameTool },
                { _solutionOverviewTool.ToolName, _solutionOverviewTool },
                { _findSymbolReferencesTool.ToolName, _findSymbolReferencesTool }
            };
        }

        public IReadOnlyList<ToolDefinition> GetAllToolDefinitions()
        {
            return _allToolDefinitions;
        }

        public bool ToolExists(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return false;

            return _toolsByName.ContainsKey(toolName);
        }

        public IVsTool GetTool(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));

            if (_toolsByName.TryGetValue(toolName, out var tool))
                return tool;

            throw new ArgumentException($"Unknown tool: '{toolName}'", nameof(toolName));
        }

        public async Task<object> ExecuteAsync(
            string toolName,
            IServiceProvider sp,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            if (toolName == _activeDocTool.ToolName)
                return await ExecuteActiveDocAsync(sp, cancellationToken);
            else if (toolName == _searchTool.ToolName)
                return await ExecuteSearchAsync(sp, parameters, cancellationToken);
            else if (toolName == _fileLinesReaderTool.ToolName)
                return await ExecuteFileLinesAsync(sp, parameters, cancellationToken);
            else if (toolName == _findFilesByNameTool.ToolName)
                return await ExecuteFindFilesByNameAsync(sp, parameters, cancellationToken);
            else if (toolName == _solutionOverviewTool.ToolName)
                return await ExecuteSolutionOverviewAsync(sp, cancellationToken);
            else if (toolName == _findSymbolReferencesTool.ToolName)
                return await ExecuteFindSymbolReferencesAsync(sp, parameters, cancellationToken);
            else
                throw new ArgumentException($"Unknown tool: '{toolName}'", nameof(toolName));
        }

        private async Task<object> ExecuteSearchAsync(
            IServiceProvider sp,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            if (!parameters.TryGetValue("query", out object queryObj) || !(queryObj is string))
                throw new ArgumentException("Parameter 'query' is required and must be a string.", "query");

            string query = (string)queryObj;
            string extensionFilter = parameters.TryGetValue("extension_filter", out object extObj) ? extObj as string : null;
            string projectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null;

            try
            {
                var result = await _searchTool.ExecuteAsync(sp, query, extensionFilter, cancellationToken, projectFilter);
                return result;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"SolutionSearchTool execution failed: {ex.Message}", ex);
                throw;
            }
        }

        private async Task<object> ExecuteActiveDocAsync(
            IServiceProvider sp,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await _activeDocTool.ExecuteAsync(sp, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"ActiveDocumentTool execution failed: {ex.Message}", ex);
                throw;
            }
        }

        private async Task<object> ExecuteFileLinesAsync(
            IServiceProvider sp,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string))
                throw new ArgumentException("Parameter 'file_path' is required and must be a string.", nameof(parameters));

            if (!parameters.TryGetValue("start_line", out object startLineObj) || !TryParseInt(startLineObj, out int startLine))
                throw new ArgumentException("Parameter 'start_line' is required and must be an integer.", nameof(parameters));

            if (!parameters.TryGetValue("end_line", out object endLineObj) || !TryParseInt(endLineObj, out int endLine))
                throw new ArgumentException("Parameter 'end_line' is required and must be an integer.", nameof(parameters));

            string filePath = (string)filePathObj;

            try
            {
                var result = await _fileLinesReaderTool.ExecuteAsync(sp, filePath, startLine, endLine, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"FileLinesReaderTool execution failed: {ex.Message}", ex);
                throw;
            }
        }

        private async Task<object> ExecuteFindFilesByNameAsync(
            IServiceProvider sp,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            if (!parameters.TryGetValue("file_name", out object fileNameObj) || !(fileNameObj is string))
                throw new ArgumentException("Parameter 'file_name' is required and must be a string.", nameof(parameters));

            string fileName = (string)fileNameObj;
            string fileExtension = parameters.TryGetValue("file_extension", out object extObj) ? extObj as string : null;
            string projectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null;

            try
            {
                var result = await _findFilesByNameTool.ExecuteAsync(sp, fileName, fileExtension, cancellationToken, projectFilter);
                return result;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"FindFilesByNameTool execution failed: {ex.Message}", ex);
                throw;
            }
        }

        private async Task<object> ExecuteSolutionOverviewAsync(
            IServiceProvider sp,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await _solutionOverviewTool.ExecuteAsync(sp, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"GetSolutionOverviewTool execution failed: {ex.Message}", ex);
                throw;
            }
        }

        private async Task<object> ExecuteFindSymbolReferencesAsync(
            IServiceProvider sp,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            if (!parameters.TryGetValue("symbol_name", out object symbolNameObj) || !(symbolNameObj is string))
                throw new ArgumentException("Parameter 'symbol_name' is required and must be a string.", nameof(parameters));

            string symbolName = (string)symbolNameObj;

            try
            {
                var result = await _findSymbolReferencesTool.ExecuteAsync(sp, symbolName, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"FindSymbolReferencesTool execution failed: {ex.Message}", ex);
                throw;
            }
        }

        private bool TryParseInt(object value, out int result)
        {
            result = 0;
            if (value is int intVal)
            {
                result = intVal;
                return true;
            }
            if (value is long longVal)
            {
                if (longVal >= int.MinValue && longVal <= int.MaxValue)
                {
                    result = (int)longVal;
                    return true;
                }
                return false;
            }
            if (value is string strVal && int.TryParse(strVal, out int parsed))
            {
                result = parsed;
                return true;
            }
            return false;
        }

        public string GetProcessingMessage(string toolName, Dictionary<string, object> parameters)
        {
            switch (toolName)
            {
                case var _ when toolName == _searchTool.ToolName:
                    {
                        var query = parameters.TryGetValue("query", out var q) ? q.ToString() : "";
                        var ext = parameters.TryGetValue("extension_filter", out var e) ? e.ToString() : null;
                        var project = parameters.TryGetValue("project_filter", out var p) ? p.ToString() : null;

                        var message = $"Searching for '{query}'";
                        if (!string.IsNullOrEmpty(ext))
                            message += $" in files with extension '{ext}'";
                        else
                            message += " in all files";
                        if (!string.IsNullOrEmpty(project))
                            message += $" in project '{project}'";
                        message += "... ";

                        return message;
                    }

                case var _ when toolName == _findFilesByNameTool.ToolName:
                    {
                        var fileName = parameters.TryGetValue("file_name", out var fn) ? fn.ToString() : "";
                        var ext = parameters.TryGetValue("file_extension", out var fe) ? fe.ToString() : null;
                        var project = parameters.TryGetValue("project_filter", out var pf) ? pf.ToString() : null;

                        var message = $"Finding files matching '{fileName}'";
                        if (!string.IsNullOrEmpty(ext))
                            message += $" with extension '{ext}'";
                        if (!string.IsNullOrEmpty(project))
                            message += $" in project '{project}'";
                        message += "... ";

                        return message;
                    }

                case var _ when toolName == _fileLinesReaderTool.ToolName:
                    {
                        var file = parameters.TryGetValue("file_path", out var f) ? f.ToString() : "";
                        var start = parameters.TryGetValue("start_line", out var s) && int.TryParse(s.ToString(), out var si) ? si : 1;
                        var end = parameters.TryGetValue("end_line", out var en) && int.TryParse(en.ToString(), out var ei) ? ei : 1;
                        return $"Reading lines {start}-{end} from '{file}'... ";
                    }

                case var _ when toolName == _activeDocTool.ToolName:
                    return "Reading active document... ";

                case var _ when toolName == _solutionOverviewTool.ToolName:
                    return "Loading solution overview... ";

                case var _ when toolName == _findSymbolReferencesTool.ToolName:
                    {
                        var symbolName = parameters.TryGetValue("symbol_name", out var s) ? s.ToString() : "";
                        return $"Finding all references for symbol '{symbolName}'... ";
                    }

                default:
                    return "Processing...";
            }
        }

        public string GetCompletionMessage(string toolName, object result)
        {
            switch (toolName)
            {
                case var _ when toolName == _searchTool.ToolName:
                    {
                        if (result is SearchResultsResponse searchResults)
                            return $"Found {searchResults.Results.Count} matches.";
                        break;
                    }

                case var _ when toolName == _findFilesByNameTool.ToolName:
                    {
                        if (result is FileSearchResultsResponse fileResults)
                            return $"Found {fileResults.Results.Count} files.";
                        break;
                    }

                case var _ when toolName == _fileLinesReaderTool.ToolName:
                    {
                        if (result is FileLinesResponse fileResult)
                            return $"Read {fileResult.Lines.Count} lines.";
                        break;
                    }

                case var _ when toolName == _activeDocTool.ToolName:
                    {
                        if (result is ActiveDocumentResponse docResult)
                            return $"Retrieved: '{Path.GetFileName(docResult.FilePath)}'.";
                        break;
                    }

                case var _ when toolName == _solutionOverviewTool.ToolName:
                    {
                        if (result is SolutionOverviewResponse solutionResult)
                            return $"Loaded solution with {solutionResult.TotalProjects} projects ({solutionResult.TotalFiles} files).";
                        break;
                    }

                case var _ when toolName == _findSymbolReferencesTool.ToolName:
                    {
                        if (result is SymbolReferencesResponse symbolResult)
                            return $"Found {symbolResult.TotalReferences} references to '{symbolResult.SymbolName}'.";
                        break;
                    }
            }

            return "Completed";
        }
    }
}
