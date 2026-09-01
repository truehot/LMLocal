using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Application.Abstractions.Ports;

namespace LMLocal.Infrastructure.Security
{
    internal interface IServerCertificateTrust : IDisposable
    {
        bool RequiresCustomCertificate();
        bool Validate(X509Certificate2 serverCertificate, SslPolicyErrors errors);
    }

    internal sealed class ServerCertificateTrust : IServerCertificateTrust
    {
        private readonly ISettingsManager _settingsManager;
        private readonly IX509CertificateLoader _certificateLoader;
        private readonly IServerCertificateValidator _certificateValidator;
        private readonly object _lock = new object();

        private volatile CertificateInfo _expectedCertificate;
        private string _cachedCertificatePath;
        private bool _disposed;

        public ServerCertificateTrust(
            ISettingsManager settingsManager,
            IX509CertificateLoader certificateLoader,
            IServerCertificateValidator certificateValidator)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _certificateLoader = certificateLoader ?? throw new ArgumentNullException(nameof(certificateLoader));
            _certificateValidator = certificateValidator ?? throw new ArgumentNullException(nameof(certificateValidator));

            _settingsManager.SettingsChanged += OnSettingsChanged;
        }

        public bool RequiresCustomCertificate()
        {
            ThrowIfDisposed();

            string certificatePath = _settingsManager.Current?.TrustedServerCertificatePath;

            if (_expectedCertificate != null &&
                string.Equals(_cachedCertificatePath, certificatePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(certificatePath))
            {
                ResetUnderLock();
                return false;
            }

            if (!File.Exists(certificatePath))
            {
                InternalLogger.Warn($"TrustedServerCertificatePath '{certificatePath}' does not exist; falling back to default trust.");
                return false;
            }

            CertificateInfo certificateInfo = _certificateLoader.LoadCertificateInfo(certificatePath);
            if (certificateInfo == null)
            {
                InternalLogger.Warn($"TrustedServerCertificatePath '{certificatePath}' could not be loaded; falling back to default trust.");
                return false;
            }

            lock (_lock)
            {
                if (_expectedCertificate != null &&
                    string.Equals(_cachedCertificatePath, certificatePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                _expectedCertificate = certificateInfo;
                _cachedCertificatePath = certificatePath;
                return true;
            }
        }

        public bool Validate(X509Certificate2 serverCertificate, SslPolicyErrors errors)
        {
            return _certificateValidator.Validate(serverCertificate, errors, _expectedCertificate);
        }

        private void OnSettingsChanged(AppSettings settings)
        {
            string newPath = settings?.TrustedServerCertificatePath;

            lock (_lock)
            {
                if (!string.Equals(_cachedCertificatePath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    _expectedCertificate = null;
                    _cachedCertificatePath = null;
                }
            }
        }

        private void ResetUnderLock()
        {
            lock (_lock)
            {
                _expectedCertificate = null;
                _cachedCertificatePath = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _settingsManager.SettingsChanged -= OnSettingsChanged;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ServerCertificateTrust));
            }
        }
    }
}
