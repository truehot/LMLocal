using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    /// <summary>
    /// Per-call repository facts: root, git dir, HEAD, branch, in-progress operation and the mapping
    /// between solution-relative paths (what other tools use) and repository-relative paths (what git uses).
    /// </summary>
    internal sealed class GitRepositoryContext
    {
        private readonly IPathResolver _pathResolver;

        private GitRepositoryContext(IPathResolver pathResolver, IGitProcessRunner runner, string solutionDir)
        {
            _pathResolver = pathResolver;
            Runner = runner;
            SolutionDir = solutionDir;
        }

        public IGitProcessRunner Runner { get; }
        public string SolutionDir { get; }

        public string RepoRoot { get; private set; }
        public string GitDir { get; private set; }

        /// <summary>Full sha of HEAD; null in a repository without commits.</summary>
        public string Head { get; private set; }
        public string Branch { get; private set; }
        public bool Detached { get; private set; }
        public bool Shallow { get; private set; }
        public string InProgress { get; private set; }

        public static async Task<GitRepositoryContext> CreateAsync(
            IPathResolver pathResolver,
            string solutionDir,
            IGitProcessRunner runner,
            CancellationToken cancellationToken)
        {
            if (pathResolver == null)
                throw new ArgumentNullException(nameof(pathResolver));
            if (runner == null)
                throw new ArgumentNullException(nameof(runner));
            if (string.IsNullOrWhiteSpace(solutionDir))
                throw new GitToolException("No solution is currently open.");

            var context = new GitRepositoryContext(pathResolver, runner, solutionDir);

            // One call in the usual case; a repository without commits fails on HEAD and is asked again without it.
            GitProcessResult facts = await runner.RunAsync(
                GitArguments.Build("rev-parse", "--show-toplevel", "--absolute-git-dir", "--is-shallow-repository", "HEAD", "--abbrev-ref", "HEAD"),
                solutionDir, cancellationToken).ConfigureAwait(false);

            bool hasHead = facts.ExitCode == 0;
            if (!hasHead)
            {
                facts = await runner.RunAsync(
                    GitArguments.Build("rev-parse", "--show-toplevel", "--absolute-git-dir", "--is-shallow-repository"),
                    solutionDir, cancellationToken).ConfigureAwait(false);
                GitProcessGuard.EnsureSuccess(facts, "Not a git repository");
            }

            string[] lines = facts.StandardOutput.Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < (hasHead ? 5 : 3))
                throw new GitToolException("Cannot determine the git repository layout (unexpected rev-parse output).");

            context.RepoRoot = Path.GetFullPath(lines[0].Trim());
            context.GitDir = Path.GetFullPath(lines[1].Trim());
            context.Shallow = string.Equals(lines[2].Trim(), "true", StringComparison.OrdinalIgnoreCase);

            if (hasHead)
            {
                context.Head = lines[3].Trim();
                string branch = lines[4].Trim();
                context.Detached = branch == "HEAD";
                context.Branch = context.Detached ? null : branch;
            }

            context.InProgress = DetectInProgress(context.GitDir);
            return context;
        }

        /// <summary>Resolves a model-supplied path to a repository-relative, forward-slash path; throws a model-facing error otherwise.</summary>
        public string ResolveRepoPath(string userPath)
        {
            string relative;
            string error;
            if (!TryResolveRepoPath(userPath, out relative, out error))
                throw new GitToolException(error);
            return relative;
        }

        public bool TryResolveRepoPath(string userPath, out string repositoryRelativePath, out string error)
        {
            repositoryRelativePath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(userPath))
            {
                error = "A path must not be empty.";
                return false;
            }

            string absolute;
            if (!_pathResolver.TryResolveFilePath(userPath.Trim(), SolutionDir, out absolute) || string.IsNullOrEmpty(absolute))
            {
                error = $"Cannot resolve path: {userPath}";
                return false;
            }

            try { absolute = Path.GetFullPath(absolute); }
            catch (Exception ex)
            {
                error = $"Invalid path '{userPath}': {ex.Message}";
                return false;
            }

            if (!_pathResolver.IsPathInsideDirectory(absolute, RepoRoot))
            {
                error = $"Path '{userPath}' is outside the repository ({RepoRoot}).";
                return false;
            }

            if (IsInsideGitDirectory(absolute))
            {
                error = $"Path '{userPath}' is inside the .git directory and cannot be used.";
                return false;
            }

            string relative;
            if (!_pathResolver.TryGetRelativePath(absolute, RepoRoot, out relative) || string.IsNullOrEmpty(relative) || relative == ".")
            {
                error = $"Path '{userPath}' cannot be expressed relative to the repository root.";
                return false;
            }

            repositoryRelativePath = relative.Replace('\\', '/');
            return true;
        }

        /// <summary>Paths in responses are solution-relative when possible, otherwise repository-relative.</summary>
        public string ToDisplayPath(string repositoryRelativePath)
        {
            if (string.IsNullOrEmpty(repositoryRelativePath))
                return repositoryRelativePath;

            string absolute = Path.GetFullPath(Path.Combine(RepoRoot, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            string relative;
            if (_pathResolver.IsPathInsideDirectory(absolute, SolutionDir) &&
                _pathResolver.TryGetRelativePath(absolute, SolutionDir, out relative) &&
                !string.IsNullOrEmpty(relative) && relative != ".")
            {
                return relative.Replace('\\', '/');
            }

            return repositoryRelativePath;
        }

        private bool IsInsideGitDirectory(string absolutePath)
        {
            if (_pathResolver.IsPathInsideDirectory(absolutePath, GitDir))
                return true;

            string[] segments = absolutePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string segment in segments)
            {
                if (string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string DetectInProgress(string gitDir)
        {
            if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
                return "merge";
            if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply")))
                return "rebase";
            if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")))
                return "cherry-pick";
            if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD")))
                return "revert";

            return null;
        }
    }
}
