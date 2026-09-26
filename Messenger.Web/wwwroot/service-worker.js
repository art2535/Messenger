const CACHE_VERSION = 'guap-messenger-v0.12.2';
const STATIC_CACHE = `${CACHE_VERSION}-static`;
const RUNTIME_CACHE = `${CACHE_VERSION}-runtime`;
const CHATS_CACHE = `${CACHE_VERSION}-chats`;
const MESSAGES_CACHE = `${CACHE_VERSION}-messages`;

const MAX_CHATS_ENTRIES = 60;
const MAX_MESSAGES_ENTRIES = 40;
const MAX_RUNTIME_ENTRIES = 120;

let API_BASE_URL = null;

self.addEventListener('message', event => {
    if (!event.data) return;
    if (event.data.type === 'SET_CONFIG') {
        API_BASE_URL = event.data.apiBaseUrl || null;
        console.log('[SW] API_BASE_URL:', API_BASE_URL);
    }
    if (event.data.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
    if (event.data.type === 'GET_VERSION') {
        event.source && event.source.postMessage({ type: 'SW_VERSION', version: CACHE_VERSION });
    }
});

const STATIC_ASSETS = [
    '/', '/Index', '/Privacy', '/Error',
    '/Authorization/Authorization', '/Authorization', '/Authorization/Logout',
    '/Account/Chats', '/Account/Settings',
    '/manifest.json',
    '/images/web-app-manifest-192x192.png',
    '/images/web-app-manifest-512x512.png',
    '/css/site.css', '/css/layout.css', '/css/index.css', '/css/privacy.css',
    '/css/error.css', '/css/authorization.css', '/css/chats.css', '/css/settings.css',
    '/css/ui-dialogs.css',
    '/app.js', '/js/layout.js', '/js/ui-dialogs.js', '/js/authorization.js',
    '/js/settings.js', '/js/session-cleanup.js',
    '/js/offline-store.js', '/js/chats-offline.js', '/js/desktop-notifications.js',
    '/js/chats/chats-core.js', '/js/chats/chats-messages.js', '/js/chats/chats-forward.js',
    '/js/chats/chats-menus.js', '/js/chats/chats-pins.js', '/js/chats/chats-send.js',
    '/js/chats/chats-signalr.js', '/js/chats/chats-chat-session.js', '/js/chats/chats-list.js',
    '/js/chats/chats-search.js', '/js/chats/chats-participants.js', '/js/chats/chats-ui.js'
];

async function precacheAssets(cache, urls) {
    let ok = 0;
    for (const url of urls) {
        try {
            const res = await fetch(url, { cache: 'no-cache' });
            if (res && res.ok) {
                await cache.put(url, res);
                ok++;
            }
        } catch (e) {
            console.warn('[SW] skip:', url);
        }
    }
    console.log('[SW] precached', ok, '/', urls.length);
}

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(STATIC_CACHE)
            .then(cache => precacheAssets(cache, STATIC_ASSETS))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(keys =>
            Promise.all(
                keys.filter(k => k.startsWith('guap-messenger-') && !k.startsWith(CACHE_VERSION))
                    .map(k => caches.delete(k))
            )
        ).then(() => self.clients.claim())
    );
});

async function trimCache(cacheName, maxEntries) {
    const cache = await caches.open(cacheName);
    const keys = await cache.keys();
    if (keys.length <= maxEntries) return;
    await Promise.all(keys.slice(0, keys.length - maxEntries).map(k => cache.delete(k)));
}

function isApiRequest(url) {
    return url.pathname.includes('/api/') || (API_BASE_URL && url.href.startsWith(API_BASE_URL));
}
function isChatsListRequest(url) {
    const p = url.pathname;
    return /\/chats\/?$/i.test(p) || /\/api\/v\d+\/chats\/?$/i.test(p);
}
function isMessagesRequest(url) {
    return /\/messages(\/|$)/i.test(url.pathname);
}
function isHubSensitive(url) {
    return url.pathname.includes('/hubs/') || url.pathname.includes('/oauth') ||
        url.pathname.includes('/login') || url.pathname.includes('/connect');
}

async function networkFirst(request, cacheName, maxEntries) {
    try {
        const response = await fetch(request);
        if (response && response.ok && request.method === 'GET') {
            const cache = await caches.open(cacheName);
            cache.put(request, response.clone());
            await trimCache(cacheName, maxEntries);
        }
        return response;
    } catch (err) {
        const cached = await caches.match(request);
        if (cached) return cached;
        return new Response(JSON.stringify({ isSuccess: false, message: 'Offline', offline: true }), {
            status: 503,
            headers: { 'Content-Type': 'application/json', 'X-Offline': '1' }
        });
    }
}

