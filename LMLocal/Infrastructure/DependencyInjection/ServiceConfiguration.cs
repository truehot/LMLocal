using System;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Autocompletions;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.ModelsList;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Usage;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Autocompletions;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Instructions;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.ModelsConfig;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Providers;
using LMLocal.Infrastructure.RecentModels;
using LMLocal.Infrastructure.Security;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Streaming;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Syntax;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Js;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot.Infrastructure;
using LMLocal.Infrastructure.VisualStudio;
using LMLocal.Infrastructure.Tooling.Mcp;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.WebView;
using LMLocal.Infrastructure.WebView.Controllers;
using LMLocal.Infrastructure.WebView.Environment;
using LMLocal.Infrastructure.WebView.Hosting;
using LMLocal.Infrastructure.WebView.Initialization;
using LMLocal.Infrastructure.WebView.Navigation;
using LMLocal.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
namespace LMLocal.Infrastructure.DependencyInjection
{
    /// <summary>
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
            services.AddSingleton<IInstructionDefaultsProvider, FileInstructionDefaultsProvider>();
            services.AddSingleton<IInstructionsManager, InstructionsManager>();
            services.AddSingleton<IMcpConfigManager, McpConfigManager>();
            services.AddSingleton<IProvidersConfigManager, ProvidersConfigManager>();
            services.AddSingleton<IModelsConfigManager, ModelsConfigManager>();
            services.AddSingleton<IToolsConfigManager, ToolsConfigManager>();
            services.AddSingleton<IMcpToolManager, McpToolManager>();
            services.AddSingleton<ISettingsManager, SettingsManager>();
            services.AddSingleton<IRecentModelsManager, RecentModelsManager>();
            services.AddSingleton<IPathResolver, PathResolver>();
            services.AddSingleton<IMissingFileHint, MissingFileHint>();
            services.AddSingleton<IVsDependencies, VsDependencies>();
            services.AddSingleton<IUiThreadGuard, VsUiThreadGuard>();
            services.AddSingleton<ISearchResultCache, SearchResultCache>();
            services.AddSingleton<IToolFileOpener, ToolFileOpener>();
            services.AddSingleton<ICloseWhileGeneratingGuard, CloseWhileGeneratingGuard>();
            services.AddSingleton<ITaskbarAttentionNotifier, TaskbarAttentionNotifier>();
            services.AddSingleton<IAttentionNotifier>(sp => sp.GetRequiredService<ITaskbarAttentionNotifier>());
            services.AddSingleton<IDebuggerContextTracker, DebuggerContextService>();
            services.AddSingleton<IDebuggerContextService>(sp => sp.GetRequiredService<IDebuggerContextTracker>());
            services.AddSingleton<IAutocompletionsConfigManager, AutocompletionsConfigManager>();

            services.AddTransient<ISolutionFileProvider, SolutionFileProvider>();
            services.AddTransient<IVsSolutionFilesScanner, VsSolutionFilesScanner>();
            services.AddTransient<IJsConfigResolver, JsConfigResolver>();

            services.AddSingleton<IFileLockManager>(FileLockManager.Shared);

            services.AddSingleton<ISnapshotPathsFactory, SnapshotPathsFactory>();
            services.AddSingleton<ISnapshotSolutionEvents, SnapshotSolutionEvents>();
            services.AddSingleton<ISnapshotManager, SnapshotManager>();
            services.AddTransient<IChangesReader, ChangesService>();

            services.AddTransient<IBuiltInTool, BuildSolution>();
            services.AddTransient<IBuiltInTool, CreateFile>();
            services.AddTransient<IBuiltInTool, DeleteFile>();
            services.AddTransient<IBuiltInTool, FindFiles>();
            services.AddTransient<IBuiltInTool, GetSymbolInfoJs>();
            services.AddTransient<IBuiltInTool, GetSymbolInfo>();
            services.AddTransient<IBuiltInTool, InspectType>();
            services.AddTransient<IBuiltInTool, FormatDocument>();
            services.AddTransient<IBuiltInTool, GetActiveDocument>();
            services.AddTransient<IBuiltInTool, GetChangesDiff>();
            services.AddTransient<IBuiltInTool, GitRead>();
            services.AddTransient<IBuiltInTool, GetSolutionOverview>();
            services.AddTransient<IBuiltInTool, InsertFileLines>();
            services.AddTransient<IBuiltInTool, ListDirectory>();
            services.AddTransient<IBuiltInTool, ReadFileLines>();
            services.AddTransient<IBuiltInTool, ReplaceFileLines>();
            services.AddTransient<IBuiltInTool, ReplaceFileContent>();
            services.AddTransient<IBuiltInTool, RunTests>();
            services.AddTransient<IBuiltInTool, SearchFileContent>();
            services.AddTransient<IBuiltInTool, SetFileProjectStatus>();
            services.AddTransient<IBuiltInTool, SearchKnowledge>();
            services.AddTransient<IBuiltInTool, SearchChatHistory>();

