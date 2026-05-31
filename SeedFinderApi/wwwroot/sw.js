// PKM Universe Seed Finder — Service Worker
// Network-first for HTML + /api/species so updates are seen immediately. Cache-first for
// static icons + external CDN sprites/cries (which are immutable URLs).
const CACHE = 'pkmu-seeds-v9';
const SHELL = ['/manifest.webmanifest', '/icon.svg'];

self.addEventListener('install', e => {
  self.skipWaiting();
  e.waitUntil(caches.open(CACHE).then(c => c.addAll(SHELL).catch(() => {})));
});
self.addEventListener('activate', e => {
  e.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim()));
});
self.addEventListener('fetch', e => {
  const url = new URL(e.request.url);
  if (e.request.method !== 'GET') return;
  // Bypass entirely — these endpoints must always hit network
  if (url.pathname.startsWith('/api/stream') || url.pathname.startsWith('/api/auth') || url.pathname.startsWith('/api/host')
      || url.pathname.startsWith('/api/nowhosting') || url.pathname.startsWith('/api/queue')
      || url.pathname.startsWith('/api/search') || url.pathname.startsWith('/api/lookup')
      || url.pathname.startsWith('/api/myraids') || url.pathname.startsWith('/api/wishlist')
      || url.pathname.startsWith('/api/ai') || url.pathname.startsWith('/api/r/')
      || url.pathname.startsWith('/api/leaderboard') || url.pathname.startsWith('/r/')
      || url.pathname.startsWith('/api/species') || url.pathname.startsWith('/api/network')
      || url.pathname.startsWith('/api/achievements')) return;
  // Network-first for the HTML shell (so deploys are seen instantly), cache-first for everything else
  if (url.pathname === '/' || url.pathname.endsWith('.html')) {
    e.respondWith(fetch(e.request).then(resp => {
      if (resp && resp.ok && url.origin === self.location.origin) {
        const clone = resp.clone();
        caches.open(CACHE).then(c => c.put(e.request, clone)).catch(() => {});
      }
      return resp;
    }).catch(() => caches.match(e.request).then(hit => hit || caches.match('/'))));
    return;
  }
  // Cache-first for icons + external CDN sprites/cries (immutable)
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
