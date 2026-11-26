using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    [TestFixture]
    public class SubAgentPipelineRunnerTests
    {
        private Mock<ISubAgentsService> _service;
        private List<SubAgentRunRequest> _requests;
        private Queue<SubAgentsRunResponse> _responses;
        private Func<SubAgentRunRequest, SubAgentsRunResponse> _responder;
        private List<TimeSpan> _delays;
        private Action _onDelay;
        private readonly object _sync = new object();

        [SetUp]
        public void SetUp()
        {
            _service = new Mock<ISubAgentsService>();
            _requests = new List<SubAgentRunRequest>();
            _responses = new Queue<SubAgentsRunResponse>();
            _responder = null;
            _delays = new List<TimeSpan>();
            _onDelay = null;

            _service
                .Setup(s => s.ExecutePromptAsync(It.IsAny<SubAgentRunRequest>(), It.IsAny<CancellationToken>()))
                .Returns((SubAgentRunRequest request, CancellationToken cancellationToken) =>
                {
                    lock (_sync)
                    {
                        _requests.Add(request);
                    }

                    var response = _responder != null
                        ? _responder(request)
                        : (_responses.Count > 0 ? _responses.Dequeue() : Ok("ok"));

                    return Task.FromResult(response);
                });
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static SubAgentsConfig Config()
        {
            var config = new SubAgentsConfig();
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "explorer",
                Description = "E",
                Model = "m",
                CustomBaseUrl = "http://localhost:1234",
                Enabled = true
            });
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "analyzer",
                Description = "A",
                Model = "m",
                CustomBaseUrl = "http://localhost:1234",
                Enabled = true
            });
            return config;
        }

        private static SubAgentPipelineStep Step(string agent, string prompt, string id = null)
        {
            return new SubAgentPipelineStep { Id = id, Agent = agent, Prompt = prompt };
        }

        private static SubAgentPipelineDefinition Pipeline(params SubAgentPipelineStep[] steps)
        {
            return new SubAgentPipelineDefinition
            {
                Id = "review_flow",
                Description = "Pipeline review_flow",
                Steps = steps.ToList()
            };
        }

        private static SubAgentsRunResponse Ok(string content)
        {
            return new SubAgentsRunResponse { Success = true, Content = content };
        }

        private static SubAgentsRunResponse Fail(SubAgentFailureKind kind, string error)
        {
            return new SubAgentsRunResponse { Success = false, Error = error, FailureKind = kind };
        }

        private void Respond(params SubAgentsRunResponse[] responses)
        {
            foreach (var response in responses)
                _responses.Enqueue(response);
        }

        private SubAgentsRunResponse Run(
            SubAgentPipelineDefinition pipeline,
            string task,
            IProgress<ToolActivityEvent> progress = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var built = SubAgentPipelineRequestFactory.Create(pipeline, Config(), task, progress);
            Assert.That(built.Request, Is.Not.Null, built.Error);

            var runner = new SubAgentPipelineRunner(_service.Object, FakeDelay);
            return runner.RunAsync(built.Request, cancellationToken).GetAwaiter().GetResult();
        }

        /// <summary>Records the pause instead of waiting; honours cancellation like Task.Delay.</summary>
        private Task FakeDelay(TimeSpan delay, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _delays.Add(delay);
            }

            _onDelay?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        private sealed class Collector : IProgress<ToolActivityEvent>
        {
            private readonly object _sync = new object();

            internal List<ToolActivityEvent> Events { get; } = new List<ToolActivityEvent>();

            public void Report(ToolActivityEvent value)
            {
                lock (_sync)
                {
                    Events.Add(value);
                }
            }
        }

        // ------------------------------------------------------------------
        // Sequence and placeholders
        // ------------------------------------------------------------------

        [Test]
        public void Run_RunsStepsInOrderAndPassesPrev()
        {
            var pipeline = Pipeline(
                Step("explorer", "{{task}}"),
                Step("analyzer", "task={{task}} prev={{prev}}"));

            Respond(Ok("one"), Ok("two"));

            var result = Run(pipeline, "find X");

            Assert.That(result.Success, Is.True);
            Assert.That(result.Content, Is.EqualTo("two"));
            Assert.That(_requests, Has.Count.EqualTo(2));
            Assert.That(_requests[0].Prompt, Is.EqualTo("find X"));
            Assert.That(_requests[1].Prompt, Is.EqualTo("task=find X prev=one"));
            Assert.That(_requests[0].AgentName, Is.EqualTo("explorer"));
            Assert.That(_requests[1].AgentName, Is.EqualTo("analyzer"));
        }

        [Test]
        public void Run_SubstitutesNamedStepOutput()
        {
            var pipeline = Pipeline(
                Step("explorer", "{{task}}", "explore"),
                Step("analyzer", "{{steps.explore}}"));

            Respond(Ok("found in Foo.cs"), Ok("final"));

            Run(pipeline, "task");

            Assert.That(_requests[1].Prompt, Is.EqualTo("found in Foo.cs"));
        }

        [Test]
        public void Run_DoesNotExpandPlaceholdersOfAnAnswerAgain()
        {
            var pipeline = Pipeline(
                Step("explorer", "{{task}}"),
                Step("analyzer", "{{prev}}"));

            Respond(Ok("see {{task}} please"), Ok("final"));

            Run(pipeline, "the task");

            Assert.That(_requests[1].Prompt, Is.EqualTo("see {{task}} please"));
        }

        [Test]
        public void Run_StopsAtTheFailingStepAndReportsItsPosition()
        {
            var pipeline = Pipeline(
                Step("explorer", "{{task}}"),
                Step("analyzer", "{{prev}}"));

            Respond(Fail(SubAgentFailureKind.Provider, "boom"));

            var result = Run(pipeline, "task");

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(SubAgentFailureKind.Provider));
            Assert.That(result.Error, Does.Contain("pipeline 'review_flow' step 1/2"));
            Assert.That(result.Error, Does.Contain("boom"));
            Assert.That(_requests, Has.Count.EqualTo(1), "the second step must not run");
        }

        [Test]
        public void Run_EmptyAnswer_FailsAsAgentFailure()
        {
            var pipeline = Pipeline(Step("explorer", "{{task}}"));

            Respond(Ok("   "));

            var result = Run(pipeline, "task");

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(SubAgentFailureKind.Agent));
            Assert.That(result.Error, Does.Contain("empty answer"));
        }

        [Test]
        public void Run_SetsThePipelineFieldsOfEveryStep()
        {
            var pipeline = Pipeline(
                Step("explorer", "{{task}}", "explore"),
                Step("analyzer", "{{prev}}"));

            Respond(Ok("one"), Ok("two"));

            Run(pipeline, "task");

            Assert.That(_requests[0].PipelineId, Is.EqualTo("review_flow"));
            Assert.That(_requests[0].PipelineStep, Is.EqualTo(1));
            Assert.That(_requests[0].PipelineStepId, Is.EqualTo("explore"));
            Assert.That(_requests[1].PipelineStep, Is.EqualTo(2));
            Assert.That(_requests[1].PipelineAttempt, Is.Null);
            Assert.That(_requests[0].PipelineRunId, Is.Not.Null.And.Not.Empty);
            Assert.That(_requests[0].PipelineRunId, Is.EqualTo(_requests[1].PipelineRunId), "one id groups the steps of a run");
        }

        [Test]
        public void Run_CancelledToken_Throws()
        {
            var pipeline = Pipeline(Step("explorer", "{{task}}"));
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => Run(pipeline, "task", null, cts.Token));
        }

        // ------------------------------------------------------------------
        // Retries
        // ------------------------------------------------------------------

        [Test]
        public void Run_RetriesAProviderFailureWithinTheLimit()
        {
            var step = Step("explorer", "{{task}}");
            step.Retries = 1;

            Respond(Fail(SubAgentFailureKind.Provider, "502 Bad Gateway"), Ok("done"));

            var result = Run(Pipeline(step), "task");

            Assert.That(result.Success, Is.True);
            Assert.That(result.Content, Is.EqualTo("done"));
            Assert.That(_requests, Has.Count.EqualTo(2));
            Assert.That(_requests[1].PipelineAttempt, Is.EqualTo(2));
        }

        [Test]
        public void Run_WaitsBackoffBeforeRetryingAProviderFailure()
        {
            var step = Step("explorer", "{{task}}");
            step.Retries = 3;

            Respond(
                Fail(SubAgentFailureKind.Provider, "down"),
                Fail(SubAgentFailureKind.Provider, "down"),
                Fail(SubAgentFailureKind.Provider, "down"),
                Fail(SubAgentFailureKind.Provider, "down"));

            var result = Run(Pipeline(step), "task");

            Assert.That(result.Success, Is.False);
            Assert.That(_requests, Has.Count.EqualTo(4));
            Assert.That(_delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8) }),
                "a pause before every retry and none after the last attempt");
        }

        [Test]
        public void Run_DoesNotRetryAProviderNotRetryable()
        {
            var step = Step("explorer", "{{task}}");
            step.Retries = 3;

            Respond(Fail(SubAgentFailureKind.ProviderNotRetryable, "model not found"));

            var result = Run(Pipeline(step), "task");

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(SubAgentFailureKind.ProviderNotRetryable));
            Assert.That(_requests, Has.Count.EqualTo(1));
            Assert.That(_delays, Is.Empty);
        }

        [TestCase(SubAgentFailureKind.Timeout)]
        [TestCase(SubAgentFailureKind.Incomplete)]
        public void Run_DoesNotRetryATimeoutOrIncomplete(SubAgentFailureKind kind)
        {
            var step = Step("explorer", "{{task}}");
            step.Retries = 3;

            Respond(Fail(kind, "failed"));

            var result = Run(Pipeline(step), "task");

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(kind));
            Assert.That(_requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Run_StopDuringPause_Cancels()
        {
            var step = Step("explorer", "{{task}}");
            step.Retries = 2;
            var cts = new CancellationTokenSource();
            _onDelay = cts.Cancel;

            Respond(Fail(SubAgentFailureKind.Provider, "down"), Ok("never"));

            Assert.Throws<OperationCanceledException>(() => Run(Pipeline(step), "task", null, cts.Token));
            Assert.That(_requests, Has.Count.EqualTo(1), "no second run after Stop during the pause");
        }

        [Test]
        public void Run_DoesNotRetryAnAgentFailure()
        {
            var step = Step("explorer", "{{task}}");
            step.Retries = 2;

            Respond(Fail(SubAgentFailureKind.Agent, "max rounds"));

            var result = Run(Pipeline(step), "task");

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(SubAgentFailureKind.Agent));
            Assert.That(_requests, Has.Count.EqualTo(1), "the same task by the same agent would fail again");
        }

        // ------------------------------------------------------------------
        // Output modes
        // ------------------------------------------------------------------

        [Test]
        public void Run_OutputAll_JoinsEveryAnswerWithHeading()
        {
            var pipeline = Pipeline(
                Step("explorer", "{{task}}", "explore"),
                Step("analyzer", "{{prev}}", "analyze"));
            pipeline.Output = SubAgentPipelineDefinition.OutputAll;

            Respond(Ok("one"), Ok("two"));

            var result = Run(pipeline, "task");

            Assert.That(result.Content, Does.Contain("### [1/2 explore]"));
            Assert.That(result.Content, Does.Contain("one"));
            Assert.That(result.Content, Does.Contain("### [2/2 analyze]"));
            Assert.That(result.Content, Does.Contain("two"));
        }

        [Test]
        public void Run_SumsMetricsOfTheSteps()
        {
            var pipeline = Pipeline(Step("explorer", "{{task}}"), Step("analyzer", "{{prev}}"));

            Respond(
                new SubAgentsRunResponse
                {
                    Success = true,
                    Content = "one",
                    PromptTokens = 10,
                    CompletionTokens = 5,
                    TotalTokens = 15,
                    Rounds = 2,
                    DurationMs = 100,
                    ExpandedRefCount = 1,
                    ToolsUsed = new List<string> { "read_file_lines" },
                    Model = "model-a",
                    RunId = "run-1"
                },
                new SubAgentsRunResponse
                {
                    Success = true,
                    Content = "two",
                    PromptTokens = 20,
                    CompletionTokens = 7,
                    TotalTokens = 27,
                    Rounds = 3,
                    DurationMs = 200,
                    UnresolvedRefCount = 2,
                    ToolsUsed = new List<string> { "read_file_lines", "find_files" },
                    Model = "model-b",
                    RunId = "run-2"
                });

            var result = Run(pipeline, "task");

            Assert.That(result.PromptTokens, Is.EqualTo(30));
            Assert.That(result.CompletionTokens, Is.EqualTo(12));
            Assert.That(result.TotalTokens, Is.EqualTo(42));
            Assert.That(result.Rounds, Is.EqualTo(5));
            Assert.That(result.DurationMs, Is.LessThan(300),
                "the duration is the wall clock of the run, not the sum of the step durations");
            Assert.That(result.TokensPerSecond, Is.EqualTo(0d), "no step reported a speed");
            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(2));
            Assert.That(result.ToolsUsed, Is.EquivalentTo(new[] { "read_file_lines", "find_files" }));
            Assert.That(result.Model, Is.EqualTo("model-b"));
            Assert.That(result.RunId, Is.EqualTo("run-2"));
        }

        // ------------------------------------------------------------------
        // Parallel step (2B)
        // ------------------------------------------------------------------

        [Test]
        public void Run_ParallelGroup_JoinsAnswersInConfigOrder()
        {
            var group = new SubAgentPipelineStep
            {
                MaxParallel = 2,
                Parallel = new List<SubAgentPipelineStep>
                {
                    Step("explorer", "branch one {{task}}", "left"),
                    Step("analyzer", "branch two {{task}}", "right")
                }
            };

            var pipeline = Pipeline(group, Step("analyzer", "{{prev}}|{{steps.left}}|{{steps.right}}"));

            _responder = request =>
            {
                if (request.Prompt.Contains("branch one"))
                    return Ok("LEFT");
                if (request.Prompt.Contains("branch two"))
                    return Ok("RIGHT");
                return Ok("final");
            };

            var result = Run(pipeline, "the task");

            Assert.That(result.Success, Is.True);
            Assert.That(_requests, Has.Count.EqualTo(3));

            var lastPrompt = _requests.Last(r => r.Prompt.Contains("|")).Prompt;
            Assert.That(lastPrompt, Does.Contain("### [1/2 left]"));
            Assert.That(lastPrompt, Does.Contain("### [2/2 right]"));
            Assert.That(lastPrompt.IndexOf("LEFT", StringComparison.Ordinal),
                Is.LessThan(lastPrompt.IndexOf("RIGHT", StringComparison.Ordinal)), "answers follow the config order");
            Assert.That(lastPrompt, Does.Contain("|LEFT|RIGHT"));
        }

        [Test]
        public void Run_ParallelGroup_StopMode_FailsThePipeline()
        {
            var group = new SubAgentPipelineStep
            {
                Parallel = new List<SubAgentPipelineStep>
                {
                    Step("explorer", "branch one", "left"),
                    Step("analyzer", "branch two", "right")
                }
            };

            var pipeline = Pipeline(group, Step("analyzer", "{{prev}}"));

            _responder = request => request.Prompt.Contains("branch one")
                ? Fail(SubAgentFailureKind.Provider, "provider down")
                : Ok("RIGHT");

            var result = Run(pipeline, "task");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("branch 1/2"), "the failing branch is named in the error");
            Assert.That(_requests.Any(r => r.Prompt.Contains("{{prev}}")), Is.False, "the next step must not run");
        }

        [Test]
        public void Run_ParallelGroup_ContinueMode_ReportsTheBranchErrorAsText()
        {
            var group = new SubAgentPipelineStep
            {
                OnBranchError = SubAgentPipelineStep.BranchErrorContinue,
                Parallel = new List<SubAgentPipelineStep>
                {
                    Step("explorer", "branch one", "left"),
                    Step("analyzer", "branch two", "right")
                }
            };

            var pipeline = Pipeline(group, Step("analyzer", "prev:\n{{prev}}"));

            _responder = request =>
            {
                if (request.Prompt.Contains("branch one"))
                    return Fail(SubAgentFailureKind.Provider, "provider down");
                if (request.Prompt.Contains("branch two"))
                    return Ok("RIGHT");
                return Ok("final");
            };

            var result = Run(pipeline, "task");

            Assert.That(result.Success, Is.True);

            var lastPrompt = _requests.Last(r => r.Prompt.StartsWith("prev:", StringComparison.Ordinal)).Prompt;
            Assert.That(lastPrompt, Does.Contain("ERROR: provider down"));
            Assert.That(lastPrompt, Does.Contain("RIGHT"));
        }

        // ------------------------------------------------------------------
        // Progress
        // ------------------------------------------------------------------

        [Test]
        public void Run_ReportsTheStepLabelInEveryProgressMessage()
        {
            var collector = new Collector();
            var pipeline = Pipeline(Step("explorer", "{{task}}"), Step("analyzer", "{{prev}}"));

            Respond(Ok("one"), Ok("two"));

            Run(pipeline, "task", collector);

            Assert.That(collector.Events, Has.Count.EqualTo(2));
            Assert.That(collector.Events[0].Message, Does.Contain("[1/2 explorer]"));
            Assert.That(collector.Events[0].Message, Does.Contain("starting"));
            Assert.That(collector.Events[1].Message, Does.Contain("[2/2 analyzer]"));
            Assert.That(collector.Events[1].Step, Is.GreaterThan(collector.Events[0].Step), "the step counter is shared by the run");
        }

        [Test]
        public void Run_ComputesTokensPerSecondFromTheStepTotals()
        {
            var pipeline = Pipeline(Step("explorer", "{{task}}"), Step("analyzer", "{{prev}}"));

            Respond(
                new SubAgentsRunResponse
                {
                    Success = true,
                    Content = "one",
                    CompletionTokens = 100,
                    TokensPerSecond = 50d
                },
                new SubAgentsRunResponse
                {
                    Success = true,
                    Content = "two",
                    CompletionTokens = 300,
                    TokensPerSecond = 100d
                });

            var result = Run(pipeline, "task");

            // 100 tokens in 2000 ms plus 300 tokens in 3000 ms = 400 tokens in 5000 ms.
            Assert.That(result.CompletionTokens, Is.EqualTo(400));
            Assert.That(result.TokensPerSecond, Is.EqualTo(80d).Within(0.001),
                "summing the rates would show 150 t/s to the user");
        }

        // ------------------------------------------------------------------
        // Parallel step: usage position, error attribution and width
        // ------------------------------------------------------------------

        [Test]
        public void Run_ParallelGroup_WritesTheBranchNumberToTheUsage()
        {
            var group = new SubAgentPipelineStep
            {
                Parallel = new List<SubAgentPipelineStep>
                {
                    Step("explorer", "branch one", "left"),
                    Step("analyzer", "branch two", "right")
                }
            };

            _responder = request => Ok("ok");

            Run(Pipeline(group, Step("analyzer", "{{prev}}")), "task");

            var branchRequests = _requests.Where(r => r.Prompt.Contains("branch")).ToList();

            Assert.That(branchRequests, Has.Count.EqualTo(2));
            Assert.That(
                branchRequests.Select(r => r.PipelineBranch).OrderBy(v => v),
                Is.EqualTo(new int?[] { 1, 2 }),
                "a branch of a group carries its 1-based number");
            Assert.That(_requests.Last().PipelineBranch, Is.Null, "a sequential step has no branch number");
        }

        [Test]
        public void Run_ParallelGroup_StopMode_BlamesTheFailingBranchNotACancelledSibling()
        {
            var slowStarted = new TaskCompletionSource<bool>();

            _service
                .Setup(s => s.ExecutePromptAsync(It.IsAny<SubAgentRunRequest>(), It.IsAny<CancellationToken>()))
                .Returns((SubAgentRunRequest request, CancellationToken token) => RunBranchAsync(request, token, slowStarted));

            var result = Run(Pipeline(GroupWithSlowAndFailingBranch()), "task");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("branch 2/2"), "the branch that failed is the cause");
            Assert.That(result.Error, Does.Contain("provider down"));
            Assert.That(result.Error, Does.Not.Contain("branch 1/2"),
                "the sibling was cancelled by the runner, so it must not be blamed");
        }

        [Test]
        public void Run_ParallelGroup_CancelledSibling_IsCancelledByItsCaller()
        {
            var slowStarted = new TaskCompletionSource<bool>();

            _service
                .Setup(s => s.ExecutePromptAsync(It.IsAny<SubAgentRunRequest>(), It.IsAny<CancellationToken>()))
                .Returns((SubAgentRunRequest request, CancellationToken token) => RunBranchAsync(request, token, slowStarted));

            var result = Run(Pipeline(GroupWithSlowAndFailingBranch()), "task");

            Assert.That(result.Success, Is.False);

            var cancelled = _requests.Single(r => r.Prompt.Contains("slow branch"));
            var failed = _requests.Single(r => r.Prompt.Contains("failing branch"));

            Assert.That(cancelled.Cancellation, Is.Not.Null, "a branch tells the service who owns its token");
            Assert.That(cancelled.Cancellation.StoppedByCaller, Is.True,
                "the run cancelled by the group must not read as a user stop");
            Assert.That(failed.Cancellation, Is.SameAs(cancelled.Cancellation), "one owner per group");
        }

        [Test]
        public void Run_ParallelGroup_SiblingCancelledMidRun_ByResponse_IsNotBlamed()
        {
            // The real service does not throw when its token is cancelled: it returns a Cancelled response. The sibling
            // must still be treated as cancelled by the group, not as a failure of its own.
            var slowStarted = new TaskCompletionSource<bool>();

            _service
                .Setup(s => s.ExecutePromptAsync(It.IsAny<SubAgentRunRequest>(), It.IsAny<CancellationToken>()))
                .Returns((SubAgentRunRequest request, CancellationToken token) =>
                    RunBranchReturningCancelledAsync(request, token, slowStarted));

            var result = Run(Pipeline(GroupWithSlowAndFailingBranch()), "task");

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(SubAgentFailureKind.Provider), "the failure kind of the failing branch");
            Assert.That(result.Error, Does.Contain("branch 2/2"));
            Assert.That(result.Error, Does.Contain("provider down"));
            Assert.That(result.Error, Does.Not.Contain("branch 1/2"));
        }

        /// <summary>
        /// Like <see cref="RunBranchAsync"/>, but the slow branch answers a cancellation the way the service does:
        /// with a Cancelled response instead of an exception.
        /// </summary>
        private async Task<SubAgentsRunResponse> RunBranchReturningCancelledAsync(
            SubAgentRunRequest request,
            CancellationToken token,
            TaskCompletionSource<bool> slowStarted)
        {
            try
            {
                return await RunBranchAsync(request, token, slowStarted).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Fail(SubAgentFailureKind.Cancelled, "SubAgent stopped: cancelled by its caller, not by the user.");
            }
        }

        /// <summary>Group whose first branch runs until it is cancelled and whose second branch fails.</summary>
        private static SubAgentPipelineStep GroupWithSlowAndFailingBranch()
        {
            return new SubAgentPipelineStep
            {
                Parallel = new List<SubAgentPipelineStep>
                {
                    Step("explorer", "slow branch", "left"),
                    Step("analyzer", "failing branch", "right")
                }
            };
        }

        /// <summary>
        /// Deterministic stand-in for the service: the failing branch fails only when the slow one is really running, so
        /// the test never depends on thread scheduling.
        /// </summary>
        private async Task<SubAgentsRunResponse> RunBranchAsync(
            SubAgentRunRequest request,
            CancellationToken token,
            TaskCompletionSource<bool> slowStarted)
        {
            lock (_sync)
            {
                _requests.Add(request);
            }

            if (request.Prompt.Contains("failing branch"))
            {
                await Task.WhenAny(slowStarted.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
                return Fail(SubAgentFailureKind.Provider, "provider down");
            }

            slowStarted.TrySetResult(true);

            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return Ok("never");
        }

        [Test]
        public void Create_DisabledAgent_FailsForTheChatButRunsForIpc()
        {
            var config = Config();
            config.Agents.Single(a => a.Id == "analyzer").Enabled = false;

            var pipeline = Pipeline(Step("explorer", "first"), Step("analyzer", "second {{prev}}"));

            var forChat = SubAgentPipelineRequestFactory.Create(pipeline, config, "task");
            Assert.That(forChat.Request, Is.Null, "the chat never runs a pipeline with a disabled agent");
            Assert.That(forChat.Error, Does.Contain("not found or disabled"));

            var forIpc = SubAgentPipelineRequestFactory.Create(pipeline, config, "task", requireEnabledAgents: false);
            Assert.That(forIpc.Request, Is.Not.Null, "IPC ignores the enabled flag, like RunSubAgent");
            Assert.That(forIpc.Request.Steps[1].Request.AgentName, Is.EqualTo("analyzer"));

            var missing = Pipeline(Step("ghost", "first"));
            var missingForIpc = SubAgentPipelineRequestFactory.Create(missing, config, "task", requireEnabledAgents: false);
            Assert.That(missingForIpc.Request, Is.Null, "an agent that does not exist still fails");
            Assert.That(missingForIpc.Error, Does.Contain("'ghost' not found"));
            Assert.That(missingForIpc.Error, Does.Not.Contain("disabled"));
        }

        [Test]
        public void Create_GroupWidth_FallsBackToThePipelineThenTheRootThenTheDefault()
        {
            var group = new SubAgentPipelineStep
            {
                Parallel = new List<SubAgentPipelineStep> { Step("explorer", "a"), Step("analyzer", "b") }
            };

            var config = Config();
            config.MaxParallel = 3;

            var fromRoot = SubAgentPipelineRequestFactory.Create(Pipeline(group), config, "task");
            Assert.That(fromRoot.Request.MaxParallel, Is.EqualTo(3), "the root value applies when the pipeline has none");

            var withPipelineValue = Pipeline(group);
            withPipelineValue.MaxParallel = 4;
            Assert.That(
                SubAgentPipelineRequestFactory.Create(withPipelineValue, config, "task").Request.MaxParallel,
                Is.EqualTo(4),
                "the pipeline value wins over the root value");

            group.MaxParallel = 1;
            Assert.That(
                SubAgentPipelineRequestFactory.Create(Pipeline(group), config, "task").Request.Steps[0].MaxParallel,
                Is.EqualTo(1),
                "the step value wins over everything");
        }
    }
}
