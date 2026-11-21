// Image Segmentation using Transformers.js (Segment Anything Model)
// Uses SlimSAM for efficient in-browser segmentation
// This is a pure ES module - all UI handling is done by the View

import { 
    env, 
    SamModel,
    AutoProcessor, 
    RawImage,
    Tensor
} from './transformers.min.js';

// Configuration
const MODEL_ID = 'Xenova/slimsam-77-uniform';

// State
let model = null;
let processor = null;
let isModelReady = false;

// Image state
let currentImage = null;
let imageProcessed = null;
let imageEmbeddings = null;
let isEncoding = false;
let isDecoding = false;
let decodePending = false;

// Track download progress
const downloadProgress = new Map();

/**
 * Check if WebGPU is supported
 */
function checkWebGPUSupport() {
    return !!navigator.gpu;
}

/**
 * Show WebGPU warning message
 */
function showWebGPUWarning() {
    // This will be called from the view to display the warning
    const event = new CustomEvent('webgpu-unsupported');
    window.dispatchEvent(event);
}

/**
 * Initialize the environment
 */
function init() {
    env.allowLocalModels = false;
    env.useBrowserCache = true;
    console.log('[Segmentation] Initialized');

    // Check WebGPU support and show message if not available
    if (!checkWebGPUSupport()) {
        showWebGPUWarning();
    }
}

/**
 * Check if model is cached
 */
async function isModelCached() {
    try {
        const cache = await caches.open('transformers-cache');
        const keys = await cache.keys();
        return keys.some(req => req.url.includes('slimsam') || req.url.includes('SlimSAM'));
    } catch (e) {
        return isModelReady;
    }
}

/**
 * Load the SAM model
 */
async function loadModel(progressCallback) {
    if (isModelReady && model && processor) {
        console.log('[Segmentation] Model already loaded');
        if (progressCallback) {
            progressCallback({ status: 'ready', progress: 100, cached: true });
        }
        return true;
    }

    downloadProgress.clear();

    try {
        console.log('[Segmentation] Loading model...');
        
        const handleProgress = (progress) => {
            if (!progressCallback) return;
            
            const file = progress.file || 'unknown';
            
            if (progress.status === 'initiate') {
                downloadProgress.set(file, { loaded: 0, total: progress.total || 0, done: false });
            } else if (progress.status === 'progress') {
                const total = progress.total || downloadProgress.get(file)?.total || 0;
                downloadProgress.set(file, { 
                    loaded: progress.loaded || 0, 
                    total: total,
                    done: false
                });
                
                // Only count files with known sizes
                let totalLoaded = 0;
                let totalSize = 0;
                downloadProgress.forEach(({ loaded, total, done }) => {
                    if (total > 0) {
                        totalLoaded += loaded;
                        totalSize += total;
                    }
                });
                
                // Only report if we have valid data
                if (totalSize > 0) {
                    const overallPercent = (totalLoaded / totalSize) * 100;
                    progressCallback({
                        status: 'progress',
                        progress: Math.min(overallPercent, 99),
                        file: file,
                        loaded: totalLoaded,
                        total: totalSize
                    });
                }
            } else if (progress.status === 'done') {
                const entry = downloadProgress.get(file);
                if (entry && entry.total > 0) {
                    downloadProgress.set(file, { loaded: entry.total, total: entry.total, done: true });
                }
            }
        };

        // Load SAM model with WebGPU if available
        model = await SamModel.from_pretrained(MODEL_ID, {
            dtype: 'fp16',
            device: navigator.gpu ? 'webgpu' : 'wasm',
            progress_callback: handleProgress
        });

        processor = await AutoProcessor.from_pretrained(MODEL_ID, {
            progress_callback: handleProgress
        });

        isModelReady = true;
        console.log('[Segmentation] Model loaded successfully');
        
        if (progressCallback) {
            progressCallback({ status: 'ready', progress: 100, cached: false });
        }
        
        return true;

    } catch (error) {
        console.error('[Segmentation] Model loading failed:', error);
        isModelReady = false;
        throw error;
    }
}

/**
 * Encode an image (extract embeddings)
 * @param {string} imageUrl - URL or data URL of image
 * @returns {Promise<{width: number, height: number}>}
 */
async function encodeImage(imageUrl) {
    if (!isModelReady) {
        throw new Error('Model not loaded');
    }
    
    if (isEncoding) {
        throw new Error('Already encoding an image');
    }

    isEncoding = true;
    
    try {
        console.log('[Segmentation] Encoding image...');
        
        currentImage = await RawImage.fromURL(imageUrl);
        imageProcessed = await processor(currentImage);
        imageEmbeddings = await model.get_image_embeddings(imageProcessed);
        
        console.log('[Segmentation] Image encoded:', currentImage.width, 'x', currentImage.height);
        
        return {
            width: currentImage.width,
            height: currentImage.height
        };
    } finally {
        isEncoding = false;
    }
}

