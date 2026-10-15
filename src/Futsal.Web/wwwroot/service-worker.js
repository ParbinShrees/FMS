// FutsalPro PWA Service Worker
// Caches app shell only. Never caches availability, bookings, or payment data.

const CACHE_NAME = 'futsalpro-shell-v1';
const APP_SHELL = [
    '/',
    '/v',
    '/app.css',
    '/favicon.png',
    '/manifest.webmanifest'
];

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME).then(cache => {
            return cache.addAll(APP_SHELL);
        })
    );
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(keys => Promise.all(
            keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k))
        ))
    );
    self.clients.claim();
});

self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);

    // CRITICAL: NEVER cache availability, payments, signalr hubs or auth API data
    if (url.pathname.startsWith('/api/') ||
        url.pathname.startsWith('/hubs/') ||
        url.pathname.startsWith('/bff/') ||
        url.pathname.includes('/availability') ||
        url.pathname.includes('/payments')) {
        return;
    }

    if (event.request.method !== 'GET') {
        return;
    }

    event.respondWith(
        fetch(event.request).catch(() => caches.match(event.request))
    );
});
