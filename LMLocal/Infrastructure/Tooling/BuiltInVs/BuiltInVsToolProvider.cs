
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs
{

    /// <summary>
    /// Interface for built-in VS tool provider.
    /// </summary>
    internal interface IBuiltInVsToolProvider
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
        ITool GetTool(string toolName);

        /// <summary>
        /// Executes a tool with the given parameters from LLM response.
        /// </summary>
        Task<object> ExecuteAsync(
            string toolName,
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

    /// <summary>
    /// Factory for Visual Studio built-in tools.
    /// </summary>
    internal class BuiltInVsToolProvider : IBuiltInVsToolProvider
    {
        private readonly ISolutionSearch _solutionSearch;
        private readonly IActiveDocument _activeDocument;
        private readonly IFileLinesReader _fileLinesReader;
        private readonly IFindFilesByName _findFilesByName;
        private readonly IGetSolutionOverview _getSolutionOverview;
        private readonly IFindSymbolReferences _findSymbolReferences;
        private readonly IListDirectoryContents _listDirectoryContents;

        private readonly IReadOnlyList<ToolDefinition> _allToolDefinitions;
        private readonly Dictionary<string, IBuiltInTool> _toolsByName;

        public BuiltInVsToolProvider(
            ISolutionSearch solutionSearch,
            IActiveDocument activeDocument,
            IFileLinesReader fileLinesReader,
            IFindFilesByName findFilesByName,
            IGetSolutionOverview getSolutionOverview,
            IFindSymbolReferences findSymbolReferences,
            IListDirectoryContents listDirectoryContents)
        {
            _solutionSearch = solutionSearch ?? throw new ArgumentNullException(nameof(solutionSearch));
            _activeDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));
            _fileLinesReader = fileLinesReader ?? throw new ArgumentNullException(nameof(fileLinesReader));
            _findFilesByName = findFilesByName ?? throw new ArgumentNullException(nameof(findFilesByName));
            _getSolutionOverview = getSolutionOverview ?? throw new ArgumentNullException(nameof(getSolutionOverview));
            _findSymbolReferences = findSymbolReferences ?? throw new ArgumentNullException(nameof(findSymbolReferences));
            _listDirectoryContents = listDirectoryContents ?? throw new ArgumentNullException(nameof(listDirectoryContents));

            _allToolDefinitions = new List<ToolDefinition>
            {
                _solutionSearch.GetToolInfo(),
                _activeDocument.GetToolInfo(),
                _fileLinesReader.GetToolInfo(),
                _findFilesByName.GetToolInfo(),
                _getSolutionOverview.GetToolInfo(),
                _findSymbolReferences.GetToolInfo(),
                _listDirectoryContents.GetToolInfo()
            }.AsReadOnly();

            _toolsByName = new Dictionary<string, IBuiltInTool>(StringComparer.OrdinalIgnoreCase)
            {
                { _solutionSearch.ToolName, _solutionSearch },
                { _activeDocument.ToolName, _activeDocument },
                { _fileLinesReader.ToolName, _fileLinesReader },
                { _findFilesByName.ToolName, _findFilesByName },
                { _getSolutionOverview.ToolName, _getSolutionOverview },
                { _findSymbolReferences.ToolName, _findSymbolReferences },
                { _listDirectoryContents.ToolName, _listDirectoryContents }
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

        public ITool GetTool(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));

            if (_toolsByName.TryGetValue(toolName, out var tool))
                return tool;

            throw new ArgumentException($"Unknown tool: '{toolName}'", nameof(toolName));
        }

        public async Task<object> ExecuteAsync(
            string toolName,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));


            if (!_toolsByName.TryGetValue(toolName, out var tool))
                throw new ArgumentException($"Unknown tool: '{toolName}'", nameof(toolName));

            switch (tool)
            {
                case IActiveDocument activeDocument:
                    return await activeDocument.ExecuteAsync(cancellationToken);
                case IGetSolutionOverview solutionOverview:
                    return await solutionOverview.ExecuteAsync(cancellationToken);
                case ISolutionSearch solutionSearch:
                    return await solutionSearch.ExecuteAsync(parameters, cancellationToken);
                case IFileLinesReader fileLinesReader:
                    return await fileLinesReader.ExecuteAsync(parameters, cancellationToken);
                case IFindFilesByName findFilesByName:
                    return await findFilesByName.ExecuteAsync(parameters, cancellationToken);
                case IFindSymbolReferences findSymbolReferences:
                    return await findSymbolReferences.ExecuteAsync(parameters, cancellationToken);
                case IListDirectoryContents listDirectoryContents:
                    return await listDirectoryContents.ExecuteAsync(parameters, cancellationToken);
                default:
                    throw new NotSupportedException($"Tool type '{tool.GetType().Name}' is not supported.");
            }
        }

        public string GetProcessingMessage(string toolName, Dictionary<string, object> parameters)
        {
            if (!string.IsNullOrEmpty(toolName) && _toolsByName.TryGetValue(toolName, out var tool))
            {
                return tool.GetProcessingMessage(parameters);
            }

            return "Processing...";
        }

        public string GetCompletionMessage(string toolName, object result)
        {
            if (!string.IsNullOrEmpty(toolName) && _toolsByName.TryGetValue(toolName, out var tool))
            {
                return tool.GetCompletionMessage(result);
            }

            return "Completed";
        }
    }
}
