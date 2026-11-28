const API_BASE_URL = window.API_BASE_URL;
const HUB_URL = window.HUB_URL;

let currentToken = null;

function getAuthToken() {
    let token = localStorage.getItem('token');

    if (!token) {
        const metaToken = document.querySelector('meta[name="access-token"]');
        if (metaToken) token = metaToken.content;
    }

    if (!token && typeof sessionToken !== 'undefined') {
        token = sessionToken;
    }

    if (token && !token.startsWith('Bearer ')) {
        token = 'Bearer ' + token;
    }

    currentToken = token;
    return token;
}

// Service Worker регистрируется централизованно в _LayoutPWA.cshtml.
// Здесь используем уже готовую регистрацию, чтобы не создавать конкурирующие
// register() вызовы на странице чатов.
if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.ready
            .then(registration => {
                window.setTimeout(() => initializePushNotifications(registration), 1500);
            })
            .catch(() => {
                // PWA недоступна; базовые функции чатов продолжают работать.
            });
    });
}

/**
 * Push в БД регистрируем только когда нужно:
 * - нет локальной отметки синхронизации / сменился endpoint
 * - forceSync = true (вход, смена настроек)
 * На каждом открытии страницы API не дергаем.
 */
const PUSH_LS_SUBSCRIBED = 'pushSubscribed';
const PUSH_LS_ENDPOINT = 'pushSubscriptionEndpoint';
const PUSH_LS_ENABLED = 'pushEnabled';

function isPushLocallySynced(endpoint) {
    try {
        if (localStorage.getItem(PUSH_LS_SUBSCRIBED) !== 'true') return false;
        if (localStorage.getItem(PUSH_LS_ENABLED) === 'false') return false;
        const saved = localStorage.getItem(PUSH_LS_ENDPOINT) || '';
        return !!endpoint && saved === endpoint;
    } catch (_) {
        return false;
    }
}

function markPushSynced(endpoint, enabled) {
    try {
        if (endpoint) localStorage.setItem(PUSH_LS_ENDPOINT, endpoint);
        localStorage.setItem(PUSH_LS_SUBSCRIBED, 'true');
        if (enabled === false) localStorage.setItem(PUSH_LS_ENABLED, 'false');
        else localStorage.setItem(PUSH_LS_ENABLED, 'true');
    } catch (_) { /* private mode */ }
}

function clearPushSyncMarkers() {
    try {
        localStorage.removeItem(PUSH_LS_SUBSCRIBED);
        localStorage.removeItem(PUSH_LS_ENDPOINT);
        // pushEnabled оставляем — предпочтение пользователя
    } catch (_) { }
}

async function fetchVapidPublicKey() {
    const keyResponse = await fetch(`${API_BASE_URL}/push/vapid-public-key`, {
        method: 'GET',
        headers: { 'Accept': 'application/json, text/plain, */*' }
    });
    if (!keyResponse.ok) throw new Error('Не удалось получить VAPID ключ');
    let publicKey = '';
    const ct = (keyResponse.headers.get('content-type') || '').toLowerCase();
    if (ct.includes('json')) {
        const j = await keyResponse.json();
        publicKey = String(j.data ?? j.Data ?? j.publicKey ?? '').trim();
    } else {
        const text = (await keyResponse.text()).trim();
        if (text.startsWith('{')) {
            try {
                const j = JSON.parse(text);
                publicKey = String(j.data ?? j.Data ?? j.publicKey ?? '').trim();
            } catch { publicKey = text; }
        } else publicKey = text;
    }
    publicKey = publicKey.replace(/^"|"$/g, '');
    if (!publicKey) throw new Error('Пустой VAPID ключ');
    return publicKey;
}

async function postPushSubscribe(subscription, token) {
    const subJson = subscription.toJSON();
    const response = await fetch(`${API_BASE_URL}/push/subscribe`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'Authorization': token
        },
        body: JSON.stringify({
            endpoint: subscription.endpoint,
            p256dh: subJson.keys?.p256dh,
            auth: subJson.keys?.auth
        })
    });
    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || ('HTTP ' + response.status));
    }
    markPushSynced(subscription.endpoint, true);
    return true;
}

/**
 * @param {ServiceWorkerRegistration} registration
 * @param {{ forceSync?: boolean }} [options]
 */
