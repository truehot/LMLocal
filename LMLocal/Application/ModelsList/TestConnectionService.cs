using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.Security;

namespace LMLocal.Application.ModelsList
{
    /// <summary>
    /// Orchestrates a Test Connection probe: resolves the provider-specific
    /// endpoint, delegates the HTTP call (with an optional certificate path) to
    /// the adapter, and classifies any failure into a user-presentable message.
    /// </summary>
    internal interface ITestConnectionService
    {
        Task<TestConnectionResult> TestAsync(
            string providerName,
            string baseUrl,
            string apiKey,
            string certificatePath,
            CancellationToken cancellationToken);
    }

    internal sealed class TestConnectionService : ITestConnectionService
    {
        private readonly IOpenApiAdapter _openApiAdapter;
        private readonly ITestConnectionErrorClassifier _errorClassifier;

        public TestConnectionService(IOpenApiAdapter openApiAdapter, ITestConnectionErrorClassifier errorClassifier)
        {
            _openApiAdapter = openApiAdapter ?? throw new ArgumentNullException(nameof(openApiAdapter));
            _errorClassifier = errorClassifier ?? throw new ArgumentNullException(nameof(errorClassifier));
        }

        public async Task<TestConnectionResult> TestAsync(
            string providerName, string baseUrl, string apiKey, string certificatePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return TestConnectionResult.Fail("Base URL is required");

            baseUrl = baseUrl.TrimEnd('/');

            try
            {
                ModelProvider provider = ProviderResolver.ResolveProvider(providerName);
                string endpoint = ProviderResolver.GetListModelsEndpoint(provider);

                await _openApiAdapter.ListModelsRawAsync(endpoint, baseUrl, apiKey, cancellationToken, certificatePath);

                return TestConnectionResult.Ok();
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Test connection failed for {providerName} at {baseUrl}", ex);
                return TestConnectionResult.Fail(_errorClassifier.Classify(ex));
            }
        }
    }
}
