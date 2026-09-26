async function loadChats() {
    const container = document.getElementById('chats-container');
    const loading = document.getElementById('chats-loading');
    const emptyState = document.getElementById('empty-state');

    try {
        const res = await fetchWithAuth(`${API_BASE}/chats`);
        if (!res) return;

        const json = await res.json();
        allChats = json.data;
        if (window.ChatsOffline && Array.isArray(json.data)) {
            ChatsOffline.onChatsLoaded(json.data);
        } else if (window.OfflineStore && Array.isArray(json.data)) {
            OfflineStore.saveChatsCache(json.data).catch(() => {});
        }
        try {
            const orderIds = (json.data || []).map(c => String(c.chatId || c.id || c.ChatId)).filter(Boolean);
            if (orderIds.length) setChatBaseOrderFromIds(orderIds);
        } catch (_) {}

        if (loading) loading.style.display = 'none';
        container.innerHTML = '';

        if (!json.isSuccess || !json.data?.length) {
            emptyState.classList.remove('hidden');
            emptyState.classList.add('flex');
            return;
        }

        emptyState.classList.add('hidden');
        emptyState.classList.remove('flex');

        json.data.forEach(chat => {
            const div = document.createElement('div');
            div.className = 'p-4 chat-item flex items-center space-x-3 cursor-pointer hover:bg-gray-100 transition rounded-lg mx-2';
            div.dataset.chatId = chat.chatId || chat.id;

            const isPrivate = chat.type === 'private' || chat.isPrivate;

            let displayName = chat.name || 'Без имени';
            let displayAvatar = chat.avatar || 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png';
            let partnerId = null;

            if (isPrivate && chat.participants && chat.participants.length > 0) {
                const interlocutor = chat.participants.find(p =>
                    (p.id || p.userId || "").toString().toLowerCase() !== me.toLowerCase()
                );

                if (interlocutor) {
                    displayName = interlocutor.fullName || interlocutor.name || displayName;
                    displayAvatar = interlocutor.avatar || interlocutor.avatarPath || displayAvatar;
                    partnerId = interlocutor.id || interlocutor.userId;
                    if (partnerId) div.dataset.partnerId = String(partnerId);
                }
            }

            const lastMessage = chat.lastMessage && typeof chat.lastMessage === 'object' ? chat.lastMessage : null;
            const lastMessageSenderId = lastMessage?.senderId ?? lastMessage?.userId ?? lastMessage?.fromUserId;
            const lastMessageIsMine = lastMessageSenderId != null && String(lastMessageSenderId) === String(me);
            const lastMessageTime = lastMessage?.sentAt ?? lastMessage?.sentTime ?? lastMessage?.createdAt ?? chat.lastMessageSentAt ?? chat.lastMessageTime ?? chat.lastMessageAt ?? chat.updatedAt;
            const lastMessageStatus = lastMessage?.status ?? lastMessage?.deliveryStatus ?? lastMessage?.messageStatus ?? (lastMessage?.isRead ? 'read' : null) ?? (lastMessage?.isDelivered ? 'delivered' : null) ?? 'sent';
            const initialMeta = renderChatListMessageMeta(
                typeof chat.lastMessage === 'string' ? chat.lastMessage : (lastMessage?.messageText || ''),
                lastMessage?.attachments || [], lastMessageTime, lastMessageIsMine, lastMessageStatus
            );

            div.innerHTML = `
                <div class="relative flex-shrink-0">
                    <img src="${displayAvatar}" class="w-12 h-12 rounded-full object-cover">
                    <span class="chat-online-dot absolute bottom-0 right-0 w-3.5 h-3.5 bg-green-500 rounded-full border-2 border-white hidden"></span>
                </div>
                <div class="flex-1 min-w-0">
                    <div class="flex items-center gap-2">
                        <h3 class="font-medium truncate flex-1 min-w-0">${displayName}</h3>
                        <span class="chat-list-time">${initialMeta.time}</span>
                        <span class="unread-badge hidden flex-shrink-0"></span>
                    </div>
                    <div class="text-sm text-gray-500 truncate chat-list-message-meta">
                        ${initialMeta.previewHTML}
                    </div>
                    <div class="typing-in-list hidden text-xs text-blue-600 font-medium mt-0.5">
                        <span class="typing-name"></span> печатает...
                    </div>
                </div>
            `;

            if (partnerId) div.dataset.partnerId = partnerId;

            div.onclick = () => openChat(div);
            container.appendChild(div);

            const unread = chat.unreadCount ?? chat.unreadMessagesCount ?? 0;
            if (unread > 0) {
                const cid = typeof normChatId === 'function' ? normChatId(div.dataset.chatId) : String(div.dataset.chatId);
                unreadCounts.set(cid, unread);
                updateUnreadBadge(div.dataset.chatId, unread);
            }
        });

        cacheChatItems();
        applyPinnedChatsOrder();
        filterChats();
        feather.replace();
        renderAllDraftPreviews();

        await hydrateChatListStatuses(json.data);
        renderAllDraftPreviews();

        setTimeout(updateAllOnlineIndicators, 300);
    } catch (err) {
        console.error("Ошибка загрузки:", err);
        try {
            let cached = null;
            if (window.ChatsOffline) {
                cached = await ChatsOffline.getCachedChats();
            } else if (window.OfflineStore) {
                cached = await OfflineStore.loadChatsCache();
            }
            if (cached && cached.length) {
                allChats = cached;
                if (loading) loading.style.display = 'none';
                if (container) container.innerHTML = '';
                if (emptyState) {
                    emptyState.classList.add('hidden');
                    emptyState.classList.remove('flex');
                }
                cached.forEach(chat => {
                    const div = document.createElement('div');
                    div.className = 'p-4 chat-item flex items-center space-x-3 cursor-pointer hover:bg-gray-100 transition rounded-lg mx-2';
                    div.dataset.chatId = chat.chatId || chat.id;
                    const isPrivate = chat.type === 'private' || chat.isPrivate;
                    let displayName = chat.name || 'Без имени';
                    let displayAvatar = chat.avatar || 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png';
                    let partnerId = null;
                    if (isPrivate && chat.participants && chat.participants.length > 0) {
                        const interlocutor = chat.participants.find(p =>
                            (p.id || p.userId || "").toString().toLowerCase() !== me.toLowerCase()
                        );
                        if (interlocutor) {
                            displayName = interlocutor.fullName || interlocutor.name || displayName;
                            displayAvatar = interlocutor.avatar || interlocutor.avatarPath || displayAvatar;
                            partnerId = interlocutor.id || interlocutor.userId;
                        }
                    }
                    const lastMessage = chat.lastMessage && typeof chat.lastMessage === 'object' ? chat.lastMessage : null;
                    const lastMessageSenderId = lastMessage?.senderId ?? lastMessage?.userId ?? lastMessage?.fromUserId;
                    const lastMessageIsMine = lastMessageSenderId != null && String(lastMessageSenderId) === String(me);
                    const lastMessageTime = lastMessage?.sentAt ?? lastMessage?.sentTime ?? lastMessage?.createdAt ?? chat.lastMessageSentAt ?? chat.lastMessageTime ?? chat.lastMessageAt ?? chat.updatedAt;
                    const lastMessageStatus = lastMessage?.status ?? lastMessage?.deliveryStatus ?? lastMessage?.messageStatus ?? (lastMessage?.isRead ? 'read' : null) ?? (lastMessage?.isDelivered ? 'delivered' : null) ?? 'sent';
                    const initialMeta = typeof renderChatListMessageMeta === 'function'
                        ? renderChatListMessageMeta(
                            typeof chat.lastMessage === 'string' ? chat.lastMessage : (lastMessage?.messageText || ''),
                            lastMessage?.attachments || [], lastMessageTime, lastMessageIsMine, lastMessageStatus
                            )
                        : { time: '', previewHTML: '' };
                    div.innerHTML = `
                        <div class="relative flex-shrink-0">
                            <img src="${displayAvatar}" class="w-12 h-12 rounded-full object-cover">
                            <span class="chat-online-dot absolute bottom-0 right-0 w-3.5 h-3.5 bg-green-500 rounded-full border-2 border-white hidden"></span>
                        </div>
                        <div class="flex-1 min-w-0">
                            <div class="flex items-center gap-2">
                                <h3 class="font-medium truncate flex-1 min-w-0">${displayName}</h3>
                                <span class="chat-list-time">${initialMeta.time || ''}</span>
                                <span class="unread-badge hidden flex-shrink-0"></span>
                            </div>
                            <div class="text-sm text-gray-500 truncate chat-list-message-meta">
                                ${initialMeta.previewHTML || ''}
                            </div>
                        </div>
                    `;
                    if (partnerId) div.dataset.partnerId = partnerId;
                    div.onclick = () => openChat(div);
                    container.appendChild(div);
                });
                if (typeof cacheChatItems === 'function') cacheChatItems();
                if (typeof feather !== 'undefined' && feather.replace) feather.replace();
                if (typeof showToast === 'function') showToast('Показаны чаты из offline-кэша', 'warning');
                return;
            }
        } catch (cacheErr) {
            console.warn('offline chats cache failed', cacheErr);
        }
        const status = document.getElementById('chats-status-container');
        if (status) status.innerHTML = '<div class="p-4 text-red-500">Ошибка загрузки</div>';
    }
}