async function initializePushNotifications(registration, options) {
    const forceSync = !!(options && options.forceSync);
    const token = getAuthToken();
    if (!token) {
        return;
    }

    if (!API_BASE_URL) {
        return;
    }

    try {
        if (!('Notification' in window) || !('PushManager' in window)) {
            return;
        }

        if (Notification.permission === 'denied') {
            return;
        }

        if (Notification.permission !== 'granted') {
            // не запрашиваем permission на каждой странице
            return;
        }

        // Локальный флаг «выключено в настройках» — без запроса к API
        try {
            if (!forceSync && localStorage.getItem(PUSH_LS_ENABLED) === 'false') {
                return;
            }
        } catch (_) { /* */ }

        let subscription = await registration.pushManager.getSubscription();

        // Уже синхронизировано с тем же endpoint — не трогаем API
        if (!forceSync && subscription && isPushLocallySynced(subscription.endpoint)) {
            return;
        }

        // Нет браузерной подписки: создаём только при forceSync или если раньше push был включён
        if (!subscription) {
            let shouldCreate = forceSync;
            try {
                shouldCreate = shouldCreate || localStorage.getItem(PUSH_LS_ENABLED) === 'true'
                    || localStorage.getItem(PUSH_LS_SUBSCRIBED) === 'true';
            } catch (_) { /* */ }
            if (!shouldCreate) {
                return;
            }
            const publicKey = await fetchVapidPublicKey();
            subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: urlBase64ToUint8Array(publicKey)
            });
        }

        await postPushSubscribe(subscription, token);
    } catch (err) {
        if (forceSync && typeof showToast === 'function') showToast('Не удалось настроить push-уведомления', 'error');
        try { localStorage.removeItem(PUSH_LS_SUBSCRIBED); } catch (_) { }
    }
}

/** Вызов после логина или из настроек — принудительная запись в БД */
async function syncPushSubscriptionToServer() {
    if (!('serviceWorker' in navigator)) return false;
    try {
        const registration = await navigator.serviceWorker.ready;
        await initializePushNotifications(registration, { forceSync: true });
        return true;
    } catch (e) {
        if (typeof showToast === 'function') showToast('Не удалось синхронизировать push-подписку', 'error');
        return false;
    }
}

window.syncPushSubscriptionToServer = syncPushSubscriptionToServer;
window.clearPushSyncMarkers = clearPushSyncMarkers;
window.markPushSynced = markPushSynced;

function urlBase64ToUint8Array(base64String) {
    let s = String(base64String || '').trim().replace(/^"|"$/g, '').replace(/\s+/g, '');
    if (s.startsWith('{')) {
        try {
            const j = JSON.parse(s);
            s = String(j.data ?? j.Data ?? j.publicKey ?? '').trim();
        } catch (e) {
            throw new Error('Некорректный VAPID-ключ');
        }
    }
    const padding = '='.repeat((4 - s.length % 4) % 4);
    const base64 = (s + padding).replace(/-/g, '+').replace(/_/g, '/');
    const rawData = window.atob(base64);
    const outputArray = new Uint8Array(rawData.length);
    for (let i = 0; i < rawData.length; ++i) {
        outputArray[i] = rawData.charCodeAt(i);
    }
    return outputArray;
}

if ('serviceWorker' in navigator) navigator.serviceWorker.addEventListener('message', async function (event) {
    if (event.data && event.data.type === 'OPEN_SPECIFIC_CHAT') {
        const { chatId, notificationId } = event.data;


        if (chatId) {
            setTimeout(async () => {
                await openChatById(chatId);
            }, 800);
        }

        if (notificationId) {
            setTimeout(() => {
                markNotificationAsRead(notificationId);
            }, 1500);
        }
    }
});

async function openChatById(chatId) {
    if (!chatId) return;


    document.querySelectorAll('.chat-item').forEach(item => item.classList.remove('active'));

    let chatItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);

    if (chatItem) {
        chatItem.click();
        return;
    }

    await loadChats();

    chatItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
    if (chatItem) {
        chatItem.click();
    } else {
        showToast('Не удалось открыть чат', 'warning');
    }
}

async function markNotificationAsRead(notificationId) {
    try {
        const token = getAuthToken();
        if (!token) {
            return;
        }

        if (!API_BASE_URL) return;

        const response = await fetch(`${API_BASE_URL}/push/${notificationId}/read`, {
            method: 'POST',
            headers: {
                'Authorization': token,
                'Content-Type': 'application/json'
            }
        });

        if (response.ok) {
        } else {
        }
    } catch (err) {
    }
}