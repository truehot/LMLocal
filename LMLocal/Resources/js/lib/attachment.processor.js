"use strict";

import { compressToWebP } from '@app/lib/image.utils.js';
import { wrapAsCodeFence } from '@app/lib/file.fence.js';

/**
 * Stateless helpers that turn raw File objects (images / text) into data the
 * InputComponent attaches to the current input session.
 *
 * These functions never touch the DOM, toast, UI state, pending counters, or
 * the InputComponent itself — they read a file and return a plain result or
 * throw an Error. Cancellation is handled by the caller via session identity
 * (dropping results from stale sessions), not by AbortController.
 */

/**
 * Extracts the MIME type from a data URL, falling back to the file's type.
 */
function getMimeType(dataUrl, fallbackType) {
    const header = String(dataUrl).split(';')[0];
    const mime = header.split(':')[1];
    return mime || fallbackType || 'application/octet-stream';
}

/**
 * Validates a single image file. Defensive: callers already pre-filter by allowed MIME types before reaching this.
 */
export function validateImageFile(file, { maxSize, allowedTypes } = {}) {
    if (allowedTypes && allowedTypes.length > 0 && !allowedTypes.includes(file.type)) {
        const error = new Error(`Unsupported image format: ${file.type}`);
        error.code = 'unsupported';
        throw error;
    }
    if (maxSize && file.size > maxSize) {
        const error = new Error('Image exceeds size limit');
        error.code = 'too-large';
        throw error;
    }
    return file;
}

/**
 * Validates a single text file for the drag-and-drop code-fence flow.
 */
export function validateTextFile(file, { maxSize, allowedExtensions } = {}) {
    const ext = file.name.split('.').pop()?.toLowerCase();
    if (!ext || (allowedExtensions && !allowedExtensions.test('.' + ext))) {
        const error = new Error(`Unsupported file type: ${file.name}`);
        error.code = 'unsupported';
        throw error;
    }
    if (maxSize && file.size > maxSize) {
        const error = new Error(`File too large: ${file.name}`);
        error.code = 'too-large';
        throw error;
    }
    return file;
}

/**
 * Reads an image file as a data URL, optionally re-encoding to WebP.
 */
export function readImageFile(file, { compress = true, compressOptions = {} } = {}) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();

        reader.onload = async () => {
            try {
                const rawDataUrl = reader.result;
                let dataUrl = rawDataUrl;

                if (compress) {
                    try {
                        const compressed = await compressToWebP(rawDataUrl, compressOptions);
                        if (compressed) dataUrl = compressed;
                    } catch (err) {
                        console.warn('[ImagePaste] WebP skipped, using original:', file.name);
                    }
                }

                resolve({
                    name: file.name || 'pasted-image',
                    mimeType: getMimeType(dataUrl, file.type),
                    dataUrl,
                });
            } catch (err) {
                reject(err);
            }
        };

        reader.onerror = () => {
            reject(new Error(`Failed to read image: ${file.name}`));
        };

        reader.readAsDataURL(file);
    });
}

/**
 * Reads a text file and wraps it in a markdown code fence.
 */
export async function readTextFile(file) {
    const text = await file.text();
    return {
        fileName: file.name,
        markdown: wrapAsCodeFence(file.name, text),
    };
}
