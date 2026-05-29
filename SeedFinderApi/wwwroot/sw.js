// PKM Universe Seed Finder — Service Worker
// Caches the app shell + species list so the page opens instantly and works offline-ish.
const CACHE = 'pkmu-seeds-v2';
const SHELL = ['/', '/manifest.webmanifest', '/icon.svg', '/api/species'];

self.addEventListener('install', e => {
  self.skipWaiting();
  e.waitUntil(caches.open(CACHE).then(c => c.addAll(SHELL).catch(() => {})));
});
self.addEventListener('activate', e => {
  e.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim()));
});
self.addEventListener('fetch', e => {
  const url = new URL(e.request.url);
  // Never cache the SSE stream, auth, host calls, or any non-GET
  if (e.request.method !== 'GET') return;
  if (url.pathname.startsWith('/api/stream') || url.pathname.startsWith('/api/auth') || url.pathname.startsWith('/api/host')
      || url.pathname.startsWith('/api/nowhosting') || url.pathname.startsWith('/api/queue')
      || url.pathname.startsWith('/api/search') || url.pathname.startsWith('/api/lookup')
      || url.pathname.startsWith('/api/myraids') || url.pathname.startsWith('/api/wishlist')
      || url.pathname.startsWith('/api/ai')) return;
  // Cache-first for shell + sprites/cries (external CDNs)
  e.respondWith(
    caches.match(e.request).then(hit => hit || fetch(e.request).then(resp => {
      if (resp && resp.ok && (url.origin === self.location.origin || url.host.includes('githubusercontent.com'))) {
        const clone = resp.clone();
        caches.open(CACHE).then(c => c.put(e.request, clone)).catch(() => {});
      }
      return resp;
    }).catch(() => caches.match('/')))
  );
});
