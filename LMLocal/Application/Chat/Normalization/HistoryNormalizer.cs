using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;

namespace LMLocal.Application.Chat.Normalization
{
    /// <summary>
    /// Normalizes history for providers (e.g. LlamaCpp) that require strict user/assistant alternation.
    /// </summary>
    internal sealed class HistoryNormalizer
    {
        private List<ChatMessage> _cachedNormalized;
        private int _lastCheckedVersion = 0;

        /// <summary>
        /// True when the cache already covers a history of <paramref name="historyCount"/> messages.
        /// </summary>
        public bool IsUpToDate(int historyCount)
        {
            return _lastCheckedVersion == historyCount;
        }

        /// <summary>
        /// Extends the cache up to the end of <paramref name="snapshot"/>; messages before the last checked version
        /// are not revisited.
        /// </summary>
        public void EnsureNormalized(IReadOnlyList<ChatMessage> snapshot, ISettingsManager settingsManager)
        {
            if (_cachedNormalized == null)
            {
                _cachedNormalized = new List<ChatMessage>();
                _lastCheckedVersion = 0;
            }

            if (snapshot.Count > 0)
            {
                var startFrom = _lastCheckedVersion;
                var isInsidePrompt = false;
                var isInsideTools = false;
                var isInsideToolResults = false;
                for (int i = startFrom; i < snapshot.Count; i++)
                {
                    var msg = snapshot[i];
                    var isLastMessage = (i + 1 >= snapshot.Count);
                    if (isInsidePrompt)
                    {
                        if (isInsideToolResults)
                        {
                            if (msg.Role == "tool")
                            {
                                if (isLastMessage)
                                {
                                    _cachedNormalized.Add(msg);
                                    _cachedNormalized.Add(new ChatMessage("assistant", settingsManager.AssistantPlaceholder));
                                    isInsideToolResults = false;
                                    isInsideTools = false;
                                    isInsidePrompt = false;
                                    //wait for next user message
                                    continue;
                                }
                                this._cachedNormalized.Add(msg);
                                //wait for other tools
                                continue;
                            }

                            if (msg.Role == "assistant")
                            {
                                if (msg.ToolCalls != null)
                                {
                                    //assistant with tool calls, wait for next tool results
                                    this._cachedNormalized.Add(msg);
                                    continue;
                                }
                                //normal finishing
                                isInsideTools = false;
                                isInsidePrompt = false;
                                isInsideToolResults = false;
                                this._cachedNormalized.Add(msg);
                                //wait for next user message
                                continue;
                            }

                            //anything else except tools or assitant, close validation, wait for next user message
                            _cachedNormalized.Add(new ChatMessage("assistant", settingsManager.AssistantPlaceholder));
                            isInsideTools = false;
                            isInsidePrompt = false;
                            isInsideToolResults = false;
                            continue;
                        }

                        if (isInsideTools)
                        {
                            if (msg.Role == "tool")
                            {
                                if (isLastMessage)
                                {
                                    _cachedNormalized.Add(msg);
                                    _cachedNormalized.Add(new ChatMessage("assistant", settingsManager.AssistantPlaceholder));
                                    isInsideToolResults = false;
                                    isInsideTools = false;
                                    isInsidePrompt = false;
                                    //wait for next user message
                                    continue;
                                }

                                isInsideToolResults = true;
                                isInsideTools = false;
                                this._cachedNormalized.Add(msg);
                                continue;
                            }

                            _cachedNormalized.RemoveAt(_cachedNormalized.Count - 1);
                            _cachedNormalized.Add(new ChatMessage("assistant", settingsManager.AssistantPlaceholder));
                            isInsideTools = false;
                            isInsidePrompt = false;
                            isInsideToolResults = false;
                            //wait for next user message
                            continue;
                        }

                        if (msg.Role == "assistant")
                        {
                            if (msg.ToolCalls != null)
                            {
                                if (isLastMessage)
                                {
                                    _cachedNormalized.Add(new ChatMessage("assistant", settingsManager.AssistantPlaceholder));
                                    isInsidePrompt = false;
                                    //if no next block finishing
                                    continue;
                                }

                                isInsideTools = true;
                                this._cachedNormalized.Add(msg);
                                continue;
                            }
                            else
                            {
                                //finishing, wait for next user message
                                this._cachedNormalized.Add(msg);
                                isInsidePrompt = false;
                                continue;
                            }
                        }

                        if (msg.Role == "user")
                        {
                            //remove prev user message, as it is not followed by assistant
                            _cachedNormalized.RemoveAt(_cachedNormalized.Count - 1);
                            isInsidePrompt = false;
                            continue;
                        }
                    }

                    if (msg.Role == "user")
                    {
                        if (isLastMessage)
                        {
                            //will not appear in history
                            continue;
                        }
                        isInsidePrompt = true;
                        this._cachedNormalized.Add(msg);
                    }
                }
                _lastCheckedVersion = snapshot.Count;
            }
        }

        /// <summary>
        /// The normalized history followed by the messages appended after the last normalization, or a copy of the
        /// raw history while the cache is empty.
        /// </summary>
        public List<ChatMessage> Compose(IReadOnlyList<ChatMessage> history)
        {
            if (_cachedNormalized == null)
                return new List<ChatMessage>(history);

            var result = new List<ChatMessage>(_cachedNormalized);
            result.AddRange(history.Skip(_lastCheckedVersion));
            return result;
        }

        /// <summary>
        /// Drops the cache without touching the version marker.
        /// </summary>
        public void Invalidate()
        {
            _cachedNormalized = null;
        }
    }
}
