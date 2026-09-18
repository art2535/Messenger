const CACHE_NAME = 'guap-messenger-v0.10.1';

let API_BASE_URL = null;

self.addEventListener('message', event => {
    if (event.data && event.data.type === 'SET_CONFIG') {
        API_BASE_URL = event.data.apiBaseUrl;
        console.log('[SW] Получен API_BASE_URL:', API_BASE_URL);
    }
});

const STATIC_ASSETS = [
    '/',
    '/Account/Chats',
    '/manifest.json',
    '/css/site.css',
    '/images/web-app-manifest-192x192.png',
    '/images/web-app-manifest-512x512.png'
];

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => cache.addAll(STATIC_ASSETS).catch(err => {
                console.warn('[SW] Не все статичные ресурсы закэшированы:', err);
            }))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(keys => {
            return Promise.all(keys.map(key => key !== CACHE_NAME ? caches.delete(key) : null));
        }).then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);

    if (url.pathname.includes('/hubs/') || url.pathname.includes('/api/')) {
        return;
    }

    if (url.pathname.startsWith('/Authorization')) {
        event.respondWith(
            fetch(event.request).catch(() => {
                return new Response('Нет сети', { status: 503, statusText: 'Offline' });
            })
        );
        return;
    }

    event.respondWith(
        caches.match(event.request).then(response => {
            return response || fetch(event.request).catch(() => {
                console.warn('[SW] Фоновый запрос не удался:', event.request.url);
                return new Response(null, { status: 404 });
            });
        })
    );
});

self.addEventListener('push', event => {
    let data = {
        title: 'GUAP Messenger',
        body: 'Новое сообщение',
        sender: null,
        chatId: null,
        notificationId: null,
        icon: '/images/web-app-manifest-192x192.png',
        url: '/Account/Chats'
    };

    if (event.data) {
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
                icon: payload.icon || payload.Icon || data.icon,
                url: payload.url || payload.Url || null
            };
        } catch (e) {
            try {
                const text = event.data.text();
                if (text) data.body = text;
            } catch (_) { }
        }
    }

    if (!data.url) {
        data.url = data.chatId
            ? `/Account/Chats?chatId=${data.chatId}`
            : '/Account/Chats';
    }

    const title = data.sender || data.title || 'GUAP Messenger';
    const options = {
        body: data.body || 'У вас новое сообщение',
        icon: data.icon || '/images/web-app-manifest-192x192.png',
        badge: '/images/web-app-manifest-192x192.png',
        vibrate: [200, 100, 200],
        tag: data.chatId ? `chat-${data.chatId}` : 'guap-default',
        renotify: true,
        requireInteraction: false,
        data: {
            url: data.url,
            chatId: data.chatId,
            notificationId: data.notificationId
        }
    };

    event.waitUntil(
        self.registration.showNotification(title, options)
    );
});

self.addEventListener('notificationclick', event => {
    event.notification.close();

    const nData = event.notification.data || {};
    const chatId = nData.chatId;
    const notificationId = nData.notificationId;
    const targetUrl = nData.url
        || (chatId ? `/Account/Chats?chatId=${chatId}` : '/Account/Chats');

    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then(existingClients => {
            for (const client of existingClients) {
                if (client.url.includes('/Account/Chats') || client.url.includes('/Account/Settings')) {
                    client.postMessage({
                        type: 'OPEN_SPECIFIC_CHAT',
                        chatId: chatId,
                        notificationId: notificationId
                    });
                    if ('focus' in client) {
                        return client.focus();
                    }
                }
            }
            if (clients.openWindow) {
                return clients.openWindow(targetUrl);
            }
        })
    );
});

self.addEventListener('pushsubscriptionchange', event => {
    console.log('[SW] pushsubscriptionchange — клиент должен переподписаться');
});