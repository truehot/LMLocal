using System;
using System.Collections.Generic;
using System.Threading;
using LMLocal.Application.Tool;

namespace LMLocal.Application.SubAgents
{
    /// <summary>Sends pipeline progress to the main chat: step lines, and the token total of the call over all steps.</summary>
    internal sealed class SubAgentPipelineProgress
    {
        private readonly IProgress<ToolActivityEvent> _inner;
        private readonly object _tokenLock = new object();
        private readonly Dictionary<object, TokenSlot> _slots = new Dictionary<object, TokenSlot>();
        private int _step;

        internal SubAgentPipelineProgress(IProgress<ToolActivityEvent> inner)
        {
            _inner = inner;
        }

        /// <summary>Progress of a sequential step: <c>[2/3 analyze] analyzer · find_files</c>.</summary>
        internal SubAgentStepProgress BeginStep(string label, string agentDisplayName)
        {
            var fixedLabel = label ?? string.Empty;
            return new SubAgentStepProgress(this, () => fixedLabel, agentDisplayName);
        }

        /// <summary>Progress of a parallel group: <c>[1/2 collect · 1/3 done] explorer · ...</c>.</summary>
        internal SubAgentGroupProgress BeginGroup(string stepLabel, int total)
        {
            return new SubAgentGroupProgress(this, stepLabel, total);
        }

        internal void Report(string label, string agent, string payload)
        {
            if (_inner == null)
                return;

            var prefix = "[" + (label ?? string.Empty) + "] " + (agent ?? string.Empty) + " · ";
            _inner.Report(new ToolActivityEvent
            {
                Message = string.IsNullOrEmpty(payload) ? prefix + "step" : prefix + payload,
                Step = Interlocked.Increment(ref _step)
            });
        }

        /// <summary>Stores the tokens of one step or branch and reports the sum over all of them.</summary>
        internal void ReportTokens(object source, string agent, int tokens, double tokensPerSecond)
        {
            if (_inner == null)
                return;

            lock (_tokenLock)
            {
                _slots[source] = new TokenSlot(tokens, tokensPerSecond);

                int total = 0;
                double speed = 0;

                foreach (TokenSlot slot in _slots.Values)
                {
                    total += slot.Tokens;
                    speed += slot.Speed;
                }

                _inner.Report(new ToolActivityEvent
                {
                    Tokens = total,
                    TokensPerSecond = speed,
                    AgentName = agent
                });
            }
        }

        /// <summary>Last tokens and speed of one step or branch.</summary>
        private struct TokenSlot
        {
            internal TokenSlot(int tokens, double speed)
            {
                Tokens = tokens;
                Speed = speed;
            }

            internal readonly int Tokens;
            internal readonly double Speed;
        }
    }

    /// <summary>
    /// Progress of one step or of one branch of a parallel step.
    /// </summary>
    internal sealed class SubAgentStepProgress : IProgress<ToolActivityEvent>
    {
        private readonly SubAgentPipelineProgress _sink;
        private readonly Func<string> _label;
        private readonly string _agent;
        private int _previousAttemptsTokens;
        private int _attemptTokens;

        internal SubAgentStepProgress(SubAgentPipelineProgress sink, Func<string> label, string agent)
        {
            _sink = sink;
            _label = label;
            _agent = agent;
        }

        public void Report(ToolActivityEvent value)
        {
            if (value != null && value.IsTokenEvent)
            {
                _attemptTokens = value.Tokens.Value;
                _sink.ReportTokens(this, _agent, _previousAttemptsTokens + _attemptTokens, value.TokensPerSecond ?? 0);
                return;
            }

            string payload;
            if (value == null)
                payload = "step";
            else
                payload = !string.IsNullOrWhiteSpace(value.ToolName) ? value.ToolName : value.Message;

            _sink.Report(_label == null ? null : _label(), _agent, payload);
        }

        /// <summary>
        /// Announce the start of the step (or of an attempt) before its first tool call, so the card updates at once.
        /// </summary>
        internal void ReportStart(string extra)
        {
            _previousAttemptsTokens += _attemptTokens;
            _attemptTokens = 0;

            var payload = string.IsNullOrWhiteSpace(extra) ? "starting" : extra.Trim() + " · starting";
            _sink.Report(_label == null ? null : _label(), _agent, payload);
        }
    }

    /// <summary>
    /// Progress of a parallel group: the label gains the number of finished branches, so the card shows
    /// how far the group has come without an extra event.
    /// </summary>
    internal sealed class SubAgentGroupProgress
    {
        private readonly SubAgentPipelineProgress _sink;
        private readonly string _stepLabel;
        private readonly int _total;
        private int _completed;

        internal SubAgentGroupProgress(SubAgentPipelineProgress sink, string stepLabel, int total)
        {
            _sink = sink;
            _stepLabel = stepLabel ?? string.Empty;
            _total = total;
        }

        internal string Label
        {
            get { return _stepLabel + " · " + Volatile.Read(ref _completed) + "/" + _total + " done"; }
        }

        internal SubAgentStepProgress CreateBranchProgress(string agentDisplayName)
        {
            return new SubAgentStepProgress(_sink, () => Label, agentDisplayName);
        }

        internal void MarkCompleted()
        {
            Interlocked.Increment(ref _completed);
        }
    }
}
