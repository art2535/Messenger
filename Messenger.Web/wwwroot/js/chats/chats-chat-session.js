sendMessage = async function () {
    if (typingTimeout) {
        clearTimeout(typingTimeout);
        if (connection) {
            connection.invoke("StopTyping", currentChatId).catch(() => { });
        }
    }
    return originalSendMessage.apply(this, arguments);
};

const originalOpenChat = openChat;
openChat = function (item) {
    clearMessageSearch(true);
    const result = originalOpenChat.call(this, item);
    setTimeout(updateCurrentChatSubtitle, 300);
    return result;
};

function getUserStatus(userId) {
    if (!userId || typeof userStatuses === 'undefined') return null;
    const raw = String(userId).trim();
    if (!raw) return null;
    return userStatuses.get(raw)
        || userStatuses.get(raw.toLowerCase())
        || userStatuses.get(raw.toUpperCase())
        || null;
}

function updateAllOnlineIndicators() {
    document.querySelectorAll('.chat-item').forEach(item => {
        const partnerId = item.dataset.partnerId;
        if (!partnerId) return;
        // в избранном (чат с собой) точку не показываем как «собеседник»
        if (item.dataset.isSelfChat === '1' || item.dataset.isFavorites === '1') {
            const onlineDot = item.querySelector('.chat-online-dot');
            if (onlineDot) onlineDot.classList.add('hidden');
            return;
        }

        const status = getUserStatus(partnerId);
        const onlineDot = item.querySelector('.chat-online-dot');
        if (onlineDot) {
            onlineDot.classList.toggle('hidden', !(status && status.isOnline === true));
        }
    });
}
window.updateAllOnlineIndicators = updateAllOnlineIndicators;
window.getUserStatus = getUserStatus;

function updateTypingInChatList(chatId, userId, isTyping) {
    const chatItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
    if (!chatItem) return;

    const typingBlock = chatItem.querySelector('.typing-in-list');
    const lastMsgBlock = chatItem.querySelector('.chat-list-message-meta') ||
                            chatItem.querySelector('.last-message-preview');

    if (!typingBlock) return;

    const myId = String(me || "").trim();
    const typingUserId = String(userId).trim();

    if (typingUserId === myId) {
        typingBlock.classList.add('hidden');
        if (lastMsgBlock) lastMsgBlock.classList.remove('hidden');
        return;
    }

    if (isTyping) {
        const currentItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
        if (!currentItem) return;
        const tBlock = currentItem.querySelector('.typing-in-list');
        const mBlock = currentItem.querySelector('.chat-list-message-meta') ||
                        currentItem.querySelector('.last-message-preview');
        if (!tBlock) return;

        // В личных чатах — только «печатает...», без имени
        const isPrivate = (currentItem.dataset.chatType || '').toLowerCase() === 'private'
            || !!currentItem.dataset.partnerId;

        if (isPrivate) {
            tBlock.innerHTML = 'печатает...';
            tBlock.classList.remove('hidden');
            if (mBlock) mBlock.classList.add('hidden');
            return;
        }

        loadUserName(typingUserId).then(name => {
            const item = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
            if (!item) return;
            const block = item.querySelector('.typing-in-list');
            const meta = item.querySelector('.chat-list-message-meta') ||
                            item.querySelector('.last-message-preview');
            if (!block) return;

            let nameEl = block.querySelector('.typing-name');
            if (!nameEl) {
                block.innerHTML = `<span class="typing-name"></span> печатает...`;
                nameEl = block.querySelector('.typing-name');
            }
            if (nameEl) nameEl.textContent = name || 'Собеседник';

            block.classList.remove('hidden');
            if (meta) meta.classList.add('hidden');
        });
    } else {
        typingBlock.classList.add('hidden');
        if (lastMsgBlock) lastMsgBlock.classList.remove('hidden');
    }
}

function showToast(message, type = 'info', duration = 4000) {
    // тосты отключены по требованию
    return;
}