self.addEventListener('fetch', event => {
    const request = event.request;
    const url = new URL(request.url);

    if (isHubSensitive(url) || request.method !== 'GET') return;

    if (isApiRequest(url) && isChatsListRequest(url)) {
        event.respondWith(networkFirst(request, CHATS_CACHE, MAX_CHATS_ENTRIES));
        return;
    }
    if (isApiRequest(url) && isMessagesRequest(url)) {
        event.respondWith(networkFirst(request, MESSAGES_CACHE, MAX_MESSAGES_ENTRIES));
        return;
    }
    if (isApiRequest(url) || url.pathname.includes('/api/')) return;

    if (url.pathname.startsWith('/Authorization')) {
        event.respondWith(
            fetch(request).catch(async () => {
                return (await caches.match(request)) ||
                    (await caches.match('/Authorization/Authorization')) ||
                    (await caches.match('/Authorization')) ||
                    new Response('Нет сети. Проверьте подключение.', {
                        status: 503, headers: { 'Content-Type': 'text/plain; charset=utf-8' }
                    });
            })
        );
        return;
    }

    event.respondWith(
        caches.match(request).then(cached => {
            if (cached) {
                if (request.mode === 'navigate' || request.destination === 'document') {
                    fetch(request).then(res => {
                        if (res && res.ok) caches.open(STATIC_CACHE).then(c => c.put(request, res));
                    }).catch(() => {});
                }
                return cached;
            }
            return fetch(request).then(response => {
                if (response && response.ok && (
                    request.destination === 'style' || request.destination === 'script' ||
                    request.destination === 'image' || request.destination === 'font' ||
                    request.mode === 'navigate'
                )) {
                    const clone = response.clone();
                    caches.open(RUNTIME_CACHE).then(c => {
                        c.put(request, clone);
                        trimCache(RUNTIME_CACHE, MAX_RUNTIME_ENTRIES);
                    });
                }
                return response;
            }).catch(async () => {
                if (request.mode === 'navigate') {
                    const path = url.pathname.replace(/\/$/, '') || '/';
                    const candidates = [
                        path,
                        path === '/Index' ? '/' : null,
                        path.startsWith('/Authorization') ? '/Authorization/Authorization' : null,
                        path.startsWith('/Account/Settings') ? '/Account/Settings' : null,
                        path.startsWith('/Account') ? '/Account/Chats' : null,
                        '/Account/Chats', '/', '/Index', '/Privacy', '/Error'
                    ].filter(Boolean);
                    for (const c of candidates) {
                        const hit = await caches.match(c);
                        if (hit) return hit;
                    }
                    return new Response(
                        '<!DOCTYPE html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Offline</title><style>body{font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;background:#0f172a;color:#e2e8f0;text-align:center;padding:1.5rem}a{color:#38bdf8}</style></head><body><div><h1>Нет сети</h1><p>GUAP Messenger недоступен offline.</p><p><a href="/">На главную</a> · <a href="/Account/Chats">Чаты</a></p></div></body></html>',
                        { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8' } }
                    );
                }
                return new Response(null, { status: 404 });
            });
        })
    );
});

self.addEventListener('sync', event => {
    if (event.tag === 'sync-offline-messages') {
        event.waitUntil((async () => {
            const list = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
            for (const c of list) c.postMessage({ type: 'FLUSH_OFFLINE_QUEUE' });
        })());
    }
});

self.addEventListener('periodicsync', event => {
    if (event.tag === 'guap-offline-flush') {
        event.waitUntil((async () => {
            const list = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
            for (const c of list) c.postMessage({ type: 'FLUSH_OFFLINE_QUEUE' });
        })());
    }
});

const NOTIFICATION_TTL_MS = 10000;
const MAX_VISIBLE_NOTIFICATIONS = 8;

function buildUniqueNotificationTag(data) {
    const id = data.notificationId || data.messageId || data.MessageId || data.msgId;
    if (id != null && String(id).length > 0) return 'msg-' + String(id);
    return 'msg-' + Date.now() + '-' + Math.random().toString(36).slice(2, 9);
}

function parsePushPayload(event) {
    let data = {
        title: 'GUAP Messenger',
        body: 'Новое сообщение',
        sender: null,
        chatId: null,
        notificationId: null,
        messageId: null,
        icon: '/images/web-app-manifest-192x192.png',
        url: '/Account/Chats'
    };
    if (!event.data) return data;
    try {
        const payload = event.data.json();
        data = {
            ...data,
            ...payload,
            title: payload.title || payload.Title || payload.sender || payload.Sender || data.title,
            body: payload.body || payload.Body || payload.message || payload.Message || data.body,
            sender: payload.sender || payload.Sender || null,
            chatId: payload.chatId || payload.ChatId || null,
            notificationId: payload.notificationId || payload.NotificationId || null,
            messageId: payload.messageId || payload.MessageId || payload.msgId || null,
            icon: payload.icon || payload.Icon || data.icon,
            url: payload.url || payload.Url || null
        };
    } catch (e) {
        try {
            const t = event.data.text();
            if (t) data.body = t;
        } catch (_) {}
    }
    if (!data.url) {
        data.url = data.chatId
            ? '/Account/Chats?chatId=' + encodeURIComponent(data.chatId)
            : '/Account/Chats';
    }
    return data;
}

async function shouldSuppressOsNotification(data) {
    const list = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    if (!list.length) return false;

    const focused = list.filter(c => c.focused || c.visibilityState === 'visible');
    if (!focused.length) return false;

    const results = await Promise.all(focused.map(client => new Promise(resolve => {
        const channel = new MessageChannel();
        const timer = setTimeout(() => resolve(false), 120);
        channel.port1.onmessage = (ev) => {
            clearTimeout(timer);
            resolve(!!(ev.data && ev.data.suppress));
        };
        try {
            client.postMessage({
                type: 'PUSH_SHOULD_SUPPRESS',
                chatId: data.chatId,
                messageId: data.messageId,
                notificationId: data.notificationId
            }, [channel.port2]);
        } catch (_) {
            clearTimeout(timer);
            resolve(false);
        }
    })));

    return results.some(Boolean);
}

async function notifyClientsNotificationShown(tag, data) {
    const list = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const c of list) {
        try {
            c.postMessage({
                type: 'NOTIFICATION_SHOWN',
                tag: tag,
                messageId: data && data.messageId,
                notificationId: data && data.notificationId,
                chatId: data && data.chatId
            });
        } catch (_) {}
    }
}