function teardownHistoryObserver() {
    if (historyScrollObserver) {
        historyScrollObserver.disconnect();
        historyScrollObserver = null;
    }
    const sentinel = document.getElementById('history-load-sentinel');
    if (sentinel) sentinel.remove();
}

function setupHistoryObserver(chatId) {
    teardownHistoryObserver();
    const container = document.getElementById('messages-container');
    if (!container || !messagesHasMore) return;

    let sentinel = document.getElementById('history-load-sentinel');
    if (!sentinel) {
        sentinel = document.createElement('div');
        sentinel.id = 'history-load-sentinel';
        sentinel.className = 'py-2 text-center text-xs text-gray-400';
        sentinel.textContent = 'Загрузка истории...';
        container.prepend(sentinel);
    }

    historyScrollObserver = new IntersectionObserver(async (entries) => {
        if (!entries[0]?.isIntersecting) return;
        if (isLoadingOlder || !messagesHasMore) return;
        await loadOlderMessages(chatId);
    }, { root: container, rootMargin: '80px', threshold: 0.01 });

    historyScrollObserver.observe(sentinel);
}

async function loadOlderMessages(chatId) {
    if (isLoadingOlder || !messagesHasMore || oldestSequence == null) return;
    isLoadingOlder = true;
    const container = document.getElementById('messages-container');
    const prevHeight = container.scrollHeight;
    const prevTop = container.scrollTop;
    const sentinel = document.getElementById('history-load-sentinel');

    try {
        const url = `${API_BASE}/messages/${chatId}?beforeSequence=${oldestSequence}&limit=50`;
        const res = await fetchWithAuth(url, {
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (!res || isRateLimited(res) || !res.ok) return;

        const json = await res.json();
        const messages = json.data || [];
        messagesHasMore = !!json.hasMore;

        if (messages.length === 0) {
            messagesHasMore = false;
            teardownHistoryObserver();
            return;
        }

        const seqs = messages.map(m => m.sequenceNumber).filter(s => s != null && s !== undefined);
        if (seqs.length) {
            oldestSequence = Math.min(...seqs.concat(oldestSequence != null ? [oldestSequence] : []));
        }

        messages.forEach(msg => originalMessages.unshift(msg));

        const marker = document.createElement('div');
        marker.id = '__older_marker';
        marker.style.display = 'none';
        if (sentinel) {
            container.insertBefore(marker, sentinel.nextSibling);
        } else {
            container.prepend(marker);
        }

        const beforeIds = new Set([...container.querySelectorAll('[data-mid]')].map(el => el.dataset.mid));
        messages.forEach(msg => {
            if (msg.messageId && beforeIds.has(String(msg.messageId))) return;
            appendMessage(msg);
        });

        const added = [...container.querySelectorAll('[data-mid]')].filter(el => !beforeIds.has(el.dataset.mid));
        let ref = marker;
        added.forEach(n => {
            container.insertBefore(n, ref.nextSibling);
            ref = n;
        });
        marker.remove();

        container.scrollTop = container.scrollHeight - prevHeight + prevTop;

        if (!messagesHasMore) teardownHistoryObserver();
        else setupHistoryObserver(chatId);
    } catch (err) {
        console.error('Ошибка подгрузки истории:', err);
    } finally {
        isLoadingOlder = false;
    }
}

async function loadMessages(chatId) {
    const container = document.getElementById('messages-container');
    container.innerHTML = '<div class="flex items-center justify-center h-full text-gray-500">Загрузка сообщений...</div>';
    lastRenderedDateKey = null;
    messagesHasMore = false;
    oldestSequence = null;
    isLoadingOlder = false;
    teardownHistoryObserver();

    try {
        const res = await fetchWithAuth(`${API_BASE}/messages/${chatId}?limit=50`, {
            headers: { 'Authorization': `Bearer ${token}` }
        });

        if (!res || !res.ok) {
            throw new Error(`HTTP error! status: ${res?.status}`);
        }

        const json = await res.json();
        const messages = json.data || json || [];
        if (window.ChatsOffline && Array.isArray(messages)) {
            ChatsOffline.onMessagesLoaded(chatId, messages);
        } else if (window.OfflineStore && Array.isArray(messages)) {
            OfflineStore.saveMessagesCache(chatId, messages).catch(() => {});
        }
        messagesHasMore = !!json.hasMore;
        const seqs = messages.map(m => m.sequenceNumber).filter(s => s != null);
        oldestSequence = seqs.length ? Math.min(...seqs) : null;

        originalMessages = [...messages];

        container.innerHTML = '';

        if (messages.length === 0) {
            container.innerHTML = `
                <div class="flex-1 flex flex-col items-center justify-center text-gray-400 py-12">
                    <i data-feather="message-square" class="w-12 h-12 mb-4 opacity-30"></i>
                    <p class="text-lg">Сообщений пока нет</p>
                    <p class="text-sm mt-1">Напишите первое сообщение</p>
                </div>`;
        } else {
            messages.forEach(msg => appendMessage(msg));
            setupHistoryObserver(chatId);
        }

        setTimeout(() => { scrollToBottom(); }, 10);
        setTimeout(() => { scrollToBottom(); }, 100);

        if (typeof pinnedBarDismissed !== 'undefined') {
            pinnedBarDismissed.delete(String(chatId));
        }
        if (typeof updatePinnedMessageBar === 'function') {
            updatePinnedMessageBar();
        }
        if (typeof highlightPinnedMessageInView === 'function') {
            highlightPinnedMessageInView();
        }

    } catch (err) {
        console.error('Ошибка загрузки сообщений:', err);
        try {
            let cached = null;
            if (window.ChatsOffline) {
                cached = await ChatsOffline.getCachedMessages(chatId);
            } else if (window.OfflineStore) {
                cached = await OfflineStore.loadMessagesCache(chatId);
            }
            const list = cached && (cached.messages || cached);
            if (list && list.length) {
                originalMessages = [...list];
                container.innerHTML = '';
                list.forEach(msg => appendMessage(msg));
                if (typeof showToast === 'function') showToast('История из offline-кэша', 'warning');
                setTimeout(() => { scrollToBottom(); }, 10);
                return;
            }
        } catch (cacheErr) {
            console.warn('offline messages cache failed', cacheErr);
        }
        container.innerHTML = `
            <div class="flex flex-col items-center justify-center h-full text-red-500 text-center px-4">
                <i data-feather="alert-triangle" class="w-12 h-12 mb-4"></i>
                <p class="font-medium">Не удалось загрузить сообщения</p>
                <p class="text-sm mt-2">Проверьте подключение или попробуйте обновить страницу</p>
            </div>`;
    }
}

function renderLastMessagePreview(lastMessageValue) {
    if (typeof lastMessageValue === "string") {
        const text = lastMessageValue.trim();
        if (text === "") {
            return '<i data-feather="paperclip" class="w-4 h-4"></i> Вложение';
        }
        return text.length > 38 ? text.substring(0, 35) + '...' : text;
    }

    if (typeof lastMessageValue === "object" && lastMessageValue !== null) {
        if (lastMessageValue.messageText && lastMessageValue.messageText.trim()) {
            const text = lastMessageValue.messageText.trim();
            return text.length > 38 ? text.substring(0, 35) + '...' : text;
        }

        if (lastMessageValue.attachments && lastMessageValue.attachments.length > 0) {
            const imgs = lastMessageValue.attachments.filter(a => a.fileType?.startsWith('image/')).length;
            const vids = lastMessageValue.attachments.filter(a => a.fileType?.startsWith('video/')).length;
            const files = lastMessageValue.attachments.length - imgs - vids;

            const parts = [];
            if (imgs > 0) parts.push(`<i data-feather="image" class="w-4 h-4"></i> Фото${imgs > 1 ? ' (' + imgs + ')' : ''}`);
            if (vids > 0) parts.push(`<i data-feather="video" class="w-4 h-4"></i> Видео${vids > 1 ? ' (' + vids + ')' : ''}`);
            if (files > 0) parts.push(`<i data-feather="paperclip" class="w-4 h-4"></i> Файл${files > 1 ? 'ов: ' + files : ''}`);

            return parts.join('&nbsp;&nbsp;') || '<i data-feather="paperclip"></i> Вложение';
        }

        return '<i data-feather="message-square" class="w-4 h-4"></i> Сообщение';
    }

    return '<span class="italic text-gray-400">Нет сообщений</span>';
}

let originalMessages = [];
let searchDebounceTimer = null;
let currentSearchQuery = "";
let foundMessageElements = [];
let currentHighlightIndex = -1;
let currentSearchScope = 'chat';
let currentSearchFilters = {};
let senderSearchTimer = null;
let globalSearchResults = [];