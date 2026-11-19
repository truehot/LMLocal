using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs
{
    /// <summary>
    /// Guards the provider's side of the timeout contract: what the caller can spare wins over the declared default,
    /// and a spent budget means the tool is never started.
    /// </summary>
    [TestFixture]
    public class BuiltInVsToolProviderBudgetTests
    {
        private const string ToolName = "slow_tool";

        private static BuiltInVsToolProvider CreateProvider(IBuiltInTool tool)
        {
            return new BuiltInVsToolProvider(
                new[] { tool },
                new Mock<IToolsConfigManager>().Object,
                new Mock<ISearchResultCache>().Object,
                new Mock<IToolFileOpener>().Object);
        }

        private static ToolExecutionBudget Budget(int seconds) => new ToolExecutionBudget(TimeSpan.FromSeconds(seconds));

        [Test]
        public async Task ExecuteAsync_NoBudget_PassesDeclaredDefault()
        {
            var tool = new BudgetAwareTool(TimeSpan.FromSeconds(600));

            await CreateProvider(tool).ExecuteAsync(ToolName, new Dictionary<string, object>(), CancellationToken.None);

            Assert.That(tool.AppliedOnCall, Is.EqualTo(TimeSpan.FromSeconds(600)));
        }

        [Test]
        public async Task ExecuteAsync_BudgetBelowDefault_PassesBudgetMinusGrace()
        {
            var tool = new BudgetAwareTool(TimeSpan.FromSeconds(600));

            await CreateProvider(tool).ExecuteAsync(ToolName, new Dictionary<string, object>(), CancellationToken.None, Budget(94));

            Assert.That(tool.AppliedOnCall, Is.EqualTo(TimeSpan.FromSeconds(84)),
                "A SubAgent with 94 s left gives the tool 84 s: the 10 s teardown grace stays inside the budget.");
        }

        [Test]
        public async Task ExecuteAsync_BudgetAboveDefault_PassesDefault()
        {
            var tool = new BudgetAwareTool(TimeSpan.FromSeconds(600));

            await CreateProvider(tool).ExecuteAsync(ToolName, new Dictionary<string, object>(), CancellationToken.None, Budget(900));

            Assert.That(tool.AppliedOnCall, Is.EqualTo(TimeSpan.FromSeconds(600)));
        }

        [Test]
        public void ExecuteAsync_BudgetExhausted_DoesNotStartTool()
        {
            var tool = new BudgetAwareTool(TimeSpan.FromSeconds(600));

            var ex = Assert.ThrowsAsync<ToolBudgetExhaustedException>(async () =>
                await CreateProvider(tool).ExecuteAsync(ToolName, new Dictionary<string, object>(), CancellationToken.None, Budget(-3)));

            Assert.That(ex.Message, Does.Contain("budget exhausted"));
            Assert.That(ex.Message, Does.Contain("0 s left"), "A negative remainder is reported as zero, not as a negative wait.");
            Assert.That(tool.AppliedOnCall, Is.Null, "The tool must not have run at all.");
        }

        [Test]
        public void GetToolTimeout_ReturnsDefaultPlusGrace()
        {
            var provider = CreateProvider(new BudgetAwareTool(TimeSpan.FromSeconds(600)));

            Assert.That(provider.GetToolTimeout(ToolName), Is.EqualTo(TimeSpan.FromSeconds(610)));
        }

        [Test]
        public void GetToolTimeout_ToolWithoutLimits_ReturnsNull()
        {
            var provider = CreateProvider(new PlainTool());

            Assert.That(provider.GetToolTimeout(PlainTool.PlainToolName), Is.Null,
                "A fast tool keeps the orchestrator default.");
        }

        /// <summary>Long-running tool: declares a default and honours the time the caller actually gave it.</summary>
        private sealed class BudgetAwareTool : IBuiltInTool, IBuiltInToolAppliedTimeout
        {
            public BudgetAwareTool(TimeSpan defaultTimeout)
            {
                TimeoutLimits = new ToolExecutionLimits(defaultTimeout);
            }

            public TimeSpan? AppliedOnCall { get; private set; }

            public ToolExecutionLimits TimeoutLimits { get; }

            public string ToolName => "slow_tool";

            public ToolAccessLevel AccessLevel => ToolAccessLevel.Execution;

            public Task<object> ExecuteAsync(Dictionary<string, object> parameters, TimeSpan appliedTimeout, CancellationToken cancellationToken)
            {
                AppliedOnCall = appliedTimeout;
                return Task.FromResult<object>("ok");
            }

            public Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
                => ExecuteAsync(parameters, TimeoutLimits.DefaultTimeout, cancellationToken);

            public ToolDefinition GetToolInfo() => new ToolDefinition { Name = ToolName };

            public string GetProcessingMessage(Dictionary<string, object> parameters) => "running";

            public string GetCompletionMessage(object result) => "done";
        }

        /// <summary>Fast tool: declares no limits.</summary>
        private sealed class PlainTool : IBuiltInTool
        {
            public const string PlainToolName = "fast_tool";

            public string ToolName => PlainToolName;

            public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

            public Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
                => Task.FromResult<object>("ok");

            public ToolDefinition GetToolInfo() => new ToolDefinition { Name = ToolName };

            public string GetProcessingMessage(Dictionary<string, object> parameters) => "reading";

            public string GetCompletionMessage(object result) => "done";
        }
    }
}
