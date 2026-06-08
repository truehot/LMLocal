using System;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Instructions;
using LMLocal.Infrastructure.Mcp;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Providers;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.WebView;
using LMLocal.Services.Tool;
using Microsoft.Extensions.DependencyInjection;

namespace LMLocal.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Central configuration point for the dependency injection container.
    /// Initializes asynchronously without blocking the UI thread.
    /// Supports graceful cleanup on extension shutdown.
    /// </summary>
    public static class ServiceConfiguration
    {
        private static IServiceProvider _serviceProvider;
        private static readonly object _syncLock = new object();

        /// <summary>
        /// Initializes the DI container on a background thread.
        /// </summary>
        public static Task InitializeAsync()
        {
            lock (_syncLock)
            {
                if (_serviceProvider != null) return Task.CompletedTask;
                RegisterServices();
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Register all application services across
        /// </summary>
        private static void RegisterServices()
        {
            lock (_syncLock)
            {
                if (_serviceProvider != null)
                {
                    return;
                }

                var services = new ServiceCollection();

                RegisterSettings(services);

                _serviceProvider = services.BuildServiceProvider();
            }
        }

        /// <summary>
        /// Singleton - single instance for entire application lifecycle.
        /// </summary>
        private static void RegisterSettings(IServiceCollection services)
        {
            services.AddSingleton<IInstructionsManager, InstructionsManager>();
            services.AddSingleton<IMcpConfigManager, McpConfigManager>();
            services.AddSingleton<IProvidersConfigManager, ProvidersConfigManager>();

            services.AddSingleton<IMcpToolManager, McpToolManager>();

            services.AddSingleton<ISettingsManager, SettingsManager>();
            services.AddSingleton<IPathResolver, PathResolver>();
            services.AddSingleton<IVsDependencies, VsDependencies>();
            services.AddSingleton<IUiThreadGuard, VsUiThreadGuard>();
            services.AddTransient<IVsSolutionFilesScanner, VsSolutionFilesScanner>();

            services.AddTransient<ISolutionSearch, SolutionSearch>();
            services.AddTransient<IActiveDocument, ActiveDocument>();
            services.AddTransient<IFileLinesReader, FileLinesReader>();
            services.AddTransient<IFindFilesByName, FindFilesByName>();
            services.AddTransient<IGetSolutionOverview, GetSolutionOverview>();
            services.AddTransient<IFindSymbolReferences, FindSymbolReferences>();
            services.AddTransient<IListDirectoryContents, ListDirectoryContents>();

            services.AddSingleton<IFileSystem, DefaultFileSystem>();
            services.AddSingleton<IHttpClientWrapper, HttpClientWrapper>();
            services.AddSingleton<IChatPersistenceService, ChatPersistenceService>();
            services.AddSingleton<IChatHistoryManager, ChatHistoryManager>();
            services.AddTransient<IStreamProcessorFactory, StreamProcessorFactory>();

            services.AddSingleton<IBuiltInVsToolProvider, BuiltInVsToolProvider>();
            services.AddSingleton<ICompositeToolFactory, CompositeToolFactory>();

            services.AddSingleton<IOpenApiAdapter, OpenApiAdapter>();
            services.AddSingleton<IModelsListService, ModelsListService>();

            services.AddSingleton<IWebViewBridgeFactory, WebViewBridgeFactory>();
            services.AddSingleton<IChatSessionOrchestratorFactory, ChatSessionOrchestratorFactory>();
            services.AddSingleton<IActiveModelContext, ActiveModelContext>();
            services.AddSingleton<IHistoryCompactor, HistoryCompactor>();

            services.AddSingleton<IChatSessionOrchestrator, ChatSessionOrchestrator>();
            services.AddSingleton<IChatStreamService, ChatStreamService>();
            services.AddSingleton<IToolExecutionManager, ToolExecutionManager>();
            services.AddSingleton<ISessionManager, SessionManager>();
        }

        /// <summary>
        /// Retrieves a registered service from the container.
        /// </summary>
        public static T GetService<T>() where T : class
        {
            lock (_syncLock)
            {
                if (_serviceProvider == null)
                {
                    throw new InvalidOperationException(
                        "Service container is not initialized. Call InitializeAsync() or Initialize() first.");
                }

                var service = _serviceProvider.GetService(typeof(T)) as T ?? throw new InvalidOperationException(
                        $"Service '{typeof(T).Name}' is not registered in the DI container.");
                return service;
            }
        }

        /// <summary>
        /// Checks if the DI container has been initialized.
        /// </summary>
        public static bool IsInitialized
        {
            get
            {
                lock (_syncLock)
                {
                    return _serviceProvider != null;
                }
            }
        }

        /// <summary>
        /// Cleans up the DI container and disposes all singleton services.
        /// </summary>
        public static void Cleanup()
        {
            lock (_syncLock)
            {
                (_serviceProvider as IDisposable)?.Dispose();
                _serviceProvider = null;
            }
        }
    }
}
