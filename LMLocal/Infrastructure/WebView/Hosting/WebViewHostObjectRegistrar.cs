using System;
using System.Threading.Tasks;
using LMLocal.Infrastructure.WebView.Controllers;

namespace LMLocal.Infrastructure.WebView.Hosting
{
    /// <summary>
    /// Registers the fixed set of host objects (bridge + 11 controllers) into the WebView2 page.
    /// </summary>
    internal sealed class WebViewHostObjectRegistrar : IWebViewHostObjectRegistrar
    {
        public const string Bridge = "bridge";
        public const string Host = "host";
        public const string Instructions = "instructions";
        public const string Providers = "providers";
        public const string Tools = "tools";
        public const string Settings = "settings";
        public const string Mcp = "mcp";
        public const string Models = "models";
        public const string ModelsConfig = "modelsConfig";
        public const string Autocompletions = "autocompletions";
        public const string ChatSession = "chatSession";
        public const string SubAgents = "subAgents";
        public const string RecentModels = "recentModels";

        private readonly IWebViewBridgeFactory _bridgeFactory;
        private readonly IWebViewHostController _hostController;
        private readonly IInstructionsController _instructionsController;
        private readonly IProvidersController _providersController;
        private readonly IToolsController _toolsController;
        private readonly ISettingsController _settingsController;
        private readonly IMcpController _mcpController;
        private readonly IModelsController _modelsController;
        private readonly IModelsConfigController _modelsConfigController;
        private readonly IAutocompletionsController _autocompletionsController;
        private readonly IChatSessionController _chatSessionController;
        private readonly ISubAgentsController _subAgentsController;
        private readonly IRecentModelsController _recentModelsController;

        public WebViewHostObjectRegistrar(
            IWebViewBridgeFactory bridgeFactory,
            IWebViewHostController hostController,
            IInstructionsController instructionsController,
            IProvidersController providersController,
            IToolsController toolsController,
            ISettingsController settingsController,
            IMcpController mcpController,
            IModelsController modelsController,
            IModelsConfigController modelsConfigController,
            IAutocompletionsController autocompletionsController,
            IChatSessionController chatSessionController,
            ISubAgentsController subAgentsController,
            IRecentModelsController recentModelsController)
        {
            _bridgeFactory = bridgeFactory ?? throw new ArgumentNullException(nameof(bridgeFactory));
            _hostController = hostController ?? throw new ArgumentNullException(nameof(hostController));
            _instructionsController = instructionsController ?? throw new ArgumentNullException(nameof(instructionsController));
            _providersController = providersController ?? throw new ArgumentNullException(nameof(providersController));
            _toolsController = toolsController ?? throw new ArgumentNullException(nameof(toolsController));
            _settingsController = settingsController ?? throw new ArgumentNullException(nameof(settingsController));
            _mcpController = mcpController ?? throw new ArgumentNullException(nameof(mcpController));
            _modelsController = modelsController ?? throw new ArgumentNullException(nameof(modelsController));
            _modelsConfigController = modelsConfigController ?? throw new ArgumentNullException(nameof(modelsConfigController));
            _autocompletionsController = autocompletionsController ?? throw new ArgumentNullException(nameof(autocompletionsController));
            _chatSessionController = chatSessionController ?? throw new ArgumentNullException(nameof(chatSessionController));
            _subAgentsController = subAgentsController ?? throw new ArgumentNullException(nameof(subAgentsController));
            _recentModelsController = recentModelsController ?? throw new ArgumentNullException(nameof(recentModelsController));
        }

        public void Register(IWebViewHostObjectSink sink, Func<Task> focusAction)
        {
            if (sink == null)
                throw new ArgumentNullException(nameof(sink));
            if (focusAction == null)
                throw new ArgumentNullException(nameof(focusAction));

            IWebViewBridge bridge = _bridgeFactory.CreateBridge(sink.Core);
            sink.AddHostObject(Bridge, bridge);

            _hostController.ConfigureFocus(focusAction);
            sink.AddHostObject(Host, _hostController);

            sink.AddHostObject(Instructions, _instructionsController);
            sink.AddHostObject(Providers, _providersController);
            sink.AddHostObject(Tools, _toolsController);
            sink.AddHostObject(Settings, _settingsController);
            sink.AddHostObject(Mcp, _mcpController);
            sink.AddHostObject(Models, _modelsController);
            sink.AddHostObject(ModelsConfig, _modelsConfigController);
            sink.AddHostObject(Autocompletions, _autocompletionsController);
            sink.AddHostObject(ChatSession, _chatSessionController);
            sink.AddHostObject(SubAgents, _subAgentsController);
            sink.AddHostObject(RecentModels, _recentModelsController);
        }
    }
}
