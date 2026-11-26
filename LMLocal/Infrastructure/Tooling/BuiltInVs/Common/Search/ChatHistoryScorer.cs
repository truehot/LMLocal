using System;
using System.Collections.Generic;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// A single conversational turn: the user's own text (with the files inserted into it) and the final assistant answer of that turn.
    /// </summary>
    internal sealed class ChatHistoryTurn
    {
        /// <summary>User's own text of the turn: inserted files and the consolidated tool-result dump are already cut off.</summary>
        public string Question { get; set; }

        /// <summary>Paths of the files inserted into the question or listed as changed in the turn's step.</summary>
        public List<string> QuestionFiles { get; set; } = new List<string>();

        /// <summary>Final assistant message of the turn without fenced code blocks; may be null when the turn has no answer yet.</summary>
        public string Answer { get; set; }

        /// <summary>Paths of the files the answer names outside code blocks.</summary>
        public List<string> AnswerFiles { get; set; } = new List<string>();

        /// <summary>UTC timestamp of the turn's user message.</summary>
        public DateTime TimestampUtc { get; set; }
    }

    /// <summary>
    /// A chat session model.
    /// </summary>
    internal sealed class ChatHistorySession
    {
        public string SessionId { get; set; }

        /// <summary>UTC timestamp of the most recent message in the session (scan cap ordering only; matches rank by turn time).</summary>
        public DateTime TimestampUtc { get; set; }

        public List<ChatHistoryTurn> Turns { get; set; } = new List<ChatHistoryTurn>();
    }

    /// <summary>
    /// A prepared query: its tokens, the significant words used by the partial fallback, and how many of them a turn must contain.
    /// </summary>
    internal sealed class ChatHistoryQuery
    {
        /// <summary>Query text as typed; the exact-phrase tiers search for it verbatim.</summary>
        public string Text { get; }

        /// <summary>Unique word tokens of the query.</summary>
        public string[] Tokens { get; }

        /// <summary>Words the partial fallback counts; empty for a single-word query or fewer than two significant words.</summary>
        public string[] PartialTerms { get; }

        /// <summary>Words a turn needs for the partial fallback to accept it.</summary>
        public int RequiredPartialTerms { get; }

        /// <summary>True when the query has more than one token, so phrase and all-token tiers apply.</summary>
        public bool MultiWord => Tokens.Length > 1;

        private ChatHistoryQuery(string text, string[] tokens, string[] partialTerms)
        {
            Text = text;
            Tokens = tokens;
            PartialTerms = partialTerms;
            RequiredPartialTerms = partialTerms.Length == 0 ? 0 : KnowledgePartialMatcher.GetRequiredCount(partialTerms.Length);
        }

        /// <summary>
        /// Tokenizes the query and prepares the partial-words fallback exactly as <c>search_solution_knowledge</c> does.
        /// </summary>
        public static ChatHistoryQuery Create(string query)
        {
            string[] tokens = KnowledgeScorer.TokenizeQuery(query);

            string[] partialTerms = tokens.Length > 1
                ? KnowledgePartialMatcher.GetSignificantTerms(query)
                : Array.Empty<string>();
            if (partialTerms.Length < 2)
                partialTerms = Array.Empty<string>();

            return new ChatHistoryQuery(query, tokens, partialTerms);
        }
    }

    /// <summary>
    /// Immutable result of scoring one session.
    /// </summary>
    internal readonly struct ChatHistoryScoreResult
    {
        public ChatHistoryMatchKind Kind { get; }

        /// <summary>Index of the matched turn within the session.</summary>
        public int TurnIndex { get; }

        /// <summary>Where the match sits: "user" (question) or "assistant" (answer).</summary>
        public string Role { get; }

        /// <summary>The matched turn's own question text.</summary>
        public string Heading { get; }

        /// <summary>
        /// Position of the match inside the question or the answer, or -1 when only the inserted file names matched
        /// (they carry no readable context of their own).
        /// </summary>
        public int MatchIndex { get; }

        /// <summary>Length of the matched text at <see cref="MatchIndex"/>: the phrase, or the first matched word.</summary>
        public int MatchLength { get; }

        /// <summary>Fragment around the match; empty when the match has no usable context.</summary>
        public string Snippet { get; }

        /// <summary>Query words found in the turn; set only for the partial tier.</summary>
        public IReadOnlyList<string> MatchedTerms { get; }

        public bool IsMatch => Kind != ChatHistoryMatchKind.None;

        public ChatHistoryScoreResult(
            ChatHistoryMatchKind kind,
            int turnIndex,
            string role,
            string heading,
            int matchIndex,
            int matchLength,
            string snippet,
            IReadOnlyList<string> matchedTerms)
        {
            Kind = kind;
            TurnIndex = turnIndex;
            Role = role;
            Heading = heading;
            MatchIndex = matchIndex;
            MatchLength = matchLength;
            Snippet = snippet;
            MatchedTerms = matchedTerms;
        }

        public static readonly ChatHistoryScoreResult NoMatch =
            new ChatHistoryScoreResult(ChatHistoryMatchKind.None, -1, null, null, -1, 0, null, null);
    }

    /// <summary>
    /// Stateless projector + tiered scorer for search_chat_history.
    /// </summary>
    internal static class ChatHistoryScorer
    {
        public const int SnippetRadius = 150;

        /// <summary>Maximum length of the matched user question shown in a search result; the full text is still searched.</summary>
        public const int MaxHeadingLength = 300;

        /// <summary>Maximum length of the final answer's opening shown in a search result — the conclusion is at its start.</summary>
        public const int MaxAnswerStartLength = 300;

        /// <summary>Maximum number of file paths listed in one search result.</summary>
        public const int MaxFilesInResult = 8;

        private const string UserRole = "user";
        private const string AssistantRole = "assistant";
        private const string ToolRole = "tool";

        /// <summary>
        /// Projects a session's chronological log entries into turns of searchable text.
        /// </summary>
        public static ChatHistorySession ProjectSession(string sessionId, IReadOnlyList<ChatLogEntry> entries)
        {
            var session = new ChatHistorySession
            {
                SessionId = sessionId,
                Turns = new List<ChatHistoryTurn>()
            };

            if (entries == null || entries.Count == 0)
                return session;

            ChatHistoryTurn current = null;
            DateTime last = DateTime.MinValue;

            foreach (var entry in entries)
            {
                if (entry?.Message == null)
                    continue;

                last = entry.TimestampUtc;

                var message = entry.Message;
                string role = message.Role ?? string.Empty;

                if (string.Equals(role, ToolRole, StringComparison.OrdinalIgnoreCase))
                    continue;

                string text = ContentTextExtractor.ExtractTextContent(message.Content);
                bool isUser = string.Equals(role, UserRole, StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(text))
                {
                    if (isUser)
                        current = null;

                    continue;
                }

                if (isUser)
                {
                    ChatHistoryTurnProjector.ProjectQuestion(text, out string question, out List<string> questionFiles);
                    if (question.Length == 0 && questionFiles.Count == 0)
                    {
                        current = null;
                        continue;
                    }

                    current = new ChatHistoryTurn
                    {
                        Question = question,
                        QuestionFiles = questionFiles,
                        TimestampUtc = entry.TimestampUtc
                    };
                    session.Turns.Add(current);
                }
                else if (string.Equals(role, AssistantRole, StringComparison.OrdinalIgnoreCase))
                {
                    if (HasToolCalls(message) || current == null)
                        continue;

                    ChatHistoryTurnProjector.ProjectAnswer(text, out string answer, out List<string> answerFiles);
                    current.Answer = answer;
                    current.AnswerFiles = answerFiles;
                }
            }

            session.TimestampUtc = last;
            return session;
        }

        /// <summary>
        /// Scores a session by selecting the best matching turn.
        /// </summary>
        public static ChatHistoryScoreResult Score(ChatHistorySession session, string query)
        {
            return Score(session, ChatHistoryQuery.Create(query), null);
        }

        /// <summary>
        /// Scores a session by selecting the best matching turn, marking the partial-fallback words found anywhere in it.
        /// </summary>
        /// <param name="session">Projected session.</param>
        /// <param name="query">Prepared query.</param>
        /// <param name="foundTerms">
        /// Per partial term: set to true when the term occurs in this session, so the caller can report the words found nowhere.
        /// May be null.
        /// </param>
        public static ChatHistoryScoreResult Score(ChatHistorySession session, ChatHistoryQuery query, bool[] foundTerms)
        {
            if (session?.Turns == null || session.Turns.Count == 0 || query == null || query.Tokens.Length == 0)
                return ChatHistoryScoreResult.NoMatch;

            // The newest strong turn wins whatever its tier: a later turn ("продолжи", a correction) supersedes an earlier
            // one, so an older phrase hit must not hide the newer turn that holds the outcome.
            int strongTurn = -1;
            ChatHistoryMatchKind strongKind = ChatHistoryMatchKind.None;
            int strongIndex = -1;
            int strongLength = 0;
            bool strongInQuestion = false;

            // Weak matches compete among themselves: the most significant words wins, ties go to the newer turn.
            int partialTurn = -1;
            int partialCount = 0;
            int partialIndex = -1;
            int partialLength = 0;
            bool partialInQuestion = false;
            List<string> partialTerms = null;

            for (int t = 0; t < session.Turns.Count; t++)
            {
                var turn = session.Turns[t];
                string answer = turn.Answer ?? string.Empty;
                string questionText = QuestionText(turn);

                ChatHistoryMatchKind turnKind = ChatHistoryMatchKind.None;
                int turnIndex = -1;
                int turnLength = 0;
                bool turnInQuestion = false;

                int index = FindPhrase(questionText, query.Text, query.MultiWord);
                if (index >= 0)
                {
                    turnKind = ChatHistoryMatchKind.QuestionExact;
                    turnIndex = index;
                    turnLength = query.Text.Length;
                    turnInQuestion = true;
                }

                if (answer.Length > 0)
                {
                    int answerIndex = FindPhrase(answer, query.Text, query.MultiWord);
                    if (answerIndex >= 0 && turnKind < ChatHistoryMatchKind.AnswerExact)
                    {
                        turnKind = ChatHistoryMatchKind.AnswerExact;
                        turnIndex = answerIndex;
                        turnLength = query.Text.Length;
                        turnInQuestion = false;
                    }
                }

                if (query.MultiWord && turnKind < ChatHistoryMatchKind.TurnAllTokens)
                {
                    bool allInTurn = true;
                    int firstIndex = -1;
                    int firstLength = 0;
                    bool firstInQuestion = false;

                    foreach (string token in query.Tokens)
                    {
                        bool inQuestion = KnowledgeScorer.TryIndexOfTokenWithWordBoundaries(questionText, token, out int inQ);

                        int inA = -1;
                        bool inAnswer = answer.Length > 0 && KnowledgeScorer.TryIndexOfTokenWithWordBoundaries(answer, token, out inA);

                        if (!inQuestion && !inAnswer)
                        {
                            allInTurn = false;
                            break;
                        }

                        if (firstIndex < 0)
                        {
                            firstIndex = inQuestion ? inQ : inA;
                            firstLength = token.Length;
                            firstInQuestion = inQuestion;
                        }
                    }

                    if (allInTurn)
                    {
                        turnKind = ChatHistoryMatchKind.TurnAllTokens;
                        turnIndex = firstIndex;
                        turnLength = firstLength;
                        turnInQuestion = firstInQuestion;
                    }
                }

                if (turnKind != ChatHistoryMatchKind.None)
                {
                    strongTurn = t;
                    strongKind = turnKind;
                    strongIndex = turnIndex;
                    strongLength = turnLength;
                    strongInQuestion = turnInQuestion;
                }

                if (query.PartialTerms.Length == 0)
                    continue;

                int matched = 0;
                int firstPartialIndex = -1;
                int firstPartialLength = 0;
                bool firstPartialInQuestion = false;
                var matchedTerms = new List<string>();

                for (int i = 0; i < query.PartialTerms.Length; i++)
                {
                    string stem = KnowledgePartialMatcher.Stem(query.PartialTerms[i]);
                    int inQ = KnowledgePartialMatcher.IndexOfWordStart(questionText, stem, 0, questionText.Length);
                    int inA = answer.Length > 0 ? KnowledgePartialMatcher.IndexOfWordStart(answer, stem, 0, answer.Length) : -1;

                    if (inQ < 0 && inA < 0)
                        continue;

                    if (foundTerms != null && i < foundTerms.Length)
                        foundTerms[i] = true;

                    matched++;
                    matchedTerms.Add(query.PartialTerms[i]);

                    if (firstPartialIndex < 0)
                    {
                        firstPartialIndex = inQ >= 0 ? inQ : inA;
                        firstPartialLength = stem.Length;
                        firstPartialInQuestion = inQ >= 0;
                    }
                }

                // Fewer words than the threshold is not a weak match but noise; on equal counts the newer turn wins.
                if (matched >= query.RequiredPartialTerms && (partialTurn < 0 || matched >= partialCount))
                {
                    partialTurn = t;
                    partialCount = matched;
                    partialIndex = firstPartialIndex;
                    partialLength = firstPartialLength;
                    partialInQuestion = firstPartialInQuestion;
                    partialTerms = matchedTerms;
                }
            }

            if (strongTurn >= 0)
            {
                return Build(
                    strongKind, session.Turns[strongTurn], strongTurn,
                    strongInQuestion, strongIndex, strongLength, null);
            }

            if (partialTurn >= 0)
            {
                return Build(
                    ChatHistoryMatchKind.TurnPartial, session.Turns[partialTurn], partialTurn,
                    partialInQuestion, partialIndex, partialLength, partialTerms);
            }

            return ChatHistoryScoreResult.NoMatch;
        }

        /// <summary>
        /// Text of a turn's question side: the user's own words followed by the file names inserted with them.
        /// </summary>
        private static string QuestionText(ChatHistoryTurn turn)
        {
            string question = turn.Question ?? string.Empty;
            if (turn.QuestionFiles == null || turn.QuestionFiles.Count == 0)
                return question;

            string files = string.Join("\n", turn.QuestionFiles);
            return question.Length == 0 ? files : question + "\n" + files;
        }

        private static ChatHistoryScoreResult Build(
            ChatHistoryMatchKind kind,
            ChatHistoryTurn turn,
            int turnIndex,
            bool inQuestion,
            int index,
            int length,
            IReadOnlyList<string> matchedTerms)
        {
            string question = turn.Question ?? string.Empty;
            string role = inQuestion ? UserRole : AssistantRole;

            // A match inside the inserted file names has no readable context of its own: no snippet.
            bool matchInFiles = inQuestion && index >= question.Length;
            if (matchInFiles || index < 0)
                return new ChatHistoryScoreResult(kind, turnIndex, role, question, -1, 0, string.Empty, matchedTerms);

            string content = inQuestion ? question : (turn.Answer ?? string.Empty);
            string snippet = KnowledgeScorer.BuildSnippet(content, index, SnippetRadius);

            return new ChatHistoryScoreResult(kind, turnIndex, role, question, index, length, snippet, matchedTerms);
        }

        /// <summary>
        /// Finds the first occurrence of the query in <paramref name="text"/>.
        /// Multi-word queries match as a plain substring; single-word queries require word boundaries.
        /// </summary>
        private static int FindPhrase(string text, string query, bool multiWord)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
                return -1;

            if (multiWord)
                return text.IndexOf(query, StringComparison.OrdinalIgnoreCase);

            return KnowledgeScorer.TryIndexOfTokenWithWordBoundaries(text, query, out int index) ? index : -1;
        }

        /// <summary>
        /// True when the message carries a non-empty tool-call collection.
        /// </summary>
        private static bool HasToolCalls(ChatMessage message)
        {
            return message.ToolCalls != null && message.ToolCalls.Count > 0;
        }
    }
}
