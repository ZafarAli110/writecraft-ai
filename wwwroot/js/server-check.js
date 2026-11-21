(function () {
    const BANNER_ID = 'serverOfflineBanner';
    const BACKEND_STATUS_ID = 'backendStatus';
    const CLOSE_BTN_ID = 'serverBannerCloseBtn';

    let isClosedByUser = false;
    let attempt = 0;
    let isConnected = false;

    function showBanner(msg) {
        if (isClosedByUser) return;
        const el = document.getElementById(BANNER_ID);
        const status = document.getElementById(BACKEND_STATUS_ID);
        if (!el) return;
        el.classList.remove('d-none');
        el.classList.add('show');
        if (status) {
            status.innerHTML = '<span class="text-danger">● Server Offline</span>';
        }
        isConnected = false;
    }

    function hideBanner() {
        const el = document.getElementById(BANNER_ID);
        const status = document.getElementById(BACKEND_STATUS_ID);
        if (!el) return;
        el.classList.add('d-none');
        el.classList.remove('show');
        if (status) {
            status.innerHTML = '<span class="success-color">● Server Running</span>';
        }
        isConnected = true;
    }

    async function checkServer() {
        attempt++;
        const base = window.location.origin;
        
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 2000);

        try {
            const url = `${base}/api/setup/health?t=${Date.now()}`;
            const res = await fetch(url, { 
                method: 'GET',
                cache: 'no-store',
                mode: 'same-origin',
                signal: controller.signal,
                headers: { 'Cache-Control': 'no-cache' }
            });
            
            clearTimeout(timeoutId);
            
            if (res && res.status === 200) {
                const data = await res.json();
                if (data.isReady) {
                    hideBanner();
                    attempt = 0;
                    return;
                }
            }
            showBanner('Server not ready');
        } catch (e) {
            clearTimeout(timeoutId);
            console.debug(`Server check failed: ${e.message}`);
            showBanner('Server stopped');
        }

        const delay = Math.min(1000 * Math.pow(2, Math.min(attempt, 6)), 60000);
        setTimeout(checkServer, delay + Math.random() * 3000);
    }

    function init() {
        const status = document.getElementById(BACKEND_STATUS_ID);
        if (status) {
            status.innerHTML = '<span class="text-muted">● Checking...</span>';
        }

        const closeBtn = document.getElementById(CLOSE_BTN_ID);
        if (closeBtn) {
            closeBtn.addEventListener('click', () => {
                isClosedByUser = true;
                const banner = document.getElementById(BANNER_ID);
                if (banner) {
                    banner.classList.add('d-none');
                }
            });
        }

        setTimeout(checkServer, 500);

        document.addEventListener('visibilitychange', () => {
            if (!document.hidden && !isConnected) {
                attempt = 0;
                checkServer();
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();