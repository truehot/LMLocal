using System;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Application.Usage;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.WebView.Models;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for the Usage &amp; Stats dialog.
    /// </summary>
    public interface IUsageController
    {
        /// <summary>
        /// Statistics of one selection: <c>current</c>, <c>today</c>, <c>yesterday</c>, <c>3d</c>, <c>7d</c> or <c>30d</c>.
        /// </summary>
        Task<string> GetUsageStatsAsync(string period);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class UsageController : IUsageController
    {
        private readonly IUsageStatsService _statsService;
        private readonly IChatHistoryManager _history;
        private readonly ISettingsManager _settingsManager;

        internal UsageController(IUsageStatsService statsService, IChatHistoryManager history, ISettingsManager settingsManager)
        {
            _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _settingsManager = settingsManager;
        }

        public async Task<string> GetUsageStatsAsync(string period)
        {
            UsagePeriod parsed = UsagePeriods.Parse(period);

            try
            {
                UsageStats stats = parsed == UsagePeriod.Current
                    ? await _statsService.GetSessionStatsAsync(UsageIds.SessionId(_history?.CurrentSessionId)).ConfigureAwait(false)
                    : await _statsService.GetPeriodStatsAsync(parsed).ConfigureAwait(false);

                return UsageStatsMapper.From(stats, parsed, LoggingEnabled()).ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"UsageController: GetUsageStatsAsync('{period}') failed", ex);

                return new UsageStatsResponse
                {
                    Period = UsagePeriods.ToWireName(parsed),
                    LoggingEnabled = LoggingEnabled()
                }.ToJson();
            }
        }

        /// <summary>
        /// Whether usage is being recorded at all; the dialog turns this into its "turn the option on" hint.
        /// </summary>
        private bool LoggingEnabled()
        {
            AppSettings settings = _settingsManager?.Current;

            return settings?.EnableChatLogging != false;
        }
    }
}
