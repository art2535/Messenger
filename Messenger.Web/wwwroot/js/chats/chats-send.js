function removeMessageFromUI(messageId, chatIdHint) {
    const chatId = chatIdHint || currentChatId;
    const el = document.querySelector(`[data-mid="${messageId}"]`);
    if (el) {
        const prev = el.previousElementSibling;
        el.remove();
        if (prev && prev.classList.contains('date-separator')) {
            const next = prev.nextElementSibling;
            const nextIsMsg = next && next.dataset && next.dataset.mid;
            if (!nextIsMsg) prev.remove();
        }
        messagesContainer.querySelectorAll('.date-separator').forEach(sep => {
            const n = sep.nextElementSibling;
            if (!n || n.classList.contains('date-separator') || !(n.dataset && n.dataset.mid)) {
                sep.remove();
            }
        });
        const seps = messagesContainer.querySelectorAll('.date-separator');
        if (typeof lastRenderedDateKey !== 'undefined') {
            lastRenderedDateKey = seps.length
                ? (seps[seps.length - 1].dataset.dateKey || null)
                : null;
        }
    }
    if (editingMessageId && String(editingMessageId) === String(messageId)) {
        exitEditMode();
    }
    try {
        const seen = window.__seenMessageIds;
        if (seen && messageId) seen.delete(String(messageId));
    } catch (_) {}
    if (chatId) {
        if (typeof isMessagePinned === 'function' && isMessagePinned(chatId, messageId)) {
            unpinMessage(chatId, messageId);
        }
        if (typeof refreshChatListPreviewFromDom === 'function') {
            refreshChatListPreviewFromDom(chatId);
        }
        if (typeof reorderUnpinnedChatsByLastActivity === 'function') {
            reorderUnpinnedChatsByLastActivity();
        }
    }
}

function getOutgoingStatusFromBubble(bubble) {
    if (!bubble) return 'sent';
    const statusEl = bubble.querySelector('.msg-status');
    if (!statusEl) return 'sent';
    if (statusEl.classList.contains('read')) return 'read';
    if (statusEl.classList.contains('pending')) return 'pending';
    if (statusEl.classList.contains('sent')) return 'sent';
    const checks = statusEl.querySelectorAll('i[data-feather="check"], svg.feather-check');
    if (checks.length >= 2) return 'read';
    if (checks.length === 1) return 'sent';
    return 'sent';
}

function normalizeMessageStatus(raw) {
    const s = String(raw || '').toLowerCase();
    if (s === 'read' || s === 'seen') return 'read';
    if (s === 'pending' || s === 'sending') return 'pending';
    if (s === 'delivered' || s === 'sent') return 'sent';
    return s || 'sent';
}

