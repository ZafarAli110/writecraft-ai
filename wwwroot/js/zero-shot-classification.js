// Zero-Shot Classification using Transformers.js
// Uses DeBERTa model for high-quality zero-shot text classification
// This is a pure ES module - all UI handling is done by the View

import {
    env,
    pipeline
} from './transformers.min.js';

// Configuration
const MODEL_ID = 'MoritzLaurer/deberta-v3-xsmall-zeroshot-v1.1-all-33';

// State
let classifier = null;
let isModelReady = false;

// Track download progress
let downloadProgress = { loaded: 0, total: 0 };

/**
 * Initialize the environment (call once on page load)
 */
function init() {
    // Configure environment
    env.allowLocalModels = false;
    env.useBrowserCache = true;
    console.log('[Zero-Shot] Initialized');
}

/**
 * Check if the model is cached in browser storage
 * @returns {Promise<boolean>}
 */
async function isModelCached() {
    try {
        // Check Cache API
        const cache = await caches.open('transformers-cache');
        const keys = await cache.keys();
        const hasCacheModel = keys.some(req => req.url.includes('deberta') || req.url.includes('zeroshot'));

        // Check IndexedDB
        let hasIndexedDBModel = false;
        try {
            const databases = await indexedDB.databases();
            hasIndexedDBModel = databases.some(db => db.name && (db.name.includes('transformers') || db.name.includes('onnx')));
        } catch (e) {
            // indexedDB.databases() not supported in some browsers
        }

        return hasCacheModel || hasIndexedDBModel;
    } catch (e) {
        // Fallback: check if model loads instantly (already in memory)
        return isModelReady;
    }
}

/**
 * Load the AI model with progress callback
 * @param {Function} progressCallback - Called with progress updates: { status, progress, loaded, total }
 * @returns {Promise<boolean>} - True if model loaded successfully
 */
async function loadModel(progressCallback) {
    if (isModelReady && classifier) {
        console.log('[Zero-Shot] Model already loaded');
        if (progressCallback) {
            progressCallback({ status: 'ready', progress: 100, cached: true });
        }
        return true;
    }

    // Reset progress tracking
    downloadProgress = { loaded: 0, total: 0 };
    const startTime = Date.now();

    try {
        console.log('[Zero-Shot] Loading model...');

        const handleProgress = (progress) => {
            if (!progressCallback) return;

            if (progress.status === 'progress') {
                downloadProgress.loaded = progress.loaded || 0;
                downloadProgress.total = progress.total || downloadProgress.total;

                if (downloadProgress.total > 0) {
                    const percent = (downloadProgress.loaded / downloadProgress.total) * 100;
                    progressCallback({
                        status: 'progress',
                        progress: Math.min(percent, 99),
                        loaded: downloadProgress.loaded,
                        total: downloadProgress.total
                    });
                }
            }
        };

        // Load classifier pipeline
        classifier = await pipeline('zero-shot-classification', MODEL_ID, {
            quantized: true,
            progress_callback: handleProgress
        });

        isModelReady = true;
        const loadTime = Date.now() - startTime;
        const cached = loadTime < 1000; // Loaded in less than 1 second, likely from cache
        console.log(`[Zero-Shot] Model loaded successfully in ${loadTime}ms${cached ? ' (from cache)' : ''}`);

        if (progressCallback) {
            progressCallback({ status: 'ready', progress: 100, cached });
        }

        return true;

    } catch (error) {
        console.error('[Zero-Shot] Model loading failed:', error);
        isModelReady = false;
        throw error;
    }
}

/**
 * Classify text into categories
 * @param {string} text - Text to classify (can contain multiple lines)
 * @param {string[]} labels - Array of category labels
 * @returns {Promise<Array>} - Classification results
 */
async function classifyText(text, labels) {
    if (!isModelReady || !classifier) {
        throw new Error('Model is not loaded. Call loadModel() first.');
    }

    console.log('[Zero-Shot] Classifying text...');

    try {
        const lines = text.split('\n').map(line => line.trim()).filter(line => line);
        const results = [];

        for (const line of lines) {
            const output = await classifier(line, labels, {
                hypothesis_template: 'This text is about {}.',
                multi_label: true,
            });

            // Format results
            const formattedLabels = output.labels.map((label, index) => ({
                label: label,
                score: output.scores[index]
            }));

            results.push({
                text: line,
                labels: formattedLabels
            });
        }

        return results;

    } catch (error) {
        console.error('[Zero-Shot] Classification failed:', error);
        throw error;
    }
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
        classifier = null;
        isModelReady = false;

        console.log('[Zero-Shot] Cache cleared');
        return true;
    } catch (e) {
        console.error('[Zero-Shot] Failed to clear cache:', e);
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
        console.error('[Zero-Shot] Failed to get cache size:', e);
    }
    return { usage: 0, quota: 0 };
}

// Export all public functions
export {
    init,
    loadModel,
    classifyText,
    getModelStatus,
    isModelCached,
    clearCache,
    getCacheSize
};