/**
 * Segment based on points
 * @param {Array<{x: number, y: number, label: number}>} points - Normalized coords (0-1), label: 1=positive, 0=negative
 * @returns {Promise<{mask: ImageData, score: number}>}
 */
async function segmentWithPoints(points) {
    if (!imageEmbeddings) {
        throw new Error('No image encoded. Call encodeImage first.');
    }

    if (isDecoding) {
        decodePending = true;
        return null;
    }

    isDecoding = true;

    try {
        const reshaped = imageProcessed.reshaped_input_sizes[0];
        
        // Convert normalized points to image coordinates
        const pointCoords = points
            .map(p => [p.x * reshaped[1], p.y * reshaped[0]])
            .flat();
        const labels = points.map(p => BigInt(p.label));

        const numPoints = points.length;
        const input_points = new Tensor('float32', pointCoords, [1, 1, numPoints, 2]);
        const input_labels = new Tensor('int64', labels, [1, 1, numPoints]);

        // Generate mask
        const { pred_masks, iou_scores } = await model({
            ...imageEmbeddings,
            input_points,
            input_labels
        });

        // Post-process masks
        const masks = await processor.post_process_masks(
            pred_masks,
            imageProcessed.original_sizes,
            imageProcessed.reshaped_input_sizes
        );

        const maskTensor = masks[0][0];
        const scores = iou_scores.data;

        // Select best mask
        const numMasks = scores.length;
        let bestIndex = 0;
        for (let i = 1; i < numMasks; i++) {
            if (scores[i] > scores[bestIndex]) {
                bestIndex = i;
            }
        }

        // Convert to ImageData
        const mask = RawImage.fromTensor(maskTensor);
        const width = mask.width;
        const height = mask.height;
        
        const imageData = new ImageData(width, height);
        const pixelData = imageData.data;

        for (let i = 0; i < width * height; i++) {
            if (mask.data[numMasks * i + bestIndex] === 1) {
                const offset = 4 * i;
                pixelData[offset] = 0;       // R
                pixelData[offset + 1] = 114; // G
                pixelData[offset + 2] = 189; // B
                pixelData[offset + 3] = 180; // A (semi-transparent)
            }
        }

        return {
            mask: imageData,
            score: scores[bestIndex],
            width,
            height,
            bestIndex,
            numMasks,
            rawMask: mask,
            scores
        };

    } finally {
        isDecoding = false;
        
        if (decodePending) {
            decodePending = false;
        }
    }
}

/**
 * Cut out the masked region from the original image
 * @param {Object} segmentResult - Result from segmentWithPoints
 * @returns {Promise<Blob>}
 */
async function cutMask(segmentResult) {
    if (!currentImage || !segmentResult) {
        throw new Error('No image or mask available');
    }

    const { rawMask, bestIndex, numMasks } = segmentResult;
    const width = currentImage.width;
    const height = currentImage.height;

    // Create canvas for result
    const canvas = new OffscreenCanvas(width, height);
    const ctx = canvas.getContext('2d');
    
    const imageData = ctx.createImageData(width, height);
    const pixelData = imageData.data;
    const srcPixels = currentImage.data;

    for (let i = 0; i < width * height; i++) {
        if (rawMask.data[numMasks * i + bestIndex] === 1) {
            const srcOffset = 3 * i;  // RGB
            const dstOffset = 4 * i;  // RGBA
            
            pixelData[dstOffset] = srcPixels[srcOffset];
            pixelData[dstOffset + 1] = srcPixels[srcOffset + 1];
            pixelData[dstOffset + 2] = srcPixels[srcOffset + 2];
            pixelData[dstOffset + 3] = 255;
        }
    }

    ctx.putImageData(imageData, 0, 0);
    return await canvas.convertToBlob({ type: 'image/png' });
}

/**
 * Reset the current image state
 */
function resetImage() {
    currentImage = null;
    imageProcessed = null;
    imageEmbeddings = null;
    isEncoding = false;
    isDecoding = false;
    decodePending = false;
}

/**
 * Clear model cache
 */
async function clearCache() {
    try {
        await caches.delete('transformers-cache');
        
        const databases = await indexedDB.databases();
        for (const db of databases) {
            if (db.name && (db.name.includes('transformers') || db.name.includes('onnx'))) {
                indexedDB.deleteDatabase(db.name);
            }
        }
        
        model = null;
        processor = null;
        isModelReady = false;
        resetImage();
        
        console.log('[Segmentation] Cache cleared');
        return true;
    } catch (e) {
        console.error('[Segmentation] Failed to clear cache:', e);
        return false;
    }
}

/**
 * Get model status
 */
function getModelStatus() {
    return {
        ready: isModelReady,
        hasImage: !!imageEmbeddings,
        encoding: isEncoding,
        decoding: isDecoding
    };
}

export {
    init,
    loadModel,
    isModelCached,
    encodeImage,
    segmentWithPoints,
    cutMask,
    resetImage,
    clearCache,
    getModelStatus,
    checkWebGPUSupport
};
