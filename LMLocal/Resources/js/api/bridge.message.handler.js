import { Config } from '@app/constants/app.globals.js';
import { AppStatus } from '@app/store/app.status.js';
import appStore from '@app/store/app.store.js';
import sessionStore from '@app/store/session.store.js';
import changesStore from '@app/store/changes.store.js';
import { ChunkBuffer } from '@app/lib/chunk.buffer.js';
import { createCallback } from '@app/lib/callback.js';

class BridgeMessageHandler {
    constructor() {
        this.contentBuffer = new ChunkBuffer(Config.STREAM_BUFFER_INTERVAL_MS);
        this.thoughtBuffer = new ChunkBuffer(Config.STREAM_BUFFER_INTERVAL_MS);
        this.onToolRoundStart = createCallback();
        this.onFinalRound = createCallback();
    }

    handleCompactionStart() {
        appStore.setState({ status: AppStatus.COMPACTING });
    }

    handleCompactionEnd() {
        if (appStore.getState().status !== AppStatus.ERROR) {
            appStore.setState({ status: AppStatus.IDLE });
        }
    }

    handleStreamThought(chunk, count, speed) {
        const status = appStore.getState().status;

        if (status !== AppStatus.THINKING && status === AppStatus.PROCESSING) {
            appStore.setState({ status: AppStatus.THINKING });
        }

        this.thoughtBuffer.append(chunk);
        const now = Date.now();
        if (this.thoughtBuffer.shouldFlush(now)) {
            const flushed = this.thoughtBuffer.flush();
            appStore.setState(prevState => ({
                status: AppStatus.THINKING,
                accumulatedThoughtText: prevState.accumulatedThoughtText + flushed,
                roundTokens: count,
                tokenSpeed: speed
            }));
        }
    }

    handleStreamContent(chunk, count, speed) {
        const status = appStore.getState().status;

        if (status === AppStatus.PROCESSING || status === AppStatus.THINKING) {
            appStore.setState({ status: AppStatus.STREAMING });
        }

        this.contentBuffer.append(chunk);
        const now = Date.now();
        if (this.contentBuffer.shouldFlush(now)) {
            const flushed = this.contentBuffer.flush();
            appStore.setState(prevState => ({
                status: AppStatus.STREAMING,
                accumulatedText: prevState.accumulatedText + flushed,
                roundTokens: count,
                tokenSpeed: speed
            }));
        }
    }

    handleStreamEnd() {
        if (!this.thoughtBuffer.isEmpty()) {
            const flushedThought = this.thoughtBuffer.flush();
            appStore.setState(prevState => ({
                accumulatedThoughtText: prevState.accumulatedThoughtText + flushedThought
            }));
        }

        if (!this.contentBuffer.isEmpty()) {
            const flushed = this.contentBuffer.flush();
            appStore.setState(prevState => ({
                accumulatedText: prevState.accumulatedText + flushed
            }));
        }

        appStore.setState({
            status: AppStatus.FINISHING,
        });
    }

    handleChatSessionIterating(data) {
        const estimatedToolResultTokens = data.EstimatedToolResultTokens || 0;
        if (estimatedToolResultTokens > 0) {
            sessionStore.setState(prevState => ({
                toolResultTokens: (prevState?.toolResultTokens || 0) + estimatedToolResultTokens
            }));
        }

        if (!!data.IsFinalRound) {
            this.onFinalRound.emit();
            return;
        }
        this.onToolRoundStart.emit(data.RoundNumber || 0, data.ToolCount || 0);
    }

    handleChatSessionCancelled(errorMessage) {
        this.contentBuffer.reset();
        this.thoughtBuffer.reset();

        appStore.setState({
            status: AppStatus.ERROR,
            error: errorMessage,
            tokenSpeed: 0
        });
    }

    handleChatSessionStart() {
        this.contentBuffer.reset();
        this.thoughtBuffer.reset();
        sessionStore.setState({ toolResultTokens: 0 });
    }

    handleChatSessionComplete(metadata) {
        const totalTokens = metadata.TotalTokens || 0;
        const reasoningTokens = metadata.ReasoningTokens || 0;
        const contextTokens = totalTokens - reasoningTokens;

        const requestPromptTokens = metadata.RequestPromptTokens ?? metadata.PromptTokens ?? 0;
        const requestCompletionTokens = metadata.RequestCompletionTokens ?? metadata.CompletionTokens ?? 0;
        const requestCachedTokens = metadata.RequestCachedTokens ?? metadata.CachedTokens ?? 0;

        sessionStore.setState(prevState => ({
            contextTokens: contextTokens,
            toolResultTokens: 0,
            sessionPromptTokens: (prevState?.sessionPromptTokens || 0) + requestPromptTokens,
            sessionCompletionTokens: (prevState?.sessionCompletionTokens || 0) + requestCompletionTokens,
            sessionCachedTokens: (prevState?.sessionCachedTokens || 0) + requestCachedTokens
        }));

        appStore.setState({
            status: AppStatus.IDLE,
            lastAnswerTokens: contextTokens,
            cachedTokens: metadata.CachedTokens || 0,
            tokenSpeed: metadata.TokensPerSecond || 0
        });
    }

    handleChatSessionError(errorMessage) {
        this.contentBuffer.reset();
        this.thoughtBuffer.reset();

        appStore.setState({
            status: AppStatus.ERROR,
            error: errorMessage,
            tokenSpeed: 0
        });
    }

    handleStreamToolCall(toolCall) {
        appStore.setState({
            status: AppStatus.EXECUTING,
            toolCallId: toolCall.CallId,
            toolMessage: toolCall.Message
        });
    }

    handleStreamToolStep(toolCall) {
        appStore.setState({
            status: AppStatus.STEPPING,
            toolCallId: toolCall.CallId,
            toolStep: Number.isFinite(toolCall.Step) ? toolCall.Step : null,
            toolMessage: toolCall.Message
        });
    }

    handleStreamToolEnd(toolCall) {
        appStore.setState({
            status: AppStatus.RESPONDING,
            toolCallId: toolCall.CallId,
            toolWithError: toolCall.IsError,
            toolMessage: toolCall.Message
        });
    }

    handleStreamToolGroupStart(group) {
        this._setToolGroup(group, group.Completed);
    }

    handleStreamToolGroupProgress(group) {
        this._setToolGroup(group, group.Completed);
    }

    handleStreamToolGroupEnd(group) {
        this._setToolGroup(group, group.Total);
    }

    _setToolGroup(group, completed) {
        const total = group.Total || 0;
        appStore.setState({
            toolGroupId: group.GroupId || "",
            toolGroupTotal: total,
            toolGroupCompleted: typeof completed === 'number' ? completed : total
        });
    }

    handleSnapshotFilesChanged(message) {
        changesStore.setState({
            loading: false,
            loaded: true,
            error: null,
            changedFiles: message.ChangedFiles || [],
            visible: (message.ChangedFiles && message.ChangedFiles.length > 0)
        });
    }
}

const bridgeMessageHandler = new BridgeMessageHandler();
export { bridgeMessageHandler };