            services.AddSingleton<TodoWrite>();
            services.AddSingleton<ITodoTool>(sp => sp.GetRequiredService<TodoWrite>());
            services.AddSingleton<IBuiltInTool>(sp => sp.GetRequiredService<TodoWrite>());

            services.AddTransient<IGetActiveDocument, GetActiveDocument>();

            services.AddSingleton<IFileSystem, DefaultFileSystem>();
            services.AddSingleton<CSharpSyntaxChecker>();
            services.AddSingleton<VisualBasicSyntaxChecker>();
            services.AddSingleton<ISyntaxCheckerFactory>(sp =>
                new SyntaxCheckerFactory(
                    sp.GetRequiredService<CSharpSyntaxChecker>(),
                    sp.GetRequiredService<VisualBasicSyntaxChecker>()));
            services.AddSingleton<IX509CertificateLoader, X509CertificateLoader>();
            services.AddSingleton<IServerCertificateValidator, ServerCertificateValidator>();
            services.AddSingleton<ICertificatePathValidator, CertificatePathValidator>();
            services.AddSingleton<IHttpClientHandlerFactory, HttpClientHandlerFactory>();
            services.AddSingleton<ITemporaryHttpClientFactory, TemporaryHttpClientFactory>();
            services.AddSingleton<ITestConnectionErrorClassifier, TestConnectionErrorClassifier>();
            services.AddSingleton<IServerCertificateTrust, ServerCertificateTrust>();
            services.AddSingleton<IHttpClientWrapper, HttpClientWrapper>();
            services.AddSingleton<IChatPersistenceService, ChatPersistenceService>();
            services.AddSingleton<IChatPersistenceFactory, ChatPersistenceServiceFactory>();
            services.AddSingleton<IProviderPolicy, ProviderPolicy>();
            services.AddSingleton<IChatHistoryManager, ChatHistoryManager>();
            services.AddSingleton<IChatHistoryService, ChatHistoryService>();
            services.AddSingleton<ITimeProvider, SystemTimeProvider>();
            services.AddSingleton<UsageStore>(sp => new UsageStore(
                sp.GetRequiredService<ISettingsManager>(),
                sp.GetRequiredService<IFileSystem>(),
                sp.GetRequiredService<IFileLockManager>(),
                () => sp.GetRequiredService<IVsDependencies>().GetSolutionDirectory(),
                sp.GetRequiredService<ITimeProvider>()));
            services.AddSingleton<IUsageReader>(sp => sp.GetRequiredService<UsageStore>());
            services.AddSingleton<IUsageRecorder>(sp => sp.GetRequiredService<UsageStore>());
            services.AddSingleton<IUsageStatsService, UsageStatsService>();
            services.AddSingleton<IToolCallArgumentsGuardFactory, ToolCallArgumentsGuardFactory>();
            services.AddTransient<IStreamProcessorFactory, StreamProcessorFactory>();

            services.AddSingleton<IBuiltInVsToolProvider, BuiltInVsToolProvider>();

            services.AddSingleton<Func<ISubAgentsService>>(sp => () => sp.GetRequiredService<ISubAgentsService>());

            services.AddSingleton<ISubAgentsCatalog>(sp => (ISubAgentsCatalog)sp.GetRequiredService<ISubAgentsConfigManager>());
            services.AddSingleton<IToolQueueProvider, ToolQueueProvider>();

            services.AddSingleton<ISubAgentsToolSource, SubAgentsToolSource>();
            services.AddSingleton<IToolRouter, ToolRouter>();
            services.AddSingleton<IToolResultMarkdownFormatter, ToolResultMarkdownFormatter>();

