using System;
using System.Security.Authentication;

namespace LMLocal.Infrastructure.Security
{
    /// <summary>
    /// Converts any exception raised during a Test Connection probe into a single user-presentable message.
    /// </summary>
    internal interface ITestConnectionErrorClassifier
    {
        string Classify(Exception ex);
    }

    internal sealed class TestConnectionErrorClassifier : ITestConnectionErrorClassifier
    {
        public string Classify(Exception ex)
        {
            if (ex == null)
                return "Unknown error";

            if (ex is OperationCanceledException)
                return "Request timed out";

            if (ex is CertificatePathException certificatePathException)
                return certificatePathException.Message;

            AuthenticationException authentication = FindInnerException<AuthenticationException>(ex);
            if (authentication != null)
                return $"TLS handshake failed: {authentication.Message}";

            return ex.Message;
        }

        private static T FindInnerException<T>(Exception ex) where T : Exception
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (current is T match)
                    return match;
            }
            return null;
        }
    }
}
