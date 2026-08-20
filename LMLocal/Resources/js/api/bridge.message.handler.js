import { Config } from '@app/constants/app.globals.js';
import { AppStatus } from '@app/store/app.status.js';
import appStore from '@app/store/app.store.js';
import modelStore from '@app/store/model.store.js'
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
                tokenUsed: count,
                tokenSpeed: speed
            }));
        }
    }

    handleStreamContent(chunk, count, speed) {
        const status = appStore.getState().status;

        if (status !== AppStatus.STREAMING && status === AppStatus.THINKING) {
            appStore.setState({ status: AppStatus.STREAMING });
        }

        this.contentBuffer.append(chunk);
        const now = Date.now();
        if (this.contentBuffer.shouldFlush(now)) {
            const flushed = this.contentBuffer.flush();
            appStore.setState(prevState => ({
                status: AppStatus.STREAMING,
                accumulatedText: prevState.accumulatedText + flushed,
                tokenUsed: count,
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
    }

    handleChatSessionComplete(metadata) {
        const totalTokens = metadata.TotalTokens || 0;
        const reasoningTokens = metadata.ReasoningTokens || 0;

        modelStore.setState({
            tokenUsed: totalTokens - reasoningTokens
        });

        appStore.setState({
            status: AppStatus.IDLE,
            tokenUsed: totalTokens - reasoningTokens,
            totalTokens: totalTokens,
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

    handleStreamToolEnd(toolCall) {
        appStore.setState({
            status: AppStatus.RESPONDING,
            toolCallId: toolCall.CallId,
            toolWithError: toolCall.IsError,
            toolMessage: toolCall.Message
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