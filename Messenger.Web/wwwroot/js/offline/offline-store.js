(function (global) {
    'use strict';

    const DB_NAME = 'guap-messenger-offline';
    const DB_VERSION = 2;

    const STORE_QUEUE = 'messageQueue';
    const STORE_CHATS = 'chatsCache';
    const STORE_MESSAGES = 'messagesCache';
    const STORE_META = 'meta';

    const QUEUE_MAX = 300;
    const CHATS_TTL_MS = 14 * 24 * 60 * 60 * 1000;
    const MESSAGES_TTL_MS = 7 * 24 * 60 * 60 * 1000;
    const MAX_MESSAGES_PER_CHAT = 200;

    let dbPromise = null;
    let isFlushing = false;

    function openDb() {
        if (dbPromise) return dbPromise;
        dbPromise = new Promise((resolve, reject) => {
            const req = indexedDB.open(DB_NAME, DB_VERSION);
            req.onerror = () => reject(req.error);
            req.onsuccess = () => resolve(req.result);
            req.onupgradeneeded = (e) => {
                const db = e.target.result;
                const old = e.oldVersion;

                if (old < 1) {
                    if (!db.objectStoreNames.contains(STORE_QUEUE)) {
                        const store = db.createObjectStore(STORE_QUEUE, { keyPath: 'id', autoIncrement: true });
                        store.createIndex('byChat', 'chatId', { unique: false });
                        store.createIndex('byCreated', 'createdAt', { unique: false });
                        store.createIndex('byStatus', 'status', { unique: false });
                        store.createIndex('byClientId', 'clientId', { unique: true });
                    }
                    if (!db.objectStoreNames.contains(STORE_CHATS)) {
                        db.createObjectStore(STORE_CHATS, { keyPath: 'key' });
                    }
                    if (!db.objectStoreNames.contains(STORE_META)) {
                        db.createObjectStore(STORE_META, { keyPath: 'key' });
                    }
                }
                if (old < 2) {
                    if (!db.objectStoreNames.contains(STORE_MESSAGES)) {
                        const ms = db.createObjectStore(STORE_MESSAGES, { keyPath: 'chatId' });
                        ms.createIndex('bySavedAt', 'savedAt', { unique: false });
                    }
                    if (db.objectStoreNames.contains(STORE_QUEUE)) {
                        const tx = e.target.transaction;
                        const store = tx.objectStore(STORE_QUEUE);
                        if (!store.indexNames.contains('byClientId')) {
                            try { store.createIndex('byClientId', 'clientId', { unique: true }); } catch (_) {}
                        }
                    }
                }
            };
        });
        return dbPromise;
    }

    function tx(storeNames, mode) {
        return openDb().then(db => db.transaction(storeNames, mode));
    }

    function idbReq(request) {
        return new Promise((resolve, reject) => {
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    async function enqueueMessage({ chatId, text, clientId, replyPayload }) {
        if (!chatId || !(text && String(text).trim())) {
            throw new Error('chatId and text required');
        }
        const item = {
            clientId: clientId || ('temp-' + Date.now() + '-' + Math.random().toString(36).slice(2, 8)),
            chatId: String(chatId),
            text: String(text),
            replyPayload: replyPayload || null,
            createdAt: Date.now(),
            status: 'pending',
            error: null,
            attempts: 0
        };

        const t = await tx(STORE_QUEUE, 'readwrite');
        const store = t.objectStore(STORE_QUEUE);

        const count = await idbReq(store.count());
        if (count >= QUEUE_MAX) {
            throw new Error('Offline-очередь заполнена (макс. ' + QUEUE_MAX + ')');
        }

        try {
            const idx = store.index('byClientId');
            const existing = await idbReq(idx.get(item.clientId));
            if (existing) {
                item.id = existing.id;
                await idbReq(store.put(item));
            } else {
                await idbReq(store.add(item));
            }
        } catch (_) {
            await idbReq(store.add(item));
        }

        await registerBackgroundSync();
        global.dispatchEvent(new CustomEvent('offline-store:enqueued', { detail: item }));
        return item;
    }

    async function listPending(chatId) {
        const t = await tx(STORE_QUEUE, 'readonly');
        const store = t.objectStore(STORE_QUEUE);
        const all = await idbReq(store.getAll());
        let items = (all || []).filter(i => i.status === 'pending' || i.status === 'failed' || i.status === 'sending');
        if (chatId) items = items.filter(i => String(i.chatId) === String(chatId));
        items.sort((a, b) => a.createdAt - b.createdAt);
        return items;
    }

    async function getByClientId(clientId) {
        const t = await tx(STORE_QUEUE, 'readonly');
        const store = t.objectStore(STORE_QUEUE);
        try {
            const idx = store.index('byClientId');
            return await idbReq(idx.get(clientId));
        } catch (_) {
            const all = await idbReq(store.getAll());
            return (all || []).find(i => i.clientId === clientId) || null;
        }
    }

    async function removeById(id) {
        const t = await tx(STORE_QUEUE, 'readwrite');
        await idbReq(t.objectStore(STORE_QUEUE).delete(id));
    }

    async function removeByClientId(clientId) {
        const row = await getByClientId(clientId);
        if (row && row.id != null) await removeById(row.id);
    }

    async function updateItem(id, patch) {
        const t = await tx(STORE_QUEUE, 'readwrite');
        const store = t.objectStore(STORE_QUEUE);
        const row = await idbReq(store.get(id));
        if (!row) return null;
        Object.assign(row, patch);
        await idbReq(store.put(row));
        return row;
    }

    async function countPending() {
        const items = await listPending();
        return items.length;
    }

    async function saveChatsCache(chatsArray) {
        const payload = {
            key: 'recentChats',
            data: Array.isArray(chatsArray) ? chatsArray : [],
            savedAt: Date.now()
        };
        const t = await tx(STORE_CHATS, 'readwrite');
        await idbReq(t.objectStore(STORE_CHATS).put(payload));
    }

    async function loadChatsCache() {
        const t = await tx(STORE_CHATS, 'readonly');
        const row = await idbReq(t.objectStore(STORE_CHATS).get('recentChats'));
        if (!row || !row.data) return null;
        if (Date.now() - (row.savedAt || 0) > CHATS_TTL_MS) return null;
        return row.data;
    }

    async function saveMessagesCache(chatId, messagesArray, meta) {
        if (!chatId) return;
        let list = Array.isArray(messagesArray) ? messagesArray.slice() : [];
        if (list.length > MAX_MESSAGES_PER_CHAT) {
            list = list.slice(-MAX_MESSAGES_PER_CHAT);
        }
        const payload = {
            chatId: String(chatId),
            data: list,
            savedAt: Date.now(),
            meta: meta || null
        };
        const t = await tx(STORE_MESSAGES, 'readwrite');
        await idbReq(t.objectStore(STORE_MESSAGES).put(payload));
    }

    async function loadMessagesCache(chatId) {
        if (!chatId) return null;
        const t = await tx(STORE_MESSAGES, 'readonly');
        const row = await idbReq(t.objectStore(STORE_MESSAGES).get(String(chatId)));
        if (!row || !row.data) return null;
        if (Date.now() - (row.savedAt || 0) > MESSAGES_TTL_MS) return null;
        return { messages: row.data, savedAt: row.savedAt, meta: row.meta };
    }

    async function appendMessageToCache(chatId, message) {
        if (!chatId || !message) return;
        const existing = await loadMessagesCache(chatId);
        const list = existing && existing.messages ? existing.messages.slice() : [];
        const mid = message.messageId || message.id || message.MessageId;
        if (mid && list.some(m => String(m.messageId || m.id || m.MessageId) === String(mid))) {
            return;
        }
        list.push(message);
        await saveMessagesCache(chatId, list, existing && existing.meta);
    }

    async function setMeta(key, value) {
        const t = await tx(STORE_META, 'readwrite');
        await idbReq(t.objectStore(STORE_META).put({ key, value, updatedAt: Date.now() }));
    }

    async function getMeta(key) {
        const t = await tx(STORE_META, 'readonly');
        const row = await idbReq(t.objectStore(STORE_META).get(key));
        return row ? row.value : null;
    }

    async function registerBackgroundSync() {
        if (!('serviceWorker' in navigator) || !('SyncManager' in window)) return false;
        try {
            const reg = await navigator.serviceWorker.ready;
            await reg.sync.register('sync-offline-messages');
            return true;
        } catch (e) {
            console.warn('[OfflineStore] Background Sync unavailable:', e);
            return false;
        }
    }

    async function flushQueue(sender) {
        if (isFlushing) return { sent: 0, failed: 0, skipped: true };
        if (!navigator.onLine) return { sent: 0, failed: 0, offline: true };
        if (typeof sender !== 'function') throw new Error('sender required');

        isFlushing = true;
        let sent = 0;
        let failed = 0;

        try {
            const pending = await listPending();
            for (const item of pending) {
                if (!navigator.onLine) break;
                if ((item.attempts || 0) >= 8) {
                    await updateItem(item.id, { status: 'failed', error: 'Превышено число попыток' });
                    failed++;
                    continue;
                }

                await updateItem(item.id, { status: 'sending', attempts: (item.attempts || 0) + 1 });

                try {
                    const result = await sender({
                        chatId: item.chatId,
                        text: item.text,
                        clientId: item.clientId,
                        replyPayload: item.replyPayload
                    });

                    if (result && result.ok) {
                        await removeById(item.id);
                        sent++;
                        global.dispatchEvent(new CustomEvent('offline-store:sent', {
                            detail: {
                                clientId: item.clientId,
                                chatId: item.chatId,
                                serverMessageId: result.serverMessageId
                            }
                        }));
                    } else {
                        await updateItem(item.id, {
                            status: 'failed',
                            error: (result && result.error) || 'Send failed'
                        });
                        failed++;
                        global.dispatchEvent(new CustomEvent('offline-store:failed', {
                            detail: {
                                clientId: item.clientId,
                                chatId: item.chatId,
                                error: result && result.error
                            }
                        }));
                    }
                } catch (err) {
                    await updateItem(item.id, {
                        status: 'failed',
                        error: err.message || String(err)
                    });
                    failed++;
                    global.dispatchEvent(new CustomEvent('offline-store:failed', {
                        detail: {
                            clientId: item.clientId,
                            chatId: item.chatId,
                            error: err.message
                        }
                    }));
                }
            }
        } finally {
            isFlushing = false;
        }

        global.dispatchEvent(new CustomEvent('offline-store:flushed', { detail: { sent, failed } }));
        return { sent, failed };
    }

    function isOnline() {
        return typeof navigator !== 'undefined' && navigator.onLine !== false;
    }

    if (typeof window !== 'undefined') {
        window.addEventListener('online', () => {
            global.dispatchEvent(new CustomEvent('offline-store:online'));
            registerBackgroundSync();
        });
        window.addEventListener('offline', () => {
            global.dispatchEvent(new CustomEvent('offline-store:offline'));
        });
    }

    if (typeof navigator !== 'undefined' && navigator.serviceWorker) {
        navigator.serviceWorker.addEventListener('message', (event) => {
            if (event.data && event.data.type === 'FLUSH_OFFLINE_QUEUE') {
                global.dispatchEvent(new CustomEvent('offline-store:flush-request'));
            }
        });
    }

    const api = {
        enqueueMessage,
        listPending,
        getByClientId,
        removeById,
        removeByClientId,
        updateItem,
        countPending,
        flushQueue,
        registerBackgroundSync,
        saveChatsCache,
        loadChatsCache,
        saveMessagesCache,
        loadMessagesCache,
        appendMessageToCache,
        setMeta,
        getMeta,
        isOnline,
        openDb,
        DB_NAME,
        DB_VERSION
    };

    global.OfflineStore = api;
    global.OfflineQueue = api;
})(typeof window !== 'undefined' ? window : self);