function refreshChatListPreviewFromDom(chatId) {
    if (!chatId) return;
    const chatItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
    if (!chatItem) return;

    if (String(currentChatId) === String(chatId)) {
        const rows = [...messagesContainer.querySelectorAll('[data-mid]:not([data-mid^="temp-"])')];
        if (!rows.length) {
            const preview = chatItem.querySelector('.last-message-preview');
            const timeEl = chatItem.querySelector('.chat-list-time');
            if (preview) {
                preview.outerHTML = `<div class="flex items-center min-w-0 gap-1 last-message-preview"><span class="truncate text-gray-400">Нет сообщений</span></div>`;
            }
            if (timeEl) timeEl.textContent = '';
            chatItem.dataset.lastMessageId = '';
            chatItem.dataset.lastMessageStatus = '';
            chatItem.dataset.lastMessageAt = '';
            return;
        }
        const last = rows[rows.length - 1];
        const mid = last.dataset.mid;
        const bubble = last.querySelector('.message-bubble');
        const textEl = bubble?.querySelector('p');
        let text = textEl ? (textEl.innerText || textEl.textContent || '').trim() : '';
        if (typeof stripMessageMetaForPreview === 'function') {
            text = stripMessageMetaForPreview(text);
        } else if (typeof stripReplyForPreview === 'function') {
            text = stripReplyForPreview(text);
        }
        const isMine = !!bubble?.classList.contains('outgoing');
        const hasImg = !!bubble?.querySelector('img.image-preview, img[src]');
        const hasFile = !!bubble?.querySelector('.file-attachment');
        const attachments = (hasImg || hasFile)
            ? [{ fileType: hasImg ? 'image/png' : 'application/octet-stream' }]
            : [];
        if (!text && (hasImg || hasFile)) text = '';
        const status = isMine ? getOutgoingStatusFromBubble(bubble) : null;
        const sentAt = last.dataset.sentAt || last.getAttribute('data-sent-at') || null;
        updateChatLastMessagePreview(chatId, text, attachments, sentAt, isMine, status);
        chatItem.dataset.lastMessageId = mid;
        chatItem.dataset.lastMessageStatus = status || '';
        if (sentAt) chatItem.dataset.lastMessageAt = sentAt;
        return;
    }

    fetchWithAuth(`${API_BASE}/messages/${chatId}?limit=1`)
        .then(res => res && res.ok ? res.json() : null)
        .then(json => {
            if (!json) return;
            const messages = json.data || json.Data || [];
            const list = Array.isArray(messages) ? messages : [];
            if (!list.length) {
                const preview = chatItem.querySelector('.last-message-preview');
                const timeEl = chatItem.querySelector('.chat-list-time');
                if (preview) {
                    preview.outerHTML = `<div class="flex items-center min-w-0 gap-1 last-message-preview"><span class="truncate text-gray-400">Нет сообщений</span></div>`;
                }
                if (timeEl) timeEl.textContent = '';
                chatItem.dataset.lastMessageId = '';
                chatItem.dataset.lastMessageStatus = '';
                chatItem.dataset.lastMessageAt = '';
                return;
            }
            let msg = list[list.length - 1];
            if (list.length > 1) {
                msg = list.reduce((a, b) => {
                    const sa = a.sequenceNumber ?? a.SequenceNumber ?? 0;
                    const sb = b.sequenceNumber ?? b.SequenceNumber ?? 0;
                    if (sa || sb) return sa >= sb ? a : b;
                    const ta = new Date(a.sentAt || a.SentAt || a.sendTime || a.SendTime || 0).getTime();
                    const tb = new Date(b.sentAt || b.SentAt || b.sendTime || b.SendTime || 0).getTime();
                    return ta >= tb ? a : b;
                });
            }
            const isMine = String(msg.senderId || msg.SenderId) === String(me);
            const status = isMine
                ? normalizeMessageStatus(
                    msg.status || msg.Status || msg.deliveryStatus || msg.DeliveryStatus ||
                    msg.messageStatus || msg.MessageStatus || 'sent'
                )
                : null;
            const sentAt = msg.sentAt || msg.SentAt || msg.sendTime || msg.SendTime || null;
            updateChatLastMessagePreview(
                chatId,
                msg.messageText || msg.MessageText || '',
                msg.attachments || msg.Attachments || [],
                sentAt,
                isMine,
                status
            );
            chatItem.dataset.lastMessageId = String(msg.messageId || msg.MessageId || '');
            chatItem.dataset.lastMessageStatus = status || '';
            if (sentAt) {
                try {
                    const d = new Date(sentAt);
                    if (!isNaN(d.getTime())) chatItem.dataset.lastMessageAt = d.toISOString();
                } catch (_) {}
            }
            if (typeof reorderUnpinnedChatsByLastActivity === 'function') {
                reorderUnpinnedChatsByLastActivity();
            }
        })
        .catch(e => console.warn('[refreshChatListPreviewFromDom]', e));
}

function getHiddenMessageIds() {
    try {
        const key = 'guap_hidden_msgs_' + encodeURIComponent(String(me || 'anon'));
        const raw = localStorage.getItem(key);
        const arr = raw ? JSON.parse(raw) : [];
        return new Set(Array.isArray(arr) ? arr.map(String) : []);
    } catch {
        return new Set();
    }
}