            services.AddSingleton<IApiRequestBuilder, ApiRequestBuilder>();
            services.AddSingleton<IOpenApiAdapter, OpenApiAdapter>();
            services.AddSingleton<ISubAgentsConfigManager, SubAgentsConfigManager>();
            services.AddSingleton<ISubAgentsService>(sp => new SubAgentsService(
                sp.GetRequiredService<ISettingsManager>(),
                sp.GetRequiredService<IFileSystem>(),
                sp.GetRequiredService<IStreamingRoundService>(),
                () => sp.GetRequiredService<IToolExecutionManager>(),
                sp.GetRequiredService<IToolQueueProvider>(),
                sp.GetRequiredService<IChatPersistenceFactory>(),
                sp.GetRequiredService<IProviderPolicy>(),
                SubAgentsService.DefaultLoopRecoveryEnabled,
                sp.GetRequiredService<IUsageRecorder>(),
                sp.GetRequiredService<ITimeProvider>()));
            services.AddSingleton<ISubAgentPipelineRunner, SubAgentPipelineRunner>();
            services.AddSingleton<ITestConnectionService, TestConnectionService>();
            services.AddSingleton<IAutocompletionsService, AutocompletionsService>();
            services.AddSingleton<IModelsListService, ModelsListService>();

            services.AddSingleton<IWebViewBridgeFactory, WebViewBridgeFactory>();
            services.AddSingleton<ICoreWebView2EnvironmentFactory, DefaultCoreWebView2EnvironmentFactory>();
            services.AddSingleton<IWebViewEnvironmentProvider, WebViewEnvironmentProvider>();
            services.AddSingleton<IWebViewHostObjectRegistrar, WebViewHostObjectRegistrar>();
            services.AddSingleton<IWebViewNavigator, WebViewNavigator>();
            services.AddSingleton<IExternalUriLauncher, ShellExternalUriLauncher>();
            services.AddSingleton<IWebViewInitializer, WebViewInitializer>();
            services.AddSingleton<IActiveModelContext, ActiveModelContext>();
            services.AddSingleton<IContextCalibration, ContextCalibration>();
            services.AddSingleton<IHistoryCompactor, HistoryCompactor>();

            services.AddSingleton<ISubAgentsParallelPolicy, SubAgentsParallelPolicy>();
            services.AddSingleton<ISubAgentsParallelRunner, SubAgentsParallelRunner>();

            services.AddSingleton<ChatUsageWriter>();
            services.AddSingleton<ChatToolRoundRunner>();
            services.AddSingleton<IChatSessionOrchestrator, ChatSessionOrchestrator>();
            services.AddSingleton<IStreamingRoundService, StreamingRoundService>();
            services.AddSingleton<IChatStreamService, ChatStreamService>();
            services.AddSingleton<IToolExecutionManager, ToolExecutionManager>();
            services.AddSingleton<ISessionManager, SessionManager>();

            services.AddSingleton<IInstructionsController, InstructionsController>();
            services.AddSingleton<IProvidersController, ProvidersController>();
            services.AddSingleton<IModelsConfigController, ModelsConfigController>();
            services.AddSingleton<IToolsController, ToolsController>();
            services.AddSingleton<ISubAgentsController, SubAgentsController>();
            services.AddSingleton<ISettingsController>(sp => new SettingsController(sp.GetRequiredService<ISettingsManager>(), sp.GetRequiredService<ITestConnectionService>(), sp.GetRequiredService<ICertificatePathValidator>()));
            services.AddSingleton<IMcpController>(sp => new McpController(sp.GetRequiredService<IMcpConfigManager>(), sp.GetRequiredService<IMcpToolManager>(), sp.GetRequiredService<ISettingsManager>()));

            services.AddSingleton<IModelsController>(sp => new ModelsController(sp.GetRequiredService<ISettingsManager>(), sp.GetRequiredService<IModelsListService>(), sp.GetRequiredService<IActiveModelContext>()));
            services.AddSingleton<IRecentModelsController>(sp => new RecentModelsController(sp.GetRequiredService<IRecentModelsManager>()));
            services.AddSingleton<IFileLinkResolver, FileLinkResolver>();
            services.AddSingleton<IFileLinksController>(sp => new FileLinksController(sp.GetRequiredService<IFileLinkResolver>()));
            services.AddSingleton<IAutocompletionsController>(sp => new AutocompletionsController(sp.GetRequiredService<IAutocompletionsConfigManager>(), sp.GetRequiredService<IAutocompletionsService>(), sp.GetRequiredService<IModelsListService>()));
            services.AddSingleton<IChatSessionController>(sp => new ChatSessionController(sp.GetRequiredService<IChatHistoryManager>()));
            services.AddSingleton<IUsageController>(sp => new UsageController(sp.GetRequiredService<IUsageStatsService>(), sp.GetRequiredService<IChatHistoryManager>(), sp.GetRequiredService<ISettingsManager>()));
            services.AddSingleton<IWebViewHostController>(sp => new WebViewHostController(
                sp.GetRequiredService<IDebuggerContextService>(),
                sp.GetRequiredService<IVsDependencies>(),
                sp.GetRequiredService<IPathResolver>(),
                () => ChatInjectionService.Instance));
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
