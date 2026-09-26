async function startSignalR() {
    if (!token) {
        console.error("Нет токена для SignalR");
        updateConnectionStatus('Нет токена', 'red');
        return;
    }

    async function getFreshToken() {
        let freshToken = (typeof token === 'string' && token) ? token : (localStorage.getItem('token') || '');
        if (!freshToken) {
            console.warn("Токен пропал — редирект");
            window.location.href = "/Authorization/Authorization";
            return null;
        }
        return freshToken;
    }

    connection = new signalR.HubConnectionBuilder()
        .withUrl(HUB_URL, {
            accessTokenFactory: () => getFreshToken()
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .build();

    connection.on('MessagesRead', (data) => {
        if (!data?.chatId) return;

        const chatId = String(data.chatId);
        const readerId = data.readerId ?? data.ReaderId;

        if (String(readerId) !== String(me)) {
            updateChatListStatus(chatId, 'read');
        }

        if (chatId === String(currentChatId) &&
            String(readerId) !== String(me)) {

            document.querySelectorAll('.message-bubble.outgoing .msg-status.sent').forEach(el => {
                el.classList.remove('sent');
                el.classList.add('read');
                el.innerHTML = `
                    <i data-feather="check" class="w-3.5 h-3.5"></i>
                    <i data-feather="check" class="w-3.5 h-3.5 -ml-2"></i>
                `;
                el.title = 'Прочитано';
            });
            feather.replace();
        }
    });

    connection.on('ParticipantCountChanged', (data) => {
        if (!data) return;

        const chatId = String(data.chatId || data.ChatId || "").trim();
        const newCount = parseInt(data.count || data.Count || "0");

        if (!chatId) return;

        console.log(`[PARTICIPANT COUNT] Чат ${chatId} → ${newCount} участников`);

        if (currentChatId === chatId) {
            const subtitleEl = document.getElementById('chat-subtitle');
            if (subtitleEl && currentChatInfo?.type === 'group') {
                subtitleEl.textContent = `${newCount} участников`;
            if (typeof updateChatSubtitleMarquee === 'function') updateChatSubtitleMarquee();
                console.log(`[PARTICIPANT COUNT] Обновлено в шапке чата: ${newCount} участников`);
            }

            if (document.getElementById('chat-info-modal').classList.contains('show')) {
                const countEl = document.getElementById('participants-count');
                if (countEl) countEl.textContent = `(${newCount})`;
            }

            if (currentChatInfo) {
                currentChatInfo.participantsCount = newCount;
            }
        }
    });

    connection.on("UserOnlineStatusChanged", (data) => {
        const uid = String(data.userId || data.UserId || '').trim();
        const isOnline = !!(data.isOnline ?? data.IsOnline ?? data.online ?? data.Online);
        let lastActivity = data.lastActivity ?? data.LastActivity ?? data.lastSeen ?? data.LastSeen ?? null;
        console.log(`[STATUS] ${uid} → ${isOnline ? 'ONLINE' : 'OFFLINE'}`, data);

        const prev = userStatuses.get(uid);
        if (!lastActivity && prev?.lastActivity) {
            lastActivity = prev.lastActivity;
        }

        userStatuses.set(uid, {
            isOnline,
            lastActivity
        });

        if (currentChatId) {
            updateCurrentChatSubtitle();
        }

        if (document.getElementById('chat-info-modal').classList.contains('show') && currentChatInfo?.type === 'group') {
            const userIdStr = data.userId.toString().trim();
            const isOnlineNow = !!data.isOnline;

            const participantEl = document.querySelector(`#participants-list [data-user-id="${userIdStr}"]`);
            if (participantEl) {
                const onlineDot = participantEl.querySelector('.chat-online-dot');
                if (onlineDot) {
                    onlineDot.classList.toggle('hidden', !isOnlineNow);
                }
            }
        }
    });

    connection.on("UserIsTyping", (data) => {
        if (!data?.chatId || !data?.userId) return;

        const typingUserId = String(data.userId).trim();
        const myId = String(me || "").trim();

        console.log(`[TYPING RECEIVED] chat=${data.chatId}, user=${typingUserId}, isTyping=${data.isTyping}, myId=${myId}`);

        if (typingUserId === myId) {
            console.log(`[TYPING] Игнорируем своё печатание`);
            return;
        }

        if (data.chatId === currentChatId) {
            const typingIndicator = document.getElementById('typing-indicator');
            const typingNameEl = document.getElementById('typing-name');
            const subtitle = document.getElementById('chat-subtitle');

            if (data.isTyping) {
                loadUserName(typingUserId).then(name => {
                    console.log(`[TYPING] Показываем: ${name} печатает...`);
                    if (typingNameEl) typingNameEl.textContent = name || "Собеседник";
                    if (typingIndicator) typingIndicator.classList.remove('hidden');
                    if (subtitle) subtitle.classList.add('hidden');
                });
            } else {
                if (typingIndicator) typingIndicator.classList.add('hidden');
                if (subtitle) subtitle.classList.remove('hidden');
            }
        }

        updateTypingInChatList(data.chatId, typingUserId, data.isTyping);
    });

    function normId(id) {
        return String(id || '').toLowerCase().replace(/[{}]/g, '').trim();
    }

    function participantUserId(p) {
        if (!p) return '';
        return p.id || p.Id || p.userId || p.UserId
            || p.user?.id || p.user?.userId || p.User?.Id || '';
    }

    function toAbsoluteAvatarUrl(url) {
        if (!url) return null;
        const u = String(url).trim();
        if (!u) return null;
        if (/^(https?:|data:)/i.test(u)) return u;
        const base = (window.API_BASE_URL || '').replace(/\/api\/?$/i, '').replace(/\/$/, '');
        if (u.startsWith('/') && base) return base + u;
        if (u.startsWith('/')) return window.location.origin + u;
        return u;
    }

    function applyUserAvatarUpdate(data) {
        console.log('[Profile/AvatarUpdated] raw', data);
        if (!data) return;

        const uid = normId(data.userId ?? data.UserId);
        if (!uid) return;

        const meId = normId(me || "");
        const absUrl = toAbsoluteAvatarUrl(data.avatarUrl ?? data.AvatarUrl ?? '');
        const displayName = data.displayName ?? data.DisplayName ?? null;
        const cacheBust = absUrl
            ? (absUrl + (absUrl.includes('?') ? '&' : '?') + 't=' + Date.now())
            : null;
        const fallbackAvatar = 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png';
        const finalSrc = cacheBust || fallbackAvatar;

        const allPartnerIds = Array.from(document.querySelectorAll('.chat-item'))
            .map(el => ({ chatId: el.dataset.chatId, partnerId: el.dataset.partnerId || null }));
        console.log('[Profile/AvatarUpdated] chat-items', allPartnerIds);

        if (uid === meId) {
            const avatarContainer = document.querySelector('.relative.w-10.h-10');
            if (avatarContainer) {
                if (displayName) {
                    const nameEl = avatarContainer.parentElement?.querySelector('p.font-medium');
                    if (nameEl) nameEl.textContent = displayName;
                }
                if (cacheBust) {
                    let img = document.getElementById('current-user-avatar');
                    if (!img) {
                        document.getElementById('current-user-avatar-placeholder')?.remove();
                        img = document.createElement('img');
                        img.id = 'current-user-avatar';
                        img.className = 'w-10 h-10 rounded-full object-cover shadow-md';
                        img.alt = 'Аватар';
                        avatarContainer.appendChild(img);
                    }
                    img.src = cacheBust;
                }
            }
        }

        let updatedList = 0;
        document.querySelectorAll('.chat-item').forEach(item => {
            if (normId(item.dataset.partnerId) !== uid) return;
            const img = item.querySelector('img');
            if (img) img.src = finalSrc;
            if (displayName) {
                const h3 = item.querySelector('h3');
                if (h3) h3.textContent = displayName;
            }
            updatedList++;
        });

        if (Array.isArray(allChats)) {
            allChats.forEach(chat => {
                const chatId = String(chat.chatId || chat.ChatId || chat.id || chat.Id || '');
                const participants = chat.participants || chat.Participants || [];
                const type = (chat.type || chat.Type || '').toLowerCase();
                const isPrivate = type === 'private' || chat.isPrivate === true;

                if (!participants.some(p => normId(participantUserId(p)) === uid)) return;

                const item = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
                if (!item) return;

                if (isPrivate) item.dataset.partnerId = uid;

                const img = item.querySelector('img');
                if (img) img.src = finalSrc;
                if (displayName && isPrivate) {
                    const h3 = item.querySelector('h3');
                    if (h3) h3.textContent = displayName;
                }
                updatedList++;
            });
        }

        if (currentChatInfo) {
            const parts = currentChatInfo.participants || currentChatInfo.Participants || [];
            const type = (currentChatInfo.type || currentChatInfo.Type || '').toLowerCase();
            const partner = parts.find(p => normId(participantUserId(p)) === uid);

            if (partner || (type === 'private' && uid !== meId)) {
                const headerAvatar = document.getElementById('chat-info-avatar');
                if (headerAvatar) headerAvatar.src = finalSrc;
                if (displayName) {
                    const title = document.getElementById('chat-title');
                    if (title) title.textContent = displayName;
                }
                if (partner) {
                    partner.fullName = displayName || partner.fullName;
                    partner.avatar = absUrl || null;
                }
            }
        }

        document.querySelectorAll('#participants-list [data-user-id]').forEach(row => {
            if (normId(row.dataset.userId) !== uid) return;
            const img = row.querySelector('img');
            if (img) img.src = finalSrc;
            if (displayName) {
                const nameP = row.querySelector('p.font-semibold');
                if (nameP) nameP.textContent = displayName;
            }
        });

        console.log(`[Profile/AvatarUpdated] uid=${uid} me=${meId} listUpdated=${updatedList} url=${absUrl || '(none)'}`);
    }

    connection.on('ProfileUpdated', applyUserAvatarUpdate);
    connection.on('AvatarUpdated', applyUserAvatarUpdate);

    connection.on('MessageSendingStatus', (data) => {
        const msgId = data.MessageId || data.messageId;
        let status = (data.Status || data.status || '').toLowerCase();
        const reason = data.Reason || data.reason;

        console.log(`[Status] ${msgId} → ${status}`, data);

        let element = document.querySelector(`[data-mid="${msgId}"]`);
        if (!element) {
            for (const [tempId, info] of pendingMessages) {
                if (info.serverMessageId === msgId) {
                    element = document.querySelector(`[data-mid="${tempId}"]`);
                    console.log(`[Status] Найдено по pendingMessages: ${tempId} → ${msgId}`);
                    break;
                }
            }
        }

        if (element) {
            if (status === 'sent' || status === 'delivered') {
                updateMessageStatus(msgId, 'sent');
                const statusChatId = element.closest('.chat-item')?.dataset?.chatId || currentChatId;
                if (statusChatId) updateChatListStatus(statusChatId, status === 'delivered' ? 'sent' : 'sent');
                console.log(`[Status] Обновлено на Sent для ${msgId}`);
            } else if (status === 'failed') {
                updateMessageStatus(msgId, 'failed', reason || 'Ошибка');
            }
        } else {
            console.warn(`[Status] Элемент не найден для ${msgId} (уже удалён ReceiveMessage?)`);
        }
    });

    const seenMessageIds = window.__seenMessageIds || (window.__seenMessageIds = new Set());

    connection.on('MessageDeleted', (data) => {
        const mid = data?.messageId || data?.MessageId || data;
        if (!mid) return;
        const chatId = data?.chatId || data?.ChatId || currentChatId;
        removeMessageFromUI(mid, chatId);

        if (typeof GuapNotify !== 'undefined' && GuapNotify.closeMessageNotification) {
            GuapNotify.closeMessageNotification(mid);
        }

        const open = currentChatId != null && String(currentChatId).toLowerCase() === String(chatId).toLowerCase();
        if (!open) {
            if (typeof decrementUnread === 'function') {
                decrementUnread(chatId, 1, mid);
            }
        }

        if (typeof refreshChatListPreviewFromDom === 'function') {
            refreshChatListPreviewFromDom(chatId);
        }
    });

    connection.on('ReactionUpdated', (data) => {
        if (!data) return;
        const mid = data.messageId || data.MessageId;
        const uid = data.userId || data.UserId;
        const emoji = data.reactionType || data.ReactionType || null;
        const action = (data.action || data.Action || 'add').toLowerCase();
        if (!mid || !uid) return;
        if (String(uid) === String(me)) return;
        if (action === 'add' && emoji) {
            applyReactionLocally(mid, uid, emoji, 'add');
        } else {
            refreshMessageReactions(mid);
        }
    });

    connection.on('ReceiveMessage', (msg) => {
        if (!msg?.messageId) return;
        if (typeof isMessageHiddenForMe === 'function' && isMessageHiddenForMe(msg.messageId)) return;

        const mid = String(msg.messageId);
        const isMyMessage = String(msg.senderId || msg.SenderId) === String(me);
        const chatId = msg.chatId || msg.ChatId;
        const msgText = msg.messageText ?? msg.MessageText ?? '';

        if (seenMessageIds.has(mid) || document.querySelector(`[data-mid="${mid}"]`)) {
            console.log(`[ReceiveMessage] обновление существующего ${mid}`);
            if (msgText !== undefined && msgText !== null) {
                updateExistingMessageText(mid, msgText);
            }
            const chatItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
            const lastMid = chatItem?.dataset?.lastMessageId;
            const rows = (String(currentChatId) === String(chatId))
                ? [...messagesContainer.querySelectorAll('[data-mid]:not([data-mid^="temp-"])')]
                : [];
            const isLastInOpenChat = rows.length && rows[rows.length - 1].dataset.mid === mid;
            if (isLastInOpenChat || !lastMid || String(lastMid) === mid) {
                updateChatLastMessagePreview(
                    chatId,
                    msgText,
                    msg.attachments || msg.Attachments || [],
                    msg.sentAt || msg.SentAt || msg.sendTime || msg.SendTime,
                    isMyMessage,
                    isMyMessage ? (msg.status || msg.Status || 'sent') : null
                );
                if (chatItem) chatItem.dataset.lastMessageId = mid;
            }
            return;
        }

        seenMessageIds.add(mid);
        if (seenMessageIds.size > 500) {
            const first = seenMessageIds.values().next().value;
            seenMessageIds.delete(first);
        }

        console.log(`[ReceiveMessage] ${mid} в чате ${chatId}`);

        const isCurrentChat = String(chatId) === String(currentChatId);

        updateChatLastMessagePreview(
            msg.chatId,
            msg.messageText || '',
            msg.attachments || [],
            msg.sentAt || msg.sentTime,
            isMyMessage,
            msg.status || msg.deliveryStatus || msg.messageStatus || 'sent'
        );

        bumpChatToTop(msg.chatId || chatId);

        if (!isMyMessage && typeof GuapNotify !== 'undefined' && GuapNotify.onIncomingMessage) {
            GuapNotify.onIncomingMessage(msg);
        }

        if (!isCurrentChat) {
            if (!isMyMessage) {
                incrementUnread(msg.chatId || chatId, mid);
            }
            return;
        }

        if (!isMyMessage) {
            setTimeout(() => markChatAsRead(msg.chatId), 300);
        }

        let removedOptimistic = false;
        for (const [tempId, meta] of pendingMessages.entries()) {
            if (meta.serverMessageId && String(meta.serverMessageId) === mid) {
                const el = document.querySelector(`[data-mid="${tempId}"]`);
                if (el) el.remove();
                pendingMessages.delete(tempId);
                removedOptimistic = true;
                break;
            }
        }
        if (!removedOptimistic && isMyMessage) {
            const firstTemp = document.querySelector('[data-mid^="temp-"]');
            if (firstTemp) {
                pendingMessages.delete(firstTemp.dataset.mid);
                firstTemp.remove();
            }
        }

        if (document.querySelector(`[data-mid="${mid}"]`)) {
            console.log(`[ReceiveMessage] Сообщение ${mid} уже показано — обновляем текст`);
            if (msgText !== undefined && msgText !== null) {
                updateExistingMessageText(mid, msgText);
            }
            return;
        }

        appendMessage(msg);
        const chatItemAfter = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
        if (chatItemAfter) chatItemAfter.dataset.lastMessageId = mid;

        if (isMyMessage) {
            clearInputAfterSend();
        }

        clearUnread(msg.chatId);
    });

    connection.on('MessageDeliveryFailed', data => {
        if (data.chatId !== currentChatId) return;

        const tempEl = document.querySelector(`[data-mid="${data.messageId}"]`) ||
            document.querySelector(`[data-mid^="temp-"]`);

        if (tempEl) {
            const statusContainer = tempEl.querySelector('div[id^="status-"]') || tempEl.querySelector('.opacity-75');
            if (statusContainer) {
                statusContainer.innerHTML = `<span class="text-red-400 font-medium">Ошибка отправки</span>`;
            }
            tempEl.style.opacity = "0.6";
        }

        showToast('Сообщение не удалось доставить на сервер', 'error');
    });

    connection.on('ReceiveSearchResults', users => {
        userSearchResults.innerHTML = "";

        if (!users || users.length === 0) {
            userSearchResults.innerHTML = "<div class='p-2 text-muted'>Нет пользователей</div>";
            return;
        }

        users.forEach(user => {
            const userDiv = document.createElement("div");
            userDiv.className = "user-search-item p-2 border-bottom";
            userDiv.style.cursor = "pointer";
            userDiv.dataset.userId = user.id;

            userDiv.innerHTML = `
                <div class="d-flex align-items-center">
                    <img src="/avatars/${user.avatarPath || 'default.png'}"
                            class="rounded-circle me-2" width="32" height="32">
                    <span>${user.fullName}</span>
                </div>
            `;

            userDiv.onclick = () => {
                toggleUserSelection(user.id, user.fullName);
            };

            userSearchResults.appendChild(userDiv);
        });
    });

    connection.on('NewChat', chat => {
        const container = document.getElementById('chats-container');
        const existing = container.querySelector(`.chat-item[data-chat-id="${chat.chatId}"]`);

        if (!existing) {
            let displayName = chat.name || 'Новый чат';
            let displayAvatar = chat.avatar || 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png';
            let partnerId = null;

            if (chat.type === 'private' || chat.isPrivate) {
                const currentUserId = (me || "").toLowerCase();
                if (chat.participants && chat.participants.length > 0) {
                    const interlocutor = chat.participants.find(p =>
                        (p.id && p.id.toLowerCase() !== currentUserId) ||
                        (p.userId && p.userId.toLowerCase() !== currentUserId)
                    );

                    if (interlocutor) {
                        displayName = interlocutor.fullName || interlocutor.name || displayName;
                        displayAvatar = interlocutor.avatar || interlocutor.avatarPath || displayAvatar;
                        partnerId = interlocutor.id || interlocutor.userId;
                    }
                }
            }

            const div = document.createElement('div');
            div.className = 'p-4 chat-item flex items-center space-x-3 cursor-pointer hover:bg-gray-100 transition rounded-lg mx-2';
            div.dataset.chatId = chat.chatId;
            if (partnerId) div.dataset.partnerId = String(partnerId);
            const newChatMeta = renderChatListMessageMeta('Чат создан', [], new Date(), false);

            div.innerHTML = `
                <div class="relative flex-shrink-0">
                    <img src="${displayAvatar}" class="w-12 h-12 rounded-full object-cover">
                    <span class="chat-online-dot absolute bottom-0 right-0 w-3.5 h-3.5 bg-green-500 rounded-full border-2 border-white hidden"></span>
                </div>
                <div class="flex-1 min-w-0">
                    <div class="flex items-center gap-2">
                        <h3 class="font-medium truncate flex-1 min-w-0">${displayName}</h3>
                        <span class="chat-list-time">${newChatMeta.time}</span>
                        <span class="unread-badge hidden flex-shrink-0"></span>
                    </div>
                    <div class="text-sm text-gray-500 truncate chat-list-message-meta">
                        ${newChatMeta.previewHTML}
                    </div>
                    <div class="typing-in-list hidden text-xs text-blue-600 font-medium mt-0.5">
                        <span class="typing-name"></span> печатает...
                    </div>
                </div>
            `;

            div.onclick = () => openChat(div);

            container.prepend(div);

            toggleEmptyState();

            cacheChatItems();
            filterChats();
            feather.replace();

            connection.invoke("JoinChat", chat.chatId).catch(err => console.error(err));
        }
    });

    connection.on('YouWereRemovedFromChat', (data) => {
        chatId = String(data?.chatId || data?.ChatId || data || "").trim();

        if (!chatId) {
            console.warn("[YouWereRemovedFromChat] Получен пустой chatId");
            return;
        }

        console.log(`[KICKED] Вас удалили из чата ${chatId}`);

        const chatItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
        if (chatItem) {
            chatItem.remove();
        }

        if (currentChatId === chatId) {
            if (!hasBeenKickedNotificationShown) {
                hasBeenKickedNotificationShown = true;
                showToast("Вы были удалены из чата", "info", 5000);
            }

            setTimeout(() => {
                closeCurrentChat();
                hasBeenKickedNotificationShown = false;
            }, 700);
        }

        setTimeout(() => {
            loadChats();
        }, 400);

        if (connection?.state === signalR.HubConnectionState.Connected) {
            connection.invoke("LeaveChat", chatId).catch(() => { });
        }
    });

    connection.on('ParticipantRemoved', ({ chatId, userId }) => {
        if (chatId !== currentChatId) return;

        const el = document.querySelector(`#participants-list [data-user-id="${userId}"]`);
        if (el) el.remove();

        if (currentChatInfo) {
            currentChatInfo.participants = currentChatInfo.participants.filter(p => p.id !== userId);
            document.getElementById('participants-count').textContent = `(${currentChatInfo.participants.length})`;
        }
    });

    connection.on('ParticipantAdded', ({ chatId, user }) => {
        if (chatId !== currentChatId) return;

        const addedName = user?.name || user?.fullName || user?.login || 'Новый участник';
        showToast(`${addedName} добавлен в чат`, 'success');

        if (document.getElementById('chat-info-modal').classList.contains('show')) {
            if (currentChatInfo && !currentChatInfo.participants.some(p => p.id === user.id)) {
                currentChatInfo.participants.push(user);

                const list = document.getElementById('participants-list');
                const div = document.createElement('div');
                div.className = 'flex items-center justify-between p-4 bg-gray-50 rounded-xl';
                div.dataset.userId = user.id;
                div.innerHTML = `
                    <div class="flex items-center gap-4">
                        <img src="${user.avatar || 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png'}"
                                class="w-12 h-12 rounded-full object-cover">
                        <div>
                            <p class="font-semibold">${addedName}</p>
                        </div>
                    </div>
                    <button type="button" class="text-red-600 hover:bg-red-100 rounded-full p-2 transition"
                            onclick="removeParticipantFromChat('${user.id}', this)">
                        <i data-feather="x" class="w-5 h-5"></i>
                    </button>
                `;
                list.appendChild(div);
                feather.replace();

                document.getElementById('participants-count').textContent =
                    `(${currentChatInfo.participants.length})`;
            }
        }
    });

    connection.on('ChatUpdated', ({ chatId, name }) => {
        const item = document.querySelector(`.chat-item[data-chat-id="${chatId}"] h3`);
        if (item) item.textContent = name;

        if (currentChatId === chatId) {
            document.getElementById('chat-title').textContent = name;
        }

        showToast(`Название чата изменено на: ${name}`, 'info');
    });

    connection.on('ChatDeleted', (chatId) => {
        const chatElement = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
        if (chatElement) {
            chatElement.remove();
            toggleEmptyState();
        }

        if (currentChatId === chatId) {
            currentChatId = null;
            currentChatInfo = null;
            messagesContainer.innerHTML = `
                <div class="flex items-center justify-center h-full text-gray-500">
                    <p>Чат был удалён</p>
                </div>`;
            document.getElementById('chat-title').textContent = 'Выберите чат';
        }

        cacheChatItems();
        filterChats();
    });

    connection.onreconnecting(() => updateConnectionStatus('Переподключение...', 'orange'));
    connection.onreconnected(async () => {
        updateConnectionStatus('Онлайн', 'green');
        await rejoinCurrentChat();
        try { await joinAllMyChats(); } catch (e) { console.warn(e); }
        document.querySelectorAll('.chat-item[data-partner-id]').forEach(el => {
            const pid = el.dataset.partnerId;
            if (pid) requestPartnerStatus(pid);
        });
        if (currentChatId && currentChatInfo?.type === 'private') {
            const partner = currentChatInfo.participants?.find(p =>
                String(p.id || p.userId || '').toLowerCase() !== String(me).toLowerCase()
            );
            if (partner) requestPartnerStatus(String(partner.id || partner.userId || ''));
        }
    });
    connection.onclose(() => { updateConnectionStatus('Отключено', 'red'); setTimeout(startSignalR, 5000); });

    try {
        await connection.start();
        console.log("SignalR успешно подключён");

        updateConnectionStatus('Онлайн', 'green');

        setTimeout(async () => {
            console.log("Запуск joinAllMyChats...");
            await joinAllMyChats();

            if (currentChatId) {
                setTimeout(updateCurrentChatSubtitle, 300);
            }
        }, 1500);

    } catch (err) {
        console.error("Ошибка подключения SignalR:", err);
        updateConnectionStatus('Ошибка подключения', 'red');

        setTimeout(startSignalR, 5000);
    }
}

function updateAllOnlineIndicators() {
    document.querySelectorAll('.chat-item').forEach(item => {
    });
}

function updateCurrentChatSubtitle() {
    if (!currentChatId || !currentChatInfo) {
        console.log("updateCurrentChatSubtitle: пропуск — нет currentChatId или currentChatInfo");
        return;
    }

    const subtitle = document.getElementById('chat-subtitle');
    const onlineDot = document.getElementById('chat-online-indicator');

    if (currentChatInfo.type !== 'private') {
        if (subtitle) subtitle.textContent = `${currentChatInfo.participants?.length || 0} участников`;
            if (typeof updateChatSubtitleMarquee === 'function') updateChatSubtitleMarquee();
        if (onlineDot) onlineDot.classList.add('hidden');
        return;
    }

    const partner = currentChatInfo.participants?.find(p => {
        const pid = String(p.id || p.userId || "").toLowerCase().trim();
        return pid !== String(me).toLowerCase().trim();
    });

    if (!partner) {
        console.log("updateCurrentChatSubtitle: собеседник не найден в currentChatInfo");
        return;
    }

    const partnerId = String(partner.id || partner.userId || "").trim();
    let status = userStatuses.get(partnerId)
        || userStatuses.get(partnerId.toLowerCase())
        || userStatuses.get(partnerId.toUpperCase());
    if (!status) {
        for (const [k, v] of userStatuses.entries()) {
            if (String(k).toLowerCase() === partnerId.toLowerCase()) {
                status = v;
                break;
            }
        }
    }

    const partnerLast =
        partner.lastActivity || partner.LastActivity ||
        partner.lastSeen || partner.LastSeen ||
        partner.lastOnline || partner.LastOnline || null;

    const lastActivity = status?.lastActivity || status?.LastActivity || partnerLast || null;
    const isOnline = status?.isOnline === true || status?.IsOnline === true;

    console.log(`[Subtitle Update] Партнёр ${partnerId} → online=${isOnline}, lastActivity=`, lastActivity, status);

    if (onlineDot) {
        onlineDot.classList.toggle('hidden', !isOnline);
    }

    if (!subtitle) return;

    if (isOnline) {
        subtitle.textContent = "в сети";
            if (typeof updateChatSubtitleMarquee === 'function') updateChatSubtitleMarquee();
        subtitle.classList.add('text-green-600', 'font-medium');
    } else if (lastActivity) {
        subtitle.textContent = `был(а) ${formatLastSeen(lastActivity)}`;
            if (typeof updateChatSubtitleMarquee === 'function') updateChatSubtitleMarquee();
        subtitle.classList.remove('text-green-600', 'font-medium');
    } else {
        subtitle.textContent = "был(а) недавно";
            if (typeof updateChatSubtitleMarquee === 'function') updateChatSubtitleMarquee();
        subtitle.classList.remove('text-green-600', 'font-medium');
    }
}

function parseActivityDate(value) {
    if (value == null || value === '') return null;
    if (typeof value === 'number') {
        const d = new Date(value < 1e12 ? value * 1000 : value);
        return isNaN(d.getTime()) ? null : d;
    }
    let s = String(value).trim();
    if (/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}/.test(s)) {
        s = s.replace(' ', 'T');
    }
    const d = new Date(s);
    if (!isNaN(d.getTime())) return d;
    const d2 = new Date(s.endsWith('Z') ? s : s + 'Z');
    return isNaN(d2.getTime()) ? null : d2;
}

function formatLastSeen(isoString) {
    const date = parseActivityDate(isoString);
    if (!date) return "недавно";

    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    if (diffMs < 0) return "только что";

    const diffMinutes = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMinutes / 60);

    if (diffMinutes < 1) return "только что";
    if (diffMinutes < 60) return `${diffMinutes} мин назад`;
    if (diffHours < 6) return `${diffHours} ч назад`;

    const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const startOfYesterday = new Date(startOfToday);
    startOfYesterday.setDate(startOfYesterday.getDate() - 1);

    const hours = date.getHours().toString().padStart(2, '0');
    const minutes = date.getMinutes().toString().padStart(2, '0');
    const timeStr = `${hours}:${minutes}`;

    if (date >= startOfToday) {
        return `сегодня в ${timeStr}`;
    }
    if (date >= startOfYesterday) {
        return `вчера в ${timeStr}`;
    }

    const day = date.getDate().toString().padStart(2, '0');
    const month = (date.getMonth() + 1).toString().padStart(2, '0');
    const year = date.getFullYear();
    if (year === now.getFullYear()) {
        return `${day}.${month} в ${timeStr}`;
    }
    return `${day}.${month}.${year} в ${timeStr}`;
}

let typingTimeout = null;

messageInput.addEventListener('input', () => {
    if (currentChatId && !editingMessageId) {
        scheduleCurrentDraftSave();
    }

    if (!connection || !currentChatId) return;

    const hasText = messageInput.value.trim().length > 0;
    const now = Date.now();

    if (hasText && (now - lastTypingSentAt) >= TYPING_THROTTLE_MS) {
        lastTypingSentAt = now;
        connection.invoke("StartTyping", currentChatId).catch(() => { });
    }

    clearTimeout(typingTimeout);
    typingTimeout = setTimeout(() => {
        if (connection && currentChatId) {
            connection.invoke("StopTyping", currentChatId).catch(() => { });
        }
    }, 1500);
});

const originalSendMessage = sendMessage;