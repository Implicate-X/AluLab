/* Minimal COI service worker dummy.
 * Purpose: prevent 404/500 cascades if an external layer tries to register `coi-serviceworker.js`.
 * This file does NOT implement COOP/COEP. It only proxies requests and avoids caching surprises.
 */

/** @type {ServiceWorkerGlobalScope} */
const sw = self;

sw.addEventListener('install', (event) => {
  // Activate immediately.
  sw.skipWaiting();
});

sw.addEventListener('activate', (event) => {
  // Take control without waiting for reload.
  event.waitUntil(sw.clients.claim());
});

sw.addEventListener('fetch', (event) => {
  // Passthrough. No caching.
  event.respondWith(fetch(event.request));
});