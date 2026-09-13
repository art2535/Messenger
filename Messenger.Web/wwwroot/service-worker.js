const CACHE_NAME = 'guap-messenger-v0.10';

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
            .then(cache => cache.addAll(STATIC_ASSETS))
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
                return new Response('Нет сети', { status: 503 });
            })
        );
        return;
    }

    event.respondWith(
        caches.match(event.request).then(response => {
            return response || fetch(event.request).catch(err => {
                console.warn('Фоновый запрос не удался:', event.request.url);
                return new Response(null, { status: 404 });
            });
        })
    );
});

self.addEventListener('push', event => {
    let data = {
        title: 'GUAP Messenger',
        body: 'Новое сообщение',
        sender: 'Кто-то',
        chatId: null,
        notificationId: null
    };

    if (event.data) {
        try {
            data = { ...data, ...event.data.json() };
        } catch (e) {
            data.body = event.data.text();
        }
    }

    const options = {
        body: data.body || 'У вас новое сообщение',
        icon: '/images/web-app-manifest-192x192.png',
        badge: '/images/web-app-manifest-192x192.png',
        vibrate: [200, 100, 200],
        tag: data.chatId ? `chat-${data.chatId}` : 'default',
        renotify: true,
        data: {
            url: data.chatId ? `/Account/Chats?chatId=${data.chatId}` : '/Account/Chats',
            chatId: data.chatId,
            notificationId: data.notificationId
        }
    };

    event.waitUntil(self.registration.showNotification(data.sender || 'Новое сообщение', options));

    setTimeout(() => {
        self.registration.getNotifications({ tag: options.tag })
            .then(nots => nots.forEach(n => n.close()));
    }, 5000);
});

self.addEventListener('notificationclick', event => {
    event.notification.close();

    const { chatId, notificationId } = event.notification.data || {};
    const targetUrl = chatId
        ? `/Account/Chats?chatId=${chatId}`
        : '/Account/Chats';

    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then(existingClients => {
            for (let client of existingClients) {
                if (client.url.includes('/Account/Chats')) {
                    client.postMessage({
                        type: 'OPEN_SPECIFIC_CHAT',
                        chatId: chatId,
                        notificationId: notificationId
                    });
                    return client.focus();
                }
            }

            console.log(`[SW] Открываем чат напрямую: ${targetUrl}`);
            return clients.openWindow(targetUrl);
        })
    );
});