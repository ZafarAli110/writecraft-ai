// Text-to-Speech using Transformers.js
// Uses SpeechT5 model for high-quality speech synthesis
// This is a pure ES module - all UI handling is done by the View

import { env, Tensor, AutoTokenizer, SpeechT5ForTextToSpeech, SpeechT5HifiGan } from './transformers.min.js';
import { encodeWAV } from './Tts-utils.js';

// Configuration
const MODEL_ID = 'Xenova/speecht5_tts';
const VOCODER_ID = 'Xenova/speecht5_hifigan';
const SPEAKER_EMBEDDINGS_BASE_URL = 'https://huggingface.co/datasets/Xenova/cmu-arctic-xvectors-extracted/resolve/main/';

// State
let tokenizer = null;
let model = null;
let vocoder = null;
let isModelReady = false;

// Speaker embeddings cache
const speakerEmbeddingsCache = new Map();

// Track download progress
const downloadProgress = new Map();

/**
 * Initialize the environment (call once on page load)
 */
export function init() {
    env.allowLocalModels = false;
    env.useBrowserCache = true;
    console.log('[TTS] Initialized');
}

/**
 * Check if the model is cached
 * @returns {Promise<boolean>}
 */
export async function isModelCached() {
    try {
        const cache = await caches.open('transformers-cache');
        const keys = await cache.keys();
        return keys.some(req => 
            req.url.includes('speecht5') || req.url.includes('speecht5_tts')
        );
    } catch (e) {
        return isModelReady;
    }
}

/**
 * Load the AI model with progress callback
 * @param {Function} progressCallback - Called with progress updates
 * @returns {Promise<boolean>}
 */
export async function loadModel(progressCallback) {
    // Already loaded in memory
    if (isModelReady && tokenizer && model && vocoder) {
        console.log('[TTS] Model already loaded in memory');
        if (progressCallback) {
            progressCallback({ status: 'ready', progress: 100, cached: true });
        }
        return true;
    }

    downloadProgress.clear();
    
    // Check if cached - if so, loading will be fast with no progress events
    const isCached = await isModelCached();

    try {
        console.log('[TTS] Loading models...', isCached ? '(from cache)' : '(downloading)');
        
        const handleProgress = (progress) => {
            if (!progressCallback) return;
            
            const file = progress.file || 'unknown';
            
            if (progress.status === 'initiate') {
                downloadProgress.set(file, { loaded: 0, total: progress.total || 0 });
            } else if (progress.status === 'progress') {
                const existingTotal = downloadProgress.get(file)?.total || 0;
                const total = progress.total || existingTotal;
                downloadProgress.set(file, { 
                    loaded: progress.loaded || 0, 
                    total: total
                });
                
                // Only count files with known sizes > 0
                let totalLoaded = 0;
                let totalSize = 0;
                downloadProgress.forEach(({ loaded, total }) => {
                    if (total > 0) {
                        totalLoaded += loaded;
                        totalSize += total;
                    }
                });
                
                // Only report if we have meaningful data (> 1MB total to avoid tiny config files)
                if (totalSize > 1024 * 1024) {
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
                    downloadProgress.set(file, { loaded: entry.total, total: entry.total });
                }
            }
        };

        // Load tokenizer
        tokenizer = await AutoTokenizer.from_pretrained(MODEL_ID, {
            progress_callback: handleProgress
        });

        // Load model
        model = await SpeechT5ForTextToSpeech.from_pretrained(MODEL_ID, {
            dtype: 'fp32',
            progress_callback: handleProgress
        });

        // Load vocoder
        vocoder = await SpeechT5HifiGan.from_pretrained(VOCODER_ID, {
            dtype: 'fp32',
            progress_callback: handleProgress
        });

        isModelReady = true;
        console.log('[TTS] Model loaded successfully');
        
        if (progressCallback) {
            progressCallback({ status: 'ready', progress: 100, cached: isCached });
        }
        
        return true;

    } catch (error) {
        console.error('[TTS] Model loading failed:', error);
        isModelReady = false;
        throw error;
    }
}

/**
 * Get speaker embeddings
 */
