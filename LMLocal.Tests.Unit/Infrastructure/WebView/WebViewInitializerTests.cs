using System;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.Mcp;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.WebView.Environment;
using LMLocal.Infrastructure.WebView.Hosting;
using LMLocal.Infrastructure.WebView.Initialization;
using LMLocal.Infrastructure.WebView.Navigation;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.WebView
{
    [TestFixture]
    public class WebViewInitializerTests
    {
        private static Mock<ISettingsManager> Settings() => new Mock<ISettingsManager>();
        private static Mock<IToolsConfigManager> Tools() => new Mock<IToolsConfigManager>();
        private static Mock<IMcpToolManager> McpTools() => new Mock<IMcpToolManager>();
        private static Mock<IMcpConfigManager> McpConfig() => new Mock<IMcpConfigManager>();
        private static Mock<IWebViewEnvironmentProvider> Environment() => new Mock<IWebViewEnvironmentProvider>();
        private static Mock<IWebViewHostObjectRegistrar> Registrar() => new Mock<IWebViewHostObjectRegistrar>();
        private static Mock<IWebViewNavigator> Navigator() => new Mock<IWebViewNavigator>();

        private static WebViewInitializer Create(
            ISettingsManager settings,
            IToolsConfigManager tools,
            IMcpToolManager mcpTool,
            IMcpConfigManager mcpConfig,
            IWebViewEnvironmentProvider environment,
            IWebViewHostObjectRegistrar registrar,
            IWebViewNavigator navigator)
            => new WebViewInitializer(settings, tools, mcpTool, mcpConfig, environment, registrar, navigator);

        [Test]
        public void Ctor_NullSettingsManager_Throws()
        {
            Assert.That(
                () => Create(null, Tools().Object, McpTools().Object, McpConfig().Object,
                    Environment().Object, Registrar().Object, Navigator().Object),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("settingsManager"));
        }

        [Test]
        public void Ctor_NullToolsConfigManager_Throws()
        {
            Assert.That(
                () => Create(Settings().Object, null, McpTools().Object, McpConfig().Object,
                    Environment().Object, Registrar().Object, Navigator().Object),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("toolsConfigManager"));
        }

        [Test]
        public void Ctor_NullMcpToolManager_Throws()
        {
            Assert.That(
                () => Create(Settings().Object, Tools().Object, null, McpConfig().Object,
                    Environment().Object, Registrar().Object, Navigator().Object),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("mcpToolManager"));
        }

        [Test]
        public void Ctor_NullMcpConfigManager_Throws()
        {
            Assert.That(
                () => Create(Settings().Object, Tools().Object, McpTools().Object, null,
                    Environment().Object, Registrar().Object, Navigator().Object),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("mcpConfigManager"));
        }

        [Test]
        public void Ctor_NullEnvironmentProvider_Throws()
        {
            Assert.That(
                () => Create(Settings().Object, Tools().Object, McpTools().Object, McpConfig().Object,
                    null, Registrar().Object, Navigator().Object),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("environmentProvider"));
        }

        [Test]
        public void Ctor_NullHostObjectRegistrar_Throws()
        {
            Assert.That(
                () => Create(Settings().Object, Tools().Object, McpTools().Object, McpConfig().Object,
                    Environment().Object, null, Navigator().Object),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("hostObjectRegistrar"));
        }

        [Test]
        public void Ctor_NullNavigator_Throws()
        {
            Assert.That(
                () => Create(Settings().Object, Tools().Object, McpTools().Object, McpConfig().Object,
                    Environment().Object, Registrar().Object, null),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("navigator"));
        }

        [Test]
        public void Ctor_AllDependenciesProvided_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Create(
                Settings().Object, Tools().Object, McpTools().Object, McpConfig().Object,
                Environment().Object, Registrar().Object, Navigator().Object));
        }
    }
}
