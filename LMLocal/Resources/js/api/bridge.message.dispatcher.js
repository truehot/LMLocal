import bridgeClient from '@app/api/bridge.client.js';

/**
 * BridgeMessageDispatcher - Singleton that binds a single listener to the WebView2 `message` event
 * and routes validated messages to the supplied `AppManager` based on message type.
 */
class BridgeMessageDispatcher {
    constructor() {
        this._isListening = false;
        this._handler = null;

        this._onMessage = this._onMessage.bind(this);
    }

    start(handler) {

        if (!handler) {
            console.error('[BridgeMessageDispatcher] AppManager is required');
            return;
        }

        this._handler = handler;

        const webview = bridgeClient.getWebview();
        if (!webview) {
            console.error('[BridgeMessageDispatcher] WebView is unavailable');
            return;
        }

        if (this.isListening) return;

        webview.addEventListener('message', this._onMessage);
        this._isListening = true;
    }

    stop() {
        const webview = bridgeClient.getWebview();
        if (webview && this.isListening) {
            webview.removeEventListener('message', this._onMessage);
            this._isListening = false;
        }
        this._handler = null;
    }

    _onMessage(event) {
        if (!this._handler) {
            console.warn('[BridgeMessageDispatcher] No AppManager, ignoring message');
            return;
        }

        const data = event.data;
        switch (data.Type) {
            case 'ChatSessionStart':
                this._handler.handleChatSessionStart();
                break;
            case 'ChatSessionIterating':
                this._handler.handleChatSessionIterating();
                break;
            case 'ChatSessionComplete':
                this._handler.handleChatSessionComplete(data);
                break;
            case 'ChatSessionError':
                this._handler.handleChatSessionError(data.Payload);
                break;
            case 'ChatSessionCancelled':
                this._handler.handleChatSessionCancelled(data.Payload);
                break;

            case 'StreamContent':
                this._handler.handleStreamContent(data.Payload, data.Count, data.TokensPerSecond);
                break;
            case 'StreamThought':
                this._handler.handleStreamThought(data.Payload, data.Count, data.TokensPerSecond);
                break;
            case 'StreamEnd':
                this._handler.handleStreamEnd();
                break;

            case 'StreamToolCall':
                this._handler.handleStreamToolCall(data);
                break;
            case 'StreamToolEnd':
                this._handler.handleStreamToolEnd(data);
                break;

            case 'CompactionStart':
                this._handler.handleCompactionStart();
                break;
            case 'CompactionEnd':
                this._handler.handleCompactionEnd();
                break;

            case 'SnapshotFilesChanged':
                this._handler.handleSnapshotFilesChanged(data);
                break;

            default:
                console.warn('[BridgeMessageDispatcher] Unknown message type:', data.Type);
        }
    }
    get isListening() {
        return this._isListening;
    }
}

const bridgeMessageDispatcher = new BridgeMessageDispatcher();
export default bridgeMessageDispatcher;