function persistHiddenMessageIds(set) {
    try {
        const key = 'guap_hidden_msgs_' + encodeURIComponent(String(me || 'anon'));
        localStorage.setItem(key, JSON.stringify([...set]));
    } catch (e) {
        console.warn('[hidden] save failed', e);
    }
}

function hideMessageForMe(messageId) {
    const set = getHiddenMessageIds();
    set.add(String(messageId));
    persistHiddenMessageIds(set);
}

function isMessageHiddenForMe(messageId) {
    return getHiddenMessageIds().has(String(messageId));
}

async function deleteMessage(messageId, scope = 'everyone', opts = {}) {
    if (!messageId || !currentChatId) return false;
    const silent = !!opts.silent;
    try {
        if (scope === 'me') {
            hideMessageForMe(messageId);
            removeMessageFromUI(messageId, currentChatId);
            if (!silent) showToast('Сообщение удалено только у вас', 'success');
            return true;
        }

        const res = await fetchWithAuth(`${API_BASE}/messages/${messageId}?chatId=${currentChatId}`, {
            method: 'DELETE',
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (!res.ok && res.status !== 204) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.error || err.Error || `HTTP ${res.status}`);
        }
        removeMessageFromUI(messageId, currentChatId);
        if (!silent) showToast('Сообщение удалено для всех', 'success');
        return true;
    } catch (err) {
        console.error('Ошибка удаления:', err);
        if (!silent) showToast(err.message || 'Не удалось удалить сообщение', 'error');
        return false;
    }
}

