// Bookmark Search - Client-side embeddings with all-MiniLM-L6-v2

export class BookmarkSearch {
    constructor() {
        this.embeddings = new Map(); // bookmarkId -> embedding
        this.bookmarks = []; // bookmark objects
        this.model = null;
        this.ready = false;
        this.modelLoaded = false;
        this.modelLoadFailed = false;
    }

    async init(progressCallback = null) {
        if (this.ready) return;

        // Check if model is cached to avoid brief loading message
        try {
            const cache = await caches.open('transformers-cache');
            const keys = await cache.keys();
            if (keys.length > 0) {
                this.updateModelStatus('loading', 'Loading cached model...');
            } else {
                this.updateModelStatus('loading', 'Loading embedding model...');
            }
        } catch (e) {
            this.updateModelStatus('loading', 'Loading embedding model...');
        }

        try {
            // Load embedding model
            const { pipeline } = await import('./transformers.min.js');
            this.model = await pipeline('feature-extraction', 'Xenova/all-MiniLM-L6-v2', {
                quantized: true,
                progress_callback: (p) => {
                    this.updateModelStatus('loading', `Loading model: ${p.status}`);
                    progressCallback?.(`Loading model: ${p.status}`);
                }
            });

            this.modelLoaded = true;
            this.updateModelStatus('loaded', 'Model loaded and cached');

            // Load bookmarks and generate embeddings
            await this.loadBookmarks(progressCallback);

            this.ready = true;
            progressCallback?.('Ready');
            console.log('[BookmarkSearch] Ready with all-MiniLM-L6-v2');
        } catch (error) {
            this.updateModelStatus('error', 'Failed to load model');
            this.modelLoadFailed = true;
            console.error('[BookmarkSearch] Init failed:', error);
        }
    }

    updateModelStatus(status, message) {
        const statusEl = document.getElementById('modelStatus');
        const textEl = document.getElementById('modelStatusText');
        const cacheEl = document.getElementById('cacheStatus');
        const clearBtn = document.getElementById('clearCacheBtn');
        const loadBtn = document.getElementById('loadModelBtn');

        if (textEl) textEl.textContent = message;

        if (statusEl) {
            statusEl.classList.remove('cached');
            if (status === 'loaded') {
                statusEl.classList.add('cached');
            }
        }

        if (cacheEl) {
            if (status === 'loaded') {
                cacheEl.innerHTML = '<i class="bi bi-check-circle text-success"></i> Cached';
            } else if (status === 'loading') {
                cacheEl.innerHTML = '<i class="bi bi-hourglass-split text-warning"></i> Loading...';
            } else if (status === 'not_loaded') {
                cacheEl.innerHTML = '<i class="bi bi-circle text-muted"></i> Not loaded';
            } else {
                cacheEl.innerHTML = '<i class="bi bi-x-circle text-danger"></i> Not cached';
            }
        }

        if (clearBtn) {
            clearBtn.disabled = status !== 'loaded';
        }

        if (loadBtn) {
            loadBtn.style.display = (status === 'cleared' || status === 'error' || status === 'not_loaded') ? 'inline-block' : 'none';
        }
    }

    async loadBookmarksList() {
        try {
            // Load from backend
            const response = await fetch('/api/bookmarks');
            if (!response.ok) {
                throw new Error(`Failed to load bookmarks: ${response.status} ${response.statusText}`);
            }
            const data = await response.json();
            this.bookmarks = data.bookmarks;

            // Try to load cached embeddings
            const cachedEmbeddings = localStorage.getItem('bookmarkEmbeddings');
            if (cachedEmbeddings) {
                try {
                    const parsed = JSON.parse(cachedEmbeddings);
                    // Check if we have embeddings for all current bookmarks
                    const hasAll = this.bookmarks.every(b => parsed[b.id]);
                    if (hasAll) {
                        this.embeddings = new Map(Object.entries(parsed).map(([k, v]) => [k, new Float32Array(v)]));
                        this.ready = true;
                        console.log(`[BookmarkSearch] Loaded ${this.embeddings.size} cached embeddings`);
                    }
                } catch (e) {
                    console.warn('[BookmarkSearch] Failed to load cached embeddings:', e);
                }
            }

            // Update bookmark count
            const countEl = document.getElementById('bookmarkCount');
            if (countEl) {
                const browserCounts = this.bookmarks.reduce((acc, b) => {
                    acc[b.browser] = (acc[b.browser] || 0) + 1;
                    return acc;
                }, {});
                const breakdown = Object.entries(browserCounts).map(([browser, count]) => `<span class="browser-${browser.toLowerCase().replace(' ', '-')}">${browser}: ${count}</span>`).join(', ');
                countEl.innerHTML = `Total bookmarks: ${this.bookmarks.length} (${breakdown})`;
            }

            console.log(`[BookmarkSearch] Loaded ${this.bookmarks.length} bookmarks`);
        } catch (error) {
            console.warn('[BookmarkSearch] Could not load bookmarks:', error);
        }
    }

    async loadModelOnly() {
        if (this.model) return;

        this.updateModelStatus('loading', 'Loading model...');
        try {
            // Load embedding model
            const { pipeline } = await import('./transformers.min.js');
            this.model = await pipeline('feature-extraction', 'Xenova/all-MiniLM-L6-v2', {
                quantized: true
            });

            this.modelLoaded = true;
            this.updateModelStatus('loaded', 'Model loaded and cached');
            console.log('[BookmarkSearch] Model loaded');
        } catch (error) {
            this.updateModelStatus('error', 'Failed to load model');
            this.modelLoadFailed = true;
            console.error('[BookmarkSearch] Model load failed:', error);
            throw error;
        }
    }

