using System;
using System.IO;

namespace LMLocal.Infrastructure.Security
{
    /// <summary>
    /// Thrown when a certificate path is missing, unreadable, or expired.
    /// </summary>
    internal sealed class CertificatePathException : Exception
    {
        public CertificatePathException(string message) : base(message) { }
    }

    /// <summary>
    /// Single owner of "certificate path → trust target" validation and of the user-facing error messages for missing / invalid / expired certificate files.
    /// </summary>
    internal interface ICertificatePathValidator
    {
        /// <summary>
        /// Returns null for an empty path.
        /// </summary>
        CertificateInfo ValidateOrThrow(string certificatePath);
    }

    internal sealed class CertificatePathValidator : ICertificatePathValidator
    {
        private readonly IX509CertificateLoader _certificateLoader;
        private readonly Func<DateTime> _nowProvider;

        public CertificatePathValidator(IX509CertificateLoader certificateLoader, Func<DateTime> nowProvider = null)
        {
            _certificateLoader = certificateLoader ?? throw new ArgumentNullException(nameof(certificateLoader));
            _nowProvider = nowProvider ?? (() => DateTime.UtcNow);
        }

        public CertificateInfo ValidateOrThrow(string certificatePath)
        {
            if (string.IsNullOrWhiteSpace(certificatePath))
                return null;

            if (!File.Exists(certificatePath))
                throw new CertificatePathException($"file not found: {certificatePath}");

            CertificateInfo info = _certificateLoader.LoadCertificateInfo(certificatePath) ?? throw new CertificatePathException($"not a valid certificate: {certificatePath}");
            DateTime now = _nowProvider().ToUniversalTime();
            if (now < info.NotBefore.ToUniversalTime())
                throw new CertificatePathException($"certificate not yet valid: {info.NotBefore.ToUniversalTime():O}");
            if (now > info.NotAfter.ToUniversalTime())
                throw new CertificatePathException($"certificate expired: {info.NotAfter.ToUniversalTime():O}");

            return info;
        }
    }
}