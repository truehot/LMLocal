using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Security
{
    /// <summary>
    /// Minimal description of an X.509 certificate used for server trust decisions.
    /// </summary>
    internal sealed class CertificateInfo
    {
        public CertificateInfo(string thumbprint, DateTime notBefore, DateTime notAfter, string subject, string issuer)
        {
            Thumbprint = thumbprint;
            NotBefore = notBefore;
            NotAfter = notAfter;
            Subject = subject;
            Issuer = issuer;
        }

        public string Thumbprint { get; }
        public DateTime NotBefore { get; }
        public DateTime NotAfter { get; }
        public string Subject { get; }
        public string Issuer { get; }

        public static CertificateInfo FromCertificate(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            return new CertificateInfo(
                certificate.Thumbprint,
                certificate.NotBefore,
                certificate.NotAfter,
                certificate.Subject,
                certificate.Issuer);
        }
    }

    /// <summary>
    /// Loads X.509 certificate metadata from DER/PKCS#12 or PEM encoded files.
    /// </summary>
    internal interface IX509CertificateLoader
    {
        CertificateInfo LoadCertificateInfo(string path);
    }

    internal sealed class X509CertificateLoader : IX509CertificateLoader
    {
        private const string PemBeginMarker = "-----BEGIN CERTIFICATE-----";
        private const string PemEndMarker = "-----END CERTIFICATE-----";

        public CertificateInfo LoadCertificateInfo(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (!TryLoadCertificate(path, out X509Certificate2 certificate))
            {
                return null;
            }

            try
            {
                return CertificateInfo.FromCertificate(certificate);
            }
            catch (Exception ex) when (IsExpectedLoadFailure(ex))
            {
                InternalLogger.Warn($"Certificate metadata could not be read from '{path}': {ex.Message}");
                return null;
            }
            finally
            {
                certificate.Dispose();
            }
        }

        /// <summary>
        /// Loads a certificate from a DER or PEM encoded file. Returns false for unsupported content.
        /// </summary>
        internal bool TryLoadCertificate(string path, out X509Certificate2 certificate)
        {
            certificate = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                certificate = new X509Certificate2(path);
                return true;
            }
            catch (Exception ex) when (IsExpectedLoadFailure(ex))
            {
                InternalLogger.Debug($"Certificate file '{path}' is not a DER/PKCS#12 file; trying PEM. {ex.Message}");
            }

            try
            {
                byte[] derBytes = DecodePemCertificate(File.ReadAllText(path));
                if (derBytes == null)
                {
                    return false;
                }

                certificate = new X509Certificate2(derBytes);
                return true;
            }
            catch (Exception ex) when (IsExpectedLoadFailure(ex))
            {
                InternalLogger.Warn($"Certificate file '{path}' could not be loaded: {ex.Message}");
                certificate = null;
                return false;
            }
        }

        /// <summary>
        /// Extracts the DER bytes from a PEM encoded certificate block, or null if no block is found.
        /// </summary>
        internal static byte[] DecodePemCertificate(string pemText)
        {
            if (string.IsNullOrEmpty(pemText))
            {
                return null;
            }

            int begin = pemText.IndexOf(PemBeginMarker, StringComparison.Ordinal);
            if (begin < 0)
            {
                return null;
            }

            int contentStart = begin + PemBeginMarker.Length;
            int end = pemText.IndexOf(PemEndMarker, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                return null;
            }

            string base64 = pemText.Substring(contentStart, end - contentStart);
            base64 = base64.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                InternalLogger.Debug($"PEM certificate block contains invalid base64: {ex.Message}");
                return null;
            }
        }

        private static bool IsExpectedLoadFailure(Exception ex)
        {
            return ex is CryptographicException
                || ex is FormatException
                || ex is IOException
                || ex is UnauthorizedAccessException
                || ex is ArgumentException;
        }
    }
}
