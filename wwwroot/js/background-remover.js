// Background Remover using Transformers.js
// Uses RMBG-1.4 model for high-quality background removal
// This is a pure ES module - all UI handling is done by the View

import { 
    env, 
    AutoModel, 
    AutoProcessor, 
    RawImage 
} from './transformers.min.js';

// Configuration
const MODEL_ID = 'briaai/RMBG-1.4';

// State
let model = null;
let processor = null;
let isModelReady = false;

// Track download progress for multiple files
const downloadProgress = new Map();
let totalFiles = 0;
let completedFiles = 0;

/**
 * Initialize the environment (call once on page load)
 */
function init() {
    // Configure environment
    env.allowLocalModels = false;
    env.useBrowserCache = true;
    console.log('[BG Remover] Initialized');
}

/**
 * Check if the model is cached in browser storage
 * @returns {Promise<boolean>}
 */
async function isModelCached() {
    try {
        // Check if IndexedDB has the model cached
        const cache = await caches.open('transformers-cache');
        const keys = await cache.keys();
        const hasCachedModel = keys.some(req => req.url.includes('RMBG') || req.url.includes('rmbg'));
        return hasCachedModel;
    } catch (e) {
        // Fallback: check if model loads instantly (already in memory)
        return isModelReady;
    }
}

/**
 * Load the AI model with progress callback
 * @param {Function} progressCallback - Called with progress updates: { status, progress, file, loaded, total }
 * @returns {Promise<boolean>} - True if model loaded successfully
 */
async function loadModel(progressCallback) {
    if (isModelReady && model && processor) {
        console.log('[BG Remover] Model already loaded');
        if (progressCallback) {
            progressCallback({ status: 'ready', progress: 100, cached: true });
        }
        return true;
    }

    // Reset progress tracking
    downloadProgress.clear();
    totalFiles = 0;
    completedFiles = 0;

    try {
        console.log('[BG Remover] Loading model...');
        
        const handleProgress = (progress) => {
            if (!progressCallback) return;
            
            const file = progress.file || 'unknown';
            
            if (progress.status === 'initiate') {
                // New file starting
                totalFiles++;
                downloadProgress.set(file, { loaded: 0, total: progress.total || 0, done: false });
            } else if (progress.status === 'progress') {
                // Update file progress, use latest total if provided
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
                completedFiles++;
                const entry = downloadProgress.get(file);
                if (entry && entry.total > 0) {
                    downloadProgress.set(file, { loaded: entry.total, total: entry.total, done: true });
                }
            }
        };

        // Load model with progress tracking
        model = await AutoModel.from_pretrained(MODEL_ID, {
            progress_callback: handleProgress
        });

        // Load processor
        processor = await AutoProcessor.from_pretrained(MODEL_ID, {
            progress_callback: handleProgress
        });

        isModelReady = true;
        console.log('[BG Remover] Model loaded successfully');
        
        if (progressCallback) {
            progressCallback({ status: 'ready', progress: 100, cached: false });
        }
        
        return true;

    } catch (error) {
        console.error('[BG Remover] Model loading failed:', error);
        isModelReady = false;
        throw error;
    }
}

/**
 * Process an image and return the mask data
 * @param {string} imageUrl - URL or data URL of the image to process
 * @returns {Promise<{data: Float32Array, width: number, height: number}>} - Mask data
 */
async function processImage(imageUrl) {
    if (!isModelReady || !model || !processor) {
        throw new Error('Model is not loaded. Call loadModel() first.');
    }

    console.log('[BG Remover] Processing image...');

    try {
        // Load the image
        const image = await RawImage.fromURL(imageUrl);
        console.log(`[BG Remover] Image loaded: ${image.width}x${image.height}`);
        
        // Process with model
        const { pixel_values } = await processor(image);
        
        // Run inference
        const { output } = await model({ input: pixel_values });
        
        // Get mask data
        const maskData = output[0];
        
        // Return both the mask tensor and original image for the View to process
        return {
            mask: maskData,
            image: image,
            width: image.width,
            height: image.height
        };

    } catch (error) {
        console.error('[BG Remover] Processing failed:', error);
        throw error;
    }
}

/**
 * Apply mask to image and render to canvas
 * @param {Object} result - Result from processImage
 * @param {HTMLCanvasElement} canvas - Canvas to render to
 */
async function renderResult(result, canvas) {
    const { mask, image } = result;
    
    // Create mask image
    const maskData = mask.mul(255).to('uint8');
    const maskImage = await RawImage.fromTensor(maskData).resize(image.width, image.height);
    
    // Apply mask as alpha channel
    image.putAlpha(maskImage);
    
    // Set canvas size
    canvas.width = image.width;
    canvas.height = image.height;
    
    // Draw to canvas
    const ctx = canvas.getContext('2d');
    ctx.drawImage(image.toCanvas(), 0, 0);
    
    console.log('[BG Remover] Result rendered');
}

/**
 * Check if the model is ready
 * @returns {boolean}
 */
function getModelStatus() {
    return isModelReady;
}

/**
 * Clear the cached model from IndexedDB
 * @returns {Promise<boolean>}
 */
async function clearCache() {
    try {
        // Clear the transformers.js cache
        const cacheDeleted = await caches.delete('transformers-cache');
        
        // Also try to clear IndexedDB databases used by transformers.js
        const databases = await indexedDB.databases();
        for (const db of databases) {
            if (db.name && (db.name.includes('transformers') || db.name.includes('onnx'))) {
                indexedDB.deleteDatabase(db.name);
            }
        }
        
        // Reset state
        model = null;
        processor = null;
        isModelReady = false;
        
        console.log('[BG Remover] Cache cleared');
        return true;
    } catch (e) {
        console.error('[BG Remover] Failed to clear cache:', e);
        return false;
    }
}

/**
 * Get cache storage estimate
 * @returns {Promise<{usage: number, quota: number}>}
 */
async function getCacheSize() {
    try {
        if (navigator.storage && navigator.storage.estimate) {
            const estimate = await navigator.storage.estimate();
            return {
                usage: estimate.usage || 0,
                quota: estimate.quota || 0
            };
        }
    } catch (e) {
        console.error('[BG Remover] Failed to get cache size:', e);
    }
    return { usage: 0, quota: 0 };
}

// Export all public functions
export { 
    init, 
    loadModel, 
    processImage, 
    renderResult,
    getModelStatus,
    isModelCached,
    clearCache,
    getCacheSize
};
