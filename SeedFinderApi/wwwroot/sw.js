// PKM Universe Seed Finder — Service Worker
// Network-first for HTML + /api/species so updates are seen immediately. Cache-first for
// static icons + external CDN sprites/cries (which are immutable URLs).
const CACHE = 'pkmu-seeds-v13';
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
  // Bypass entirely — ALL live API data must always hit network (never serve a cached/stale raid).
  // (Previously a hand-maintained list missed /api/rotations, so the live "Now Hosting" went stale
  // on normal refresh while a hard refresh — which bypasses the SW — looked correct.)
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/r/') || url.pathname.startsWith('/list/')) return;
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
