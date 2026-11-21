const CACHE_NAME = 'writecraft-v1';
const urlsToCache = [
    '/css/theme.css',
    '/js/wc-core.js',
    '/js/stream-handler.js',
    '/lib/bootstrap/dist/css/bootstrap.min.css'
];

self.addEventListener('install', e => {
    e.waitUntil(caches.open(CACHE_NAME).then(c => c.addAll(urlsToCache)));
});

self.addEventListener('fetch', e => {
    // Network-first for API calls, cache-first for static
    if (e.request.url.includes('/api/')) {
        e.respondWith(fetch(e.request));
    } else {
        e.respondWith(
            caches.match(e.request).then(r => r || fetch(e.request))
        );
    }
});