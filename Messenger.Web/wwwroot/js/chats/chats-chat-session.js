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

function updateAllOnlineIndicators() {
    document.querySelectorAll('.chat-item').forEach(item => {
        const partnerId = item.dataset.partnerId;
        if (!partnerId) return;

        const status = userStatuses.get(partnerId);
        const onlineDot = item.querySelector('.chat-online-dot');

        if (onlineDot) {
            onlineDot.classList.toggle('hidden', !(status?.isOnline === true));
        }
    });
}

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
        loadUserName(typingUserId).then(name => {
            const currentItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
            if (!currentItem) return;
            const tBlock = currentItem.querySelector('.typing-in-list');
            const mBlock = currentItem.querySelector('.chat-list-message-meta') ||
                            currentItem.querySelector('.last-message-preview');
            if (!tBlock) return;

            console.log(`[TYPING LIST] Показываем в списке: "${name}" печатает...`);

            let nameEl = tBlock.querySelector('.typing-name');
            if (!nameEl) {
                tBlock.innerHTML = `<span class="typing-name"></span> печатает...`;
                nameEl = tBlock.querySelector('.typing-name');
            }

            if (nameEl) nameEl.textContent = name || "Собеседник";

            tBlock.classList.remove('hidden');
            if (mBlock) mBlock.classList.add('hidden');
        });
    } else {
        typingBlock.classList.add('hidden');
        if (lastMsgBlock) lastMsgBlock.classList.remove('hidden');
    }
}

function showToast(message, type = 'info', duration = 4000) {
    if (window.__uiShowToast) {
        return window.__uiShowToast(message, type, duration);
    }
    if (message) console.log('[toast:' + (type || 'info') + ']', message);
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

    clearUnread(chatId);
    markChatAsRead(chatId);

    document.querySelectorAll('.chat-item').forEach(i => i.classList.remove('active'));
    item.classList.add('active');

    document.getElementById('empty-right-panel').classList.add('hidden');
    document.getElementById('chat-header').classList.remove('hidden');
    document.getElementById('messages-wrapper').classList.remove('hidden');
    document.getElementById('messages-container').classList.remove('hidden');
    document.getElementById('chat-input-area').classList.remove('hidden');

    if (isMobileLayout()) {
        setMobileChatOpen(true);
    }

    const chatNameEl = item.querySelector('h3');
    document.getElementById('chat-title').textContent = chatNameEl ? chatNameEl.textContent : 'Чат';

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
                        setTimeout(() => requestPartnerStatus(partnerId), 200);
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