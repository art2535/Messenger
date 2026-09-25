(() => {
    'use strict';

    const MAX_TARGET_CHATS = 5;
    const MAX_MESSAGES = 20;
    let forwardState = null;
    let forwardModal = null;

    function escapeHtml(value) {
        return String(value ?? '').replace(/[&<>"']/g, ch => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[ch]));
    }

    function createModal() {
        if (forwardModal) return forwardModal;

        const modal = document.createElement('div');
        modal.id = 'forward-message-modal';
        modal.className = 'modal';
        modal.innerHTML = `
            <div class="bg-white dark:bg-gray-800 rounded-2xl shadow-2xl w-full max-w-lg mx-4 p-6 text-gray-900 dark:text-white max-h-[85vh] flex flex-col">
                <div class="flex items-center justify-between mb-4">
                    <div class="flex items-center gap-2 min-w-0">
                        <div class="w-9 h-9 rounded-xl bg-blue-100 dark:bg-blue-900/40 text-blue-600 dark:text-blue-300 flex items-center justify-center flex-shrink-0">
                            <span class="text-lg font-bold">↗</span>
                        </div>
                        <div class="min-w-0">
                            <h3 class="text-lg font-semibold truncate">Переслать</h3>
                            <p id="forward-message-summary" class="text-xs text-gray-500 dark:text-gray-400 truncate"></p>
                        </div>
                    </div>
                    <button type="button" id="forward-close-btn" class="w-9 h-9 rounded-full text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 transition" aria-label="Закрыть">
                        <span class="text-xl">×</span>
                    </button>
                </div>

                <div id="forward-source-block" class="rounded-xl border border-blue-100 dark:border-blue-900/50 bg-blue-50/70 dark:bg-blue-900/20 px-3 py-2.5 mb-4">
                    <div id="forward-source-name" class="text-xs font-semibold text-blue-700 dark:text-blue-300 mb-1"></div>
                    <div id="forward-source-preview" class="text-sm text-gray-700 dark:text-gray-200 whitespace-pre-wrap break-words max-h-24 overflow-y-auto"></div>
                    <div id="forward-source-files" class="text-xs text-gray-500 dark:text-gray-400 mt-1 hidden"></div>
                </div>

                <div class="relative mb-3">
                    <input type="search" id="forward-chat-search" placeholder="Поиск чата…" autocomplete="off"
                        class="w-full rounded-xl border border-gray-200 dark:border-gray-600 bg-gray-50 dark:bg-gray-900/60 px-3 py-2.5 text-sm outline-none focus:ring-2 focus:ring-blue-500 dark:text-white" />
                </div>

                <div id="forward-chat-list" class="flex-1 overflow-y-auto min-h-[180px] max-h-[40vh] space-y-1 pr-1"></div>

                <div class="flex items-center justify-between gap-3 mt-4 pt-3 border-t border-gray-200 dark:border-gray-700">
                    <div id="forward-selection-count" class="text-xs text-gray-500 dark:text-gray-400">Выберите чат</div>
                    <div class="flex gap-2">
                        <button type="button" id="forward-cancel-btn" class="px-4 py-2.5 rounded-xl text-gray-700 dark:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 font-medium transition">Отмена</button>
                        <button type="button" id="forward-submit-btn" disabled class="px-5 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-700 text-white font-medium transition disabled:opacity-50 disabled:cursor-not-allowed">
                            Переслать
                        </button>
                    </div>
                </div>
                <div id="forward-status" class="text-xs text-center text-gray-500 dark:text-gray-400 mt-2 hidden"></div>
            </div>`;

        document.body.appendChild(modal);
        forwardModal = modal;

        modal.addEventListener('click', e => {
            if (e.target === modal) closeModal();
        });
        modal.querySelector('#forward-close-btn')?.addEventListener('click', closeModal);
        modal.querySelector('#forward-cancel-btn')?.addEventListener('click', closeModal);
        modal.querySelector('#forward-chat-search')?.addEventListener('input', renderChatList);
        modal.querySelector('#forward-submit-btn')?.addEventListener('click', submitForward);

        return modal;
    }

    function getChatItems() {
        return [...document.querySelectorAll('.chat-item[data-chat-id]')]
            .map(item => ({
                id: item.dataset.chatId,
                name: (item.querySelector('h3')?.textContent || 'Без имени').trim(),
                avatar: item.querySelector('img')?.getAttribute('src') || ''
            }))
            .filter(x => x.id);
    }

    function renderChatList() {
        const modal = createModal();
        const list = modal.querySelector('#forward-chat-list');
        const search = (modal.querySelector('#forward-chat-search')?.value || '').trim().toLowerCase();
        if (!list) return;

        const items = getChatItems().filter(chat => !search || chat.name.toLowerCase().includes(search));
        list.innerHTML = '';

        if (!items.length) {
            list.innerHTML = '<div class="py-10 text-center text-sm text-gray-400">Чаты не найдены</div>';
            updateSelectionCount();
            return;
        }

        for (const chat of items) {
            const selected = forwardState?.targets?.has(String(chat.id));
            const row = document.createElement('button');
            row.type = 'button';
            row.className = 'w-full flex items-center gap-3 rounded-xl px-3 py-2.5 text-left transition ' + (selected
                ? 'bg-blue-50 dark:bg-blue-900/30 ring-1 ring-blue-300 dark:ring-blue-700'
                : 'hover:bg-gray-100 dark:hover:bg-gray-700');
            row.innerHTML =
                '<span class="w-10 h-10 rounded-full overflow-hidden bg-gray-200 dark:bg-gray-600 flex-shrink-0 flex items-center justify-center">' +
                (chat.avatar
                    ? '<img src="' + escapeHtml(chat.avatar) + '" alt="" class="w-full h-full object-cover">'
                    : '<span class="text-sm font-semibold text-gray-500">?</span>') +
                '</span>' +
                '<span class="min-w-0 flex-1">' +
                '<span class="block truncate font-medium text-sm text-gray-800 dark:text-gray-100">' + escapeHtml(chat.name) + '</span>' +
                (String(chat.id) === String(typeof currentChatId !== 'undefined' ? currentChatId : '')
                    ? '<span class="block text-[11px] text-blue-600 dark:text-blue-300">Текущий чат</span>'
                    : '') +
                '</span>' +
                '<span class="w-5 h-5 rounded-md border flex items-center justify-center flex-shrink-0 ' +
                (selected ? 'bg-blue-600 border-blue-600 text-white' : 'border-gray-300 dark:border-gray-500 text-transparent') +
                '">✓</span>';

            row.addEventListener('click', () => {
                const id = String(chat.id);
                if (forwardState.targets.has(id)) {
                    forwardState.targets.delete(id);
                } else {
                    if (forwardState.targets.size >= MAX_TARGET_CHATS) {
                        if (typeof showToast === 'function') showToast('Можно выбрать не более ' + MAX_TARGET_CHATS + ' чатов', 'warning');
                        return;
                    }
                    forwardState.targets.add(id);
                }
                renderChatList();
            });
            list.appendChild(row);
        }

        updateSelectionCount();
    }

    function updateSelectionCount() {
        if (!forwardModal || !forwardState) return;
        const count = forwardState.targets.size;
        const label = forwardModal.querySelector('#forward-selection-count');
        const submit = forwardModal.querySelector('#forward-submit-btn');
        if (label) {
            label.textContent = count
                ? 'Выбрано: ' + count + ' / ' + MAX_TARGET_CHATS
                : 'Выберите чат';
        }
        if (submit) {
            submit.disabled = count === 0 || !!forwardState.sending;
        }
    }

    function isOwnMessageRowLocal(row, cached) {
        if (typeof me === 'undefined' || me == null) return false;
        const sid = row?.dataset?.senderId || cached?.senderId || cached?.SenderId;
        return sid != null && String(sid) === String(me);
    }

    function resolveForwardSenderName(opts) {
        const cached = opts.cached;
        const row = opts.row;
        const rowSender = opts.rowSender;
        const fromCache = String(cached?.senderName || cached?.SenderName || '').trim();
        const isUiYou = (v) => {
            const s = String(v || '').trim();
            return !s || s === 'Вы' || s === 'вы' || s.toLowerCase() === 'you';
        };

        if (fromCache && !isUiYou(fromCache)) return fromCache;

        if (typeof me !== 'undefined' && me != null && typeof userNameCache !== 'undefined') {
            const mine = userNameCache.get(String(me));
            if (mine && !isUiYou(mine)) return String(mine).trim();
        }

        if (isOwnMessageRowLocal(row, cached) || isUiYou(rowSender)) return 'вас';

        const fromRow = String(rowSender || '').trim();
        if (fromRow && !isUiYou(fromRow)) return fromRow;

        return 'Пользователь';
    }

    function getMessageData(messageId, fallbackText) {
        const cache = window.__messageForwardCache;
        const cached = cache instanceof Map ? cache.get(String(messageId)) : null;
        const row = document.querySelector('[data-mid="' + CSS.escape(String(messageId)) + '"]');

        const rowSender = typeof getSenderNameFromRow === 'function'
            ? getSenderNameFromRow(row)
            : (row?.querySelector('.sender-name')?.textContent || '');

        const rowText = row?.querySelector('.message-bubble p')?.innerText || '';
        const senderName = resolveForwardSenderName({ cached: cached, row: row, rowSender: rowSender });

        let text = '';
        if (cached?.messageText != null && String(cached.messageText).trim()) {
            text = String(cached.messageText).trim();
        } else if (typeof stripMessageMetaForPreview === 'function') {
            text = stripMessageMetaForPreview(fallbackText || rowText || '').trim();
        } else if (typeof stripReplyForPreview === 'function') {
            text = stripReplyForPreview(fallbackText || rowText || '').trim();
        } else {
            text = String(fallbackText || rowText || '').trim();
        }

        const attachments = Array.isArray(cached?.attachments)
            ? cached.attachments
            : (Array.isArray(cached?.Attachments) ? cached.Attachments : []);

        const hasFiles = attachments.length > 0
            || !!(row && row.querySelector('.message-bubble img[src], .message-bubble .file-attachment'));

        return {
            messageId: String(messageId),
            text: text,
            senderName: senderName,
            hasFiles: hasFiles,
            attachmentCount: attachments.length
        };
    }

    function openModal(messageIds, fallbackText) {
        const ids = (Array.isArray(messageIds) ? messageIds : [messageIds])
            .map(String)
            .filter(id => id && !id.startsWith('temp-'))
            .slice(0, MAX_MESSAGES);

        if (!ids.length) return;

        const messages = ids.map(id => getMessageData(id, ids.length === 1 ? fallbackText : ''));
        const modal = createModal();

        forwardState = {
            messageIds: ids,
            messages: messages,
            targets: new Set(),
            sending: false
        };

        const totalFiles = messages.reduce((n, m) => n + (m.attachmentCount || (m.hasFiles ? 1 : 0)), 0);
        const summary = messages.length === 1
            ? (messages[0].hasFiles
                ? 'с вложениями · ' + (messages[0].senderName === 'вас' ? 'Вы' : messages[0].senderName)
                : (messages[0].senderName === 'вас' ? 'Вы' : messages[0].senderName))
            : messages.length + ' сообщ.' + (totalFiles ? ' · вложения' : '');

        modal.querySelector('#forward-message-summary').textContent = summary;

        if (messages.length === 1) {
            const m = messages[0];
            const displayName = m.senderName === 'вас' ? 'Вы' : (m.senderName || 'Пользователь');
            modal.querySelector('#forward-source-name').textContent = '↗ ' + displayName;
            modal.querySelector('#forward-source-preview').textContent = m.text || (m.hasFiles ? 'Вложение' : 'Сообщение без текста');
            const files = modal.querySelector('#forward-source-files');
            if (m.hasFiles) {
                files.textContent = m.attachmentCount
                    ? 'Вложений: ' + m.attachmentCount
                    : 'Есть вложения';
                files.classList.remove('hidden');
            } else {
                files.textContent = '';
                files.classList.add('hidden');
            }
        } else {
            modal.querySelector('#forward-source-name').textContent = '↗ ' + messages.length + ' сообщений';
            modal.querySelector('#forward-source-preview').textContent = messages.map((m, i) => {
                const name = m.senderName === 'вас' ? 'Вы' : m.senderName;
                const body = m.text ? m.text.slice(0, 60) : (m.hasFiles ? '[файл]' : '…');
                return (i + 1) + '. ' + name + ': ' + body;
            }).join('\n');
            const files = modal.querySelector('#forward-source-files');
            if (totalFiles) {
                files.textContent = 'Есть вложения';
                files.classList.remove('hidden');
            } else {
                files.textContent = '';
                files.classList.add('hidden');
            }
        }

        modal.querySelector('#forward-chat-search').value = '';
        modal.querySelector('#forward-status').classList.add('hidden');
        modal.querySelector('#forward-submit-btn').textContent = 'Переслать';
        modal.classList.add('show');
        renderChatList();
        if (typeof hideMessageContextMenu === 'function') hideMessageContextMenu();
        modal.querySelector('#forward-chat-search')?.focus();
    }

    function closeModal() {
        if (!forwardModal) return;
        if (forwardState?.sending) return;
        forwardModal.classList.remove('show');
        forwardState = null;
    }

    function setStatus(text, visible) {
        if (visible === undefined) visible = true;
        if (!forwardModal) return;
        const el = forwardModal.querySelector('#forward-status');
        if (!el) return;
        el.textContent = text || '';
        el.classList.toggle('hidden', !visible || !text);
    }

    async function submitForward() {
        if (!forwardState || forwardState.sending || !forwardState.targets.size) return;
        forwardState.sending = true;
        const targetIds = [...forwardState.targets];
        const messageIds = forwardState.messageIds || [];
        const submit = forwardModal.querySelector('#forward-submit-btn');
        if (submit) {
            submit.disabled = true;
            submit.textContent = 'Пересылка…';
        }
        setStatus('Пересылка на сервере…');

        try {
            const body = {
                messageIds: messageIds.map(String),
                targetChatIds: targetIds.map(String)
            };

            console.log('[forward] request', body);

            const fetchFn = typeof fetchWithAuth === 'function' ? fetchWithAuth : null;
            let res;
            if (fetchFn) {
                res = await fetchFn(API_BASE + '/messages/forward', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify(body)
                });
            } else {
                res = await fetch(API_BASE + '/messages/forward', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Accept': 'application/json',
                        'Authorization': 'Bearer ' + token
                    },
                    body: JSON.stringify(body)
                });
            }

            if (!res) throw new Error('Нет ответа от сервера');
            if (res.status === 401) {
                window.location.href = '/Authorization/Authorization';
                throw new Error('Сессия истекла');
            }
            if (!res.ok) {
                let message = 'HTTP ' + res.status;
                const data = await res.json().catch(() => null);
                console.warn('[forward] error body', data);
                if (data?.error || data?.Error) message = data.error || data.Error;
                else if (data?.title) message = data.title + (data.errors ? ': ' + JSON.stringify(data.errors) : '');
                else if (data?.errors) message = JSON.stringify(data.errors);
                // Частый случай: API без эндпоинта forward → {chatId} ловит "forward" → 400
                if (res.status === 400 && message === 'HTTP 400') {
                    message = 'HTTP 400 — проверьте, что API обновлён (эндпоинт POST /messages/forward) и перезапущен';
                }
                throw new Error(message);
            }

            const data = await res.json().catch(() => ({}));
            const published = data.published ?? data.Published ?? 0;
            const errors = data.errors || data.Errors || [];

            if (!published) throw new Error((errors && errors[0]) || 'Не удалось переслать');

            forwardModal.classList.remove('show');
            forwardState = null;
            if (typeof exitSelectMode === 'function') exitSelectMode();

            if (typeof bumpChatToTop === 'function') {
                for (const cid of targetIds) bumpChatToTop(cid);
            }

            if (typeof showToast === 'function') {
                const msgCount = messageIds.length;
                const chatCount = targetIds.length;
                showToast(
                    errors.length
                        ? 'Переслано: ' + published + ', ошибок: ' + errors.length
                        : msgCount === 1
                            ? 'Сообщение переслано в ' + chatCount + ' чат' + (chatCount === 1 ? '' : chatCount < 5 ? 'а' : 'ов')
                            : msgCount + ' сообщ. → ' + chatCount + ' чат' + (chatCount === 1 ? '' : chatCount < 5 ? 'а' : 'ов'),
                    errors.length ? 'warning' : 'success'
                );
            }
        } catch (err) {
            console.error('[forward]', err);
            setStatus(err?.message || 'Ошибка пересылки');
            if (typeof showToast === 'function') showToast(err?.message || 'Не удалось переслать', 'error');
        } finally {
            if (forwardState) {
                forwardState.sending = false;
                updateSelectionCount();
            }
            if (submit && forwardState) submit.textContent = 'Переслать';
        }
    }

    function onForwardClick(e) {
        e.preventDefault();
        e.stopPropagation();
        if (typeof selectMode !== 'undefined' && selectMode && typeof selectedMessageIds !== 'undefined' && selectedMessageIds.size > 0) {
            openModal([...selectedMessageIds]);
            return;
        }
        const messageId = typeof contextMenuMessageId !== 'undefined' ? contextMenuMessageId : null;
        const messageText = typeof contextMenuMessageText !== 'undefined' ? (contextMenuMessageText || '') : '';
        if (!messageId) return;
        openModal(messageId, messageText);
    }

    function onToolbarForwardClick(e) {
        e.preventDefault();
        e.stopPropagation();
        if (typeof selectedMessageIds === 'undefined' || !selectedMessageIds.size) {
            if (typeof showToast === 'function') showToast('Выберите сообщения', 'warning');
            return;
        }
        openModal([...selectedMessageIds]);
    }

    function installContextButton() {
        const btn = document.getElementById('ctx-forward-btn');
        if (btn) {
            btn.addEventListener('click', onForwardClick);
            return;
        }
        const replyButton = document.getElementById('ctx-reply-btn');
        if (!replyButton) return;
        const button = document.createElement('button');
        button.type = 'button';
        button.id = 'ctx-forward-btn';
        button.innerHTML =
            '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="ctx-icon">' +
            '<path d="M15 17l5-5-5-5"/><path d="M4 19v-2a4 4 0 0 1 4-4h12"/></svg><span>Переслать</span>';
        replyButton.insertAdjacentElement('afterend', button);
        button.addEventListener('click', onForwardClick);
    }

    function installToolbarButton() {
        const toolbar = document.getElementById('selection-toolbar');
        if (!toolbar) return;

        let button = document.getElementById('sel-forward-btn');
        if (!button) {
            const delBtn = document.getElementById('sel-delete-btn');
            button = document.createElement('button');
            button.type = 'button';
            button.id = 'sel-forward-btn';
            button.className = 'sel-forward';
            button.title = 'Переслать';
            button.disabled = true;
            button.innerHTML =
                '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M15 17l5-5-5-5"/><path d="M4 19v-2a4 4 0 0 1 4-4h12"/></svg><span>Переслать</span>';
            if (delBtn) delBtn.insertAdjacentElement('beforebegin', button);
            else toolbar.appendChild(button);
        }

        if (!button.dataset.forwardBound) {
            button.dataset.forwardBound = '1';
            button.addEventListener('click', onToolbarForwardClick);
        }

        const n = typeof selectedMessageIds !== 'undefined' ? selectedMessageIds.size : 0;
        button.disabled = n === 0;
    }

    function syncForwardToolbarBtn() {
        const btn = document.getElementById('sel-forward-btn');
        if (!btn) return;
        const n = typeof selectedMessageIds !== 'undefined' ? selectedMessageIds.size : 0;
        btn.disabled = n === 0;
    }

    function install() {
        installContextButton();
        installToolbarButton();
        document.getElementById('messages-container')?.addEventListener('click', () => {
            setTimeout(syncForwardToolbarBtn, 0);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install, { once: true });
    } else {
        install();
    }

    window.openForwardMessageModal = openModal;
    window.openForwardMessages = openModal;
})();
