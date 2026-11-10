(function (global) {
    'use strict';

    const TTL_MIN = 8000;
    const TTL_MAX = 12000;
    const MAX_STACK = 12;
    const DEDUP_TTL_MS = 90000;
    const active = new Map();
    const recentlyShown = new Map();

    function randomTtl() {
        return TTL_MIN + Math.floor(Math.random() * (TTL_MAX - TTL_MIN + 1));
    }

    function canNotify() {
        return typeof Notification !== 'undefined' && Notification.permission === 'granted';
    }

    function messageKey(opts) {
        const id = opts.messageId || opts.notificationId;
        if (id != null && String(id).length) return 'msg-' + String(id);
        return 'msg-' + Date.now() + '-' + Math.random().toString(36).slice(2, 8);
    }

    function pruneRecent() {
        const now = Date.now();
        for (const [k, t] of recentlyShown) {
            if (now - t > DEDUP_TTL_MS) recentlyShown.delete(k);
        }
    }

    function markShown(key) {
        if (!key) return;
        pruneRecent();
        recentlyShown.set(key, Date.now());
        try { sessionStorage.setItem('guap_notif_' + key, String(Date.now())); } catch (_) {}
    }

    function wasShown(key) {
        if (!key) return false;
        pruneRecent();
        if (recentlyShown.has(key)) return true;
        try {
            const raw = sessionStorage.getItem('guap_notif_' + key);
            if (raw && Date.now() - parseInt(raw, 10) < DEDUP_TTL_MS) return true;
            if (raw) sessionStorage.removeItem('guap_notif_' + key);
        } catch (_) {}
        return false;
    }

    function isViewingChat(chatId) {
        if (chatId == null) return false;
        if (typeof document !== 'undefined' && document.hidden) return false;
        if (typeof currentChatId === 'undefined' || !currentChatId) return false;
        return String(currentChatId) === String(chatId);
    }

    function shouldSuppressOpenChat() {
        if (typeof window.__suppressOpenChat === 'boolean') return window.__suppressOpenChat;
        try {
            if (document.documentElement.dataset.suppressOpenChat === '0') return false;
        } catch (_) {}
        return true;
    }

    function shouldPlaySound() {
        if (typeof window.__notifSound === 'boolean') return window.__notifSound;
        try {
            if (document.documentElement.dataset.notifySound === '0') return false;
        } catch (_) {}
        return true;
    }

    function shouldShowPreview() {
        if (typeof window.__notifyPreview === 'boolean') return window.__notifyPreview;
        try {
            if (document.documentElement.dataset.notifyPreview === '0') return false;
        } catch (_) {}
        return true;
    }

    function shouldShowOsNotification(chatId) {
        if (typeof document === 'undefined') return false;
        if (isViewingChat(chatId) && shouldSuppressOpenChat()) return false;
        if (!document.hidden && isViewingChat(chatId)) return false;
        return true;
    }

    function trimStack() {
        if (active.size <= MAX_STACK) return;
        const keys = [...active.keys()];
        for (let i = 0; i < active.size - MAX_STACK; i++) {
            const tag = keys[i];
            try { active.get(tag) && active.get(tag).close(); } catch (_) {}
            active.delete(tag);
        }
    }

    async function alreadyInSystemTray(tag) {
        if (!tag || !('serviceWorker' in navigator)) return false;
        try {
            const reg = await navigator.serviceWorker.getRegistration();
            if (!reg || !reg.getNotifications) return false;
            const list = await reg.getNotifications({ tag: tag });
            return !!(list && list.length);
        } catch (_) { return false; }
    }

    async function showMessageNotification(opts) {
        opts = opts || {};
        if (!canNotify()) return null;
        if (opts.chatId != null && !shouldShowOsNotification(opts.chatId)) return null;

        const tag = messageKey(opts);
        if (wasShown(tag) || active.has(tag)) return null;
        if (await alreadyInSystemTray(tag)) {
            markShown(tag);
            return null;
        }

        const title = opts.title || opts.sender || 'GUAP Messenger';
        const body = opts.body || 'Новое сообщение';
        const icon = opts.icon || '/images/web-app-manifest-192x192.png';
        const chatId = opts.chatId;
        const url = chatId
            ? '/inside/chats?chatId=' + encodeURIComponent(chatId)
            : '/inside/chats';
        const silent = opts.silent != null ? !!opts.silent : !shouldPlaySound();

        let n;
        try {
            n = new Notification(title, {
                body: body,
                icon: icon,
                badge: icon,
                tag: tag,
                renotify: false,
                requireInteraction: false,
                silent: silent,
                data: { url: url, chatId: chatId, messageId: opts.messageId || null, notificationId: opts.notificationId || null }
            });
        } catch (e) {
            console.warn('[GuapNotify]', e);
            return null;
        }

        markShown(tag);
        active.set(tag, n);
        trimStack();

        const timer = setTimeout(() => {
            try { n.close(); } catch (_) {}
            active.delete(tag);
        }, randomTtl());

        n.onclick = function (ev) {
            try { ev && ev.preventDefault && ev.preventDefault(); } catch (_) {}
            try { n.close(); } catch (_) {}
            active.delete(tag);
            clearTimeout(timer);
            try {
                window.focus();
                if (chatId && typeof openChatById === 'function') openChatById(chatId);
                else if (chatId) window.location.href = url;
            } catch (_) {}
        };
        n.onclose = function () {
            active.delete(tag);
            clearTimeout(timer);
        };
        return n;
    }

    function onIncomingMessage(msg) {
        if (!msg) return;
        const senderId = msg.senderId || msg.SenderId;
        if (typeof me !== 'undefined' && senderId != null && String(senderId) === String(me)) return;

        const s = window.__accountSettings || {};
        const chatType = (msg.chatType || msg.ChatType || '').toString().toLowerCase();
        const isGroup = !!(msg.isGroup || msg.IsGroup || chatType === 'group' ||
            (typeof currentChatInfo !== 'undefined' && currentChatInfo &&
                String(currentChatInfo.chatId) === String(msg.chatId || msg.ChatId) &&
                currentChatInfo.type === 'group'));
        if (isGroup && s.notifyGroupChats === false) return;
        if (!isGroup && s.notifyMessages === false) return;

        const chatId = msg.chatId || msg.ChatId;
        const mid = msg.messageId || msg.MessageId;
        const text = msg.messageText || msg.MessageText || '';
        const senderName = msg.senderName || msg.SenderName || msg.sender || msg.Sender || 'Сообщение';
        const chatName = msg.chatName || msg.ChatName ||
            (typeof currentChatInfo !== 'undefined' && currentChatInfo &&
                String(currentChatInfo.chatId) === String(chatId)
                ? (currentChatInfo.name || currentChatInfo.title || '')
                : '');

        const textPreview = String(text).length > 120 ? String(text).slice(0, 117) + '…' : text;
        let title;
        let body;
        if (isGroup) {
            const gName = chatName || 'Групповой чат';
            title = 'Новое сообщение в группе ' + gName;
            body = shouldShowPreview()
                ? `${senderName}: ${textPreview || 'вложение'}`
                : `${senderName}: новое сообщение`;
        } else {
            title = 'Новое сообщение от ' + senderName;
            body = shouldShowPreview()
                ? (textPreview || 'Вложение')
                : 'Новое сообщение';
        }

        if (typeof document !== 'undefined' && document.hidden) return;

        showMessageNotification({
            title: title,
            body: body,
            chatId: chatId,
            messageId: mid
        });
    }

    async function closeMessageNotification(messageId) {
        if (messageId == null) return;
        const tag = 'msg-' + String(messageId);
        if (active.has(tag)) {
            try { active.get(tag).close(); } catch (_) {}
            active.delete(tag);
        }
        try {
            if ('serviceWorker' in navigator) {
                const reg = await navigator.serviceWorker.getRegistration();
                if (reg && reg.getNotifications) {
                    const list = await reg.getNotifications({ tag: tag });
                    (list || []).forEach(n => { try { n.close(); } catch (_) {} });
                }
            }
        } catch (_) {}
    }

    if (typeof navigator !== 'undefined' && navigator.serviceWorker) {
        navigator.serviceWorker.addEventListener('message', function (event) {
            if (!event.data) return;
            if (event.data.type === 'PUSH_SHOULD_SUPPRESS') {
                const chatId = event.data.chatId;
                const suppress = isViewingChat(chatId);
                try {
                    if (event.ports && event.ports[0]) {
                        event.ports[0].postMessage({ suppress: suppress });
                    }
                } catch (_) {}
            }
            if (event.data.type === 'NOTIFICATION_SHOWN') {
                const id = event.data.tag || event.data.messageId || event.data.notificationId;
                if (id) markShown(String(id).indexOf('msg-') === 0 ? String(id) : 'msg-' + id);
            }
        });
    }

    global.GuapNotify = {
        message: showMessageNotification,
        onIncomingMessage: onIncomingMessage,
        shouldShowOsNotification: shouldShowOsNotification,
        canNotify: canNotify,
        markShown: markShown,
        wasShown: wasShown,
        closeMessageNotification: closeMessageNotification,
        isViewingChat: isViewingChat
    };
})(typeof window !== 'undefined' ? window : self);