async function deleteMessagesBatch(messageIds, scope = 'everyone') {
    const ids = (messageIds || []).map(String).filter(id => id && !id.startsWith('temp-'));
    if (!ids.length || !currentChatId) return;

    const chatId = currentChatId;
    const run = async () => {
        let ok = 0;
        let fail = 0;
        let lastError = '';

        if (scope === 'me') {
            for (const mid of ids) {
                hideMessageForMe(mid);
                removeMessageFromUI(mid, chatId);
                ok++;
            }
        } else if (ids.length > 1) {
            try {
                const res = await fetchWithAuth(`${API_BASE}/messages/bulk-delete`, {
                    method: 'POST',
                    headers: {
                        'Authorization': `Bearer ${token}`,
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({ chatId, messageIds: ids })
                });
                if (!res) throw new Error('Нет ответа от сервера');
                if (!res.ok) {
                    if (res.status === 404 || res.status === 405 || res.status === 403) {
                        for (const mid of ids) {
                            const one = await deleteMessage(mid, 'everyone', { silent: true });
                            if (one) ok++; else fail++;
                        }
                    } else {
                        const err = await res.json().catch(() => ({}));
                        throw new Error(err.error || err.Error || `HTTP ${res.status}`);
                    }
                } else {
                    const data = await res.json().catch(() => ({}));
                    const deletedIds = data.messageIds || data.MessageIds || ids;
                    (deletedIds || []).forEach(mid => {
                        removeMessageFromUI(mid, chatId);
                        ok++;
                    });
                    const deletedSet = new Set((deletedIds || []).map(String));
                    for (const mid of ids) {
                        if (!deletedSet.has(String(mid))) {
                            const one = await deleteMessage(mid, 'everyone', { silent: true });
                            if (one) ok++; else fail++;
                        }
                    }
                }
            } catch (err) {
                lastError = err.message || String(err);
                for (const mid of ids) {
                    const one = await deleteMessage(mid, 'everyone', { silent: true });
                    if (one) ok++; else fail++;
                }
            }
        } else {
            const one = await deleteMessage(ids[0], 'everyone', { silent: true });
            if (one) ok++; else fail++;
        }

        if (typeof exitSelectMode === 'function') exitSelectMode();

        if (typeof refreshChatListPreviewFromDom === 'function') {
            refreshChatListPreviewFromDom(chatId);
        }
        if (typeof reorderUnpinnedChatsByLastActivity === 'function') {
            reorderUnpinnedChatsByLastActivity();
        }

        if (ok && !fail) {
            const msg = scope === 'me'
                ? (ok === 1 ? 'Сообщение удалено только у вас' : `Удалено у вас: ${ok}`)
                : (ok === 1 ? 'Сообщение удалено для всех' : `Удалено для всех: ${ok}`);
            showToast(msg, 'success');
        } else if (ok && fail) {
            showToast(`Удалено: ${ok}, ошибок: ${fail}`, 'warning');
        } else {
            showToast(lastError || 'Не удалось удалить сообщения', 'error');
        }
    };

    const prevSuppress = typeof __suppressChatBump !== 'undefined' ? __suppressChatBump : false;
    if (typeof __suppressChatBump !== 'undefined') __suppressChatBump = true;
    try {
        await run();
    } finally {
        setTimeout(() => {
            if (typeof __suppressChatBump !== 'undefined') __suppressChatBump = prevSuppress;
            if (typeof reorderUnpinnedChatsByLastActivity === 'function') {
                reorderUnpinnedChatsByLastActivity();
            }
        }, 400);
    }
}

async function sendMessage() {
    if (!currentChatId || isSending) return;

    if (editingMessageId) {
        await submitEditMessage();
        return;
    }

    let text = messageInput.value.trim();
    if (!text && selectedFiles.length === 0) return;

    if (replyingTo) {
        text = buildReplyPayload(replyingTo, text);
    }

    isSending = true;
    sendButton.disabled = true;

    const tempId = 'temp-' + Date.now();

    appendOptimisticMessage(text, selectedFiles, tempId);
    updateChatLastMessagePreview(currentChatId, text, selectedFiles, new Date(), true, 'pending');
    if (typeof bumpChatToTop === 'function') bumpChatToTop(currentChatId);

    try {
        const formData = new FormData();
        if (text) formData.append('messageText', text);
        selectedFiles.forEach(file => formData.append('files', file));

        const res = await fetchWithAuth(`${API_BASE}/messages/${currentChatId}`, {
            method: 'POST',
            body: formData
        });

        if (!res) throw new Error('Нет ответа от сервера');

        if (isRateLimited(res)) {
            updateMessageStatus(tempId, 'failed', 'Слишком часто');
            return;
        }

        if (!res.ok) throw new Error(`HTTP ${res.status}`);

        const result = await res.json();
        const serverMessageId = result?.data?.messageId || result?.messageId;

        if (serverMessageId) {
            pendingMessages.set(tempId, { serverMessageId });
        }

        clearInputAfterSend();

    } catch (err) {
        console.error('Ошибка отправки:', err);
        updateMessageStatus(tempId, 'failed', err.message || 'Ошибка');
        showToast('Не удалось отправить сообщение', 'error');
    } finally {
        isSending = false;
        sendButton.disabled = false;
    }
}

function clearInputAfterSend() {
    const sentChatId = currentChatId;
    clearTimeout(draftSaveTimer);
    draftSaveTimer = null;
    if (sentChatId) clearChatDraft(sentChatId);
    messageInput.value = '';
    attachedFiles.innerHTML = '';
    selectedFiles = [];
    fileInput.value = '';
    if (typeof cancelReply === 'function') cancelReply();
}

document.getElementById('ctx-edit-btn')?.addEventListener('click', () => {
    if (!contextMenuMessageId) return;
    const id = contextMenuMessageId;
    const txt = contextMenuMessageText;
    hideMessageContextMenu();
    enterEditMode(id, txt);
});

document.getElementById('ctx-select-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    const id = contextMenuMessageId;
    hideMessageContextMenu();
    if (id) enterSelectMode(id);
});

document.getElementById('sel-cancel-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    exitSelectMode();
});

document.getElementById('sel-delete-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    deleteSelectedMessages();
});

document.getElementById('messages-container')?.addEventListener('click', (e) => {
    if (!selectMode) return;
    const row = e.target.closest('.message-row[data-mid]');
    if (!row) return;
    e.preventDefault();
    e.stopPropagation();
    const mid = row.dataset.mid;
    if (mid && !String(mid).startsWith('temp-')) {
        toggleMessageSelection(mid);
    }
}, true);


