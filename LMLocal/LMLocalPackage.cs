using System;
using System.Runtime.InteropServices;
using System.Threading;
using LMLocal.Commands;
using LMLocal.Infrastructure.DependencyInjection;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.VisualStudio;
using LMLocal.Ipc;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace LMLocal
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
#pragma warning disable VSSDK003 // tool window is created on-demand synchronously; async construction not required
    [ProvideToolWindow(typeof(MainWindow))]
#pragma warning restore VSSDK003
    [ProvideBindingPath()]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class LMLocalPackage : AsyncPackage
    {
        /// <summary>
        /// LMLocalPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "d2b7ae34-5422-4af0-bfc3-6129fd2641d0";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

#if DEBUG
            // Start IPC first so it does not depend on the rest of initialization succeeding.
            if (Environment.GetEnvironmentVariable("LMLocal_IPC") == "1")
            {
                var startupListener = new VsStartupListener(this, DisposalToken);
                DisposalToken.Register(() => startupListener?.Dispose());
                DisposalToken.Register(() => _ = VsIpcServer.StopAsync());
                await startupListener.InitializeAsync();
            }
#endif

            // Initialize dependency injection container early so other components can resolve services.
            await ServiceConfiguration.InitializeAsync();

            // Package-owned UI service (static singleton, mirrors ShowMainWindow.Instance).
            ChatInjectionService.Initialize(this);
            //load dependencies for solution
            var vsDependencies = ServiceConfiguration.GetService<IVsDependencies>();
            await vsDependencies.InitializeAsync();

            var snapshotSolutionEvents = ServiceConfiguration.GetService<ISnapshotSolutionEvents>();
            snapshotSolutionEvents.Initialize();

            // Ask before Visual Studio is closed while an AI request is still running.
            var closeGuard = ServiceConfiguration.GetService<ICloseWhileGeneratingGuard>();
            closeGuard.Initialize(); // owned and disposed by the DI container in ServiceConfiguration.Cleanup()

            await ShowMainWindow.InitializeAsync(this);
            await MainWindowCommand.InitializeAsync(this);
            await SendToLMFromEditorCommand.InitializeAsync(this);
            await SendAllOpenFilesCommand.InitializeAsync(this);
            await SendActiveFilesCommand.InitializeAsync(this);
            await SendToLMFromSolutionExplorerCommand.InitializeAsync(this);
            await BaseCodeCommand.InitializeAsync<ReviewCommand>(this);
            await BaseCodeCommand.InitializeAsync<BugFixCommand>(this);
            await BaseCodeCommand.InitializeAsync<AddUnitTestsCommand>(this);
            await BaseCodeCommand.InitializeAsync<AddSummaryCommand>(this);
            await BaseCodeCommand.InitializeAsync<ImproveCommand>(this);
            await BaseCodeCommand.InitializeAsync<ExplainCommand>(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ChatInjectionService.Reset();
                ServiceConfiguration.Cleanup();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
