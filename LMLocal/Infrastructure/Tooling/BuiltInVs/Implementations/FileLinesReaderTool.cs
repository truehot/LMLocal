using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    public class FileLineInfo
    {
        [JsonProperty("line_number")]
        public int LineNumber { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }

    public class FileLinesResponse
    {
        [JsonProperty("file")]
        public string FilePath { get; set; }

        [JsonProperty("lines")]
        public List<FileLineInfo> Lines { get; set; }
    }

    internal interface IFileLinesReaderTool : ITool
    {
        Task<FileLinesResponse> ExecuteAsync(
            IServiceProvider sp,
            string filePath,
            int startLine,
            int endLine,
            CancellationToken cancellationToken = default);
    }

    internal class FileLinesReaderTool : IFileLinesReaderTool
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;

        public string ToolName => "Read_Solution_File_Lines";

        public FileLinesReaderTool(IVsDependencies vsDependencies, IPathResolver pathResolver)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Reads a specific line range from a file within the current Visual Studio solution. Returns a FileLinesResponse with the requested lines (or fewer if end_line exceeds file length). Throws FileNotFoundException if the file does not exist or is outside the solution directory. Throws ArgumentException if start_line exceeds the file's total line count. Lines are returned exactly as they appear in the file, and there is no limit on the maximum number of lines per request.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Path to the source file (absolute or relative to solution root)." } },
                        { "start_line", new ToolDetails { Type = "integer", Description = "The starting line number (1-indexed)." } },
                        { "end_line", new ToolDetails { Type = "integer", Description = "The ending line number (inclusive)." } }
                    },
                    Required = new List<string> { "file_path", "start_line", "end_line" }
                }
            };
        }

        public async Task<FileLinesResponse> ExecuteAsync(
            IServiceProvider sp,
            string filePath,
            int startLine,
            int endLine,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be empty.", nameof(filePath));
            if (startLine < 1)
                throw new ArgumentException("Start line must be 1 or greater.", nameof(startLine));
            if (endLine < startLine)
                throw new ArgumentException("End line must be greater than or equal to start line.", nameof(endLine));

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await _vsDependencies.InitializeAsync();

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath) || string.IsNullOrEmpty(absolutePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            if (!File.Exists(absolutePath))
                throw new FileNotFoundException($"File not found: {absolutePath}");

            if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                throw new ArgumentException($"File '{absolutePath}' is outside the solution directory '{solutionDir}'.");

            if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                relativePath = absolutePath;

            var result = new FileLinesResponse
            {
                FilePath = relativePath,
                Lines = new List<FileLineInfo>()
            };

            int currentLine = 0;
            using (var reader = new StreamReader(absolutePath))
            {
                string line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentLine++;

                    if (currentLine < startLine)
                        continue;

                    if (currentLine > endLine)
                        break;

                    result.Lines.Add(new FileLineInfo
                    {
                        LineNumber = currentLine,
                        Text = line
                    });
                }
            }

            if (startLine > currentLine)
                throw new ArgumentException($"Start line {startLine} exceeds file line count {currentLine}.", nameof(startLine));

            return result;
        }
    }
}