document.getElementById('ctx-delete-btn')?.addEventListener('click', () => {
    if (!contextMenuMessageId) return;
    const id = contextMenuMessageId;
    hideMessageContextMenu();
    openDeleteMessageConfirm(id);
});
document.getElementById('cancel-edit-btn')?.addEventListener('click', () => exitEditMode());
document.getElementById('cancel-delete-message')?.addEventListener('click', () => closeDeleteMessageConfirm());
document.getElementById('confirm-delete-for-me')?.addEventListener('click', async () => {
    const modal = document.getElementById('delete-message-modal');
    const id = modal?.dataset?.messageId;
    const ids = modal?.dataset?.messageIds ? JSON.parse(modal.dataset.messageIds) : (id ? [id] : []);
    closeDeleteMessageConfirm();
    await deleteMessagesBatch(ids, 'me');
});

document.getElementById('confirm-delete-for-everyone')?.addEventListener('click', async () => {
    const modal = document.getElementById('delete-message-modal');
    const id = modal?.dataset?.messageId;
    const ids = modal?.dataset?.messageIds ? JSON.parse(modal.dataset.messageIds) : (id ? [id] : []);
    closeDeleteMessageConfirm();
    await deleteMessagesBatch(ids, 'everyone');
});

document.getElementById('confirm-delete-message')?.addEventListener('click', async () => {
    const modal = document.getElementById('delete-message-modal');
    const id = modal?.dataset?.messageId;
    closeDeleteMessageConfirm();
    if (id) await deleteMessagesBatch([id], 'everyone');
});
document.addEventListener('click', (e) => {
    const msgMenu = document.getElementById('message-context-menu');
    const chatMenu = document.getElementById('chat-context-menu');
    if (msgMenu && !msgMenu.contains(e.target)) hideMessageContextMenu();
    if (chatMenu && !chatMenu.contains(e.target)) hideChatContextMenu();
});
document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
        hideMessageContextMenu();
        hideChatContextMenu();
        if (selectMode) exitSelectMode();
        if (editingMessageId) exitEditMode();
        if (replyingTo) cancelReply();
        closeDeleteMessageConfirm();
        closeExportChatModal();
        document.getElementById('delete-chat-confirm-modal')?.classList.remove('show');
        const pinBar = document.getElementById('pinned-message-bar');
        if (pinBar && pinBar.classList.contains('show')) {
            if (typeof hidePinnedMessageBar === 'function') hidePinnedMessageBar();
            else pinBar.classList.remove('show');
        }
    }
});
window.addEventListener('scroll', () => {
    hideMessageContextMenu();
    hideChatContextMenu();
}, true);

function normChatId(chatId) {
    if (chatId == null || chatId === '') return null;
    return String(chatId).trim().toLowerCase();
}

const unreadMessageIds = window.__unreadMessageIds || (window.__unreadMessageIds = new Map());

function trackUnreadMessage(chatId, messageId) {
    const cid = normChatId(chatId);
    const mid = messageId != null ? String(messageId) : null;
    if (!cid || !mid) return;
    if (!unreadMessageIds.has(cid)) unreadMessageIds.set(cid, new Set());
    unreadMessageIds.get(cid).add(mid);
}

function untrackUnreadMessage(chatId, messageId) {
    const cid = normChatId(chatId);
    const mid = messageId != null ? String(messageId) : null;
    if (!cid || !mid) return false;
    const set = unreadMessageIds.get(cid);
    if (!set || !set.has(mid)) return false;
    set.delete(mid);
    return true;
}

function updateUnreadBadge(chatId, count) {
    const cid = normChatId(chatId);
    if (!cid) return;

    let item = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
    if (!item) {
        item = [...document.querySelectorAll('.chat-item')].find(
            el => normChatId(el.dataset.chatId) === cid
        );
    }
    if (!item) return;

    const badge = item.querySelector('.unread-badge');
    if (!badge) return;

    const n = Math.max(0, Number(count) || 0);
    if (n > 0) {
        badge.textContent = n > 99 ? '99+' : String(n);
        badge.classList.remove('hidden');
    } else {
        badge.classList.add('hidden');
        badge.textContent = '';
    }
}

