using EnvDTE;
using EnvDTE80;
using LMLocal.Core.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Provides read-only access to VS solution information.
    /// </summary>
    internal interface IVsDependencies
    {
        bool IsSolutionOpen { get; }

        event Action SolutionOpened;
        event Action SolutionClosed;

        /// <summary>
        /// Gets the cached solution directory.
        /// </summary>
        string GetSolutionDirectory();

        /// <summary>
        /// Gets the cached IVsSolution instance.
        /// </summary>
        IVsSolution GetSolution();

        /// <summary>
        /// Gets the cached DTE2 instance (EnvDTE automation model).
        /// Must be called on UI thread.
        /// </summary>
        DTE2 GetDTE();


        /// <summary>
        /// Initializes solution information on UI thread.
        /// </summary>
        Task InitializeAsync();

    }

    internal class VsDependencies : IVsDependencies, IVsSolutionEvents
    {
        private string _solutionDirectory;
        private IVsSolution _solution;
        private bool _initialized;
        private uint _solutionEventsCookie;
        private readonly ISearchResultCache _searchCache;
        private DTE2 _dte;


        public event Action SolutionOpened;
        public event Action SolutionClosed;

        public VsDependencies(ISearchResultCache searchCache)
        {
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        }

        public bool IsSolutionOpen
        {
            get
            {
                return !string.IsNullOrEmpty(_solutionDirectory) && _solution != null;
            }
        }

        public string GetSolutionDirectory()
        {
            return _solutionDirectory;
        }

        public IVsSolution GetSolution()
        {
            return _solution;
        }

        public DTE2 GetDTE()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_dte == null)
                _dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            return _dte;
        }


        public async Task InitializeAsync()
        {
            if (_initialized)
                return;


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

                    _solution.AdviseSolutionEvents(this, out _solutionEventsCookie);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"VsDependencies: Failed to initialize solution information: {ex}");
                throw new InvalidOperationException("Failed to initialize solution information.", ex);
            }

            _initialized = true;

        }

        public void Uninitialize()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (_solution != null && _solutionEventsCookie != 0)
            {
                try
                {
                    
                    _solution.UnadviseSolutionEvents(_solutionEventsCookie);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"VsDependencies: Failed to unadvise solution events: {ex}");
                }
                _solutionEventsCookie = 0;
            }
        }

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
            _dte = null;
            _searchCache.Clear();
            SolutionOpened?.Invoke();
            return VSConstants.S_OK;
        }

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _solution = null;
            _solutionDirectory = null;
            _dte = null;
            _searchCache.Clear();

            SolutionClosed?.Invoke();
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
