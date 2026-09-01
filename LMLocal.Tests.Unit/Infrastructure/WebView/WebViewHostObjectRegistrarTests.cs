using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.WebView;
using LMLocal.Infrastructure.WebView.Controllers;
using LMLocal.Infrastructure.WebView.Hosting;
using Microsoft.Web.WebView2.Core;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.WebView
{
    [TestFixture]
    public class WebViewHostObjectRegistrarTests
    {
        private sealed class FakeSink : IWebViewHostObjectSink
        {
            public CoreWebView2 Core => null;

            public List<Tuple<string, object>> Additions { get; } = new List<Tuple<string, object>>();

            public void AddHostObject(string name, object obj) => Additions.Add(Tuple.Create(name, obj));
        }

        private sealed class Fixture
        {
            public Mock<IWebViewBridgeFactory> BridgeFactory { get; } = new Mock<IWebViewBridgeFactory>();
            public Mock<IWebViewBridge> Bridge { get; } = new Mock<IWebViewBridge>();
            public Mock<IWebViewHostController> HostController { get; } = new Mock<IWebViewHostController>();
            public Mock<IInstructionsController> Instructions { get; } = new Mock<IInstructionsController>();
            public Mock<IProvidersController> Providers { get; } = new Mock<IProvidersController>();
            public Mock<IToolsController> Tools { get; } = new Mock<IToolsController>();
            public Mock<ISettingsController> Settings { get; } = new Mock<ISettingsController>();
            public Mock<IMcpController> Mcp { get; } = new Mock<IMcpController>();
            public Mock<IModelsController> Models { get; } = new Mock<IModelsController>();
            public Mock<IModelsConfigController> ModelsConfig { get; } = new Mock<IModelsConfigController>();
            public Mock<IAutocompletionsController> Autocompletions { get; } = new Mock<IAutocompletionsController>();
            public Mock<IChatSessionController> ChatSession { get; } = new Mock<IChatSessionController>();
            public Mock<ISubAgentsController> SubAgents { get; } = new Mock<ISubAgentsController>();
            public Mock<IRecentModelsController> RecentModels { get; } = new Mock<IRecentModelsController>();

            public Fixture()
            {
                BridgeFactory
                    .Setup(f => f.CreateBridge(It.IsAny<CoreWebView2>()))
                    .Returns(Bridge.Object);
            }

            public WebViewHostObjectRegistrar CreateRegistrar()
            {
                return new WebViewHostObjectRegistrar(
                    BridgeFactory.Object,
                    HostController.Object,
                    Instructions.Object,
                    Providers.Object,
                    Tools.Object,
                    Settings.Object,
                    Mcp.Object,
                    Models.Object,
                    ModelsConfig.Object,
                    Autocompletions.Object,
                    ChatSession.Object,
                    SubAgents.Object,
                    RecentModels.Object);
            }
        }

        [Test]
        public void Register_AllHostObjects_AreAddedUnderExpectedNames()
        {
            var fixture = new Fixture();
            var registrar = fixture.CreateRegistrar();
            var sink = new FakeSink();

            registrar.Register(sink, () => Task.CompletedTask);

            string[] names = sink.Additions.Select(a => a.Item1).ToArray();
            Assert.That(
                names,
                Is.EqualTo(new[]
                {
                    "bridge", "host", "instructions", "providers", "tools", "settings",
                    "mcp", "models", "modelsConfig", "autocompletions", "chatSession", "subAgents", "recentModels"
                }));
        }

        [Test]
        public void Register_Bridge_IsCreatedViaFactoryAndRegisteredFirst()
        {
            var fixture = new Fixture();
            var registrar = fixture.CreateRegistrar();
            var sink = new FakeSink();

            registrar.Register(sink, () => Task.CompletedTask);

            Assert.That(sink.Additions[0].Item1, Is.EqualTo("bridge"));
            Assert.That(sink.Additions[0].Item2, Is.SameAs(fixture.Bridge.Object));
            fixture.BridgeFactory.Verify(f => f.CreateBridge(It.IsAny<CoreWebView2>()), Times.Once);
        }

        [Test]
        public void Register_HostController_IsConfiguredWithFocusActionAndRegistered()
        {
            var fixture = new Fixture();
            var registrar = fixture.CreateRegistrar();
            var sink = new FakeSink();
            Func<Task> focusAction = () => Task.CompletedTask;

            registrar.Register(sink, focusAction);

            Tuple<string, object> hostEntry = sink.Additions.First(a => a.Item1 == "host");
            Assert.That(hostEntry.Item2, Is.SameAs(fixture.HostController.Object));
            fixture.HostController.Verify(h => h.ConfigureFocus(focusAction), Times.Once);
        }

        [Test]
        public void Register_Controllers_AreRegisteredUnderTheirNames()
        {
            var fixture = new Fixture();
            var registrar = fixture.CreateRegistrar();
            var sink = new FakeSink();

            registrar.Register(sink, () => Task.CompletedTask);

            AssertRegistered(sink, "instructions", fixture.Instructions.Object);
            AssertRegistered(sink, "providers", fixture.Providers.Object);
            AssertRegistered(sink, "tools", fixture.Tools.Object);
            AssertRegistered(sink, "settings", fixture.Settings.Object);
            AssertRegistered(sink, "mcp", fixture.Mcp.Object);
            AssertRegistered(sink, "models", fixture.Models.Object);
            AssertRegistered(sink, "modelsConfig", fixture.ModelsConfig.Object);
            AssertRegistered(sink, "autocompletions", fixture.Autocompletions.Object);
            AssertRegistered(sink, "chatSession", fixture.ChatSession.Object);
            AssertRegistered(sink, "subAgents", fixture.SubAgents.Object);
            AssertRegistered(sink, "recentModels", fixture.RecentModels.Object);
        }

        [Test]
        public void Register_NullSink_Throws()
        {
            var fixture = new Fixture();
            var registrar = fixture.CreateRegistrar();

            Assert.That(
                () => registrar.Register(null, () => Task.CompletedTask),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Register_NullFocusAction_Throws()
        {
            var fixture = new Fixture();
            var registrar = fixture.CreateRegistrar();

            Assert.That(
                () => registrar.Register(new FakeSink(), null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_NullBridgeFactory_Throws()
        {
            var fixture = new Fixture();

            Assert.That(
                () => new WebViewHostObjectRegistrar(
                    null,
                    fixture.HostController.Object,
                    fixture.Instructions.Object,
                    fixture.Providers.Object,
                    fixture.Tools.Object,
                    fixture.Settings.Object,
                    fixture.Mcp.Object,
                    fixture.Models.Object,
                    fixture.ModelsConfig.Object,
                    fixture.Autocompletions.Object,
                    fixture.ChatSession.Object,
                    fixture.SubAgents.Object,
                    fixture.RecentModels.Object),
                Throws.TypeOf<ArgumentNullException>());
        }

        private static void AssertRegistered(FakeSink sink, string name, object expected)
        {
            Tuple<string, object> entry = sink.Additions.First(a => a.Item1 == name);
            Assert.That(entry.Item2, Is.SameAs(expected));
        }
    }
}
