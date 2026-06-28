/**
 * Create a streaming pipeline that coordinates buffering, parsing and rendering of streamed chunks.
 **/
export function createStreamingPipeline(streamBuffer, renderer, parser, scheduler) {
    let isRunning = false;
    let isAborted = false;
    let isEnded = false;

    let onAbortCallback = null;
    let onEndCallback = null;
    let onErrorCallback = (err) => console.error('Pipeline Error:', err);


    const processChunk = async (visibleText) => {
        if (isAborted) return;
        try {
            let html = await parser.parse(visibleText);
            renderer.write(visibleText, html);
        } catch (err) {
            await onErrorCallback?.(err);
        }
    };

    const startScheduler = () => {
        if (isRunning) return;
        isRunning = true;
        scheduler.start(processChunk);
    };

    const stopScheduler = () => {
        scheduler.stop();
        isRunning = false;
    };

    const reset = () => {

        if (isRunning) {
            stopScheduler();
        }

        onAbortCallback = null;
        onEndCallback = null;
        onErrorCallback = (err) => console.error('Pipeline Error:', err);

        isRunning = false;
        isAborted = false;
        isEnded = false;


        if (streamBuffer) {
            streamBuffer.reset();
        }
        if (scheduler) {
            scheduler.reset();
        }
    };

    return {
        attach(container) {
            renderer.start(container);
        },
        write(text) {
            if (isAborted || isEnded) return false;

            streamBuffer.append(text);
            if (!isRunning) {
                startScheduler();
            } else {
                scheduler.notify();
            }
            return true;
        },
        abort() {
            if (!isRunning) return false;
            isAborted = true;
            stopScheduler();
            Promise.resolve().then(() => onAbortCallback?.());
            return true;
        },
        end() {
            if (!isRunning) return false;
            isEnded = true;
            let flushPromise = scheduler.flushChunked(0);
            stopScheduler();
            flushPromise.then(() => onEndCallback?.());
            return true;
        },
        onAbort(fn) { onAbortCallback = fn; },
        onEnd(fn) { onEndCallback = fn; },
        onError(fn) { onErrorCallback = fn; },
        reset
    };
}

/**
 * Create an immediate pipeline that coordinates parsing and rendering of loaded history.
 **/
export function createImmediatePipeline(renderer, parser) {
    let isRunning = false;

    let onAbortCallback = null;
    let onEndCallback = null;
    let onErrorCallback = (err) => console.error('Pipeline Error:', err);
    let pendingWork = Promise.resolve();

    const processChunk = async (visibleText) => {
        try {
            let html = await parser.parse(visibleText);
            renderer.write(visibleText, html);
        } catch (err) {
            await onErrorCallback?.(err);
        }
    };

    return {
        attach(container) {
            if (isRunning) return false;
            isRunning = true;
            renderer.start(container);
        },
        write(text) {
            if (!isRunning) return false;
            pendingWork = processChunk(text);
            return true;
        },
        abort() {
            if (!isRunning) return false;
            Promise.resolve().then(() => onAbortCallback?.());
            return true;
        },
        end() {
            if (!isRunning) return false;

            pendingWork.then(() => { renderer.stop(); onEndCallback?.(); });
            return true;
        },
        onAbort(fn) { onAbortCallback = fn; },
        onEnd(fn) { onEndCallback = fn; },
        onError(fn) { onErrorCallback = fn; },
        reset() {
        }
    };
}