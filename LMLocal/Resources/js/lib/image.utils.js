"use strict";

/**
 * Opens a data URL in a new tab via a blob URL.
 */
export function openImageInNewTab(dataUrl) {
    try {
        const [header, b64] = dataUrl.split(',');
        const mime = (header.match(/data:(.*);/) || [])[1] || 'image/png';
        const bin = atob(b64);
        const buf = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
        const blob = new Blob([buf], { type: mime });
        const blobUrl = URL.createObjectURL(blob);
        const w = window.open(blobUrl, '_blank');

        if (w) {
            setTimeout(() => URL.revokeObjectURL(blobUrl), 60000);
        } else {
            URL.revokeObjectURL(blobUrl);
        }
    } catch (_) {
        window.open(dataUrl, '_blank');
    }
}

export function compressToWebP(dataUrl, options) {
    const { quality, maxDimension } = options || {};
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => {
            try {
                const dims = getScaledDimensions(img, maxDimension);
                const canvas = document.createElement('canvas');
                canvas.width = dims.width;
                canvas.height = dims.height;
                const ctx = canvas.getContext('2d');
                if (!ctx) { reject(new Error('canvas 2d context unavailable')); return; }
                ctx.drawImage(img, 0, 0, dims.width, dims.height);
                if (hasTransparency(ctx, dims.width, dims.height)) {
                    reject(new Error('image has transparency, skipping WebP'));
                    return;
                }
                canvas.toBlob((blob) => {
                    if (!blob) { reject(new Error('toBlob produced no WebP blob')); return; }
                    const blobReader = new FileReader();
                    blobReader.onload = () => resolve(/** @type {string} */(blobReader.result));
                    blobReader.onerror = () => reject(new Error('blob read failed'));
                    blobReader.readAsDataURL(blob);
                }, 'image/webp', quality);
            } catch (e) {
                reject(e);
            }
        };
        img.onerror = () => reject(new Error('image decode failed'));
        img.src = dataUrl;
    });
}

/**
 * Sparse alpha-channel check (every 4th row/col, ≈6% of pixels).
 */
export function hasTransparency(ctx, width, height) {
    try {
        const data = ctx.getImageData(0, 0, width, height).data;
        const step = 4;
        for (let y = 0; y < height; y += step) {
            const base = y * width * 4;
            for (let x = 0; x < width; x += step) {
                if (data[base + x * 4 + 3] < 255) return true;
            }
        }
        return false;
    } catch (e) {
        return true;
    }
}

/**
 * Downscales dimensions so the long side is <= maxDimension.
 */
export function getScaledDimensions(img, maxDimension) {
    const maxDim = maxDimension || 0;
    const w = img.naturalWidth || img.width;
    const h = img.naturalHeight || img.height;
    if (!maxDim || (w <= maxDim && h <= maxDim)) {
        return { width: w, height: h };
    }
    const scale = Math.min(1, maxDim / Math.max(w, h));
    return {
        width: Math.max(1, Math.round(w * scale)),
        height: Math.max(1, Math.round(h * scale))
    };
}