function getUnreadCount(chatId) {
    const cid = normChatId(chatId);
    if (!cid) return 0;
    if (unreadCounts.has(cid)) return unreadCounts.get(cid) || 0;
    for (const [k, v] of unreadCounts) {
        if (normChatId(k) === cid) return v || 0;
    }
    return 0;
}

function setUnreadCount(chatId, count) {
    const cid = normChatId(chatId);
    if (!cid) return;
    const n = Math.max(0, Number(count) || 0);
    unreadCounts.set(cid, n);
    updateUnreadBadge(cid, n);
}

function incrementUnread(chatId, messageId) {
    const cid = normChatId(chatId);
    if (!cid) return;
    if (messageId != null) trackUnreadMessage(cid, messageId);
    const next = getUnreadCount(cid) + 1;
    setUnreadCount(cid, next);
}

function decrementUnread(chatId, by = 1, messageId = null) {
    const cid = normChatId(chatId);
    if (!cid) return;

    let removed = false;
    if (messageId != null) {
        removed = untrackUnreadMessage(cid, messageId);
    }

    const set = unreadMessageIds.get(cid);
    if (set && set.size >= 0 && messageId != null) {
        setUnreadCount(cid, set.size);
        return;
    }

    const cur = getUnreadCount(cid);
    if (cur <= 0) {
        setUnreadCount(cid, 0);
        return;
    }
    const next = Math.max(0, cur - (Number(by) || 1));
    setUnreadCount(cid, next);
    if (next === 0 && set) set.clear();
}

function clearUnread(chatId) {
    const cid = normChatId(chatId);
    if (!cid) return;
    setUnreadCount(cid, 0);
    if (unreadMessageIds.has(cid)) unreadMessageIds.get(cid).clear();
}

function renderChatListStatus(status) {
    const s = String(status || 'sent').toLowerCase();
    if (s === 'read') return `<span class="chat-list-status read" title="Прочитано"><i data-feather="check" class="w-3.5 h-3.5"></i><i data-feather="check" class="w-3.5 h-3.5 -ml-2"></i></span>`;
    if (s === 'pending' || s === 'sending') return `<span class="chat-list-status pending" title="Отправляется"><i data-feather="clock" class="w-3 h-3"></i></span>`;
    return `<span class="chat-list-status sent" title="Доставлено"><i data-feather="check" class="w-3.5 h-3.5"></i></span>`;
}

function renderChatListMessageMeta(messageText, attachments = [], sentAt = null, isMyMessage = false, status = null) {
    let previewHTML = '';
    if (messageText && String(messageText).trim() !== '') {
        const text = String(messageText).trim();
        previewHTML = text.length > 45 ? text.substring(0, 42) + '...' : text;
    } else if (attachments && attachments.length > 0) {
        const imageCount = attachments.filter(a => a.fileType?.startsWith('image/')).length;
        previewHTML = imageCount > 0
            ? `<i data-feather="image" class="w-4 h-4"></i> Фото${imageCount > 1 ? ` (${imageCount})` : ''}`
            : `<i data-feather="paperclip" class="w-4 h-4"></i> Вложение`;
    } else {
        previewHTML = 'Новое сообщение';
    }
    if (attachments && attachments.length > 0 && messageText && String(messageText).trim() !== '') {
        const text = String(messageText).trim();
        const shortText = text.length > 35 ? text.substring(0, 32) + '...' : text;
        previewHTML = `<i data-feather="paperclip" class="w-4 h-4"></i> Вложение: ${shortText}`;
    }
    return {
        previewHTML: `<div class="flex items-center min-w-0 gap-1 last-message-preview">${isMyMessage ? renderChatListStatus(status || 'sent') : ''}<span class="truncate">${previewHTML}</span></div>`,
        time: sentAt ? formatChatListTime(sentAt) : ''
    };
}

