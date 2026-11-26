using System;
using LMLocal.Core.Models.Usage;
using LMLocal.Infrastructure.Persistence;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    /// <summary>
    /// The pipeline position of a step must survive the usage log, and a plain agent run must not gain the fields.
    /// </summary>
    [TestFixture]
    public class SubAgentPipelineUsageTests
    {
        private static UsageRecord StepRecord()
        {
            return new UsageRecord
            {
                Timestamp = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc),
                Source = UsageSource.Subagent,
                Agent = "analyzer",
                Pipeline = "review_flow",
                PipelineRunId = "run-1",
                PipelineStep = 2,
                PipelineStepId = "verdict",
                PipelineAttempt = 3,
                PipelineBranch = 1,
                Model = "m"
            };
        }

        [Test]
        public void BuildLine_StepOfAPipeline_WritesThePosition()
        {
            var line = UsageRecordSerializer.BuildLine(StepRecord());

            Assert.That(line, Does.Contain("\"pipeline\":\"review_flow\""));
            Assert.That(line, Does.Contain("\"pipeline_run_id\":\"run-1\""));
            Assert.That(line, Does.Contain("\"pipeline_step\":2"));
            Assert.That(line, Does.Contain("\"pipeline_step_id\":\"verdict\""));
            Assert.That(line, Does.Contain("\"pipeline_attempt\":3"));
            Assert.That(line, Does.Contain("\"pipeline_branch\":1"));
        }

        [Test]
        public void BuildLine_PlainRun_DoesNotWriteThePosition()
        {
            var line = UsageRecordSerializer.BuildLine(new UsageRecord
            {
                Timestamp = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc),
                Source = UsageSource.Subagent,
                Agent = "analyzer"
            });

            Assert.That(line, Does.Not.Contain("pipeline"));
        }

        [Test]
        public void ParseLine_RoundTripsThePosition()
        {
            var original = StepRecord();

            var parsed = UsageRecordSerializer.ParseLine(UsageRecordSerializer.BuildLine(original));

            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.Pipeline, Is.EqualTo("review_flow"));
            Assert.That(parsed.PipelineRunId, Is.EqualTo("run-1"));
            Assert.That(parsed.PipelineStep, Is.EqualTo(2));
            Assert.That(parsed.PipelineStepId, Is.EqualTo("verdict"));
            Assert.That(parsed.PipelineAttempt, Is.EqualTo(3));
            Assert.That(parsed.PipelineBranch, Is.EqualTo(1));
        }

        [Test]
        public void ParseLine_PlainRun_HasNoPosition()
        {
            var line = UsageRecordSerializer.BuildLine(new UsageRecord
            {
                Timestamp = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc),
                Source = UsageSource.Main,
                RequestId = "request-1"
            });

            var parsed = UsageRecordSerializer.ParseLine(line);

            Assert.That(parsed.Pipeline, Is.Null);
            Assert.That(parsed.PipelineRunId, Is.Null);
            Assert.That(parsed.PipelineStep, Is.Null);
            Assert.That(parsed.PipelineAttempt, Is.Null);
            Assert.That(parsed.PipelineBranch, Is.Null);
        }
    }
}