    async loadBookmarks(progressCallback = null) {
        // First load bookmarks if not loaded
        if (this.bookmarks.length === 0) {
            await this.loadBookmarksList();
        }

        // Load model if not loaded
        if (!this.model) {
            await this.loadModelOnly();
        }

        // Generate embeddings
        progressCallback?.('Generating embeddings...');
        const embeddings = [];
        for (let i = 0; i < this.bookmarks.length; i++) {
            const bookmark = this.bookmarks[i];
            const text = `${bookmark.title} ${bookmark.url} ${bookmark.folderPath}`;
            const embedding = await this.model(text, { pooling: 'mean', normalize: true });

            this.embeddings.set(bookmark.id, embedding.data);

            // Prepare for server storage
            const embeddingBytes = this.floatArrayToBytes(embedding.data);
            embeddings.push({
                BookmarkId: bookmark.id,
                Embedding: Array.from(embeddingBytes),
                LastModified: new Date().toISOString()
            });

            if (i % 10 === 0) {
                progressCallback?.(`Generating embeddings: ${Math.round((i + 1) / this.bookmarks.length * 100)}%`);
            }
        }

        // Note: Embeddings are generated client-side and stored in memory
        // No need to send to server as they are recreated on each load

        // Cache embeddings in localStorage
        try {
            const embeddingsObj = Object.fromEntries(this.embeddings);
            localStorage.setItem('bookmarkEmbeddings', JSON.stringify(embeddingsObj));
        } catch (e) {
            console.warn('[BookmarkSearch] Failed to cache embeddings:', e);
        }

        this.ready = true;
        console.log(`[BookmarkSearch] Generated and stored ${this.embeddings.size} embeddings`);
    }

    async search(query, limit = 10) {
        if (!query.trim()) return [];

        // Load model if not loaded
        if (!this.model && !this.modelLoadFailed) {
            await this.loadModelOnly();
        }
        if (!this.model) {
            throw new Error('Embedding model not loaded. Please try again later.');
        }

        // Load embeddings if not ready
        if (!this.ready) {
            await this.loadBookmarks();
        }

        // Generate query embedding
        const queryEmbedding = await this.model(query, { pooling: 'mean', normalize: true });

        // Calculate similarity
        const scores = [];
        for (const [id, embedding] of this.embeddings) {
            const similarity = this.cosineSimilarity(queryEmbedding.data, embedding);
            scores.push({ id, similarity });
        }

        // Get top results
        const top = scores.sort((a, b) => b.similarity - a.similarity).slice(0, limit);

        // Get bookmark details
        return top.map(({ id, similarity }) => {
            const bookmark = this.bookmarks.find(b => b.id === id);
            return { ...bookmark, similarity };
        });
    }

    cosineSimilarity(a, b) {
        let dot = 0, normA = 0, normB = 0;
        for (let i = 0; i < a.length; i++) {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (Math.sqrt(normA) * Math.sqrt(normB));
    }

    floatArrayToBytes(floats) {
        const bytes = new Uint8Array(floats.length * 4);
        for (let i = 0; i < floats.length; i++) {
            const view = new DataView(bytes.buffer, i * 4, 4);
            view.setFloat32(0, floats[i], true); // Little-endian
        }
        return bytes;
    }

    async sync(progressCallback = null) {
        const syncBtn = document.getElementById('syncBtn');
        const syncStatus = document.getElementById('syncStatus');

        if (syncBtn) {
            syncBtn.disabled = true;
            syncBtn.innerHTML = '<i class="bi bi-arrow-repeat spinning"></i> Syncing...';
        }
        if (syncStatus) syncStatus.textContent = 'Syncing bookmarks...';

        try {
            const response = await fetch('/api/bookmarks/sync', { method: 'POST' });
            const result = await response.json();

            if (syncStatus) syncStatus.textContent = 'Generating embeddings...';

            // Reload bookmarks and embeddings after sync
            this.embeddings.clear();
            await this.loadBookmarks(progressCallback);

            if (syncStatus) syncStatus.textContent = `Synced ${result.count} bookmarks`;
            return result;
        } catch (error) {
            if (syncStatus) syncStatus.textContent = 'Sync failed';
            throw error;
        } finally {
            if (syncBtn) {
                syncBtn.disabled = false;
                syncBtn.innerHTML = '<i class="bi bi-arrow-repeat"></i> Sync Bookmarks';
            }
        }
    }

    async clearCache() {
        try {
            // Clear model from memory
            this.model = null;
            this.modelLoaded = false;
            this.ready = false;
            this.modelLoadFailed = false;
            this.embeddings.clear();

            // Clear from IndexedDB (transformers.js cache)
            if ('caches' in window) {
                const cache = await caches.open('transformers-cache');
                const keys = await cache.keys();
                await Promise.all(keys.map(key => cache.delete(key)));
            }

            this.updateModelStatus('cleared', 'Cache cleared - model will reload on next use');

            // Disable search input until model is reloaded
            const searchInput = document.getElementById('searchInput');
            if (searchInput) {
                searchInput.disabled = true;
                searchInput.placeholder = "Model cache cleared - click Load Model to reload";
            }

            console.log('[BookmarkSearch] Cache cleared');
        } catch (error) {
            console.error('[BookmarkSearch] Failed to clear cache:', error);
        }
    }
}

// Export singleton
export const bookmarkSearch = new BookmarkSearch();