function toggleEmptyState() {
    const container = document.getElementById('chats-container');
    const emptyState = document.getElementById('empty-state');

    const chatCount = container.querySelectorAll('.chat-item').length;

    if (chatCount > 0) {
        emptyState.classList.add('hidden');
        emptyState.classList.remove('flex');
    } else {
        emptyState.classList.remove('hidden');
        emptyState.classList.add('flex');

        if (typeof feather !== 'undefined') {
            feather.replace();
        }
    }
}

async function joinAllMyChats() {
    if (!connection) return;

    while (connection.state !== signalR.HubConnectionState.Connected) {
        console.log(`Ожидаем Connected... текущее состояние: ${connection.state}`);
        await new Promise(resolve => setTimeout(resolve, 300));
    }

    try {
        const res = await fetch(`${API_BASE}/chats`, {
            headers: { 'Authorization': `Bearer ${token}` }
        });

        if (!res.ok) return;

        const json = await res.json();
        console.log("Чаты для join:", json.data?.length || 0);

        if (json.data) {
            for (const chat of json.data) {
                const chatId = chat.chatId || chat.id;
                if (chatId) {
                    try {
                        await connection.invoke("JoinChat", chatId);
                        console.log(`✓ Joined chat ${chatId}`);
                    } catch (e) {
                        console.warn(`JoinChat failed for ${chatId}:`, e.message);
                    }
                }
            }
        }
    } catch (err) {
        console.error("joinAllMyChats error:", err);
    }
}

async function joinChat(id) {
    if (connection?.state === 'Connected')
        await connection.invoke('JoinChat', id).catch(() => { });
}

async function leaveChat(id) {
    if (connection?.state === 'Connected')
        await connection.invoke('LeaveChat', id).catch(() => { });
}

async function rejoinCurrentChat() {
    if (currentChatId)
        joinChat(currentChatId);
}

function isMobileLayout() {
    return window.matchMedia('(max-width: 767.98px)').matches;
}

function setMobileChatOpen(open) {
    document.body.classList.toggle('mobile-chat-open', !!open);
    if (open) {
        document.body.style.overflow = 'hidden';
    } else {
        document.body.style.overflow = '';
    }
}

