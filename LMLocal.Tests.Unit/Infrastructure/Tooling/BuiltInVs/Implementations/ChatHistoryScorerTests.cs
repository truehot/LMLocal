using System;
using System.Collections.Generic;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class ChatHistoryScorerTests
    {
        private static readonly DateTime T0 = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static ChatLogEntry Msg(string role, string text, int minute = 0, IReadOnlyList<ChatToolCall> toolCalls = null)
        {
            return new ChatLogEntry
            {
                Message = new ChatMessage(role, text) { ToolCalls = toolCalls },
                TimestampUtc = T0.AddMinutes(minute)
            };
        }

        private static ChatHistorySession Session(params ChatHistoryTurn[] turns)
        {
            return new ChatHistorySession
            {
                SessionId = "session-1",
                Turns = new List<ChatHistoryTurn>(turns)
            };
        }

        private static ChatHistoryTurn Turn(string question, string answer = null, params string[] questionFiles)
        {
            var turn = new ChatHistoryTurn { Question = question, Answer = answer, TimestampUtc = T0 };

            if (questionFiles.Length > 0)
                turn.QuestionFiles = new List<string>(questionFiles);

            return turn;
        }

        private static string InsertFile(string path, string content)
        {
            return MarkdownCodeBlockFormatter.FormatFileAsMarkdown(content, path);
        }

        // ---------------------------------------------------------------
        // Projection: turn extraction (user question + final assistant answer).
        // ---------------------------------------------------------------

        [Test]
        public void ProjectSession_UserOpensTurn_AssistantIsAnswer()
        {
            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Q1", 0),
                Msg("assistant", "A1", 1)
            });

            Assert.That(session.Turns.Count, Is.EqualTo(1));
            Assert.That(session.Turns[0].Question, Is.EqualTo("Q1"));
            Assert.That(session.Turns[0].Answer, Is.EqualTo("A1"));
        }

        [Test]
        public void ProjectSession_AssistantWithToolCalls_IsSkipped()
        {
            var toolCalls = new List<ChatToolCall> { new ChatToolCall { Id = "call" } };

            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Q1", 0),
                Msg("assistant", "A1", 1),
                Msg("assistant", "intermediate narration", 2, toolCalls)
            });

            Assert.That(session.Turns[0].Answer, Is.EqualTo("A1"),
                "An assistant message carrying tool calls must not become the turn answer.");
        }

        [Test]
        public void ProjectSession_ToolRoleMessages_AreSkipped()
        {
            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Q1", 0),
                Msg("tool", "tool output", 1),
                Msg("assistant", "A1", 2)
            });

            Assert.That(session.Turns.Count, Is.EqualTo(1));
            Assert.That(session.Turns[0].Answer, Is.EqualTo("A1"));
        }

        [Test]
        public void ProjectSession_EmptyOrWhitespaceContent_IsSkipped()
        {
            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "", 0),
                Msg("assistant", "   ", 1),
                Msg("user", "Q1", 2)
            });

            Assert.That(session.Turns.Count, Is.EqualTo(1));
            Assert.That(session.Turns[0].Question, Is.EqualTo("Q1"));
        }

        [Test]
        public void ProjectSession_LastAssistantMessageWins()
        {
            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Q1", 0),
                Msg("assistant", "A1-first", 1),
                Msg("assistant", "A1-final", 2)
            });

            Assert.That(session.Turns[0].Answer, Is.EqualTo("A1-final"));
        }

        [Test]
        public void ProjectSession_MultipleTurns()
        {
            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Q1", 0),
                Msg("assistant", "A1", 1),
                Msg("user", "Q2", 2),
                Msg("assistant", "A2", 3)
            });

            Assert.That(session.Turns.Count, Is.EqualTo(2));
            Assert.That(session.Turns[0].Question, Is.EqualTo("Q1"));
            Assert.That(session.Turns[1].Question, Is.EqualTo("Q2"));
            Assert.That(session.Turns[1].Answer, Is.EqualTo("A2"));
        }

        [Test]
        public void ProjectSession_Timestamp_IsLastEntry()
        {
            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Q1", 0),
                Msg("assistant", "A1", 5)
            });

            Assert.That(session.TimestampUtc, Is.EqualTo(T0.AddMinutes(5)));
        }

        [Test]
        public void ProjectSession_NullOrEmpty_ReturnsEmptySession()
        {
            Assert.That(ChatHistoryScorer.ProjectSession("s", null).Turns, Is.Empty);
            Assert.That(ChatHistoryScorer.ProjectSession("s", new List<ChatLogEntry>()).Turns, Is.Empty);
        }

        // ---------------------------------------------------------------
        // Projection: what is searched and what is only listed as a file.
        // ---------------------------------------------------------------

        [Test]
        public void ProjectSession_InsertedFile_GoesToQuestionFiles()
        {
            string message = "Посмотри план\n\n" + InsertFile("Docs\\plan.md", "# План\n\nРеализовать поиск.");

            var session = ChatHistoryScorer.ProjectSession("s", new[] { Msg("user", message, 0) });

            Assert.That(session.Turns.Count, Is.EqualTo(1));
            Assert.That(session.Turns[0].Question, Is.EqualTo("Посмотри план"));
            Assert.That(session.Turns[0].QuestionFiles, Is.EqualTo(new[] { "Docs\\plan.md" }));
        }

        [Test]
        public void ProjectSession_OnlyInsertedFile_LeavesQuestionEmpty()
        {
            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", InsertFile("LMLocal\\Foo.cs", "public class Foo { }"), 0)
            });

            Assert.That(session.Turns.Count, Is.EqualTo(1));
            Assert.That(session.Turns[0].Question, Is.Empty);
            Assert.That(session.Turns[0].QuestionFiles, Is.EqualTo(new[] { "LMLocal\\Foo.cs" }));
        }

        [Test]
        public void ProjectSession_ConsolidatedToolResults_AreCutOffAndFilesKept()
        {
            string message = "Продолжи реализацию"
                + "\n\n---\n\n## Tool Results\n\n```\ndump of tool output\n```\n\n"
                + "Changed in this step (re-read before use): LMLocal\\Foo.cs, Docs\\plan.md (deleted)";

            var session = ChatHistoryScorer.ProjectSession("s", new[] { Msg("user", message, 0) });

            Assert.That(session.Turns[0].Question, Is.EqualTo("Продолжи реализацию"));
            Assert.That(session.Turns[0].Question, Does.Not.Contain("tool output"));
            Assert.That(session.Turns[0].QuestionFiles,
                Is.EqualTo(new[] { "LMLocal\\Foo.cs", "Docs\\plan.md" }));
        }

        [Test]
        public void ProjectSession_PlainCodeBlock_IsRemovedFromQuestion()
        {
            string message = "Почему так?\n\n```csharp\nvar value = 1;\n```";

            var session = ChatHistoryScorer.ProjectSession("s", new[] { Msg("user", message, 0) });

            Assert.That(session.Turns[0].Question, Is.EqualTo("Почему так?"));
            Assert.That(session.Turns[0].QuestionFiles, Is.Empty);
        }

        [Test]
        public void ProjectSession_DragDropAttachment_GoesToQuestionFiles()
        {
            // Fence as attachment.processor.js / file.fence.js builds it for a dropped file.
            string message = "посмотри\n\n````markdown\n# file: plan.md\n# Plan\nsecret body\n````\n";

            var session = ChatHistoryScorer.ProjectSession("s", new[] { Msg("user", message, 0) });

            Assert.That(session.Turns[0].Question, Is.EqualTo("посмотри"));
            Assert.That(session.Turns[0].QuestionFiles, Is.EqualTo(new[] { "plan.md" }));
        }

        [Test]
        public void ProjectSession_AnswerCodeBlock_IsRemoved_InlineCodeKept()
        {
            string answer = "Готово. Правка в `ChatHistoryScorer` внесена.\n\n````csharp\npublic void Search() { }\n````";

            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Q1", 0),
                Msg("assistant", answer, 1)
            });

            Assert.That(session.Turns[0].Answer, Does.Contain("`ChatHistoryScorer`"));
            Assert.That(session.Turns[0].Answer, Does.Not.Contain("public void Search()"));
        }

        [Test]
        public void ProjectSession_AnswerFiles_AreCollectedOnceInOrder()
        {
            string answer = "Правки: Docs\\plan.md, LMLocal\\Foo.cs. Повторно Docs\\plan.md.";

            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Q1", 0),
                Msg("assistant", answer, 1)
            });

            Assert.That(session.Turns[0].AnswerFiles,
                Is.EqualTo(new[] { "Docs\\plan.md", "LMLocal\\Foo.cs" }));
        }

        [Test]
        public void ProjectSession_FileInsertContent_IsNotSearchable()
        {
            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Посмотри план\n\n" + InsertFile("Docs\\plan.md", "NLog configuration details"), 0)
            });

            Assert.That(ChatHistoryScorer.Score(session, "NLog").IsMatch, Is.False,
                "text of an inserted file must not make the turn findable");
        }

        [Test]
        public void ProjectSession_ContentParts_TextIsExtracted()
        {
            var parts = new List<ContentPart>
            {
                new ContentPart { Type = "text", Text = "NLog setup" },
                new ContentPart { Type = "image_url", ImageUrl = new ImageUrlInfo { Url = "data:image/png;base64,AAAA" } }
            };

            var entry = new ChatLogEntry
            {
                Message = new ChatMessage("user", parts),
                TimestampUtc = T0
            };

            var session = ChatHistoryScorer.ProjectSession("s", new[] { entry });

            Assert.That(session.Turns.Count, Is.EqualTo(1));
            Assert.That(session.Turns[0].Question, Is.EqualTo("NLog setup"));
        }

        // ---------------------------------------------------------------
        // Projection: an empty user message still opens a turn.
        // ---------------------------------------------------------------

        [Test]
        public void ProjectSession_EmptyUserMessage_DoesNotReuseThePreviousTurn()
        {
            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Q1", 0),
                Msg("assistant", "A1", 1),
                Msg("user", "\n\n---\n\n## Tool Results\n\ntool dump", 2),
                Msg("assistant", "A2", 3)
            });

            Assert.That(session.Turns.Count, Is.EqualTo(1));
            Assert.That(session.Turns[0].Answer, Is.EqualTo("A1"),
                "the answer of an empty user message must not land in the previous turn");
        }

        [Test]
        public void ProjectSession_WhitespaceUserMessage_DoesNotReuseThePreviousTurn()
        {
            var session = ChatHistoryScorer.ProjectSession("s", new[]
            {
                Msg("user", "Q1", 0),
                Msg("assistant", "A1", 1),
                Msg("user", "   ", 2),
                Msg("assistant", "A2", 3)
            });

            Assert.That(session.Turns.Count, Is.EqualTo(1));
            Assert.That(session.Turns[0].Answer, Is.EqualTo("A1"));
        }

        // ---------------------------------------------------------------
        // Strict tier ordering.
        // ---------------------------------------------------------------

        [Test]
        public void Score_NewerAnswerExact_BeatsOlderQuestionExact()
        {
            var result = ChatHistoryScorer.Score(
                Session(
                    Turn("How to configure NLog?", "unrelated answer"),
                    Turn("how do I log?", "Use NLog for logging")),
                "NLog");

            Assert.That(result.TurnIndex, Is.EqualTo(1), "the newer strong turn wins");
            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.AnswerExact));
        }

        [Test]
        public void Score_NewerTurnAllTokens_BeatsOlderAnswerExact()
        {
            var result = ChatHistoryScorer.Score(
                Session(
                    Turn("how do I log?", "Use NLog chat here"),
                    Turn("about NLog", "and chat")),
                "NLog chat");

            Assert.That(result.TurnIndex, Is.EqualTo(1), "the newer strong turn wins");
            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.TurnAllTokens));
        }

        [Test]
        public void Score_PhraseInFirstQuestion_AndInThirdAnswer_ThirdTurnIsChosen()
        {
            var result = ChatHistoryScorer.Score(
                Session(
                    Turn("How to configure NLog?", "nothing relevant"),
                    Turn("unrelated middle question", "unrelated middle answer"),
                    Turn("another question", "NLog is set up in the startup path")),
                "NLog");

            Assert.That(result.TurnIndex, Is.EqualTo(2));
            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.AnswerExact));
        }

        [Test]
        public void Score_TurnAllTokens_RanksAboveTurnPartial()
        {
            var allTokens = ChatHistoryScorer.Score(
                Session(Turn("about chat", "and history")), "chat history");

            // Three of the four query words, so the all-token tier is not reached but the partial threshold is.
            var partial = ChatHistoryScorer.Score(
                Session(Turn("chat and tracking notes")), "chat history tracking notes");

            Assert.That(allTokens.Kind, Is.EqualTo(ChatHistoryMatchKind.TurnAllTokens));
            Assert.That(partial.Kind, Is.EqualTo(ChatHistoryMatchKind.TurnPartial));
            Assert.That(allTokens.Kind > partial.Kind, Is.True);
        }

        [Test]
        public void Score_PartialWords_RequireHalfOfTheQueryWords()
        {
            var halfway = ChatHistoryScorer.Score(
                Session(Turn("chat and tracking here")), "chat history tracking notes");

            var oneWordOnly = ChatHistoryScorer.Score(
                Session(Turn("chat only here")), "chat history tracking notes");

            Assert.That(halfway.Kind, Is.EqualTo(ChatHistoryMatchKind.TurnPartial));
            Assert.That(oneWordOnly.IsMatch, Is.False,
                "a single word of four is below the required count of two");
        }

        [Test]
        public void Score_PartialWords_StopWordsDoNotCount()
        {
            // "or" and "and" are stop words: the query has two significant words ("chat", "history").
            var result = ChatHistoryScorer.Score(
                Session(Turn("chat or tracking")), "chat or history");

            // Both significant words found ("chat", "history" is missing) → 1 of 2 is below the threshold.
            Assert.That(result.IsMatch, Is.False);
        }

        [Test]
        public void Score_PartialWords_ReportMatchedTerms()
        {
            var result = ChatHistoryScorer.Score(
                Session(Turn("chat and tracking notes")), "chat history tracking notes");

            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.TurnPartial));
            Assert.That(result.MatchedTerms, Is.EquivalentTo(new[] { "chat", "tracking", "notes" }));
        }

        [Test]
        public void Score_PartialWords_MarkWordsFoundAnywhereInTheSession()
        {
            var query = ChatHistoryQuery.Create("chat history tracking notes");
            var found = new bool[query.PartialTerms.Length];

            ChatHistoryScorer.Score(Session(Turn("chat and tracking notes")), query, found);

            Assert.That(found[0], Is.True, "'chat' was found");
            Assert.That(found[1], Is.False, "'history' was found nowhere");
            Assert.That(found[2], Is.True, "'tracking' was found");
            Assert.That(found[3], Is.True, "'notes' was found");
        }

        // ---------------------------------------------------------------
        // Turn selection / boundaries.
        // ---------------------------------------------------------------

        [Test]
        public void Score_MatchReportedForCorrectTurn()
        {
            var result = ChatHistoryScorer.Score(
                Session(
                    Turn("completely unrelated"),
                    Turn("How to configure NLog?")),
                "NLog");

            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.QuestionExact));
            Assert.That(result.TurnIndex, Is.EqualTo(1));
            Assert.That(result.Heading, Is.EqualTo("How to configure NLog?"));
        }

        [Test]
        public void Score_NewestMatchingTurnWins()
        {
            var result = ChatHistoryScorer.Score(
                Session(
                    Turn("NLog setup"),
                    Turn("NLog again")),
                "NLog");

            Assert.That(result.TurnIndex, Is.EqualTo(1),
                "the later matching turn supersedes the earlier one");
        }

        [Test]
        public void Score_TurnAllTokens_RequiresAllTokensInOneTurn()
        {
            var result = ChatHistoryScorer.Score(
                Session(
                    Turn("chat here"),
                    Turn("history there")),
                "chat history");

            Assert.That(result.Kind, Is.Not.EqualTo(ChatHistoryMatchKind.TurnAllTokens));
            Assert.That(result.IsMatch, Is.False,
                "words spread over different turns are not a match any more");
        }

        [Test]
        public void Score_HeadingContributesToTurnTokens()
        {
            // "chat" in the question + "history" in the answer → both tokens within one turn.
            var result = ChatHistoryScorer.Score(
                Session(Turn("chat topic", "history details")), "chat history");

            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.TurnAllTokens));
        }

        [Test]
        public void Score_QuestionFileNames_AreSearchable()
        {
            var result = ChatHistoryScorer.Score(
                Session(Turn("реализуй", null, "Docs\\nlog-plan.md")), "nlog-plan");

            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.QuestionExact));
            Assert.That(result.MatchIndex, Is.EqualTo(-1), "the match sits in the file names");
            Assert.That(result.Snippet, Is.Empty, "file names carry no readable context of their own");
        }

        // ---------------------------------------------------------------
        // Role + snippet.
        // ---------------------------------------------------------------

        [Test]
        public void Score_QuestionMatch_RoleIsUser()
        {
            var result = ChatHistoryScorer.Score(
                Session(Turn("How to configure NLog?")), "NLog");

            Assert.That(result.Role, Is.EqualTo("user"));
        }

        [Test]
        public void Score_AnswerMatch_RoleIsAssistant()
        {
            var result = ChatHistoryScorer.Score(
                Session(Turn("how do I log?", "Use NLog")), "NLog");

            Assert.That(result.Role, Is.EqualTo("assistant"));
        }

        [Test]
        public void Score_Snippet_IsPopulatedAroundMatch()
        {
            var result = ChatHistoryScorer.Score(
                Session(Turn("How to configure NLog logging for the service?")), "NLog");

            Assert.That(result.Snippet, Is.Not.Empty);
            Assert.That(result.Snippet, Does.Contain("NLog"));
        }

        [Test]
        public void Score_AnswerSnippet_ComesFromAnswer()
        {
            var result = ChatHistoryScorer.Score(
                Session(Turn("some question", "Configure logging via NLog in the startup path.")), "NLog");

            Assert.That(result.Snippet, Does.Contain("NLog"));
        }

        // ---------------------------------------------------------------
        // Word boundaries + edge cases.
        // ---------------------------------------------------------------

        [Test]
        public void Score_SingleWord_RespectsWordBoundaries()
        {
            // "api" inside "rapid" must not match.
            var result = ChatHistoryScorer.Score(
                Session(Turn("the system is rapid")), "api");

            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.None));
        }

        [Test]
        public void Score_SingleWord_MatchesAtWordBoundary()
        {
            var result = ChatHistoryScorer.Score(
                Session(Turn("the api is fast")), "api");

            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.QuestionExact));
        }

        [Test]
        public void Score_EmptyOrNullQuery_ReturnsNoMatch()
        {
            Assert.That(ChatHistoryScorer.Score(Session(Turn("Q1")), "").IsMatch, Is.False);
            Assert.That(ChatHistoryScorer.Score(Session(Turn("Q1")), null).IsMatch, Is.False);
            Assert.That(ChatHistoryScorer.Score(Session(Turn("Q1")), "   ").IsMatch, Is.False);
        }

        [Test]
        public void Score_NoMatch_ReturnsNoMatch()
        {
            var result = ChatHistoryScorer.Score(Session(Turn("nothing relevant")), "zzzz");

            Assert.That(result.IsMatch, Is.False);
            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.None));
        }

        [Test]
        public void Score_EmptySession_ReturnsNoMatch()
        {
            var session = new ChatHistorySession { SessionId = "s", Turns = new List<ChatHistoryTurn>() };

            Assert.That(ChatHistoryScorer.Score(session, "anything").IsMatch, Is.False);
        }

        // ---------------------------------------------------------------
        // Tokenizer reuse: phrases, dedup, boundaries, apostrophes.
        // ---------------------------------------------------------------

        [Test]
        public void Score_MultiWord_RequiresAdjacentPhraseForExact()
        {
            // Adjacent phrase → QuestionExact.
            var adjacent = ChatHistoryScorer.Score(Session(Turn("about chat history in general")), "chat history");
            Assert.That(adjacent.Kind, Is.EqualTo(ChatHistoryMatchKind.QuestionExact));

            // Same tokens, not adjacent → only TurnAllTokens (no exact phrase).
            var separated = ChatHistoryScorer.Score(Session(Turn("chat and then history")), "chat history");
            Assert.That(separated.Kind, Is.EqualTo(ChatHistoryMatchKind.TurnAllTokens));
        }

        [Test]
        public void Score_DuplicateTokens_AreDeduplicated()
        {
            // "chat chat history" tokenizes to {chat, history}, same as "chat history".
            var result = ChatHistoryScorer.Score(Session(Turn("about chat", "and history")), "chat chat history");

            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.TurnAllTokens));
        }

        [Test]
        public void Score_UnderscoreAndDash_AreWordBoundaries()
        {
            Assert.That(
                ChatHistoryScorer.Score(Session(Turn("see foo_bar here")), "foo").Kind,
                Is.EqualTo(ChatHistoryMatchKind.QuestionExact));

            Assert.That(
                ChatHistoryScorer.Score(Session(Turn("see foo-bar here")), "foo").Kind,
                Is.EqualTo(ChatHistoryMatchKind.QuestionExact));

            // No boundary → no match.
            Assert.That(
                ChatHistoryScorer.Score(Session(Turn("foobar")), "foo").Kind,
                Is.EqualTo(ChatHistoryMatchKind.None));
        }

        [Test]
        public void Score_ApostropheIsKeptInsideToken()
        {
            var result = ChatHistoryScorer.Score(Session(Turn("what's new")), "what's");

            Assert.That(result.Kind, Is.EqualTo(ChatHistoryMatchKind.QuestionExact));
        }
    }
}
