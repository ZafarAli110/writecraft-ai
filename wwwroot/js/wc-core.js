// WriteCraftAI Core - Single-user optimizations
(function () {
    'use strict';

    // Pre-connect to API (single user benefit)
    const preconnect = document.createElement('link');
    preconnect.rel = 'preconnect';
    preconnect.href = window.location.origin;
    document.head.appendChild(preconnect);

    // Theme toggle functionality
    const initThemeToggle = () => {
        const themeToggle = document.getElementById('themeToggle');
        const themeIcon = document.getElementById('themeIcon');
        const html = document.documentElement;

        // Load saved theme or default to dark
        const savedTheme = localStorage.getItem('theme') || 'dark';
        html.setAttribute('data-theme', savedTheme);
        updateThemeIcon(savedTheme);

        function updateThemeIcon(theme) {
            if (themeIcon) {
                themeIcon.className = theme === 'dark' 
                    ? 'bi bi-sun' 
                    : 'bi bi-moon-stars';
            }
        }

        if (themeToggle) {
            themeToggle.addEventListener('click', () => {
                const currentTheme = html.getAttribute('data-theme');
                const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
                html.setAttribute('data-theme', newTheme);
                localStorage.setItem('theme', newTheme);
                updateThemeIcon(newTheme);

                // Update body background based on theme
                document.body.className = newTheme === 'dark' 
                    ? 'bg-slate-900 text-slate-100' 
                    : 'bg-light text-dark';
            });
        }
    };

    // Initialize theme toggle when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initThemeToggle);
    } else {
        initThemeToggle();
    }

    // Update backend status in navbar once loaded
    fetch('/api/setup/health')
        .then(r => r.json())
        .then(data => {
            const statusEl = document.getElementById('backendStatus');
            if (data.isReady) {
                statusEl.innerHTML = `<span class="text-success">● Ready (${data.backend})</span>`;
            }
        })
        .catch(() => {
            // Ignore - will be ready after setup
        });

    // PWA Service Worker registration with auto-update in development
    if ('serviceWorker' in navigator) {
        window.addEventListener('load', () => {
            navigator.serviceWorker.register('/sw.js')
                .then(registration => {
                    console.log('SW registered:', registration.scope);
                    
                    // Auto-update service worker in development
                    // Check for updates every 5 seconds during development
                    if (window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1') {
                        setInterval(() => {
                            registration.update();
                        }, 5000);
                        
                        // Force immediate activation of new service worker
                        registration.addEventListener('updatefound', () => {
                            const newWorker = registration.installing;
                            newWorker.addEventListener('statechange', () => {
                                if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {
                                    console.log('New service worker available, reloading...');
                                    newWorker.postMessage({ type: 'SKIP_WAITING' });
                                    window.location.reload();
                                }
                            });
                        });
                    }
                })
                .catch(err => {
                    console.log('SW registration failed:', err);
                });
        });
        
        // Reload page when new service worker takes control
        let refreshing = false;
        navigator.serviceWorker.addEventListener('controllerchange', () => {
            if (!refreshing) {
                refreshing = true;
                window.location.reload();
            }
        });
    }
})();