async function getSpeakerEmbeddings(speakerId) {
    if (speakerEmbeddingsCache.has(speakerId)) {
        return speakerEmbeddingsCache.get(speakerId);
    }

    const speakerEmbeddingsUrl = `${SPEAKER_EMBEDDINGS_BASE_URL}${speakerId}.bin`;
    
    try {
        const response = await fetch(speakerEmbeddingsUrl);
        if (!response.ok) {
            throw new Error(`Failed to load speaker embeddings: ${speakerId}`);
        }
        
        const arrayBuffer = await response.arrayBuffer();
        const speakerEmbeddings = new Tensor(
            'float32',
            new Float32Array(arrayBuffer),
            [1, 512]
        );
        
        speakerEmbeddingsCache.set(speakerId, speakerEmbeddings);
        return speakerEmbeddings;
    } catch (error) {
        console.error('Error loading speaker embeddings:', error);
        throw error;
    }
}

/**
 * Generate speech from text
 * @param {string} text - Text to convert to speech
 * @param {string} speakerId - Speaker ID (e.g., 'cmu_us_slt_arctic-wav-arctic_a0001')
 * @param {Function} progressCallback - Progress callback
 * @returns {Promise<Blob>} - Audio blob
 */
export async function generateSpeech(text, speakerId, progressCallback) {
    if (!isModelReady || !tokenizer || !model || !vocoder) {
        throw new Error('Model is not loaded. Call loadModel() first.');
    }

    console.log('[TTS] Generating speech...');

    try {
        if (progressCallback) {
            progressCallback({ status: 'progress', progress: 0, stage: 'tokenizing' });
        }

        // Tokenize
        const { input_ids } = tokenizer(text);
        
        if (progressCallback) {
            progressCallback({ status: 'progress', progress: 30, stage: 'loading_speaker' });
        }

        // Load speaker embeddings
        const speakerEmbeddings = await getSpeakerEmbeddings(speakerId);
        
        if (progressCallback) {
            progressCallback({ status: 'progress', progress: 50, stage: 'generating' });
        }

        // Generate speech
        const { waveform } = await model.generate_speech(input_ids, speakerEmbeddings, {
            vocoder: vocoder
        });
        
        if (progressCallback) {
            progressCallback({ status: 'progress', progress: 90, stage: 'encoding' });
        }

        // Encode to WAV
        const wav = encodeWAV(waveform.data);
        const audioBlob = new Blob([wav], { type: 'audio/wav' });
        
        if (progressCallback) {
            progressCallback({ status: 'progress', progress: 100, stage: 'complete' });
        }

        return audioBlob;

    } catch (error) {
        console.error('[TTS] Generation failed:', error);
        throw error;
    }
}

/**
 * Check if the model is ready
 * @returns {boolean}
 */
export function getModelStatus() {
    return isModelReady;
}

/**
 * Clear the cached model from IndexedDB
 * @returns {Promise<boolean>}
 */
export async function clearCache() {
    try {
        // Clear the transformers.js cache
        await caches.delete('transformers-cache');
        
        // Clear IndexedDB databases
        const databases = await indexedDB.databases();
        for (const db of databases) {
            if (db.name && (db.name.includes('transformers') || db.name.includes('onnx'))) {
                indexedDB.deleteDatabase(db.name);
            }
        }
        
        // Reset state
        tokenizer = null;
        model = null;
        vocoder = null;
        isModelReady = false;
        
        console.log('[TTS] Cache cleared');
        return true;
    } catch (e) {
        console.error('[TTS] Failed to clear cache:', e);
        return false;
    }
}

/**
 * Get cache storage estimate
 * @returns {Promise<{usage: number, quota: number}>}
 */
export async function getCacheSize() {
    try {
        if (navigator.storage && navigator.storage.estimate) {
            const estimate = await navigator.storage.estimate();
            return {
                usage: estimate.usage || 0,
                quota: estimate.quota || 0
            };
        }
    } catch (e) {
        console.error('[TTS] Failed to get cache size:', e);
    }
    return { usage: 0, quota: 0 };
}
