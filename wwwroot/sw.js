const CACHE_NAME = 'writecraft-v1';
const urlsToCache = [
    '/css/theme.css',
    '/js/wc-core.js',
    '/js/stream-handler.js',
    '/lib/bootstrap/dist/css/bootstrap.min.css'
];

// Offline page HTML
const OFFLINE_PAGE = `
<!DOCTYPE html>
<html lang="en" data-theme="dark">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Offline - WriteCraft.AI</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { 
            font-family: system-ui, -apple-system, sans-serif;
            background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
            color: #e2e8f0;
            display: flex;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            padding: 2rem;
        }
        .container {
            text-align: center;
            max-width: 500px;
        }
        .icon {
            font-size: 5rem;
            margin-bottom: 1.5rem;
            animation: pulse 2s ease-in-out infinite;
        }
        @keyframes pulse {
            0%, 100% { opacity: 0.6; transform: scale(1); }
            50% { opacity: 1; transform: scale(1.05); }
        }
        h1 {
            font-size: 2rem;
            margin-bottom: 1rem;
            color: #818cf8;
        }
        p {
            font-size: 1.1rem;
            margin-bottom: 1rem;
            color: #94a3b8;
            line-height: 1.6;
        }
        .status {
            display: inline-flex;
            align-items: center;
            gap: 0.5rem;
            padding: 0.75rem 1.5rem;
            background: rgba(239, 68, 68, 0.1);
            border: 1px solid rgba(239, 68, 68, 0.3);
            border-radius: 0.5rem;
            margin-top: 1rem;
            font-size: 0.9rem;
            color: #fca5a5;
        }
        .spinner {
            width: 16px;
            height: 16px;
            border: 2px solid rgba(239, 68, 68, 0.3);
            border-top-color: #ef4444;
            border-radius: 50%;
            animation: spin 1s linear infinite;
        }
        @keyframes spin {
            to { transform: rotate(360deg); }
        }
        .instructions {
            margin-top: 2rem;
            padding: 1.5rem;
            background: rgba(99, 102, 241, 0.1);
            border: 1px solid rgba(99, 102, 241, 0.3);
            border-radius: 0.75rem;
            text-align: left;
        }
        .instructions h2 {
            font-size: 1.2rem;
            margin-bottom: 1rem;
            color: #a5b4fc;
        }
        .instructions ol {
            margin-left: 1.5rem;
            color: #cbd5e1;
        }
        .instructions li {
            margin-bottom: 0.5rem;
        }
        code {
            background: rgba(0, 0, 0, 0.3);
            padding: 0.2rem 0.5rem;
            border-radius: 0.25rem;
            font-family: 'Courier New', monospace;
            color: #a5b4fc;
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="icon">⚡</div>
        <h1>Server Not Running</h1>
        <p>WriteCraft.AI requires the local server to be running.</p>
        <div class="status">
            <div class="spinner"></div>
            <span>Server Offline</span>
        </div>
        <div class="instructions">
            <h2>To get started:</h2>
            <ol>
                <li>Open your terminal/command prompt</li>
                <li>Navigate to the project directory</li>
                <li>Run: <code>dotnet run</code></li>
                <li>Refresh this page once the server is running</li>
            </ol>
        </div>
    </div>
</body>
</html>
`;

// Listen for SKIP_WAITING message
self.addEventListener('message', event => {
    if (event.data && event.data.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});

self.addEventListener('install', e => {
    console.log('Service Worker installing...');
    e.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => {
                console.log('Caching app shell');
                return cache.addAll(urlsToCache);
            })
            .catch(err => {
                console.log('Cache failed, but continuing...', err);
            })
    );
    self.skipWaiting();
});

self.addEventListener('activate', e => {
    console.log('Service Worker activating...');
    e.waitUntil(
        caches.keys().then(keyList => {
            return Promise.all(keyList.map(key => {
                if (key !== CACHE_NAME) {
                    console.log('Removing old cache', key);
                    return caches.delete(key);
                }
            }));
        })
    );
    return self.clients.claim();
});

self.addEventListener('fetch', e => {
    const url = new URL(e.request.url);
    
    // ALWAYS use network for localhost/same-origin requests (server must be running)
    // Don't try to cache offline - this is a local-first app that needs the server
    if (url.origin === location.origin) {
        // Network-only for API calls
        if (url.pathname.includes('/api/')) {
            e.respondWith(fetch(e.request));
            return;
        }
        
        // Network-first for everything else (static files)
        e.respondWith(
            fetch(e.request)
                .then(response => {
                    // Clone and cache the response
                    if (response.ok) {
                        const responseClone = response.clone();
                        caches.open(CACHE_NAME).then(cache => {
                            cache.put(e.request, responseClone);
                        });
                    }
                    return response;
                })
                .catch(() => {
                    // Only use cache as fallback for static resources
                    return caches.match(e.request).then(cached => {
                        if (cached) {
                            return cached;
                        }
                        // If not cached and it's an HTML page request, show offline page
                        if (e.request.headers.get('accept').includes('text/html')) {
                            return new Response(OFFLINE_PAGE, {
                                headers: { 'Content-Type': 'text/html' }
                            });
                        }
                        // For other resources, return error
                        return new Response('Server is not running. Please start the server.', {
                            status: 503,
                            statusText: 'Service Unavailable',
                            headers: { 'Content-Type': 'text/plain' }
                        });
                    });
                })
        );
    }
});