async function showStackedNotification(title, options, ttlMs) {
    ttlMs = typeof ttlMs === 'number' ? ttlMs : NOTIFICATION_TTL_MS;

    try {
        const existing = await self.registration.getNotifications({ tag: options.tag });
        if (existing && existing.length) return;
    } catch (_) {}

    await self.registration.showNotification(title, options);

    try {
        const all = await self.registration.getNotifications();
        if (all.length > MAX_VISIBLE_NOTIFICATIONS) {
            const excess = all.length - MAX_VISIBLE_NOTIFICATIONS;
            for (let i = 0; i < excess; i++) {
                try { all[i].close(); } catch (_) {}
            }
        }
    } catch (_) {}

    const tag = options.tag;
    setTimeout(async () => {
        try {
            const list = tag ? await self.registration.getNotifications({ tag }) : [];
            list.forEach(n => { try { n.close(); } catch (_) {} });
        } catch (_) {}
    }, ttlMs);
}

self.addEventListener('push', event => {
    const data = parsePushPayload(event);
    const title = data.sender || data.title || 'GUAP Messenger';
    const tag = buildUniqueNotificationTag(data);
    const ttl = 4000 + Math.floor(Math.random() * 4000);

    const options = {
        body: data.body || 'У вас новое сообщение',
        icon: data.icon || '/images/web-app-manifest-192x192.png',
        badge: '/images/web-app-manifest-192x192.png',
        tag: tag,
        renotify: false,
        requireInteraction: false,
        silent: false,
        vibrate: [180, 80, 180],
        timestamp: Date.now(),
        data: {
            url: data.url,
            chatId: data.chatId,
            notificationId: data.notificationId,
            messageId: data.messageId,
            tag: tag
        }
    };

    event.waitUntil((async () => {
        if (await shouldSuppressOsNotification(data)) {
            await notifyClientsNotificationShown(tag, data);
            return;
        }
        await showStackedNotification(title, options, ttl);
        await notifyClientsNotificationShown(tag, data);
    })());
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const nData = event.notification.data || {};
    const chatId = nData.chatId;
    const notificationId = nData.notificationId;
    const targetUrl = nData.url
        || (chatId ? '/Account/Chats?chatId=' + encodeURIComponent(chatId) : '/Account/Chats');
    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then(existingClients => {
            for (const client of existingClients) {
                if (client.url.includes('/Account/Chats') || client.url.includes('/Account/Settings')) {
                    client.postMessage({
                        type: 'OPEN_SPECIFIC_CHAT',
                        chatId: chatId,
                        notificationId: notificationId
                    });
                    if ('focus' in client) return client.focus();
                }
            }
            if (clients.openWindow) return clients.openWindow(targetUrl);
        })
    );
});

self.addEventListener('pushsubscriptionchange', () => {
    console.log('[SW] pushsubscriptionchange — клиент должен переподписаться');
});