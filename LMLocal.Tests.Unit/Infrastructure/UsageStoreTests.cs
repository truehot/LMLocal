using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Usage;
using LMLocal.Core.Models;
using LMLocal.Core.Models.Usage;
using LMLocal.Infrastructure.Persistence;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    /// <summary>
    /// Covers the monthly usage file: one line per record, written only when logging is on, read back by period and by
    /// session, and leaving a broken line out instead of failing the whole file.
    /// </summary>
    [TestFixture]
    public class UsageStoreTests
    {
        private sealed class FakeLockManager : IFileLockManager
        {
            public int Acquired { get; private set; }

            public Task WaitAsync(string absolutePath, CancellationToken cancellationToken = default)
            {
                Acquired++;
                return Task.CompletedTask;
            }

            public void Release(string absolutePath)
            {
            }
        }

        /// <summary>
        /// Serializes the appends the way the production lock manager does. The store must hold the lock for the whole
        /// write, otherwise the two concurrent appends interleave in the file and tear each other's lines.
        /// </summary>
        private sealed class SerializingLockManager : IFileLockManager
        {
            private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
                new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

            public Task WaitAsync(string absolutePath, CancellationToken cancellationToken = default)
            {
                return _gates.GetOrAdd(absolutePath, _ => new SemaphoreSlim(1, 1)).WaitAsync(cancellationToken);
            }

            public void Release(string absolutePath)
            {
                SemaphoreSlim gate;

                if (_gates.TryGetValue(absolutePath, out gate))
                    gate.Release();
            }
        }

        private Dictionary<string, List<byte>> _files;

        private Mock<IFileSystem> CreateFileSystem()
        {
            _files = new Dictionary<string, List<byte>>(StringComparer.OrdinalIgnoreCase);
            var fileSystem = new Mock<IFileSystem>();

            fileSystem.Setup(f => f.CreateDirectory(It.IsAny<string>()));
            fileSystem.Setup(f => f.EnsureDirectoryExistsForFile(It.IsAny<string>()));
            fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns((string path) => _files.ContainsKey(path));

            fileSystem
                .Setup(f => f.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .Returns((string path, byte[] data, CancellationToken token) =>
                {
                    _files[path] = new List<byte>(data);
                    return Task.CompletedTask;
                });

            fileSystem
                .Setup(f => f.AppendAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .Returns((string path, byte[] data, CancellationToken token) =>
                {
                    _files[path].AddRange(data);
                    return Task.CompletedTask;
                });

            fileSystem
                .Setup(f => f.ReadAllTextWithSharedReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken token) => Task.FromResult(ReadText(path)));

            fileSystem
                .Setup(f => f.ReadAllText(It.IsAny<string>()))
                .Returns((string path) => ReadText(path));

            return fileSystem;
        }

        private string ReadText(string path)
        {
            return _files.TryGetValue(path, out List<byte> bytes)
                ? Encoding.UTF8.GetString(bytes.ToArray())
                : string.Empty;
        }

        private static Mock<ISettingsManager> CreateSettings(AppSettings settings)
        {
            var manager = new Mock<ISettingsManager>();
            manager.Setup(s => s.LocalAppDataFolder).Returns("LMLocalChat");
            manager.Setup(s => s.Current).Returns(settings);

            return manager;
        }

        private UsageStore CreateStore(
            ISettingsManager settings,
            IFileSystem fileSystem,
            IFileLockManager locks = null,
            Func<string> solutionDirectory = null)
        {
            return new UsageStore(
                settings,
                fileSystem,
                locks ?? new FakeLockManager(),
                solutionDirectory ?? (() => null));
        }

        private static UsageRecord Record(
            DateTime timestampUtc,
            string sessionId = "session-1",
            string title = null,
            string solutionDir = null)
        {
            return new UsageRecord
            {
                Timestamp = timestampUtc,
                SessionId = sessionId,
                RequestId = "request-1",
                Source = UsageSource.Main,
                Origin = UsageOrigins.Chat,
                Title = title,
                SolutionDir = solutionDir,
                Provider = "lmstudio",
                BaseUrl = "http://localhost:1234",
                Model = "qwen3-coder-30b",
                Prompt = 1000,
                Completion = 100,
                Cached = 500,
                Reasoning = 10,
                Rounds = 2,
                ToolCalls = 3,
                GenCompletion = 100,
                GenMs = 5000,
                DurationMs = 42000,
                ContextMax = 32768,
                ContextPeak = 12000,
                Outcome = UsageOutcome.Ok
            };
        }

        [Test]
        public void Serializer_RoundTrip_KeepsEveryField()
        {
            UsageRecord original = Record(new DateTime(2026, 11, 20, 10, 15, 2, DateTimeKind.Utc), solutionDir: @"C:\Work\Solution");

            UsageRecord parsed = UsageRecordSerializer.ParseLine(UsageRecordSerializer.BuildLine(original));

            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.Timestamp, Is.EqualTo(original.Timestamp));
            Assert.That(parsed.SessionId, Is.EqualTo("session-1"));
            Assert.That(parsed.Source, Is.EqualTo(UsageSource.Main));
            Assert.That(parsed.Prompt, Is.EqualTo(1000));
            Assert.That(parsed.Completion, Is.EqualTo(100));
            Assert.That(parsed.Cached, Is.EqualTo(500));
            Assert.That(parsed.Reasoning, Is.EqualTo(10));
            Assert.That(parsed.Rounds, Is.EqualTo(2));
            Assert.That(parsed.ToolCalls, Is.EqualTo(3));
            Assert.That(parsed.GenMs, Is.EqualTo(5000));
            Assert.That(parsed.DurationMs, Is.EqualTo(42000));
            Assert.That(parsed.ContextMax, Is.EqualTo(32768));
            Assert.That(parsed.ContextPeak, Is.EqualTo(12000));
            Assert.That(parsed.Outcome, Is.EqualTo(UsageOutcome.Ok));
            Assert.That(parsed.SolutionDir, Is.EqualTo(@"C:\Work\Solution"));
        }

        [Test]
        public void Serializer_BrokenLine_IsSkippedInsteadOfThrowing()
        {
            Assert.That(UsageRecordSerializer.ParseLine("{not json"), Is.Null);
            Assert.That(UsageRecordSerializer.ParseLine(string.Empty), Is.Null);
            Assert.That(UsageRecordSerializer.ParseLine("{\"session_id\":\"x\"}"), Is.Null,
                "A line without a start time cannot be placed in a day or a month.");
        }

        [Test]
        public void Serializer_LineWithoutSolutionFolder_ReadsNull()
        {
            // A line written before the field existed: no "solution_dir" key at all.
            UsageRecord parsed = UsageRecordSerializer.ParseLine(
                "{\"ts\":\"2026-11-20T10:15:02Z\",\"session_id\":\"session-1\",\"source\":\"main\"}");

            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.SolutionDir, Is.Null, "Older records simply carry no solution folder.");
        }

        [Test]
        public async Task Append_FillsTheSolutionFolderFromTheProvider()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(
                CreateSettings(settings).Object,
                fileSystem.Object,
                solutionDirectory: () => @"C:\Work\Solution");

            await store.AppendAsync(Record(DateTime.UtcNow));

            IReadOnlyList<UsageRecord> records = await store.ReadAsync(DateTime.UtcNow.AddHours(-1));

            Assert.That(records.Single().SolutionDir, Is.EqualTo(@"C:\Work\Solution"),
                "The store stamps the folder of the solution that was open when the record was written.");
        }

        [Test]
        public async Task Append_KeepsTheSolutionFolderThatIsAlreadySet()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(
                CreateSettings(settings).Object,
                fileSystem.Object,
                solutionDirectory: () => @"C:\Work\Other");

            await store.AppendAsync(Record(DateTime.UtcNow, solutionDir: @"C:\Work\Solution"));

            IReadOnlyList<UsageRecord> records = await store.ReadAsync(DateTime.UtcNow.AddHours(-1));

            Assert.That(records.Single().SolutionDir, Is.EqualTo(@"C:\Work\Solution"),
                "A writer that knows the solution of its request wins over the one that happens to be open now.");
        }

        [Test]
        public async Task Append_WithoutOpenSolution_WritesTheRecordWithoutTheFolder()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore noSolution = CreateStore(
                CreateSettings(settings).Object,
                fileSystem.Object,
                solutionDirectory: () => null);
            UsageStore blankSolution = CreateStore(
                CreateSettings(settings).Object,
                fileSystem.Object,
                solutionDirectory: () => "   ");

            await noSolution.AppendAsync(Record(DateTime.UtcNow, "session-1"));
            await blankSolution.AppendAsync(Record(DateTime.UtcNow, "session-2"));

            IReadOnlyList<UsageRecord> records = await noSolution.ReadAsync(DateTime.UtcNow.AddHours(-1));

            Assert.That(records.Count, Is.EqualTo(2), "Both records are written.");
            Assert.That(records.All(r => r.SolutionDir == null), Is.True,
                "No solution open and a blank folder are both stored as null: a line never carries a blank path.");
        }

        [Test]
        public async Task Append_WhenTheSolutionProviderThrows_StillWritesTheRecordWithoutTheFolder()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(
                CreateSettings(settings).Object,
                fileSystem.Object,
                solutionDirectory: () => throw new InvalidOperationException("the solution is being closed"));

            await store.AppendAsync(Record(DateTime.UtcNow));

            IReadOnlyList<UsageRecord> records = await store.ReadAsync(DateTime.UtcNow.AddHours(-1));

            Assert.That(records.Count, Is.EqualTo(1), "A failing solution provider costs the folder, not the record.");
            Assert.That(records.Single().SolutionDir, Is.Null);
        }

        [Test]
        public async Task Append_WritesTheRecordIntoTheMonthFileOfItsStart()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            var locks = new FakeLockManager();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object, locks);

            await store.AppendAsync(Record(new DateTime(2026, 1, 31, 23, 59, 0, DateTimeKind.Utc)));

            Assert.That(_files.Keys.Count, Is.EqualTo(1));
            Assert.That(_files.Keys.Single(), Does.EndWith("202601_usage.jsonl"));
            Assert.That(locks.Acquired, Is.EqualTo(1), "The append takes the shared file lock.");
            Assert.That(ReadText(_files.Keys.Single()).Split('\n').Length, Is.EqualTo(2), "One record, one line.");
        }

        [Test]
        public async Task Append_WhenLoggingIsDisabled_WritesNothing()
        {
            var settings = new AppSettings { EnableChatLogging = false };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object);

            await store.AppendAsync(Record(DateTime.UtcNow));

            Assert.That(_files, Is.Empty);
        }

        [Test]
        public async Task Append_ChecksTheOptionAtWriteTime_NotWhenTheStoreWasCreated()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object);

            settings.EnableChatLogging = false;

            await store.AppendAsync(Record(DateTime.UtcNow));

            Assert.That(_files, Is.Empty, "A request that ended after the option was turned off is not written.");
        }

        [Test]
        public async Task ReadAsync_ReturnsOnlyRecordsOfThePeriod_AndReadsTheFilesAroundTheMonthBoundary()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object);

            DateTime nowUtc = DateTime.UtcNow;
            DateTime startOfMonth = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime lastMonth = startOfMonth.AddDays(-10).AddHours(12);
            DateTime thisMonth = startOfMonth.AddHours(12);

            await store.AppendAsync(Record(lastMonth, "old"));
            await store.AppendAsync(Record(thisMonth, "new"));

            IReadOnlyList<UsageRecord> all = await store.ReadAsync(startOfMonth.AddDays(-20));
            Assert.That(all.Count, Is.EqualTo(2));

            IReadOnlyList<UsageRecord> ofThisMonth = await store.ReadAsync(startOfMonth);
            Assert.That(ofThisMonth.Count, Is.EqualTo(1));
            Assert.That(ofThisMonth[0].SessionId, Is.EqualTo("new"));
        }

        [Test]
        public async Task ReadSessionAsync_WalksTheMonthsBack_AndStopsAtTheFirstMonthWithoutRecords()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object);

            DateTime nowUtc = DateTime.UtcNow;
            DateTime startOfMonth = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            await store.AppendAsync(Record(startOfMonth.AddHours(10), "session-1"));
            await store.AppendAsync(Record(startOfMonth.AddMonths(-1).AddHours(10), "session-1"));
            await store.AppendAsync(Record(startOfMonth.AddMonths(-3).AddHours(10), "session-1"));

            IReadOnlyList<UsageRecord> records = await store.ReadSessionAsync("session-1");

            Assert.That(records.Count, Is.EqualTo(2),
                "The search stops at the first month without a record, so a session started last month keeps its start.");
        }

        [Test]
        public async Task ReadSessionAsync_UnknownSession_ReturnsNothing()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object);

            await store.AppendAsync(Record(DateTime.UtcNow, "session-1"));

            Assert.That(await store.ReadSessionAsync("other"), Is.Empty);
            Assert.That(await store.ReadSessionAsync(null), Is.Empty);
        }

        [Test]
        public async Task ReadSessionAsync_CurrentMonthWithoutRecords_StillFindsTheSessionOfEarlierMonths()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object);

            DateTime nowUtc = DateTime.UtcNow;
            DateTime startOfMonth = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // A session that started last month has no record in the current one until its next request ends.
            await store.AppendAsync(Record(startOfMonth.AddMonths(-1).AddHours(10), "session-1"));
            await store.AppendAsync(Record(startOfMonth.AddMonths(-2).AddHours(10), "session-1"));

            IReadOnlyList<UsageRecord> records = await store.ReadSessionAsync("session-1");

            Assert.That(records.Count, Is.EqualTo(2),
                "An empty current month must not cut the search short: the start of the session lives in the previous month.");
        }

        [Test]
        public async Task ReadSessionAsync_MatchesBothIdFormats()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object);

            var id = Guid.NewGuid();
            await store.AppendAsync(Record(DateTime.UtcNow, id.ToString("N")));

            IReadOnlyList<UsageRecord> records = await store.ReadSessionAsync(id.ToString("D"));

            Assert.That(records.Count, Is.EqualTo(1), "A record written with a dashless id still belongs to its session.");
        }

        [Test]
        public async Task ReadSessionAsync_NewSession_ReadsOnlyTheLeadingWindow()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object);

            DateTime nowUtc = DateTime.UtcNow;
            DateTime startOfMonth = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // A year of files written by other sessions: a brand-new session must not read them all.
            for (int back = 0; back < UsageStore.MaxMonthsBack; back++)
                await store.AppendAsync(Record(startOfMonth.AddMonths(-back).AddHours(10), "other-session"));

            IReadOnlyList<UsageRecord> records = await store.ReadSessionAsync("brand-new-session");

            Assert.That(records, Is.Empty);
            fileSystem.Verify(
                f => f.ReadAllTextWithSharedReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Exactly(UsageStore.MaxLeadingEmptyMonths + 1),
                "A session with no records at all must end the search after the leading window, not read a year of files.");
        }

        [Test]
        public async Task ReadSessionAsync_SessionPausedLongerThanTheLeadingWindow_IsOutOfReach()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object);

            DateTime nowUtc = DateTime.UtcNow;
            DateTime startOfMonth = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            await store.AppendAsync(Record(startOfMonth.AddMonths(-3).AddHours(10), "session-1"));

            IReadOnlyList<UsageRecord> records = await store.ReadSessionAsync("session-1");

            Assert.That(records, Is.Empty,
                "The leading window is bounded on purpose: a session silent for more than it is no longer found by Current.");
        }

        [Test]
        public async Task Append_ConcurrentSubAgents_KeepEveryLineIntact()
        {
            var settings = new AppSettings { EnableChatLogging = true };
            Mock<IFileSystem> fileSystem = CreateFileSystem();
            UsageStore store = CreateStore(CreateSettings(settings).Object, fileSystem.Object, new SerializingLockManager());

            const int writers = 8;
            const int perWriter = 25;

            var appends = new List<Task>();

            for (int writer = 0; writer < writers; writer++)
            {
                int index = writer;

                appends.Add(Task.Run(async () =>
                {
                    for (int i = 0; i < perWriter; i++)
                        await store.AppendAsync(Record(DateTime.UtcNow, "subagent-" + index));
                }));
            }

            await Task.WhenAll(appends);

            string written = string.Concat(_files.Keys.Select(ReadText));
            int lines = written.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Length;

            Assert.That(lines, Is.EqualTo(writers * perWriter),
                "Parallel appends of several SubAgents must neither lose nor tear lines.");

            IReadOnlyList<UsageRecord> records = await store.ReadAsync(DateTime.UtcNow.AddHours(-1));

            Assert.That(records.Count, Is.EqualTo(writers * perWriter), "Every line must still parse into a record.");
            Assert.That(records.Select(r => r.SessionId).Distinct().Count(), Is.EqualTo(writers));
        }
    }

    /// <summary>
    /// Covers the local boundaries of the periods: the reader turns them into UTC when it picks the files.
    /// </summary>
    [TestFixture]
    public class UsagePeriodsTests
    {
        [Test]
        public void StartLocal_TodayIsMidnightOfTheGivenDay()
        {
            var now = new DateTime(2026, 11, 20, 16, 34, 0, DateTimeKind.Local);

            Assert.That(UsagePeriods.StartLocal(UsagePeriod.Today, now), Is.EqualTo(new DateTime(2026, 11, 20)));
            Assert.That(UsagePeriods.StartLocal(UsagePeriod.Yesterday, now), Is.EqualTo(new DateTime(2026, 11, 19)));
            Assert.That(UsagePeriods.StartLocal(UsagePeriod.Days3, now), Is.EqualTo(new DateTime(2026, 11, 18)));
            Assert.That(UsagePeriods.StartLocal(UsagePeriod.Days7, now), Is.EqualTo(new DateTime(2026, 11, 14)));
            Assert.That(UsagePeriods.StartLocal(UsagePeriod.Days30, now), Is.EqualTo(new DateTime(2026, 10, 22)));
            Assert.That(UsagePeriods.StartLocal(UsagePeriod.Current, now), Is.Null, "The current session is addressed by id.");
        }

        [Test]
        public void EndLocal_OnlyYesterdayStopsBeforeToday()
        {
            var now = new DateTime(2026, 11, 20, 16, 34, 0, DateTimeKind.Local);

            Assert.That(UsagePeriods.EndLocal(UsagePeriod.Yesterday, now), Is.EqualTo(new DateTime(2026, 11, 20)),
                "Yesterday ends at local midnight of today, exclusively.");
            Assert.That(UsagePeriods.EndLocal(UsagePeriod.Today, now), Is.Null, "Today runs until now.");
            Assert.That(UsagePeriods.EndLocal(UsagePeriod.Days3, now), Is.Null);
            Assert.That(UsagePeriods.EndLocal(UsagePeriod.Days7, now), Is.Null);
            Assert.That(UsagePeriods.EndLocal(UsagePeriod.Days30, now), Is.Null);
            Assert.That(UsagePeriods.EndLocal(UsagePeriod.Current, now), Is.Null);
        }

        [Test]
        public void Parse_UnkwownValueFallsBackToTheCurrentSession()
        {
            Assert.That(UsagePeriods.Parse("7d"), Is.EqualTo(UsagePeriod.Days7));
            Assert.That(UsagePeriods.Parse("30D"), Is.EqualTo(UsagePeriod.Days30));
            Assert.That(UsagePeriods.Parse("today"), Is.EqualTo(UsagePeriod.Today));
            Assert.That(UsagePeriods.Parse("yesterday"), Is.EqualTo(UsagePeriod.Yesterday));
            Assert.That(UsagePeriods.Parse("3d"), Is.EqualTo(UsagePeriod.Days3));
            Assert.That(UsagePeriods.Parse("nonsense"), Is.EqualTo(UsagePeriod.Current));
            Assert.That(UsagePeriods.Parse(null), Is.EqualTo(UsagePeriod.Current));
        }

        [Test]
        public void ToWireName_RoundTripsEveryPeriod()
        {
            Assert.That(UsagePeriods.ToWireName(UsagePeriod.Current), Is.EqualTo("current"));
            Assert.That(UsagePeriods.ToWireName(UsagePeriod.Today), Is.EqualTo("today"));
            Assert.That(UsagePeriods.ToWireName(UsagePeriod.Yesterday), Is.EqualTo("yesterday"));
            Assert.That(UsagePeriods.ToWireName(UsagePeriod.Days3), Is.EqualTo("3d"));
            Assert.That(UsagePeriods.ToWireName(UsagePeriod.Days7), Is.EqualTo("7d"));
            Assert.That(UsagePeriods.ToWireName(UsagePeriod.Days30), Is.EqualTo("30d"));

            foreach (UsagePeriod period in (UsagePeriod[])Enum.GetValues(typeof(UsagePeriod)))
            {
                Assert.That(UsagePeriods.Parse(UsagePeriods.ToWireName(period)), Is.EqualTo(period));
            }
        }
    }
}
