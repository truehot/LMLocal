using System;
using System.Net;
using LMLocal.Core.Models;

namespace LMLocal.Infrastructure.HttpWrapper
{
    /// <summary>
    /// Raises the .NET Framework per-host connection limit for the hosts LM Local talks to.
    /// </summary>
    internal static class ConnectionLimits
    {
        /// <summary>
        /// Concurrent connections allowed per host: the maximum parallel SubAgent width plus headroom
        /// for the main chat and autocompletion.
        /// </summary>
        internal const int PerHost = SubAgentsConfig.MaxParallelLimit + 2;

        /// <summary>
        /// Ensures the host of <paramref name="requestUri"/> allows at least <see cref="PerHost"/> connections.
        /// </summary>
        internal static void EnsureForHost(Uri requestUri)
        {
            if (requestUri == null || !requestUri.IsAbsoluteUri)
                return;

            ServicePoint servicePoint = ServicePointManager.FindServicePoint(requestUri);
            if (servicePoint.ConnectionLimit < PerHost)
            {
                servicePoint.ConnectionLimit = PerHost;
            }
        }
    }
}
