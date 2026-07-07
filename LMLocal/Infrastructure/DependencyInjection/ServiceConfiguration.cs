using System;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Instructions;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Providers;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Syntax;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot.Infrastructure;
using LMLocal.Infrastructure.Tooling.Mcp;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.WebView;
using LMLocal.Services.Tool;
using Microsoft.Extensions.DependencyInjection;
namespace LMLocal.Infrastructure.DependencyInjection
{

    /// Central configuration point for the dependency injection container.
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
            services.AddSingleton<IToolsConfigManager, ToolsConfigManager>();

            services.AddSingleton<IMcpToolManager, McpToolManager>();

            services.AddSingleton<ISettingsManager, SettingsManager>();
            services.AddSingleton<IPathResolver, PathResolver>();
            services.AddSingleton<IVsDependencies, VsDependencies>();
            services.AddSingleton<IUiThreadGuard, VsUiThreadGuard>();
            services.AddSingleton<ISearchResultCache, SearchResultCache>();

            services.AddTransient<ISolutionFileProvider, SolutionFileProvider>();
            services.AddTransient<IVsSolutionFilesScanner, VsSolutionFilesScanner>();

            services.AddSingleton<IFileLockManager, FileLockManager>();

            services.AddSingleton<ISnapshotPathsFactory, SnapshotPathsFactory>();
            services.AddSingleton<ISnapshotSolutionEvents, SnapshotSolutionEvents>();
            services.AddSingleton<ISnapshotManager, SnapshotManager>();

            services.AddTransient<IBuiltInTool, BuildSolution>();
            services.AddTransient<IBuiltInTool, CreateFile>();
            services.AddTransient<IBuiltInTool, DeleteFile>();
            services.AddTransient<IBuiltInTool, FindFiles>();
            services.AddTransient<IBuiltInTool, GetSymbolInfo>();
            services.AddTransient<IBuiltInTool, FormatDocument>();
            services.AddTransient<IBuiltInTool, GetActiveDocument>();
            services.AddTransient<IBuiltInTool, GetSolutionOverview>();
            services.AddTransient<IBuiltInTool, InsertFileLines>();
            services.AddTransient<IBuiltInTool, ListDirectory>();
            services.AddTransient<IBuiltInTool, OptimizeUsings>();
            services.AddTransient<IBuiltInTool, ReadFileLines>();
            services.AddTransient<IBuiltInTool, ReplaceFileLines>();
            services.AddTransient<IBuiltInTool, ReplaceFileContent>();
            services.AddTransient<IBuiltInTool, RunTests>();
            services.AddTransient<IBuiltInTool, SearchFileContent>();
            services.AddTransient<IBuiltInTool, SetFileProjectStatus>();

            services.AddTransient<IGetActiveDocument, GetActiveDocument>();

            services.AddSingleton<IFileSystem, DefaultFileSystem>();
            services.AddSingleton<ISyntaxChecker, CSharpSyntaxChecker>();
            services.AddSingleton<IHttpClientWrapper, HttpClientWrapper>();
            services.AddSingleton<IChatPersistenceService, ChatPersistenceService>();
            services.AddSingleton<IChatHistoryManager, ChatHistoryManager>();
            services.AddTransient<IStreamProcessorFactory, StreamProcessorFactory>();

            services.AddSingleton<IBuiltInVsToolProvider, BuiltInVsToolProvider>();
            services.AddSingleton<ICompositeToolFactory, CompositeToolProvider>();

            services.AddSingleton<IApiRequestBuilder, ApiRequestBuilder>();
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
