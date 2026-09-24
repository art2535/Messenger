function removeMessageFromUI(messageId, chatIdHint) {
    const chatId = chatIdHint || currentChatId;
    withFrozenChatListPosition(chatId, () => {
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
            if (isMessagePinned(chatId, messageId)) {
                unpinMessage(chatId, messageId);
            }
            refreshChatListPreviewFromDom(chatId);
        }
    });
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
            return;
        }
        const last = rows[rows.length - 1];
        const mid = last.dataset.mid;
        const bubble = last.querySelector('.message-bubble');
        const textEl = bubble?.querySelector('p');
        const text = textEl ? (textEl.innerText || textEl.textContent || '').trim() : '';
        const isMine = !!bubble?.classList.contains('outgoing');
        const hasImg = !!bubble?.querySelector('img');
        const hasFile = !!bubble?.querySelector('.file-attachment');
        const attachments = (hasImg || hasFile)
            ? [{ fileType: hasImg ? 'image/png' : 'application/octet-stream' }]
            : [];
        const status = isMine ? getOutgoingStatusFromBubble(bubble) : null;
        updateChatLastMessagePreview(chatId, text, attachments, null, isMine, status);
        chatItem.dataset.lastMessageId = mid;
        chatItem.dataset.lastMessageStatus = status || '';
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
                if (preview) {
                    preview.outerHTML = `<div class="flex items-center min-w-0 gap-1 last-message-preview"><span class="truncate text-gray-400">Нет сообщений</span></div>`;
                }
                chatItem.dataset.lastMessageId = '';
                chatItem.dataset.lastMessageStatus = '';
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
            updateChatLastMessagePreview(
                chatId,
                msg.messageText || msg.MessageText || '',
                msg.attachments || msg.Attachments || [],
                msg.sentAt || msg.SentAt || msg.sendTime || msg.SendTime || null,
                isMine,
                status
            );
            chatItem.dataset.lastMessageId = String(msg.messageId || msg.MessageId || '');
            chatItem.dataset.lastMessageStatus = status || '';
        })
        .catch(e => console.warn('[refreshChatListPreviewFromDom]', e));
}

async function deleteMessage(messageId) {
    if (!messageId || !currentChatId) return;
    try {
        const res = await fetchWithAuth(`${API_BASE}/messages/${messageId}?chatId=${currentChatId}`, {
            method: 'DELETE',
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (!res.ok && res.status !== 204) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.error || err.Error || `HTTP ${res.status}`);
        }
        removeMessageFromUI(messageId, currentChatId);
        showToast('Сообщение удалено', 'success');
    } catch (err) {
        console.error('Ошибка удаления:', err);
        showToast(err.message || 'Не удалось удалить сообщение', 'error');
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
    if (!isOwnMessageRow(row)) {
        showToast('Можно удалять только свои сообщения', 'warning');
        return;
    }
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
document.getElementById('confirm-delete-message')?.addEventListener('click', async () => {
    const modal = document.getElementById('delete-message-modal');
    const id = modal?.dataset?.messageId;
    closeDeleteMessageConfirm();
    if (id) await deleteMessage(id);
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
        // Hide pinned-messages bar (same priority as other overlays)
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



function updateUnreadBadge(chatId, count) {
    const item = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
    if (!item) return;

    const badge = item.querySelector('.unread-badge');
    if (!badge) return;

    if (count > 0) {
        badge.textContent = count > 99 ? '99+' : count;
        badge.classList.remove('hidden');
    } else {
        badge.classList.add('hidden');
        badge.textContent = '';
    }
}

function incrementUnread(chatId) {
    if (!chatId) return;
    const next = (unreadCounts.get(chatId) || 0) + 1;
    unreadCounts.set(chatId, next);
    updateUnreadBadge(chatId, next);
}

function clearUnread(chatId) {
    if (!chatId) return;
    unreadCounts.set(chatId, 0);
    updateUnreadBadge(chatId, 0);
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
        if (sentAt && meta.time) timeEl.textContent = meta.time;
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

    const parsedOpt = parseReplyPayload(text || '');
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