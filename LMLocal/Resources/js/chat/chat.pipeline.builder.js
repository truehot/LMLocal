import { StreamingBuffer } from '@app/streaming/streaming.buffer.js';
import { createStreamingPipeline, createImmediatePipeline } from '@app/streaming/streaming.pipeline.js';
import { createStreamingRenderer, StreamingMode } from '@app/streaming/streaming.renderer.js';
import { createStreamingScheduler } from '@app/streaming/streaming.scheduler.js';

/**
 * Default configuration for the streaming scheduler.
 */
const DEFAULT_SCHEDULER_CONFIG = Object.freeze({
    baseIntervalMs: 60,
    minIntervalMs: 30,
    maxIntervalMs: 300,
    targetQueueLength: 2,
});

const DEFAULT_BUFFER_SIZE = 2;
const DEFAULT_STREAMING_MODE = StreamingMode.BLOCK_TAIL;

/**
 * Factory class for creating streaming and immediate pipelines.
 */
export class PipelineBuilder {
    constructor({ scrollManager, schedulerConfig, bufferSize, streamingMode }) {
        if (!scrollManager) {
            throw new Error('PipelineBuilder: scrollManager is required');
        }
        this._scrollManager = scrollManager;
        this._schedulerConfig = schedulerConfig ?? DEFAULT_SCHEDULER_CONFIG;
        this._bufferSize = bufferSize ?? DEFAULT_BUFFER_SIZE;
        this._streamingMode = streamingMode ?? DEFAULT_STREAMING_MODE;
    }

    /**
     * Creates a streaming pipeline with a buffer and scheduler.
     */
    createStreaming(markdownParser) {
        const renderer = this._createRenderer();
        const buffer = new StreamingBuffer(this._bufferSize);
        const scheduler = createStreamingScheduler(buffer, this._schedulerConfig);

        return createStreamingPipeline(buffer, renderer, markdownParser, scheduler);
    }

    /**
     * Creates an immediate pipeline without buffer/scheduler.
     */
    createImmediate(markdownParser) {
        const renderer = this._createRenderer();
        return createImmediatePipeline(renderer, markdownParser);
    }

    /**
     * Private helper: creates a renderer bound to the scrollManager.
     */
    _createRenderer() {
        return createStreamingRenderer({
            mode: this._streamingMode,
            onUpdate: () => this._scrollManager.scrollToBottom(),
        });
    }
}