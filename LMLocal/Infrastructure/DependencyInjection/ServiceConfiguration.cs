using System;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.Http;
using LMLocal.Infrastructure.Instructions;
using LMLocal.Infrastructure.Mcp;
using LMLocal.Infrastructure.Persistence;
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
        /// Asynchronously initializes the DI container on a background thread.
        /// Safe to call multiple times; subsequent calls return immediately.
        /// Does not block the UI thread.
        /// </summary>
        public static async Task InitializeAsync()
        {
            lock (_syncLock)
            {
                if (_serviceProvider != null) return;
                RegisterServices();
            }

            // Load MCP configuration and initialize servers
            try
            {
                var mcpConfigManager = GetService<IMcpConfigManager>();
                var mcpToolManager = GetService<IMcpToolManager>();

                var config = await mcpConfigManager.GetAsync().ConfigureAwait(false);
                if (config != null)
                {
                    await mcpToolManager.RefreshServersAsync(config, System.Threading.CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw - MCP initialization failure shouldn't block app startup
                LMLocal.Common.InternalLogger.Warn($"MCP initialization failed: {ex.Message}");
            }
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

            // MCP Tool Manager - lazy initialization wrapper to load config on first access
            services.AddSingleton<IMcpToolManager>(sp =>
            {
                var mcpManager = new McpToolManager(
                    sp.GetRequiredService<IMcpConfigManager>(),
                    sp.GetRequiredService<IFileSystem>(),
                    sp.GetRequiredService<IHttpClientWrapper>(),
                    sp.GetRequiredService<ISettingsManager>());

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var configManager = sp.GetRequiredService<IMcpConfigManager>();
                        var config = await configManager.GetAsync(System.Threading.CancellationToken.None)
                            .ConfigureAwait(false);
                        if (config != null)
                        {
                            await mcpManager.RefreshServersAsync(config, System.Threading.CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        LMLocal.Common.InternalLogger.Warn($"MCP lazy initialization failed: {ex.Message}");
                    }
                });

                return mcpManager;
            });

            services.AddSingleton<ISettingsManager, SettingsManager>();
            services.AddSingleton<IPathResolver, PathResolver>();
            services.AddSingleton<IVsDependencies, VsDependencies>();
            services.AddSingleton<IUiThreadGuard, VsUiThreadGuard>();
            services.AddTransient<IVsSolutionFilesScanner, VsSolutionFilesScanner>();

            services.AddTransient<ISolutionSearchTool, SolutionSearchTool>();
            services.AddTransient<IActiveDocumentTool, ActiveDocumentTool>();
            services.AddTransient<IFileLinesReaderTool, FileLinesReaderTool>();
            services.AddTransient<IFindFilesByNameTool, FindFilesByNameTool>();
            services.AddTransient<IGetSolutionOverviewTool, GetSolutionOverviewTool>();
            services.AddTransient<IFindSymbolReferencesTool, FindSymbolReferences>();

            services.AddSingleton<IFileSystem, DefaultFileSystem>();
            services.AddSingleton<IHttpClientWrapper, HttpClientWrapper>();
            services.AddSingleton<IChatPersistenceService, ChatPersistenceService>();
            services.AddSingleton<IChatHistoryManager, ChatHistoryManager>();
            services.AddTransient<IStreamProcessorFactory, StreamProcessorFactory>();

            // Built-in tool factory
            services.AddSingleton<IBuiltInVsToolProvider, BuiltInVsToolProvider>();

            // Composite tool factory combines built-in and MCP tools
            services.AddSingleton<ICompositeToolFactory>(sp =>
                new CompositeToolFactory(
                    sp.GetRequiredService<IBuiltInVsToolProvider>(),
                    sp.GetRequiredService<IMcpToolManager>(),
                    sp.GetRequiredService<ISettingsManager>()));

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
