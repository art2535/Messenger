function hideMessageContextMenu() {
    const menu = document.getElementById('message-context-menu');
    if (menu) menu.classList.remove('show');
    contextMenuMessageId = null;
    contextMenuMessageText = '';
    const chatMenu = document.getElementById('chat-context-menu');
    if (!chatMenu || !chatMenu.classList.contains('show')) hideContextBackdrop();
}



let exportModalChatId = null;

function openExportChatModal(chatId) {
    if (!chatId) {
        showToast('Чат не выбран', 'warning');
        return;
    }
    exportModalChatId = chatId;
    hideChatContextMenu();
    const modal = document.getElementById('export-chat-modal');
    const status = document.getElementById('export-status');
    if (status) {
        status.classList.add('hidden');
        status.textContent = '';
    }
    modal?.classList.add('show');
    if (typeof feather !== 'undefined') feather.replace();
}

function closeExportChatModal() {
    document.getElementById('export-chat-modal')?.classList.remove('show');
    exportModalChatId = null;
}

async function exportChat(chatId, format) {
    const id = chatId || exportModalChatId || currentChatId;
    if (!id) {
        showToast('Чат не выбран', 'warning');
        return;
    }
    format = (format || 'txt').toLowerCase();
    if (!['txt', 'json', 'html', 'csv'].includes(format)) format = 'txt';

    const status = document.getElementById('export-status');
    if (status) {
        status.classList.remove('hidden');
        status.textContent = 'Подготовка файла…';
    }
    showToast('Экспорт чата…', 'info');

    try {
        const url = `${API_BASE}/messages/${id}/export?format=${encodeURIComponent(format)}`;
        const res = await fetch(url, {
            headers: { 'Authorization': `Bearer ${token}` }
        });

        if (res.status === 401 || res.status === 403) {
            window.location.href = "/Authorization/Authorization";
            return;
        }
        if (!res.ok) {
            let errMsg = 'Не удалось экспортировать чат';
            try {
                const body = await res.json();
                errMsg = body?.error || errMsg;
            } catch {}
            throw new Error(errMsg);
        }

        const blob = await res.blob();
        let fileName = `chat-export.${format}`;
        const cd = res.headers.get('Content-Disposition');
        if (cd) {
            const m = /filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/.exec(cd);
            if (m && m[1]) fileName = m[1].replace(/['"]/g, '');
            const m2 = /filename\*=UTF-8''([^;]+)/i.exec(cd);
            if (m2 && m2[1]) fileName = decodeURIComponent(m2[1]);
        }

        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(a.href), 2000);

        showToast(`Чат экспортирован (${format.toUpperCase()})`, 'success');
        if (status) status.textContent = 'Готово!';
        setTimeout(closeExportChatModal, 600);
    } catch (err) {
        console.error('Export error:', err);
        showToast(err.message || 'Ошибка экспорта', 'error');
        if (status) {
            status.textContent = err.message || 'Ошибка';
        }
    }
}


        let contextMenuChatId = null;

function hideChatContextMenu() {
    const menu = document.getElementById('chat-context-menu');
    if (menu) menu.classList.remove('show');
    contextMenuChatId = null;
    const msgMenu = document.getElementById('message-context-menu');
    if (!msgMenu || !msgMenu.classList.contains('show')) hideContextBackdrop();
}


function positionContextMenu(menu, x, y) {
    const backdrop = document.getElementById('ctx-menu-backdrop');
    const isMobile = window.matchMedia('(max-width: 767.98px)').matches;

    menu.classList.add('show');
    if (backdrop) {
        backdrop.classList.remove('hidden');
        backdrop.onclick = () => {
            hideMessageContextMenu();
            hideChatContextMenu();
        };
    }

    if (isMobile) {
        menu.style.left = '50%';
        menu.style.top = '50%';
        menu.style.right = 'auto';
        menu.style.bottom = 'auto';
        menu.style.transform = 'translate(-50%, -50%)';
        return;
    }

    menu.style.transform = 'none';
    menu.style.visibility = 'hidden';
    const pad = 8;
    const rect = menu.getBoundingClientRect();
    const mw = rect.width || 180;
    const mh = rect.height || 120;
    let left = typeof x === 'number' ? x : pad;
    let top = typeof y === 'number' ? y : pad;

    if (left + mw > window.innerWidth - pad) {
        left = window.innerWidth - mw - pad;
    }
    if (top + mh > window.innerHeight - pad) {
        top = y - mh;
    }
    if (top < pad) top = pad;
    if (left < pad) left = pad;
    if (mh > window.innerHeight - pad * 2) {
        top = pad;
    }

    menu.style.left = left + 'px';
    menu.style.top = top + 'px';
    menu.style.right = 'auto';
    menu.style.bottom = 'auto';
    menu.style.visibility = 'visible';
}

function hideContextBackdrop() {
    const backdrop = document.getElementById('ctx-menu-backdrop');
    if (backdrop) {
        backdrop.classList.add('hidden');
        backdrop.onclick = null;
    }
}

function showChatContextMenu(x, y, chatId) {
    const menu = document.getElementById('chat-context-menu');
    if (!menu) {
        console.warn('[chat-context-menu] not found in DOM');
        return;
    }
    hideMessageContextMenu();
    contextMenuChatId = chatId;
    const pinLabel = document.getElementById('chat-ctx-pin-label');
    if (pinLabel) pinLabel.textContent = isChatPinned(chatId) ? 'Открепить' : 'Закрепить';
    positionContextMenu(menu, x, y);
}

async function deleteChatById(chatId) {
    if (!chatId) return;
    try {
        const res = await fetch(`${API_BASE}/chats/${chatId}`, {
            method: 'DELETE',
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (!res.ok) throw new Error('Не удалось удалить чат');

        document.querySelector(`.chat-item[data-chat-id="${chatId}"]`)?.remove();
        toggleEmptyState?.();
        cacheChatItems?.();
        filterChats?.();

        if (String(currentChatId) === String(chatId)) {
            currentChatId = null;
            currentChatInfo = null;
            messagesContainer.innerHTML = `
                <div class="flex flex-col items-center justify-center h-full text-gray-500">
                    <p>Выберите чат для просмотра сообщений</p>
                </div>`;
            const title = document.getElementById('chat-title');
            if (title) title.textContent = 'Выберите чат';
            const subtitle = document.getElementById('chat-subtitle');
            if (subtitle) subtitle.textContent = 'Нажмите, чтобы посмотреть информацию';
            if (typeof updateChatSubtitleMarquee === 'function') updateChatSubtitleMarquee();
            document.getElementById('chat-input-area')?.classList.add('hidden');
            document.getElementById('empty-right-panel')?.classList.remove('hidden');
            document.getElementById('right-panel')?.classList.add('hidden');
        }
        showToast('Чат удалён', 'success');
    } catch (err) {
        console.error(err);
        showToast(err.message || 'Не удалось удалить чат', 'error');
    }
}

function openDeleteChatConfirm(chatId) {
    let modal = document.getElementById('delete-chat-confirm-modal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'delete-chat-confirm-modal';
        modal.className = 'modal';
        modal.innerHTML = `
            <div class="bg-white dark:bg-gray-800 rounded-2xl p-6 max-w-sm w-full mx-4 shadow-2xl">
                <div class="text-center mb-6">
                    <div class="w-16 h-16 bg-red-100 dark:bg-red-900/30 rounded-full flex items-center justify-center mx-auto mb-4">
                        <i data-feather="trash-2" class="w-8 h-8 text-red-600 dark:text-red-400"></i>
                    </div>
                    <h3 class="text-xl font-bold text-gray-900 dark:text-gray-100">Удалить чат навсегда?</h3>
                    <p class="text-gray-600 dark:text-gray-400 mt-2 text-sm leading-relaxed">
                        Все сообщения и файлы будут удалены.<br>
                        Это действие <strong>нельзя отменить</strong>.
                    </p>
                </div>
                <div class="flex gap-3">
                    <button type="button" id="cancel-delete-chat-ctx" class="flex-1 px-4 py-2.5 bg-gray-200 dark:bg-gray-700 hover:bg-gray-300 dark:hover:bg-gray-600 text-gray-800 dark:text-gray-100 rounded-xl font-medium transition">
                        Отмена
                    </button>
                    <button type="button" id="confirm-delete-chat-ctx" class="flex-1 px-4 py-2.5 bg-red-600 hover:bg-red-700 text-white rounded-xl font-medium transition">
                        Удалить
                    </button>
                </div>
            </div>`;
        document.body.appendChild(modal);
        modal.querySelector('#cancel-delete-chat-ctx').onclick = () => {
            modal.classList.remove('show');
            delete modal.dataset.chatId;
        };
        modal.querySelector('#confirm-delete-chat-ctx').onclick = async () => {
            const id = modal.dataset.chatId;
            modal.classList.remove('show');
            if (id) await deleteChatById(id);
            delete modal.dataset.chatId;
        };
    }
    modal.dataset.chatId = chatId;
    modal.classList.add('show');
    if (typeof feather !== 'undefined') feather.replace();
}


function showMessageContextMenu(x, y, messageId, text) {
    const menu = document.getElementById('message-context-menu');
    if (!menu) {
        console.warn('[context-menu] #message-context-menu not found in DOM');
        return;
    }
    if (typeof hideChatContextMenu === 'function') hideChatContextMenu();
    contextMenuMessageId = messageId;
    contextMenuMessageText = text || '';
    const pinLabel = document.getElementById('ctx-pin-label');
    if (pinLabel) {
        pinLabel.textContent = (currentChatId && isMessagePinned(currentChatId, messageId))
            ? 'Открепить'
            : 'Закрепить';
    }
    const row = document.querySelector(`[data-mid="${CSS.escape(String(messageId))}"]`);
    const isMine = !!(row && (
        row.querySelector('.message-bubble.outgoing') ||
        row.classList.contains('outgoing-row') ||
        (typeof isOwnMessageRow === 'function' && isOwnMessageRow(row))
    ));
    const editBtn = document.getElementById('ctx-edit-btn');
    const delBtn = document.getElementById('ctx-delete-btn');
    const selBtn = document.getElementById('ctx-select-btn');
    const replyBtn = document.getElementById('ctx-reply-btn');
    const forwardBtn = document.getElementById('ctx-forward-btn');
    const reactBtn = document.getElementById('ctx-react-btn');
    const pinBtn = document.getElementById('ctx-pin-btn');

    if (replyBtn) replyBtn.style.display = '';
    if (forwardBtn) forwardBtn.style.display = '';
    if (reactBtn) reactBtn.style.display = '';
    if (pinBtn) pinBtn.style.display = '';
    if (selBtn) selBtn.style.display = '';
    if (delBtn) delBtn.style.display = '';
    if (editBtn) editBtn.style.display = isMine ? '' : 'none';

    positionContextMenu(menu, x, y);
}

function enterEditMode(messageId, text) {
    if (!editingMessageId && currentChatId && messageInput?.value?.trim()) {
        saveCurrentDraft();
    }
    if (typeof cancelReply === 'function') cancelReply();
    editingMessageId = messageId;
    const parsedEdit = parseReplyPayload(text || '');
    messageInput.value = parsedEdit.text || '';
    messageInput.focus();
    if (editModeBar) {
        editModeBar.classList.add('show');
        const preview = editModeBar.querySelector('.edit-preview');
        if (preview) preview.textContent = (text || '').slice(0, 80) + ((text || '').length > 80 ? '…' : '');
    }
    sendButton.classList.add('edit-mode');
    sendButton.innerHTML = '<i data-feather="check" class="w-5 h-5"></i>';
    feather.replace();
    const fileLabel = document.querySelector('label[for="file-input"]');
    if (fileLabel) fileLabel.style.display = 'none';
}

function exitEditMode() {
    editingMessageId = null;
    messageInput.value = '';
    if (editModeBar) editModeBar.classList.remove('show');
    sendButton.classList.remove('edit-mode');
    sendButton.innerHTML = '<i data-feather="send" class="w-5 h-5"></i>';
    feather.replace();
    const fileLabel = document.querySelector('label[for="file-input"]');
    if (fileLabel) fileLabel.style.display = '';
    if (currentChatId) restoreDraftForChat(currentChatId);
}

async function submitEditMessage() {
    if (!editingMessageId || !currentChatId) return;
    let newText = messageInput.value.trim();
    if (!newText) {
        showToast('Текст не может быть пустым', 'error');
        return;
    }
    const editRow = document.querySelector(`[data-mid="${editingMessageId}"]`);
    if (editRow && editRow.dataset.replyTo) {
        newText = buildReplyPayload({
            messageId: editRow.dataset.replyTo,
            senderName: editRow.dataset.replyName || 'Сообщение',
            preview: editRow.dataset.replyPreview || ''
        }, newText);
    }
    isSending = true;
    sendButton.disabled = true;
    try {
        const res = await fetchWithAuth(`${API_BASE}/messages/${editingMessageId}`, {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${token}`
            },
            body: JSON.stringify({
                chatId: currentChatId,
                messageText: newText,
                hasAttachmets: false
            })
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.error || err.Error || `HTTP ${res.status}`);
        }
        updateExistingMessageText(editingMessageId, newText, true);
        updateChatLastMessagePreview(currentChatId, newText, [], new Date().toISOString(), true, 'sent');
        exitEditMode();
        showToast('Сообщение изменено', 'success');
    } catch (err) {
        console.error('Ошибка редактирования:', err);
        showToast(err.message || 'Не удалось изменить сообщение', 'error');
    } finally {
        isSending = false;
        sendButton.disabled = false;
    }
}

function openDeleteMessageConfirm(messageId, options = {}) {
    const modal = document.getElementById('delete-message-modal');
    if (!modal) return;

    const ids = options.messageIds || (messageId ? [messageId] : []);
    const isBulk = ids.length > 1;

    modal.dataset.messageId = ids[0] || '';
    if (isBulk) modal.dataset.messageIds = JSON.stringify(ids);
    else delete modal.dataset.messageIds;

    const title = modal.querySelector('#delete-message-title') || modal.querySelector('h3');
    if (title) {
        title.textContent = isBulk
            ? `Удалить сообщения (${ids.length})?`
            : 'Удалить сообщение?';
    }
    const hint = modal.querySelector('#delete-message-hint');
    if (hint) {
        hint.textContent = 'Выберите, для кого удалить:';
    }

    const btnEveryone = modal.querySelector('#confirm-delete-for-everyone');
    if (btnEveryone) {
        btnEveryone.style.display = '';
        btnEveryone.disabled = false;
    }
    const btnMe = modal.querySelector('#confirm-delete-for-me');
    if (btnMe) {
        btnMe.style.display = '';
        btnMe.disabled = false;
    }

    modal.classList.add('show');
    if (typeof feather !== 'undefined') feather.replace();
}

function closeDeleteMessageConfirm() {
    const modal = document.getElementById('delete-message-modal');
    if (modal) {
        modal.classList.remove('show');
        delete modal.dataset.messageId;
        delete modal.dataset.messageIds;
    }
}

let __suppressChatBump = false;
const PIN_CHATS_KEY = 'guap_pinned_chats';
const PIN_MSGS_KEY = 'guap_pinned_messages';