(function () {
    'use strict';
    const Store = window.OfflineStore || window.OfflineQueue;
    if (!Store) {
        console.warn('[chats-offline] OfflineStore not found');
        return;
    }

    function ensureOfflineBanner() {
        if (document.getElementById('offline-banner')) return;
        const el = document.createElement('div');
        el.id = 'offline-banner';
        el.className = 'hidden fixed top-0 left-0 right-0 z-[100] bg-amber-500 text-white text-center text-sm py-1.5 px-3 shadow';
        el.style.paddingTop = 'max(0.375rem, env(safe-area-inset-top))';
        el.setAttribute('role', 'status');
        el.innerHTML = 'Нет сети — сообщения будут отправлены при подключении <span id="offline-queue-badge" class="ml-1 opacity-90"></span>';
        document.body.prepend(el);
    }

    async function refreshQueueBadge() {
        const badge = document.getElementById('offline-queue-badge');
        if (!badge) return;
        try {
            const n = await Store.countPending();
            badge.textContent = n > 0 ? '(в очереди: ' + n + ')' : '';
        } catch (_) { badge.textContent = ''; }
    }

    function updateOfflineBanner() {
        ensureOfflineBanner();
        const el = document.getElementById('offline-banner');
        if (!el) return;
        if (navigator.onLine) el.classList.add('hidden');
        else el.classList.remove('hidden');
        refreshQueueBadge();
    }

    async function sendQueuedItem({ chatId, text, clientId }) {
        if (typeof fetchWithAuth !== 'function') return { ok: false, error: 'fetchWithAuth unavailable' };
        const formData = new FormData();
        if (text) formData.append('messageText', text);
        const res = await fetchWithAuth(API_BASE + '/messages/' + chatId, { method: 'POST', body: formData });
        if (!res) return { ok: false, error: 'Нет ответа от сервера' };
        if (res.status === 429) return { ok: false, error: 'Слишком часто' };
        if (!res.ok) {
            const errText = await res.text().catch(() => '');
            return { ok: false, error: errText || ('HTTP ' + res.status) };
        }
        const result = await res.json().catch(() => ({}));
        const serverMessageId = result?.data?.messageId || result?.messageId || result?.data?.id;
        if (serverMessageId && clientId && typeof pendingMessages !== 'undefined' && pendingMessages) {
            try { pendingMessages.set(clientId, { serverMessageId }); } catch (_) {}
        }
        if (clientId && typeof updateMessageStatus === 'function') updateMessageStatus(clientId, 'sent');
        return { ok: true, serverMessageId };
    }

    async function flushOfflineMessages() {
        if (!navigator.onLine) return;
        try {
            const result = await Store.flushQueue(sendQueuedItem);
            if (result.sent > 0 && typeof showToast === 'function') {
                showToast('Отправлено из offline-очереди: ' + result.sent, 'success');
            }
            refreshQueueBadge();
        } catch (e) {
            console.warn('[chats-offline] flush', e);
        }
    }

    function patchSendMessage() {
        if (typeof sendMessage !== 'function') return false;
        if (sendMessage.__offlinePatched) return true;
        const original = sendMessage;

        window.sendMessage = async function () {
            if (!currentChatId || isSending) return;
            if (typeof editingMessageId !== 'undefined' && editingMessageId) {
                if (typeof submitEditMessage === 'function') await submitEditMessage();
                return;
            }

            let text = (messageInput && messageInput.value || '').trim();
            const files = (typeof selectedFiles !== 'undefined' && selectedFiles) ? selectedFiles : [];
            if (!text && files.length === 0) return;

            if (files.length > 0 && !navigator.onLine) {
                if (typeof showToast === 'function') showToast('Вложения можно отправить только при подключении к сети', 'warning');
                return;
            }

            if (typeof replyingTo !== 'undefined' && replyingTo && typeof buildReplyPayload === 'function') {
                text = buildReplyPayload(replyingTo, text);
            }

            if (!navigator.onLine && files.length === 0) {
                isSending = true;
                if (sendButton) sendButton.disabled = true;
                const tempId = 'temp-' + Date.now();
                if (typeof appendOptimisticMessage === 'function') appendOptimisticMessage(text, files, tempId);
                if (typeof updateChatLastMessagePreview === 'function') {
                    updateChatLastMessagePreview(currentChatId, text, files, new Date(), true, 'pending');
                }
                if (typeof bumpChatToTop === 'function') bumpChatToTop(currentChatId);
                try {
                    await Store.enqueueMessage({ chatId: currentChatId, text: text, clientId: tempId });
                    if (typeof clearInputAfterSend === 'function') clearInputAfterSend();
                    if (typeof updateMessageStatus === 'function') updateMessageStatus(tempId, 'pending', 'В очереди');
                    if (typeof showToast === 'function') showToast('Сообщение сохранено. Отправится при появлении сети', 'info');
                    refreshQueueBadge();
                } catch (e) {
                    if (typeof updateMessageStatus === 'function') updateMessageStatus(tempId, 'failed', e.message || 'Ошибка очереди');
                    if (typeof showToast === 'function') showToast('Не удалось сохранить в offline-очередь', 'error');
                } finally {
                    isSending = false;
                    if (sendButton) sendButton.disabled = false;
                }
                return;
            }

            try {
                await original.apply(this, arguments);
            } catch (err) {
                if ((!navigator.onLine || err) && files.length === 0) {
                    try {
                        const tempId = 'temp-' + Date.now();
                        await Store.enqueueMessage({ chatId: currentChatId, text: text, clientId: tempId });
                        if (typeof updateMessageStatus === 'function') updateMessageStatus(tempId, 'pending', 'В очереди');
                        if (typeof showToast === 'function') showToast('Сообщение сохранено offline', 'info');
                        refreshQueueBadge();
                    } catch (_) {}
                }
            }
        };
        window.sendMessage.__offlinePatched = true;

        try {
            if (typeof sendButton !== 'undefined' && sendButton) sendButton.onclick = window.sendMessage;
        } catch (_) {}
        return true;
    }

    function installSendInterceptor() {
        let attempts = 0;
        const t = setInterval(() => {
            attempts++;
            if (patchSendMessage() || attempts > 50) clearInterval(t);
        }, 100);
    }

    async function onChatsLoaded(chatsArray) {
        if (Array.isArray(chatsArray)) {
            try { await Store.saveChatsCache(chatsArray); } catch (_) {}
        }
    }
    async function getCachedChats() {
        try { return await Store.loadChatsCache(); } catch (_) { return null; }
    }
    async function onMessagesLoaded(chatId, messagesArray, meta) {
        if (!chatId || !Array.isArray(messagesArray)) return;
        try { await Store.saveMessagesCache(chatId, messagesArray, meta); } catch (_) {}
    }
    async function getCachedMessages(chatId) {
        try { return await Store.loadMessagesCache(chatId); } catch (_) { return null; }
    }

    window.addEventListener('online', () => { updateOfflineBanner(); flushOfflineMessages(); });
    window.addEventListener('offline', updateOfflineBanner);
    window.addEventListener('offline-store:flush-request', flushOfflineMessages);
    window.addEventListener('offline-queue:flush-request', flushOfflineMessages);
    window.addEventListener('offline-store:sent', (e) => {
        const { clientId } = e.detail || {};
        if (clientId && typeof updateMessageStatus === 'function') updateMessageStatus(clientId, 'sent');
        refreshQueueBadge();
    });
    window.addEventListener('offline-store:failed', (e) => {
        const { clientId, error } = e.detail || {};
        if (clientId && typeof updateMessageStatus === 'function') updateMessageStatus(clientId, 'failed', error || 'Ошибка');
        refreshQueueBadge();
    });
    window.addEventListener('offline-store:enqueued', () => refreshQueueBadge());
    window.addEventListener('offline-store:flushed', () => refreshQueueBadge());

    function init() {
        ensureOfflineBanner();
        updateOfflineBanner();
        installSendInterceptor();
        setTimeout(flushOfflineMessages, 1500);
        setInterval(() => { if (navigator.onLine) flushOfflineMessages(); }, 60000);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();

    window.ChatsOffline = {
        onChatsLoaded, getCachedChats, onMessagesLoaded, getCachedMessages,
        flushOfflineMessages, updateOfflineBanner, sendQueuedItem
    };
})();