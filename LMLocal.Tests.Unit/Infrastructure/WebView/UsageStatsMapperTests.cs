using System;
using System.Collections.Generic;
using LMLocal.Application.Usage;
using LMLocal.Core.Models.Usage;
using LMLocal.Infrastructure.WebView.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.WebView
{
    /// <summary>
    /// Covers the decisions the mapper makes for the dialog: when a model row carries a provider caption and what it
    /// says, the wire names, and the formats of dates.
    /// </summary>
    [TestFixture]
    public class UsageStatsMapperTests
    {
        private static UsageModelRow Model(string model, string provider, string baseUrl)
        {
            return new UsageModelRow { Model = model, Provider = provider, BaseUrl = baseUrl, Prompt = 10, Completion = 1 };
        }

        private static UsageStats WithModels(params UsageModelRow[] models)
        {
            return new UsageStats { HasData = true, Models = new List<UsageModelRow>(models) };
        }

        [Test]
        public void NoData_KeepsPeriodAndLoggingFlag_AndNothingElse()
        {
            UsageStatsResponse response = UsageStatsMapper.From(new UsageStats(), UsagePeriod.Days7, loggingEnabled: false);

            Assert.That(response.HasData, Is.False);
            Assert.That(response.Period, Is.EqualTo("7d"));
            Assert.That(response.LoggingEnabled, Is.False, "The dialog uses it to tell 'nothing yet' from 'logging is off'.");
            Assert.That(response.Days, Is.Empty);
        }

        [Test]
        public void CachedPercent_IsOverTheReportedPrompt_AndAbsentWhenTheProviderNeverReports()
        {
            UsageModelRow deepseek = Model("deepseek-flash", "deepseek", "https://api.deepseek.com");
            deepseek.Prompt = 689829;
            deepseek.Cached = 664192;
            deepseek.CacheReportedPrompt = 689829;

            UsageModelRow gemma = Model("gemma", "lmstudio", "http://localhost:1234");
            gemma.Prompt = 4352311;

            UsageStatsResponse response = UsageStatsMapper.From(WithModels(deepseek, gemma), UsagePeriod.Today);

            Assert.That(response.Models[0].CachedPercent, Is.EqualTo(96));
            Assert.That(response.Models[1].CachedPercent, Is.Null, "No report is not 0%.");
        }

        [Test]
        public void OneProviderOneAddress_HasNoProviderCaption()
        {
            UsageStatsResponse response = UsageStatsMapper.From(
                WithModels(Model("a", "lmstudio", "http://localhost:1234"), Model("b", "lmstudio", "http://localhost:1234")),
                UsagePeriod.Today);

            Assert.That(response.Models, Has.All.Matches<UsageModelResponse>(m => !m.ShowProviderLabel && m.ProviderLabel == null),
                "A caption repeated on every row says nothing.");
        }

        [Test]
        public void TwoProviders_EveryRowShowsItsProviderName()
        {
            UsageStatsResponse response = UsageStatsMapper.From(
                WithModels(Model("a", "lmstudio", "http://localhost:1234"), Model("b", "deepseek", "https://api.deepseek.com")),
                UsagePeriod.Today);

            Assert.That(response.Models[0].ShowProviderLabel, Is.True);
            Assert.That(response.Models[0].ProviderLabel, Is.EqualTo("LM Studio"));
            Assert.That(response.Models[1].ProviderLabel, Is.EqualTo("DeepSeek"));
        }

        [Test]
        public void OneProviderTwoAddresses_CaptionAddsTheHost()
        {
            UsageStatsResponse response = UsageStatsMapper.From(
                WithModels(Model("a", "lmstudio", "http://localhost:1234"), Model("a", "lmstudio", "http://192.168.1.5:1234")),
                UsagePeriod.Today);

            Assert.That(response.Models[0].ProviderLabel, Is.EqualTo("LM Studio · localhost"));
            Assert.That(response.Models[1].ProviderLabel, Is.EqualTo("LM Studio · 192.168.1.5"),
                "Two servers of one provider are told apart by host.");
        }

        [Test]
        public void EmptyModel_IsShownAsUnknown_AndSourcesUseLowercaseWireNames()
        {
            var stats = WithModels(Model(" ", "lmstudio", null));
            stats.Sources.Add(new UsageSourceRow { Source = UsageSource.Subagent, Runs = 3 });

            UsageStatsResponse response = UsageStatsMapper.From(stats, UsagePeriod.Current);

            Assert.That(response.Models[0].Model, Is.EqualTo("unknown"));
            Assert.That(response.Sources[0].Source, Is.EqualTo("subagent"), "The dialog keys its labels by these names.");
            Assert.That(response.Period, Is.EqualTo("current"));
        }

        [Test]
        public void Session_FirstIsUtcWithZ_AndDayIsIsoDate()
        {
            var stats = new UsageStats { HasData = true };
            stats.Days.Add(new UsageDayGroup
            {
                DateLocal = new DateTime(2026, 11, 20),
                Sessions = new List<UsageSessionRow>
                {
                    new UsageSessionRow { SessionId = "s1", FirstUtc = new DateTime(2026, 11, 19, 22, 30, 0, DateTimeKind.Utc) }
                }
            });

            UsageStatsResponse response = UsageStatsMapper.From(stats, UsagePeriod.Today);

            Assert.That(response.Days[0].Date, Is.EqualTo("2026-11-20"));
            Assert.That(response.Days[0].Sessions[0].First, Is.EqualTo("2026-11-19T22:30:00.0000000Z"),
                "The dialog formats the time in the viewer's zone, so the backend sends UTC and never converts it.");
        }
    }
}
