using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class SearchChatHistoryTests
    {
        private static readonly DateTime T0 = new DateTime(2024, 3, 1, 9, 0, 0, DateTimeKind.Utc);

        private Mock<IChatPersistenceService> _persistenceMock;
        private Mock<ISettingsManager> _settingsMock;
        private SearchResultCache _cache;
        private SearchChatHistory _tool;

        [SetUp]
        public void SetUp()
        {
            _persistenceMock = new Mock<IChatPersistenceService>();
            _settingsMock = new Mock<ISettingsManager>();
            _settingsMock.SetupGet(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            _cache = new SearchResultCache();
            _tool = new SearchChatHistory(_persistenceMock.Object, _cache, _settingsMock.Object);

            _persistenceMock
                .Setup(p => p.ReadAllSessionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ChatSessionLog>());
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static ChatLogEntry Msg(string role, string text, int minute = 0)
        {
            return new ChatLogEntry
            {
                Message = new ChatMessage(role, text),
                TimestampUtc = T0.AddMinutes(minute)
            };
        }

        private static ChatSessionLog Log(string sessionId, params ChatLogEntry[] entries)
        {
            return new ChatSessionLog
            {
                SessionId = sessionId,
                Entries = new List<ChatLogEntry>(entries)
            };
        }

        private static string InsertFile(string path, string content)
        {
            return MarkdownCodeBlockFormatter.FormatFileAsMarkdown(content, path);
        }

        private void SetupSessions(params ChatSessionLog[] sessions)
        {
            _persistenceMock
                .Setup(p => p.ReadAllSessionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ChatSessionLog>(sessions));
        }

        private static ChatHistorySearchResponse Run(SearchChatHistory tool, Dictionary<string, object> parameters)
        {
            return (ChatHistorySearchResponse)tool.ExecuteAsync(parameters, CancellationToken.None).GetAwaiter().GetResult();
        }

        // ---------------------------------------------------------------
        // Metadata
        // ---------------------------------------------------------------

        [Test]
        public void GetToolInfo_ReturnsCorrectMetadata()
        {
            var info = _tool.GetToolInfo();

            Assert.That(info.Name, Is.EqualTo("search_chat_history"));
            Assert.That(_tool.ToolName, Is.EqualTo("search_chat_history"));
            Assert.That(_tool.AccessLevel, Is.EqualTo(ToolAccessLevel.ReadOnly));
            Assert.That(info.Parameters.Properties, Contains.Key("query"));
            Assert.That(info.Parameters.Properties.ContainsKey("scope"), Is.False);
            Assert.That(info.Parameters.Properties, Contains.Key("max_results"));
            Assert.That(info.Parameters.Properties["max_results"].Description, Does.Contain("Default 3, maximum 5"));
            Assert.That(info.Parameters.Required, Is.EquivalentTo(new[] { "query" }));
        }

        [Test]
        public void MatchResult_HasOnlyTheDocumentedFields()
        {
            var names = new List<string>();
            foreach (var property in typeof(ChatHistoryMatchResult).GetProperties())
                names.Add(property.Name);

            Assert.That(names, Does.Not.Contain("SessionTitle"));
            Assert.That(names, Does.Not.Contain("Role"));
            Assert.That(names, Does.Not.Contain("Heading"));
            Assert.That(names, Does.Contain("Question"));
            Assert.That(names, Does.Contain("AnswerStart"));
            Assert.That(names, Does.Contain("Files"));
            Assert.That(names, Does.Contain("LastAnswerStart"));
        }

        [Test]
        public void GetToolInfo_DescribesTheOrdering()
        {
            var info = _tool.GetToolInfo();

            Assert.That(info.Description, Does.Contain("Whole-phrase or all-words matches come first"));
            Assert.That(info.Description, Does.Contain("most matched words first"));
        }

        // ---------------------------------------------------------------
        // Basic search
        // ---------------------------------------------------------------

        [Test]
        public void Execute_MatchInQuestion_ReturnsQuestionWithoutSnippet()
        {
            SetupSessions(Log("s1", Msg("user", "How to configure NLog?", 0), Msg("assistant", "Use InternalLogger.", 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Question, Is.EqualTo("How to configure NLog?"));
            Assert.That(resp.Results[0].AnswerStart, Is.EqualTo("Use InternalLogger."));
            Assert.That(resp.Results[0].Snippet, Is.Null, "the match is already visible in the question");
            Assert.That(resp.Results[0].Files, Is.Null);
            Assert.That(resp.Results[0].LastAnswerStart, Is.Null, "the match is in the session's last answer");
            Assert.That(resp.TotalMatches, Is.EqualTo(1));
        }

        [Test]
        public void Execute_MatchInAnswer_ReturnsAnswerStart()
        {
            SetupSessions(Log("s1", Msg("user", "how do I log?", 0), Msg("assistant", "Use NLog via InternalLogger.", 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Question, Is.EqualTo("how do I log?"));
            Assert.That(resp.Results[0].AnswerStart, Does.Contain("NLog"));
            Assert.That(resp.Results[0].Snippet, Is.Null, "the match is already visible in answer_start");
        }

        [Test]
        public void Execute_LongQuestion_IsTruncatedForPresentation()
        {
            // Simulates the consolidated user message (short question + tool-result dumps): the
            // question returned to the model must be truncated even though only its own text is searched.
            string longQuestion = new string('x', 1000) + " NLog";
            SetupSessions(Log("s1", Msg("user", longQuestion, 0), Msg("assistant", "done", 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            var question = resp.Results[0].Question;
            Assert.That(question.Length, Is.EqualTo(ChatHistoryScorer.MaxHeadingLength + 3));
            Assert.That(question.EndsWith("..."), Is.True);
            Assert.That(question.StartsWith(new string('x', ChatHistoryScorer.MaxHeadingLength)), Is.True);
            Assert.That(question.Length, Is.LessThan(longQuestion.Length));
        }

        [Test]
        public void Execute_ShortQuestion_IsNotTruncated()
        {
            SetupSessions(Log("s1", Msg("user", "How to configure NLog?", 0), Msg("assistant", "done", 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Question, Is.EqualTo("How to configure NLog?"));
        }

        [Test]
        public void Execute_QuestionExactlyAtLimit_IsNotTruncated()
        {
            string question = new string('x', 295) + " NLog"; // length == MaxHeadingLength (300)
            SetupSessions(Log("s1", Msg("user", question, 0), Msg("assistant", "done", 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Question, Is.EqualTo(question));
        }

        [Test]
        public void Execute_QuestionOneOverLimit_IsTruncated()
        {
            string question = new string('x', 296) + " NLog"; // length == MaxHeadingLength + 1
            SetupSessions(Log("s1", Msg("user", question, 0), Msg("assistant", "done", 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Question.Length, Is.EqualTo(ChatHistoryScorer.MaxHeadingLength + 3));
            Assert.That(resp.Results[0].Question.EndsWith("..."), Is.True);
        }

        [Test]
        public void Execute_LongAnswer_AnswerStartIsTruncated()
        {
            string answer = new string('x', 1000) + " NLog";
            SetupSessions(Log("s1", Msg("user", "log setup", 0), Msg("assistant", answer, 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].AnswerStart.Length, Is.EqualTo(ChatHistoryScorer.MaxAnswerStartLength + 3));
        }

        [Test]
        public void Execute_LongAnswer_MatchBeyondAnswerStart_ReturnsSnippet()
        {
            string answer = new string('x', 400) + " NLog";
            SetupSessions(Log("s1", Msg("user", "log setup", 0), Msg("assistant", answer, 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Snippet, Does.Contain("NLog"),
                "a match deeper in the answer is not visible in answer_start");
        }

        [Test]
        public void Execute_MatchCutByAnswerStart_ReturnsSnippet()
        {
            // "NLog" starts two characters before the cut, so answer_start shows only "NL".
            string answer = new string('x', ChatHistoryScorer.MaxAnswerStartLength - 3) + " NLog and more text";
            SetupSessions(Log("s1", Msg("user", "log setup", 0), Msg("assistant", answer, 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].AnswerStart, Does.Not.Contain("NLog"));
            Assert.That(resp.Results[0].Snippet, Does.Contain("NLog"), "a match cut by the truncation is still shown");
        }

        [Test]
        public void Execute_MatchWhollyInsideAnswerStart_HasNoSnippet()
        {
            string answer = "Готово: NLog подключён. " + new string('x', 1000);
            SetupSessions(Log("s1", Msg("user", "log setup", 0), Msg("assistant", answer, 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].AnswerStart, Does.Contain("NLog"));
            Assert.That(resp.Results[0].Snippet, Is.Null);
        }

        [Test]
        public void Execute_CodeInQuestionAndAnswer_IsNeverReturned()
        {
            string question = "почему падает?\n\n```csharp\nvar secretQuestionCode = 1;\n```\n\n"
                + MarkdownCodeBlockFormatter.FormatFileAsMarkdown("class SecretFileBody { }", "src/App.cs");
            string answer = "NLog не настроен, см. `App.cs`.\n\n````csharp\nvar secretAnswerCode = 2;\n````";
            SetupSessions(Log("s1", Msg("user", question, 0), Msg("assistant", answer, 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });
            string json = JsonConvert.SerializeObject(resp);

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(json, Does.Not.Contain("secretQuestionCode"));
            Assert.That(json, Does.Not.Contain("SecretFileBody"));
            Assert.That(json, Does.Not.Contain("secretAnswerCode"));
            Assert.That(resp.Results[0].Files, Does.Contain("src/App.cs"));
        }

        [Test]
        public void Execute_ExcludesCurrentSession()
        {
            var currentGuid = Guid.NewGuid();
            _persistenceMock.SetupGet(p => p.CurrentSessionId).Returns((Guid?)currentGuid);

            string currentId = currentGuid.ToString();
            string otherId = Guid.NewGuid().ToString();

            SetupSessions(
                Log(currentId, Msg("user", "NLog config for the current session", 0)),
                Log(otherId, Msg("user", "NLog config in an older session", 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].SessionId, Is.EqualTo(otherId));
        }

        // ---------------------------------------------------------------
        // What the result carries: files, last answer, timestamp.
        // ---------------------------------------------------------------

        [Test]
        public void Execute_InsertedFileContent_IsNotSearched()
        {
            string message = "implement the plan\n\n" + InsertFile("Docs\\plan.md", "NLog configuration details");
            SetupSessions(Log("s1", Msg("user", message, 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Outcome, Is.EqualTo(ChatHistorySearchOutcome.NoMatches),
                "the text of an inserted file must not make the turn findable");
        }

        [Test]
        public void Execute_Files_ContainTheInsertedFile()
        {
            string message = "implement the plan\n\n" + InsertFile("Docs\\plan.md", "# Plan");
            SetupSessions(Log("s1", Msg("user", message, 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "implement" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Question, Is.EqualTo("implement the plan"));
            Assert.That(resp.Results[0].Files, Is.EqualTo(new[] { "Docs\\plan.md" }));
        }

        [Test]
        public void Execute_Files_ContainTheFilesOfTheFirstQuestion()
        {
            SetupSessions(Log("s1",
                Msg("user", "implement the plan\n\n" + InsertFile("Docs\\plan.md", "# Plan"), 0),
                Msg("assistant", "Plan read.", 1),
                Msg("user", "now add NLog logging", 2),
                Msg("assistant", "Added NLog.", 3)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Question, Is.EqualTo("now add NLog logging"));
            Assert.That(resp.Results[0].Files, Is.EqualTo(new[] { "Docs\\plan.md" }),
                "the topic of the session comes from its first question");
        }

        [Test]
        public void Execute_Files_ContainFilesNamedInTheAnswer()
        {
            SetupSessions(Log("s1",
                Msg("user", "add NLog logging", 0),
                Msg("assistant", "Done: LMLocal\\Logging.cs and Docs\\logging.md.", 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "logging" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Files, Is.EqualTo(new[] { "LMLocal\\Logging.cs", "Docs\\logging.md" }));
        }

        [Test]
        public void Execute_Files_AreCapped()
        {
            string message = "implement the plan";
            for (int i = 0; i < ChatHistoryScorer.MaxFilesInResult + 4; i++)
                message += "\n\n" + InsertFile($"Docs\\plan{i}.md", "# Plan " + i);

            SetupSessions(Log("s1", Msg("user", message, 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "implement" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Files, Has.Count.EqualTo(ChatHistoryScorer.MaxFilesInResult));
        }

        [Test]
        public void Execute_LastAnswerStart_IsTheLastAnswerOfTheSession()
        {
            SetupSessions(Log("s1",
                Msg("user", "NLog config", 0),
                Msg("assistant", "first answer", 1),
                Msg("user", "продолжи", 2),
                Msg("assistant", "final answer", 3)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].AnswerStart, Is.EqualTo("first answer"));
            Assert.That(resp.Results[0].LastAnswerStart, Is.EqualTo("final answer"));
        }

        [Test]
        public void Execute_LastAnswerStart_OmittedWhenTheMatchIsTheLastAnswer()
        {
            SetupSessions(Log("s1", Msg("user", "NLog config", 0), Msg("assistant", "done", 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].LastAnswerStart, Is.Null);
        }

        [Test]
        public void Execute_Timestamp_IsTheTimeOfTheMatchedTurn()
        {
            SetupSessions(Log("s1",
                Msg("user", "NLog config", 0),
                Msg("assistant", "done", 1),
                Msg("user", "продолжи", 10)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].Timestamp, Is.EqualTo(T0.ToString("o")),
                "the reported time is the matched turn's, not the session's last message");
        }

        // ---------------------------------------------------------------
        // Outcomes
        // ---------------------------------------------------------------

        [Test]
        public void Execute_NoSessions_ReturnsNoFilesOutcome()
        {
            SetupSessions();

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.TotalMatches, Is.EqualTo(0));
            Assert.That(resp.Outcome, Is.EqualTo(ChatHistorySearchOutcome.NoFiles));
        }

        [Test]
        public void Execute_NoMatches_ReturnsNoMatchesOutcome()
        {
            SetupSessions(Log("s1", Msg("user", "completely unrelated topic", 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "Flibbertygibbet" } });

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.TotalMatches, Is.EqualTo(0));
            Assert.That(resp.Outcome, Is.EqualTo(ChatHistorySearchOutcome.NoMatches));
        }

        // ---------------------------------------------------------------
        // scope (reserved: kept internal for the future; not exposed in the tool schema)
        // ---------------------------------------------------------------

        [Test]
        public void Execute_ScopeAll_Works()
        {
            SetupSessions(Log("s1", Msg("user", "NLog config", 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" }, { "scope", "all" } });

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.Results, Has.Count.EqualTo(1));
        }

        [Test]
        public void Execute_ScopeCurrentSolution_IsAccepted()
        {
            SetupSessions(Log("s1", Msg("user", "NLog config", 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" }, { "scope", "current_solution" } });

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.Results, Has.Count.EqualTo(1));
        }

        [Test]
        public void Execute_InvalidScope_ReturnsError()
        {
            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" }, { "scope", "nonsense" } });

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("scope"));
        }

        // ---------------------------------------------------------------
        // Ranking / limits
        // ---------------------------------------------------------------

        [Test]
        public void Execute_RespectsMaxResults_And_ReportsTotalMatches()
        {
            SetupSessions(
                Log("s1", Msg("user", "NLog config one", 0)),
                Log("s2", Msg("user", "NLog config two", 1)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" }, { "max_results", 1 } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.TotalMatches, Is.EqualTo(2));
        }

        [Test]
        public void Execute_DefaultMaxResults_IsThree()
        {
            SetupSessions(BuildDistinctSessions(6));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(3));
            Assert.That(resp.TotalMatches, Is.EqualTo(6));
        }

        [Test]
        public void Execute_MaxResultsAboveMaximum_IsCappedAtFive()
        {
            SetupSessions(BuildDistinctSessions(8));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" }, { "max_results", 10 } });

            Assert.That(resp.Results, Has.Count.EqualTo(5));
            Assert.That(resp.TotalMatches, Is.EqualTo(8));
        }

        [Test]
        public void Execute_NewerMatch_RanksFirst_EvenWithFewerOccurrences()
        {
            SetupSessions(
                Log("older-with-repeats", Msg("user", "NLog NLog NLog config", 0)),
                Log("newer-single", Msg("user", "NLog config", 30)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(2));
            Assert.That(resp.Results[0].SessionId, Is.EqualTo("newer-single"),
                "a newer discussion supersedes an older one");
        }

        [Test]
        public void Execute_PartialResult_RanksBelowOlderExact()
        {
            SetupSessions(
                Log("older-exact", Msg("user", "NLog chat tracking notes", 0)),
                Log("newer-partial", Msg("user", "NLog chat tracking", 30)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog chat tracking notes" } });

            Assert.That(resp.Results, Has.Count.EqualTo(2));
            Assert.That(resp.Results[0].SessionId, Is.EqualTo("older-exact"));
            Assert.That(resp.Results[1].MatchedTerms, Is.Not.Null);
        }

        [Test]
        public void Execute_PartialResults_MoreMatchedWordsRankAboveNewerOnes()
        {
            SetupSessions(
                Log("older-three-words", Msg("user", "NLog chat tracking widget a", 0)),
                Log("newer-two-words", Msg("user", "NLog chat widget b", 30)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog chat tracking notes" } });

            Assert.That(resp.Results, Has.Count.EqualTo(2));
            Assert.That(resp.Results[0].SessionId, Is.EqualTo("older-three-words"),
                "weak results are ordered by matched words before recency");
            Assert.That(resp.Results[1].SessionId, Is.EqualTo("newer-two-words"));
        }

        [Test]
        public void Execute_PartialResults_EqualWordCount_NewerRanksFirst()
        {
            SetupSessions(
                Log("older", Msg("user", "NLog chat widget a", 0)),
                Log("newer", Msg("user", "NLog chat widget b", 30)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog chat tracking notes" } });

            Assert.That(resp.Results, Has.Count.EqualTo(2));
            Assert.That(resp.Results[0].SessionId, Is.EqualTo("newer"));
        }

        [Test]
        public void Execute_PartialResults_CarryMatchedTermsAndNote()
        {
            SetupSessions(Log("s1", Msg("user", "chat and tracking notes for the widget", 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "chat history tracking notes" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].MatchedTerms, Is.EquivalentTo(new[] { "chat", "tracking", "notes" }));
            Assert.That(resp.Note, Does.Contain("Not found in any session: 'history'"),
                "the note lists the words found nowhere and names the place as a session");
        }

        [Test]
        public void Execute_Note_OmittedWhenNoReturnedResultIsPartial()
        {
            SetupSessions(
                Log("exact-1", Msg("user", "NLog chat tracking notes one", 30)),
                Log("exact-2", Msg("user", "NLog chat tracking notes two", 20)),
                Log("exact-3", Msg("user", "NLog chat tracking notes three", 10)),
                Log("partial", Msg("user", "NLog chat widget", 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog chat tracking notes" } });

            Assert.That(resp.Results, Has.Count.EqualTo(3));
            Assert.That(resp.TotalMatches, Is.EqualTo(4));
            Assert.That(resp.Note, Is.Null, "no returned result carries matched_terms");
        }

        [Test]
        public void Execute_Note_IsRebuiltFromTheCacheForTheReturnedPage()
        {
            SetupSessions(
                Log("exact-1", Msg("user", "NLog chat tracking notes one", 30)),
                Log("exact-2", Msg("user", "NLog chat tracking notes two", 20)),
                Log("exact-3", Msg("user", "NLog chat tracking notes three", 10)),
                Log("partial", Msg("user", "NLog chat widget", 0)));

            var wide = Run(_tool, new Dictionary<string, object> { { "query", "NLog chat tracking notes" }, { "max_results", 5 } });
            var narrow = Run(_tool, new Dictionary<string, object> { { "query", "NLog chat tracking notes" } });

            Assert.That(wide.Results, Has.Count.EqualTo(4));
            Assert.That(wide.Note, Does.Contain("matched_terms"), "the wide page carries a partial result");
            Assert.That(narrow.Results, Has.Count.EqualTo(3));
            Assert.That(narrow.Note, Is.Null, "the narrow page has no partial result");
            _persistenceMock.Verify(p => p.ReadAllSessionsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void Execute_ExactResults_HaveNoMatchedTermsAndNoNote()
        {
            SetupSessions(Log("s1", Msg("user", "NLog config", 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].MatchedTerms, Is.Null);
            Assert.That(resp.Note, Is.Null);
        }

        [Test]
        public void Execute_PartialWordsBelowThreshold_ReturnsNoMatch()
        {
            SetupSessions(Log("s1", Msg("user", "chat only here", 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "chat history tracking notes" } });

            Assert.That(resp.Outcome, Is.EqualTo(ChatHistorySearchOutcome.NoMatches));
        }

        [Test]
        public void Execute_PartialResults_AreCapped()
        {
            var sessions = new List<ChatSessionLog>();
            for (int i = 0; i < KnowledgePartialMatcher.MaxResults + 5; i++)
                sessions.Add(Log($"p{i:D2}", Msg("user", $"chat and tracking notes about widget {i}", 0)));

            SetupSessions(sessions.ToArray());

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "chat history tracking notes" } });

            Assert.That(resp.TotalMatches, Is.EqualTo(KnowledgePartialMatcher.MaxResults),
                "weak matches beyond the fallback cap are dropped");
            Assert.That(resp.Results, Has.Count.EqualTo(3));
        }

        [Test]
        public void Execute_PartialResults_CapKeepsTheBestWordsFirst()
        {
            var sessions = new List<ChatSessionLog>();

            // Five three-word partial matches, the oldest of the set.
            for (int i = 0; i < 5; i++)
                sessions.Add(Log($"three-{i:D2}", Msg("user", $"NLog chat tracking widget {i}", 0)));

            // Twenty two-word partial matches, newer than every three-word one.
            for (int i = 0; i < 20; i++)
                sessions.Add(Log($"two-{i:D2}", Msg("user", $"NLog chat widget {i} b", 30 + i)));

            SetupSessions(sessions.ToArray());

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog chat tracking notes" }, { "max_results", 5 } });

            Assert.That(resp.TotalMatches, Is.EqualTo(KnowledgePartialMatcher.MaxResults));
            Assert.That(resp.Results, Has.Count.EqualTo(5));

            for (int i = 0; i < 5; i++)
            {
                Assert.That(resp.Results[i].SessionId, Does.StartWith("three-"),
                    "the five three-word matches survive the cap even though they are older");
            }
        }

        [Test]
        public void Execute_IdenticalTurns_AreCollapsed()
        {
            SetupSessions(
                Log("older", Msg("user", "NLog config", 0)),
                Log("newer", Msg("user", "NLog config", 30)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(1));
            Assert.That(resp.Results[0].SessionId, Is.EqualTo("newer"));
            Assert.That(resp.Results[0].SimilarSessions, Is.EqualTo(2));
            Assert.That(resp.TotalMatches, Is.EqualTo(1), "total_matches counts results after collapsing");
        }

        [Test]
        public void Execute_EqualTimestamp_OrdersBySessionId()
        {
            SetupSessions(
                Log("zzz", Msg("user", "NLog config of zzz", 0)),
                Log("aaa", Msg("user", "NLog config of aaa", 0)));

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Results, Has.Count.EqualTo(2));
            Assert.That(resp.Results[0].SessionId, Is.EqualTo("aaa"));
            Assert.That(resp.Results[1].SessionId, Is.EqualTo("zzz"));
        }

        [Test]
        public void Execute_ScansAtMostMaxSessionsToScan()
        {
            var sessions = new List<ChatSessionLog>();
            for (int i = 0; i < SearchChatHistory.MaxSessionsToScan + 5; i++)
                sessions.Add(Log($"s{i:D5}", Msg("user", $"NLog config number {i}", i)));

            SetupSessions(sessions.ToArray());

            var resp = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.TotalMatches, Is.EqualTo(SearchChatHistory.MaxSessionsToScan));
        }

        private static ChatSessionLog[] BuildDistinctSessions(int count)
        {
            var sessions = new ChatSessionLog[count];
            for (int i = 0; i < count; i++)
                sessions[i] = Log($"s{i:D2}", Msg("user", $"NLog config number {i}", i));

            return sessions;
        }

        // ---------------------------------------------------------------
        // Validation
        // ---------------------------------------------------------------

        [Test]
        public void Execute_NullParameters_ReturnsError()
        {
            var resp = Run(_tool, null);

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Parameters"));
        }

        [Test]
        public void Execute_EmptyQuery_ReturnsError()
        {
            var resp = Run(_tool, new Dictionary<string, object> { { "query", "   " } });

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("query"));
        }

        // ---------------------------------------------------------------
        // Read-only + caching
        // ---------------------------------------------------------------

        [Test]
        public void Execute_DoesNotUseMutatingLoadPath()
        {
            SetupSessions(Log("s1", Msg("user", "NLog config", 0)));

            Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });

            _persistenceMock.Verify(p => p.LoadSessionByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _persistenceMock.Verify(p => p.ReadAllSessionsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void Execute_SecondCall_UsesCache_DoesNotRescan()
        {
            SetupSessions(Log("s1", Msg("user", "NLog config", 0)));

            var parameters = new Dictionary<string, object> { { "query", "NLog" } };
            Run(_tool, parameters);
            Run(_tool, parameters);

            _persistenceMock.Verify(p => p.ReadAllSessionsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void Execute_SecondCallWithOtherMaxResults_UsesCache()
        {
            SetupSessions(BuildDistinctSessions(6));

            var first = Run(_tool, new Dictionary<string, object> { { "query", "NLog" } });
            var second = Run(_tool, new Dictionary<string, object> { { "query", "NLog" }, { "max_results", 5 } });

            Assert.That(first.Results, Has.Count.EqualTo(3));
            Assert.That(second.Results, Has.Count.EqualTo(5));
            _persistenceMock.Verify(p => p.ReadAllSessionsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------------------------------------------------------------
        // Messages
        // ---------------------------------------------------------------

        [Test]
        public void GetProcessingMessage_ContainsQuery()
        {
            var msg = _tool.GetProcessingMessage(new Dictionary<string, object> { { "query", "NLog" } });

            Assert.That(msg, Does.Contain("NLog"));
            Assert.That(msg, Does.Contain("chat history"));
        }

        [Test]
        public void GetCompletionMessage_ForSuccess_ReturnsCount()
        {
            var resp = new ChatHistorySearchResponse
            {
                Success = true,
                Outcome = ChatHistorySearchOutcome.Success,
                TotalMatches = 3,
                Results = new List<ChatHistoryMatchResult>()
            };

            Assert.That(_tool.GetCompletionMessage(resp), Does.Contain("Found 3"));
        }

        [Test]
        public void GetCompletionMessage_ForNoMatches_ReturnsNoOccurrences()
        {
            var resp = new ChatHistorySearchResponse
            {
                Success = true,
                Outcome = ChatHistorySearchOutcome.NoMatches,
                TotalMatches = 0,
                Results = new List<ChatHistoryMatchResult>()
            };

            Assert.That(_tool.GetCompletionMessage(resp), Does.Contain("No occurrences"));
        }

        [Test]
        public void GetCompletionMessage_ForNoFiles_ReturnsNoHistory()
        {
            var resp = new ChatHistorySearchResponse
            {
                Success = true,
                Outcome = ChatHistorySearchOutcome.NoFiles,
                TotalMatches = 0,
                Results = new List<ChatHistoryMatchResult>()
            };

            Assert.That(_tool.GetCompletionMessage(resp), Does.Contain("No chat history"));
        }

        [Test]
        public void GetCompletionMessage_ForNoFiles_LoggingDisabled_ReportsDisabled()
        {
            _settingsMock.SetupGet(s => s.Current).Returns(new AppSettings { EnableChatLogging = false });

            var resp = new ChatHistorySearchResponse
            {
                Success = true,
                Outcome = ChatHistorySearchOutcome.NoFiles,
                TotalMatches = 0,
                Results = new List<ChatHistoryMatchResult>()
            };

            Assert.That(_tool.GetCompletionMessage(resp), Does.Contain("disabled"));
        }
    }
}
