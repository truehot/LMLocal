using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using StackFrame2 = EnvDTE90a.StackFrame2;
// Only the type, not the namespace: 'Debugger' must keep resolving to EnvDTE.Debugger.
using Stopwatch = System.Diagnostics.Stopwatch;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace LMLocal.Infrastructure.VisualStudio
{
    /// <summary>
    /// Package-side view of <see cref="DebuggerContextService"/>: the package owns the moment the
    /// debugger events are subscribed, the DI container owns the disposal.
    /// </summary>
    internal interface IDebuggerContextTracker : IDebuggerContextService
    {
        /// <summary>
        /// Subscribes to the debugger events.
        /// </summary>
        void Initialize();
    }

    /// <summary>
    /// The only place in the solution that talks to the Visual Studio debugger automation model.
    /// </summary>
    internal sealed class DebuggerContextService : IDebuggerContextTracker, IDisposable
    {
        private const int MaxFrames = 5;
        private const int MaxLocals = 20;
        private const int MaxExceptionDepth = 3;

        /// <summary>Hard cap on a single value read from the debugger, before the prompt limit applies.</summary>
        private const int MaxRawValueLength = 500;

        private const int ExpressionTimeoutMs = 1000;

        /// <summary>
        /// The whole collection shares one budget: every read blocks the UI thread, so slow evaluations
        /// must not be able to freeze Visual Studio for the sum of all their timeouts.
        /// </summary>
        private const int CollectBudgetMs = 3000;

        /// <summary>The pseudo variable that holds the exception the debugger stopped on.</summary>
        private const string ExceptionRoot = "$exception";

        private readonly ISettingsManager _settingsManager;
        private readonly IVsDependencies _vsDependencies;

        // Strong references on purpose: a COM event object that no field points at can be collected
        // by the GC, and the subscription then dies silently.
        private DTE _dte;
        private DebuggerEvents _debuggerEvents;

        private volatile DebugStopInfo _current;
        private bool _initialized;
        private bool _disposed;

        public DebuggerContextService(ISettingsManager settingsManager, IVsDependencies vsDependencies)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
        }

        /// <inheritdoc />
        public event Action StopChanged;

        /// <inheritdoc />
        public DebugStopInfo Current => _current;

        /// <inheritdoc />
        public void Initialize()
        {
            if (_initialized || _disposed)
                return;

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                _dte = _vsDependencies.GetDTE();
                if (_dte == null)
                {
                    InternalLogger.Warn("DebuggerContextService: the DTE automation model is not available; the debugger banner is disabled.");
                    return;
                }

                _debuggerEvents = _dte.Events?.DebuggerEvents;
                if (_debuggerEvents == null)
                {
                    InternalLogger.Warn("DebuggerContextService: the debugger events object is not available; the debugger banner is disabled.");
                    return;
                }

                _debuggerEvents.OnEnterBreakMode += OnEnterBreakMode;
                _debuggerEvents.OnEnterRunMode += OnEnterRunMode;
                _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;
                _settingsManager.SettingsChanged += OnSettingsChanged;

                _initialized = true;

                InternalLogger.Info("DebuggerContextService: subscribed to the debugger events.");
            }
            catch (Exception ex)
            {
                InternalLogger.Error("DebuggerContextService: cannot subscribe to the debugger events.", ex);
            }
        }

        /// <inheritdoc />
        public async Task<DebugStopContext> CollectContextAsync(CancellationToken cancellationToken = default)
        {
            if (_current == null || _disposed)
                return null;

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            DebugStopInfo info = _current;
            if (info == null || _disposed)
            {
                InternalLogger.Info("ExplainDebugStop: the stop is gone; nothing is collected.");
                return null;
            }

            DTE dte = _dte;
            if (dte == null)
                return new DebugStopContext { Info = info };

            if (!IsBreakMode(dte))
            {
                InternalLogger.Info("ExplainDebugStop: Visual Studio is no longer in Break Mode; nothing is collected.");
                return null;
            }

            Stopwatch budget = Stopwatch.StartNew();

            var context = new DebugStopContext
            {
                Info = info,
                Frames = CollectFrames(dte, info, budget),
                Locals = CollectLocals(dte, budget)
            };

            if (info.Kind == DebugStopKind.Exception)
                context.Exceptions = CollectExceptions(dte, budget);

            return context;
        }

        #region Debugger events

        /// <summary>
        /// The single place that decides whether the banner is shown. Only the cheap part is read here.
        /// </summary>
        private void OnEnterBreakMode(dbgEventReason reason, ref dbgExecutionAction executionAction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // The action is deliberately left untouched: overriding it would change what the debugger does.
            try
            {
                DebugStopReason stopReason = MapReason(reason);

                if (!IsFeatureEnabled() || !DebugStopPolicy.ShouldShow(stopReason))
                {
                    PublishStop(null);
                    return;
                }

                var info = new DebugStopInfo
                {
                    Reason = stopReason,
                    Kind = DebugStopPolicy.GetKind(stopReason)
                };

                ReadLocation(info);

                InternalLogger.Info(
                    $"DebuggerContextService: stop '{stopReason}' at '{info.File}' line {info.Line}.");

                PublishStop(info);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("DebuggerContextService: OnEnterBreakMode failed.", ex);
            }
        }

        private void OnEnterRunMode(dbgEventReason reason)
        {
            PublishStop(null);
        }

        private void OnEnterDesignMode(dbgEventReason reason)
        {
            PublishStop(null);
        }

        private void OnSettingsChanged(AppSettings settings)
        {
            try
            {
                if (settings?.EnableDebuggerContext == false)
                    PublishStop(null);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("DebuggerContextService: reacting to the settings change failed", ex);
            }
        }

        #endregion

        #region Reading the stop

        private bool IsFeatureEnabled()
        {
            try
            {
                return _settingsManager.Current?.EnableDebuggerContext ?? false;
            }
            catch (InvalidOperationException)
            {
                // Settings are not loaded yet: behave as if the feature is off.
                return false;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("DebuggerContextService: cannot read the settings", ex);
                return false;
            }
        }

        /// <summary>
        /// The file and the line of the frame the debugger stopped at.
        /// </summary>
        private void ReadLocation(DebugStopInfo info)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (!(_dte?.Debugger?.CurrentStackFrame is StackFrame2 frame))
                {
                    InternalLogger.Info("DebuggerContextService: the current stack frame exposes no source location.");
                    return;
                }

                info.File = Trimmed(SafeRead(() =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    return frame.FileName;
                }));
                info.Line = SafeReadLineNumber(() =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    return frame.LineNumber;
                });
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("DebuggerContextService: cannot read the stopping frame", ex);
            }
        }

        private static bool IsBreakMode(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                return dte.Debugger?.CurrentMode == dbgDebugMode.dbgBreakMode;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("DebuggerContextService: cannot read the debugger mode", ex);
                return false;
            }
        }

        private static IReadOnlyList<DebugStackFrameInfo> CollectFrames(DTE dte, DebugStopInfo info, Stopwatch budget)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var frames = new List<DebugStackFrameInfo>();

            try
            {
                StackFrames stackFrames = dte.Debugger?.CurrentThread?.StackFrames;
                if (stackFrames == null)
                    return frames;

                foreach (StackFrame frame in stackFrames)
                {
                    if (frames.Count >= MaxFrames || BudgetExhausted(budget, "the stack"))
                        break;

                    if (frame == null)
                        continue;

                    var item = new DebugStackFrameInfo
                    {
                        FunctionName = SafeRead(() =>
                        {
                            ThreadHelper.ThrowIfNotOnUIThread();
                            return frame.FunctionName;
                        })
                    };

                    if (frame is StackFrame2 frame2)
                    {
                        item.File = Trimmed(SafeRead(() =>
                        {
                            ThreadHelper.ThrowIfNotOnUIThread();
                            return frame2.FileName;
                        }));
                        item.Line = SafeReadLineNumber(() =>
                        {
                            ThreadHelper.ThrowIfNotOnUIThread();
                            return frame2.LineNumber;
                        });
                    }

                    if (frames.Count == 0 && info != null)
                    {
                        if (item.File == null)
                            item.File = info.File;

                        if (item.Line <= 0)
                            item.Line = info.Line;
                    }

                    frames.Add(item);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("DebuggerContextService: cannot read the stack frames", ex);
            }

            return frames;
        }

        private static IReadOnlyList<DebugLocalVariable> CollectLocals(DTE dte, Stopwatch budget)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var locals = new List<DebugLocalVariable>();

            try
            {
                Expressions expressions = dte.Debugger?.CurrentStackFrame?.Locals;
                if (expressions == null)
                    return locals;

                foreach (Expression expression in expressions)
                {
                    if (locals.Count >= MaxLocals || BudgetExhausted(budget, "the local variables"))
                        break;

                    if (expression == null)
                        continue;

                    string name = ReadName(expression);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    string type = ReadType(expression);
                    string value = DebugLocalVariable.IsSensitiveName(name)
                        ? DebugLocalVariable.HiddenValue
                        : ReadValue(expression, type);

                    locals.Add(new DebugLocalVariable { Name = name, Type = type, Value = value });
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("DebuggerContextService: cannot read the local variables", ex);
            }

            return locals;
        }

        /// <summary>
        /// The exception chain, read through explicit expressions.
        /// </summary>
        private static IReadOnlyList<DebugExceptionInfo> CollectExceptions(DTE dte, Stopwatch budget)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var chain = new List<DebugExceptionInfo>();

            try
            {
                Debugger debugger = dte.Debugger;
                if (debugger == null)
                    return chain;

                string path = ExceptionRoot;
                bool timedOut;
                Expression current = Evaluate(debugger, path, out timedOut);

                while (current != null && chain.Count < MaxExceptionDepth)
                {
                    if (timedOut)
                    {
                        InternalLogger.Warn("ExplainDebugStop: reading '$exception' timed out; the chain is incomplete.");
                        break;
                    }

                    if (BudgetExhausted(budget, "the exception chain"))
                        break;

                    string type = Trimmed(ReadType(current));
                    string message = Trimmed(DebugStopText.Unquote(ValueOf(Evaluate(debugger, path + ".Message", out _))));

                    if (type == null && message == null)
                        break;

                    chain.Add(new DebugExceptionInfo { Type = type, Message = message });

                    Expression inner = Evaluate(debugger, path + ".InnerException", out timedOut);
                    if (HasValue(inner))
                    {
                        path += ".InnerException";
                        current = inner;
                        continue;
                    }

                    if (IsAggregate(type))
                    {
                        inner = Evaluate(debugger, path + ".InnerExceptions[0]", out timedOut);
                        if (HasValue(inner))
                        {
                            path += ".InnerExceptions[0]";
                            current = inner;
                            continue;
                        }
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("DebuggerContextService: cannot read '$exception'", ex);
            }

            if (chain.Count == 0)
                InternalLogger.Info("ExplainDebugStop: '$exception' is not available; the prompt has no exception block.");

            return chain;
        }

        private static bool IsAggregate(string type)
        {
            return type?.IndexOf("AggregateException", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Evaluates one strict expression.
        /// </summary>
        private static Expression Evaluate(Debugger debugger, string expression, out bool timedOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            timedOut = false;

            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                Expression result = debugger.GetExpression(expression, true, ExpressionTimeoutMs);
                timedOut = stopwatch.ElapsedMilliseconds >= ExpressionTimeoutMs;

                if (result == null || !IsValid(result))
                    return null;

                return result;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"DebuggerContextService: cannot evaluate '{expression}'", ex);
                return null;
            }
        }

        private static string ValueOf(Expression expression)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return expression == null ? null : SafeRead(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return expression.Value;
            });
        }

        private static string ReadName(Expression expression)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return SafeRead(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return expression.Name;
            });
        }

        private static string ReadType(Expression expression)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return SafeRead(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return expression.Type;
            });
        }

        private static bool IsValid(Expression expression)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return SafeReadBool(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return expression.IsValidValue;
            });
        }

        /// <summary>True when the expression evaluated to anything but null.</summary>
        private static bool HasValue(Expression expression)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return DebugStopText.HasValue(ValueOf(expression));
        }

        /// <summary>
        /// The value of a local is read only for types that cannot run code: reading <c>.Value</c> of an
        /// object can invoke its DebuggerDisplay or ToString inside the debugged process, and up to
        /// <see cref="MaxLocals"/> of them would run on the UI thread.
        /// </summary>
        private static string ReadValue(Expression expression, string type)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!DebugStopText.IsSimpleType(type))
                return null;

            string value = ValueOf(expression);
            if (string.IsNullOrEmpty(value))
                return null;

            return value.Length > MaxRawValueLength ? value.Substring(0, MaxRawValueLength) : value;
        }

        private static string SafeRead(Func<string> read, [CallerLineNumber] int callerLine = 0)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"DebuggerContextService: a debugger read failed (called at line {callerLine})", ex);
                return null;
            }
        }

        private static bool SafeReadBool(Func<bool> read, [CallerLineNumber] int callerLine = 0)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"DebuggerContextService: a debugger read failed (called at line {callerLine})", ex);
                return false;
            }
        }

        /// <summary>Reads a one-based line number; 0 stands for "unknown".</summary>
        private static int SafeReadLineNumber(Func<uint> read, [CallerLineNumber] int callerLine = 0)
        {
            try
            {
                uint line = read();
                return line > 0 && line <= int.MaxValue ? (int)line : 0;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"DebuggerContextService: a debugger read failed (called at line {callerLine})", ex);
                return 0;
            }
        }

        private static string Trimmed(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// True once the shared budget of a collection is used up.
        /// </summary>
        private static bool BudgetExhausted(Stopwatch budget, string what)
        {
            if (budget == null || budget.ElapsedMilliseconds < CollectBudgetMs)
                return false;

            InternalLogger.Warn(
                $"ExplainDebugStop: the {CollectBudgetMs} ms collection budget is exhausted; {what} is incomplete.");
            return true;
        }

        #endregion

        #region Publishing

        /// <summary>
        /// Raises <see cref="StopChanged"/> on the UI thread. A repeated stop and a repeated hide are dropped.
        /// </summary>
        private void PublishStop(DebugStopInfo info)
        {
            if (_disposed)
                return;

            if (ThreadHelper.CheckAccess())
            {
                ApplyStop(info);
                return;
            }

            try
            {
                JoinableTaskFactory joinableTaskFactory = ThreadHelper.JoinableTaskFactory;
                if (joinableTaskFactory == null)
                {
                    ApplyStop(info);
                    return;
                }

                _ = joinableTaskFactory.RunAsync(async () =>
                {
                    await joinableTaskFactory.SwitchToMainThreadAsync();
                    ApplyStop(info);
                });
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("DebuggerContextService: cannot marshal the stop to the UI thread", ex);
            }
        }

        private void ApplyStop(DebugStopInfo info)
        {
            if (_disposed)
                return;

            DebugStopInfo previous = _current;

            if (info == null && previous == null)
                return;

            if (info != null && info.Equals(previous))
                return;

            _current = info;

            try
            {
                StopChanged?.Invoke();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("DebuggerContextService: a StopChanged subscriber failed.", ex);
            }
        }

        /// <summary>
        /// Maps the automation enum onto the Core enum.
        /// </summary>
        private static DebugStopReason MapReason(dbgEventReason reason)
        {
            return DebugStopPolicy.FromEventReasonName(reason.ToString());
        }

        #endregion

        private void UnsubscribeDebuggerEvents()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_debuggerEvents == null)
                return;

            _debuggerEvents.OnEnterBreakMode -= OnEnterBreakMode;
            _debuggerEvents.OnEnterRunMode -= OnEnterRunMode;
            _debuggerEvents.OnEnterDesignMode -= OnEnterDesignMode;
        }

        public void Dispose()
        {
            // The DI container may dispose the same instance twice, because it is registered under two interfaces.
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _settingsManager.SettingsChanged -= OnSettingsChanged;

                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    UnsubscribeDebuggerEvents();
                });
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("DebuggerContextService: cannot unsubscribe from the debugger events", ex);
            }
            finally
            {
                _debuggerEvents = null;
                _dte = null;
                _current = null;
                StopChanged = null;
            }
        }
    }
}