function updateChatLastMessagePreview(chatId, messageText, attachments = [], sentAt = null, isMyMessage = false, status = null) {
    if (typeof stripReplyForPreview === 'function') messageText = stripReplyForPreview(messageText);
    const chatItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
    if (!chatItem) return;
    let lastMessageEl = chatItem.querySelector('.last-message-preview');
    const timeEl = chatItem.querySelector('.chat-list-time');
    const meta = renderChatListMessageMeta(messageText, attachments, sentAt, isMyMessage, status);
    if (lastMessageEl) {
        lastMessageEl.outerHTML = meta.previewHTML;
    } else {
        const metaWrap = chatItem.querySelector('.chat-list-message-meta') || chatItem.querySelector('.text-sm.text-gray-500');
        if (metaWrap) {
            metaWrap.innerHTML = meta.previewHTML;
        }
    }
    if (timeEl) {
        if (sentAt === '' || sentAt === false) {
            timeEl.textContent = '';
        } else if (sentAt && meta.time) {
            timeEl.textContent = meta.time;
        }
    }
    if (sentAt === '' || sentAt === false || sentAt == null) {
        if (sentAt === '' || sentAt === false) chatItem.dataset.lastMessageAt = '';
    } else {
        try {
            const d = new Date(sentAt);
            if (!isNaN(d.getTime())) chatItem.dataset.lastMessageAt = d.toISOString();
        } catch (_) {
            chatItem.dataset.lastMessageAt = String(sentAt);
        }
    }
    if (typeof feather !== 'undefined') feather.replace();
    renderChatListDraft(chatId);
}

function updateChatListStatus(chatId, status) {
    const chatItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
    if (!chatItem) return;
    const statusEl = chatItem.querySelector('.chat-list-status');
    if (!statusEl) return;
    statusEl.outerHTML = renderChatListStatus(status);
    feather.replace();
}

let messageStatusMap = new Map();

function appendOptimisticMessage(text, files, tempId) {
    removeEmptyStateIfNeeded();
    appendDateSeparatorIfNeeded(new Date());

    const div = document.createElement('div');
    div.className = `message-row outgoing-row`;
    div.dataset.mid = tempId;
    div.dataset.sentAt = new Date().toISOString();

    const parsedOpt = (typeof parseForwardPayload === 'function')
        ? parseForwardPayload(text || '')
        : parseReplyPayload(text || '');
    const bodyText = parsedOpt.text || '';
    const replyMetaOpt = parsedOpt.reply;

    let content = '';
    if (replyMetaOpt) {
        content += `<div class="reply-quote" data-reply-to="${replyMetaOpt.messageId || ''}"><div class="reply-quote-name"></div><div class="reply-quote-text"></div></div>`;
    }
    if (bodyText) {
        content += `<p class="break-words whitespace-pre-wrap leading-relaxed text-[15px]">${bodyText.replace(/\n/g, '<br>')}</p>`;
    }

    if (files && files.length > 0) {
        const previews = files.map(file => {
            if (file.type.startsWith('image/')) {
                const url = URL.createObjectURL(file);
                return `<img src="${url}" class="w-20 h-20 object-cover rounded-lg mt-1" style="max-width:180px;">`;
            } else {
                return `
                    <div class="file-attachment bg-blue-500/30 mt-1 text-blue-100">
                        <i data-feather="file" class="w-4 h-4"></i>
                        <span class="truncate max-w-[140px]">${file.name}</span>
                    </div>
                `;
            }
        }).join('');

        content += `<div class="flex flex-wrap gap-1.5 mt-1">${previews}</div>`;
    }

    if (!content) {
        content = `<div class="opacity-70 text-sm">Отправка ${files.length} вложений...</div>`;
    }

    const isGroupOpt = currentChatInfo?.type === 'group';
    const groupedOpt = isSameSenderAsPrevious(me);
    const avatarOpt = isGroupOpt
        ? `<img class="msg-avatar" src="${getSenderAvatarUrl(me)}" alt="" loading="lazy" onerror="this.src='${DEFAULT_MSG_AVATAR}'">`
        : '';
    if (isGroupOpt) {
        div.className = `message-row outgoing-row${groupedOpt ? ' grouped' : ''}`;
        div.dataset.senderId = String(me || '');
    }
    div.innerHTML = `
        <div class="message-col">
            <div class="message-bubble outgoing px-3.5 py-2 relative">
                ${content}
                <div id="status-${tempId}" class="message-status flex items-center gap-1.5 text-[11px] mt-1.5 text-blue-100 opacity-80 justify-end">
                    <i data-feather="loader" class="w-3 h-3 animate-spin"></i>
                    <span>Отправляется...</span>
                </div>
            </div>
        </div>
        ${avatarOpt}
    `;

    messagesContainer.appendChild(div);
    if (replyMetaOpt) fillReplyQuoteInRow(div, replyMetaOpt);
    feather.replace();
    scrollToBottom();

    setTimeout(() => {
        const stillExists = document.querySelector(`[data-mid="${tempId}"]`);
        if (stillExists) {
            const statusEl = stillExists.querySelector('.message-status');
            if (statusEl && statusEl.textContent.includes('Отправляется')) {
                updateMessageStatus(tempId, 'failed', 'Таймаут подтверждения от сервера');
            }
        }
    }, 15000);
}

