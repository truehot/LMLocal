using System;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Provides read-only access to VS solution information.
    /// Data is cached after async initialization from UI thread.
    /// Automatically invalidates cache when solution changes.
    /// Safe to call from any thread after initialization.
    /// </summary>
    internal interface IVsDependencies
    {
        /// <summary>
        /// Gets the cached solution directory.
        /// Safe to call from any thread after InitializeAsync.
        /// </summary>
        string GetSolutionDirectory();

        /// <summary>
        /// Gets the cached IVsSolution instance.
        /// Safe to call from any thread after InitializeAsync.
        /// </summary>
        IVsSolution GetSolution();

        /// <summary>
        /// Gets the file provider for enumerating solution files.
        /// Must be called on UI thread.
        /// </summary>
        ISolutionFileProvider GetFileProvider();

        /// <summary>
        /// Initializes solution information on UI thread.
        /// Must be called before GetSolutionDirectory or GetSolution.
        /// </summary>
        Task InitializeAsync();
    }

    internal class VsDependencies : IVsDependencies, IVsSolutionEvents
    {
        private string _solutionDirectory;
        private IVsSolution _solution;
        private bool _initialized;
        private readonly ISearchResultCache _searchCache;

        public VsDependencies(ISearchResultCache searchCache)
        {
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        }

        public string GetSolutionDirectory()
        {
            if (!_initialized)
                throw new InvalidOperationException("VsDependencies not initialized. Call InitializeAsync() first.");
            if (_solution == null)
                throw new InvalidOperationException("No solution is currently open.");
            if (string.IsNullOrEmpty(_solutionDirectory))
                throw new InvalidOperationException("Solution directory is not available. Ensure a solution is loaded.");
            return _solutionDirectory;
        }

        public IVsSolution GetSolution()
        {
            if (!_initialized)
                throw new InvalidOperationException("VsDependencies not initialized. Call InitializeAsync() first.");
            if (_solution == null)
                throw new InvalidOperationException("No solution is currently open.");

            return _solution;
        }

        public ISolutionFileProvider GetFileProvider()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!_initialized)
                throw new InvalidOperationException("VsDependencies not initialized. Call InitializeAsync() first.");
            if (_solution == null)
                throw new InvalidOperationException("No solution is currently open.");

            return new SolutionFileProvider(_solution);
        }

        public async Task InitializeAsync()
        {
            if (_initialized)
                return;

            // Switch to UI thread to access VS services
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                _solution = (IVsSolution)ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution));

                if (_solution != null)
                {
                    if (_solution.GetSolutionInfo(out string solutionDirectory, out _, out _) == VSConstants.S_OK)
                    {
                        _solutionDirectory = solutionDirectory?.TrimEnd('\\');
                    }

                    _solution.AdviseSolutionEvents(this, out _);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"VsDependencies: Failed to initialize solution information: {ex}");
                throw new InvalidOperationException("Failed to initialize solution information.", ex);
            }

            _initialized = true;
        }

        // IVsSolutionEvents implementation - invalidate cache when solution changes
        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _solution = (IVsSolution)ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution));

            if (_solution != null)
            {
                if (_solution.GetSolutionInfo(out string solutionDirectory, out _, out _) == VSConstants.S_OK)
                {
                    _solutionDirectory = solutionDirectory?.TrimEnd('\\');
                }
            }

            _searchCache.Clear();
            return VSConstants.S_OK;
        }

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _solution = null;
            _solutionDirectory = null;
            _searchCache.Clear();

            return VSConstants.S_OK;
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            return VSConstants.S_OK;
        }

    }
}
