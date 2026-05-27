using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LMLocal.Ipc
{
    /**
     * Listens to VS shell startup and starts IPC server when VS is ready (zombie state is false).
     * This allows us to defer starting the IPC server until VS is fully initialized, which can improve reliability and performance.
     */
    internal sealed class VsStartupListener : IVsShellPropertyEvents, IDisposable
    {
        private readonly AsyncPackage package;
        private readonly CancellationToken token;
        private IVsShell shell;
        private uint cookie;
        private bool disposed;
        private bool ipcStarted;

        public VsStartupListener(AsyncPackage package, CancellationToken token)
        {
            this.package = package;
            this.token = token;
        }

        public async Task InitializeAsync()
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            token.ThrowIfCancellationRequested();

            shell = await package.GetServiceAsync(typeof(SVsShell)) as IVsShell;
            if (shell == null)
            {
                Debug.WriteLine("VsStartupListener: SVsShell service not available");
                return;
            }

            shell.AdviseShellPropertyChanges(this, out cookie);
            Debug.WriteLine($"VsStartupListener: subscribed, cookie={cookie}");

            shell.GetProperty((int)__VSSPROPID.VSSPROPID_Zombie, out var isZombieObj);
            if (isZombieObj is bool isZombie && !isZombie)
            {
                Debug.WriteLine("VsStartupListener: current zombie state is false, starting IPC now");
                StartIpc();
            }
            else
            {
                Debug.WriteLine($"VsStartupListener: current zombie state = {isZombieObj}, waiting for change");
            }
        }

        public int OnShellPropertyChange(int propid, object var)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (propid == (int)__VSSPROPID.VSSPROPID_Zombie && var is bool isZombie && isZombie == false)
            {
                Debug.WriteLine("VsStartupListener: zombie transitioned to false, starting IPC");
                StartIpc();
            }
            return VSConstants.S_OK;
        }

        private void StartIpc()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (ipcStarted) return;
            ipcStarted = true;

            VsIpcServer.Start(package, token);

            Uninitialize();
        }

        public void Uninitialize()
        {
            if (disposed) return;

            ThreadHelper.ThrowIfNotOnUIThread();

            if (shell != null && cookie != 0)
            {
                shell.UnadviseShellPropertyChanges(cookie);
                Debug.WriteLine("VsStartupListener: unsubscribed");
                cookie = 0;
                shell = null;
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                try
                {
                    _ = this.package.JoinableTaskFactory.RunAsync(async delegate
                    {
                        await package.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                        Uninitialize();
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"VsStartupListener Dispose error: {ex.Message}");
                }
                disposed = true;
            }
        }
    }
}