function removeEmptyStateIfNeeded() {
    const emptyPlaceholder = messagesContainer.querySelector('.flex-1.flex.flex-col.items-center.justify-center.text-gray-400');

    if (emptyPlaceholder) {
        emptyPlaceholder.remove();
    }
}

function updateMessageStatus(id, status, reason = null) {
    let el = document.querySelector(`[data-mid="${id}"]`);
    if (!el) return;

    const container = el.querySelector('.message-status');
    if (!container) return;

    if (status === 'sent') {
        container.innerHTML = `<span class="text-blue-200">${formatTimeOnly(new Date())}</span>`;
    } else if (status === 'failed') {
        container.className = 'message-status flex items-center gap-1.5 text-xs mt-1.5 text-red-400 font-medium';
        container.innerHTML = `<i data-feather="alert-triangle" class="w-4 h-4"></i><span>${reason || 'Ошибка'}</span>`;
    }
    feather.replace();
}

function renderAttachedPreviews() {
    attachedFiles.innerHTML = '';

    selectedFiles.forEach((file, index) => {
        const wrapper = document.createElement('div');
        wrapper.className = 'preview-attachment relative inline-block';

        const removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.innerHTML = '×';
        removeBtn.className = 'absolute -top-2 -right-2 w-7 h-7 bg-red-500 hover:bg-red-600 text-white rounded-full flex items-center justify-center text-lg font-bold shadow-lg z-10 transition';
        removeBtn.onclick = (e) => {
            e.stopPropagation();
            selectedFiles.splice(index, 1);
            renderAttachedPreviews();
        };

        if (file.type.startsWith('image/')) {
            const img = document.createElement('img');
            img.src = URL.createObjectURL(file);
            img.className = 'w-24 h-24 object-cover rounded-xl border-2 border-gray-200 shadow-md';
            img.onload = () => URL.revokeObjectURL(img.src);
            wrapper.appendChild(img);
        } else {
            const fileDiv = document.createElement('div');
            fileDiv.className = 'w-24 h-24 bg-gray-100 border-2 border-dashed border-gray-300 rounded-xl flex flex-col items-center justify-center text-gray-600 p-1';
            fileDiv.innerHTML = `
                <i data-feather="file-text" class="w-8 h-8 mb-1"></i>
                <span class="text-xs text-center truncate w-full px-1">${file.name.length > 14 ? file.name.slice(0, 11) + '...' : file.name}</span>
            `;
            wrapper.appendChild(fileDiv);
            feather.replace();
        }

        wrapper.appendChild(removeBtn);
        attachedFiles.appendChild(wrapper);
    });
}

function updateConnectionStatus(text, color) {
    document.getElementById('status-dot').className = `w-3 h-3 rounded-full ${color === 'green' ? 'bg-green-500' : color === 'orange' ? 'bg-orange-500' : 'bg-red-500'}`;
    const tooltip = document.getElementById('status-tooltip');
    if (tooltip) tooltip.textContent = text;
}

let pendingStatusUpdate = null;