async function openChat(item) {
    if (typeof pinnedBarDismissed !== "undefined" && item) {
        const cid = item.dataset?.chatId || item.getAttribute?.("data-chat-id");
        if (cid) pinnedBarDismissed.delete(String(cid));
    }

    if (typeof selectMode !== "undefined" && selectMode) exitSelectMode();
    const chatId = item.dataset.chatId;
    const onlyModal = modalChatId && String(modalChatId) === String(chatId) && String(currentChatId) !== String(chatId);
    if (String(currentChatId) === String(chatId) && !onlyModal) {
        if (isMobileLayout()) setMobileChatOpen(true);
        return;
    }

    saveCurrentDraft();
    currentChatId = chatId;
    modalChatId = null;
    if (typeof setChatUrl === 'function') {
        const pathId = typeof getChatIdFromPath === 'function' ? getChatIdFromPath() : null;
        setChatUrl(chatId, pathId === String(chatId));
    }

    clearUnread(chatId);
    markChatAsRead(chatId);

    document.querySelectorAll('.chat-item').forEach(i => i.classList.remove('active'));
    item.classList.add('active');

    const emptyRight = document.getElementById('empty-right-panel');
    if (emptyRight) {
        emptyRight.classList.add('hidden');
        emptyRight.style.setProperty('display', 'none', 'important');
    }
    const showEl = (id, display) => {
        const el = document.getElementById(id);
        if (!el) return;
        el.classList.remove('hidden');
        el.style.removeProperty('display');
        if (display) el.style.display = display;
    };
    showEl('chat-header', '');
    showEl('messages-wrapper', '');
    // messagesContainer объявляется ниже — здесь только показываем
    {
        const mc = document.getElementById('messages-container');
        if (mc) {
            mc.classList.remove('hidden');
            mc.style.removeProperty('display');
        }
    }
    showEl('chat-input-area', '');

    if (isMobileLayout()) {
        setMobileChatOpen(true);
    }

    const chatNameEl = item.querySelector('h3');
    let openTitle = chatNameEl ? chatNameEl.textContent : 'Чат';
    if (item.dataset.isSelfChat === '1' || item.dataset.isFavorites === '1') {
        openTitle = 'Избранное';
    }
    document.getElementById('chat-title').textContent = openTitle;

    const avatarEl = item.querySelector('img');
    document.getElementById('chat-info-avatar').src = avatarEl ? avatarEl.src : 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png';

    if (typeof exitEditMode === 'function') exitEditMode();
    if (typeof cancelReply === 'function') cancelReply();
    document.getElementById('message-input').value = '';
    document.getElementById('attached-files').innerHTML = '';
    selectedFiles = [];
    restoreDraftForChat(chatId);

    const messagesContainer = document.getElementById('messages-container');
    messagesContainer.innerHTML = '<div class="flex items-center justify-center h-full text-gray-500">Загрузка сообщений...</div>';

    try {
        const res = await fetch(`${API_BASE}/chats/${chatId}`, {
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (res.ok) {
            const json = await res.json();
            if (json.isSuccess && json.data) {
                currentChatInfo = json.data;

                if (currentChatInfo?.type === 'private') {
                    const partner = currentChatInfo.participants?.find(p =>
                        String(p.id || p.userId || "").toLowerCase() !== String(me).toLowerCase()
                    );

                    if (partner) {
                        const partnerId = String(partner.id || partner.userId || "");
                        if (partnerId) {
                            const listItem = document.querySelector(`.chat-item[data-chat-id="${CSS.escape(String(chatId))}"]`);
                            if (listItem) {
                                listItem.dataset.partnerId = partnerId;
                                listItem.dataset.chatType = listItem.dataset.chatType || 'private';
                            }
                            window.__privatePartnerMap = window.__privatePartnerMap || {};
                            window.__privatePartnerMap[String(chatId)] = partnerId;
                        }
                        setTimeout(() => requestPartnerStatus(partnerId), 200);
                    } else {
                        // Чат с самим собой → Избранное
                        const listItem = document.querySelector(`.chat-item[data-chat-id="${CSS.escape(String(chatId))}"]`);
                        if (listItem) {
                            listItem.dataset.isSelfChat = '1';
                            listItem.dataset.isFavorites = '1';
                            listItem.dataset.partnerId = String(me || '');
                            const h3 = listItem.querySelector('h3');
                            if (h3) h3.textContent = 'Избранное';
                        }
                        window.__favoritesChatId = String(chatId);
                        window.__privatePartnerMap = window.__privatePartnerMap || {};
                        window.__privatePartnerMap[String(chatId)] = String(me || 'self');
                        const titleEl = document.getElementById('chat-title');
                        if (titleEl) titleEl.textContent = 'Избранное';
                        currentChatInfo.isSelfChat = true;
                        currentChatInfo.isFavorites = true;
                    }
                }
            }
        }
    } catch (e) {
        console.warn("Не удалось загрузить информацию о чате", e);
    }

    await loadMessages(chatId);

    markChatAsRead(chatId);
    updatePinnedMessageBar();
    highlightPinnedMessageInView();
    joinChat(chatId);

    setTimeout(updateCurrentChatSubtitle, 100);
    setTimeout(updateCurrentChatSubtitle, 400);
    setTimeout(updateCurrentChatSubtitle, 800);
    setTimeout(updateCurrentChatSubtitle, 1300);

    if (pendingStatusUpdate) {
        setTimeout(updateCurrentChatSubtitle, 150);
        pendingStatusUpdate = null;
    }
}

async function markChatAsRead(chatId, attempt = 0) {
    if (!chatId) return;

    try {
        const res = await fetchWithAuth(`${API_BASE}/messages/${chatId}/read`, {
            method: 'POST'
        });

        if (!res) return;

        let updatedCount = 0;
        try {
            const json = await res.clone().json();
            updatedCount = json?.updatedCount ?? json?.UpdatedCount ?? 0;
        } catch { }

        if (res.ok) {
            clearUnread(chatId);
            feather.replace();
        }

        if (updatedCount === 0 && attempt < 2) {
            const delay = 400 * (attempt + 1);
            console.log(`[markChatAsRead] updatedCount=0, retry #${attempt + 1} через ${delay}ms`);
            setTimeout(() => markChatAsRead(chatId, attempt + 1), delay);
        } else {
            console.log(`[markChatAsRead] chat=${chatId} updated=${updatedCount}`);
        }
    } catch (e) {
        console.warn('Не удалось пометить чат как прочитанный', e);
        if (attempt < 2) {
            setTimeout(() => markChatAsRead(chatId, attempt + 1), 500);
        }
    }
}

async function requestPartnerStatus(partnerId) {
    if (!partnerId) return;
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
        setTimeout(() => requestPartnerStatus(partnerId), 800);
        return;
    }
    try {
        await connection.invoke("RequestAndBroadcastUserStatus", partnerId);
    } catch (err) {
        console.warn("Не удалось запросить статус партнёра", err);
    }
}

function refreshVisiblePresence() {
    document.querySelectorAll('.chat-item[data-partner-id]').forEach(el => {
        const pid = el.dataset.partnerId;
        if (pid) requestPartnerStatus(pid);
    });
}

function loadMessagesAndJoin(chatId) {
    loadMessages(chatId);
    joinChat(chatId);
}

setTimeout(async () => {
    await loadChats();
    setTimeout(refreshVisiblePresence, 500);
    setTimeout(refreshVisiblePresence, 2000);
}, 300);

async function hydrateChatListStatuses(chats) {
    if (!Array.isArray(chats) || !chats.length) return;

    const jobs = chats.map(async chat => {
        const chatId = chat?.chatId || chat?.id;
        if (!chatId) return;

        const chatItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
        if (!chatItem) return;

        try {
            const res = await fetchWithAuth(`${API_BASE}/messages/${chatId}?limit=1`, {
                headers: { 'Authorization': `Bearer ${token}` }
            });
            if (!res || !res.ok) return;

            const json = await res.json();
            const messages = Array.isArray(json?.data) ? json.data : (Array.isArray(json) ? json : []);
            if (!messages.length) return;

            const latest = [...messages].sort((a, b) => {
                const sa = Number(a?.sequenceNumber ?? -1);
                const sb = Number(b?.sequenceNumber ?? -1);
                if (sa >= 0 || sb >= 0) return sb - sa;
                const da = parseMessageDate(a?.sentAt || a?.sentTime || a?.createdAt)?.getTime() || 0;
                const db = parseMessageDate(b?.sentAt || b?.sentTime || b?.createdAt)?.getTime() || 0;
                return db - da;
            })[0];

            const senderId = latest?.senderId ?? latest?.userId ?? latest?.fromUserId;
            const isMine = senderId != null && String(senderId) === String(me);
            const sentAt = latest?.sentAt ?? latest?.sentTime ?? latest?.createdAt;
            const status = latest?.status ?? latest?.deliveryStatus ?? latest?.messageStatus
                ?? (latest?.isRead ? 'read' : null)
                ?? (latest?.isDelivered ? 'delivered' : null)
                ?? 'sent';

            const meta = renderChatListMessageMeta(
                latest?.messageText || '',
                latest?.attachments || [],
                sentAt,
                isMine,
                status
            );

            const preview = chatItem.querySelector('.last-message-preview');
            const time = chatItem.querySelector('.chat-list-time');
            if (preview) preview.outerHTML = meta.previewHTML;
            if (time) time.textContent = meta.time || '';

            chatItem.dataset.lastMessageId = latest?.messageId || '';
            chatItem.dataset.lastMessageStatus = String(status || 'sent').toLowerCase();
            chatItem.dataset.lastMessageSenderId = senderId != null ? String(senderId) : '';

            feather.replace();
        } catch (e) {
            console.warn(`[ChatList] Не удалось получить последнее сообщение для ${chatId}`, e);
        }
    });

    await Promise.allSettled(jobs);
}