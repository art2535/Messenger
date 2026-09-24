document.addEventListener("DOMContentLoaded", () => {
    loadAvatar();
    loadChats();
    startSignalR();

    document.getElementById('empty-right-panel').classList.remove('hidden');
    document.getElementById('chat-header').classList.add('hidden');
    document.getElementById('messages-wrapper').classList.add('hidden');
    document.getElementById('messages-container').classList.add('hidden');
    document.getElementById('chat-input-area').classList.add('hidden');
});

function cacheChatItems() {
    allChatItems = Array.from(chatsList.querySelectorAll('.chat-item'));
}

function filterChats() {
    const query = document.getElementById('chat-search').value.trim().toLowerCase();
    document.getElementById('clear-search').classList.toggle('hidden', query === '');
    allChatItems.forEach(item => {
        const title = (item.querySelector('h3')?.textContent || '').toLowerCase();
        const lastMsg = (item.querySelector('.last-message-preview')?.textContent || item.querySelector('p')?.textContent || '').toLowerCase();
        item.style.display = (title.includes(query) || lastMsg.includes(query)) ? '' : 'none';
    });
}
document.getElementById('chat-search').addEventListener('input', filterChats);
document.getElementById('clear-search').addEventListener('click', () => {
    document.getElementById('chat-search').value = '';
    document.getElementById('chat-search').focus();
    filterChats();
});

function renderAttachments(attachments = [], isMyMessage = false) {
    if (!attachments?.length) return '';

    const textColor = isMyMessage ? 'text-blue-100 hover:text-white' : 'text-blue-600 hover:text-blue-800';
    const bgColor = isMyMessage ? 'bg-blue-500/20' : 'bg-black/5';

    const images = [];
    const files = [];

    attachments.forEach(att => {
        const fileNameFromUrl = att.url ? att.url.split('/').pop() : (att.fileName || 'file');
        const isImage = att.fileType?.startsWith('image/') || /\.(jpg|jpeg|png|gif|webp|bmp)$/i.test(fileNameFromUrl);
        const displayUrl = att.url?.startsWith('http') ? att.url : `/uploads/${fileNameFromUrl}`;
        const downloadUrl = `${displayUrl}?download=1`;
        const size = att.sizeInBytes ? (att.sizeInBytes / 1024).toFixed(1) + ' КБ' : '';

        if (isImage) {
            images.push(`
                <img src="${displayUrl}"
                        class="image-preview cursor-zoom-in rounded-xl object-cover"
                        style="max-width: 220px; max-height: 220px;"
                        alt="${att.fileName || 'Изображение'}"
                        onclick="openLightbox('${displayUrl}')">
            `);
        } else {
            files.push(`
                <div class="file-attachment ${bgColor} mt-1">
                    <i data-feather="file" class="w-5 h-5 flex-shrink-0"></i>
                    <a href="${downloadUrl}"
                        target="_blank"
                        class="${textColor} hover:underline font-medium truncate max-w-[180px]"
                        title="${att.fileName || fileNameFromUrl}">
                        ${att.fileName || fileNameFromUrl}
                        ${size ? `<span class="text-xs opacity-70 ml-1">(${size})</span>` : ''}
                    </a>
                </div>
            `);
        }
    });

    let html = '';
    if (images.length > 0) {
        html += `<div class="flex flex-wrap gap-2 mt-2">${images.join('')}</div>`;
    }
    if (files.length > 0) {
        html += `<div class="flex flex-col gap-1.5 mt-2">${files.join('')}</div>`;
    }
    return html;
}

function openLightbox(url) {
    lightboxImg.src = url;
    lightbox.classList.add('show');
}

function updateExistingMessageText(messageId, newText, markEdited = false) {
    const el = document.querySelector(`[data-mid="${messageId}"]`);
    if (!el) return false;
    const bubble = el.querySelector('.message-bubble');
    if (!bubble) return false;

    const parsedUpd = parseReplyPayload(newText == null ? '' : String(newText));
    const p = bubble.querySelector('p');
    const oldText = p ? (p.innerText || p.textContent || '').trim() : '';
    const nextText = (parsedUpd.text || '').trim();
    if (parsedUpd.reply) {
        let q = bubble.querySelector('.reply-quote');
        if (!q) {
            q = document.createElement('div');
            q.className = 'reply-quote';
            q.innerHTML = '<div class="reply-quote-name"></div><div class="reply-quote-text"></div>';
            bubble.insertBefore(q, bubble.firstChild);
        }
        fillReplyQuoteInRow(el, parsedUpd.reply);
    }

    const shouldMarkEdited = markEdited === true || (oldText.length > 0 && nextText !== oldText);

    if (p) {
        p.innerHTML = nextText.replace(/\n/g, '<br>');
    } else if (nextText) {
        const timeRow = bubble.querySelector('[class*="text-[11px]"]') || bubble.lastElementChild;
        const pEl = document.createElement('p');
        pEl.className = 'break-words whitespace-pre-wrap leading-relaxed text-[15px]';
        pEl.innerHTML = nextText.replace(/\n/g, '<br>');
        if (timeRow) bubble.insertBefore(pEl, timeRow);
        else bubble.prepend(pEl);
    }

    if (shouldMarkEdited) {
        const timeRow = bubble.querySelector('[class*="text-[11px]"]');
        if (timeRow && !timeRow.querySelector('.message-edited-label')) {
            const label = document.createElement('span');
            label.className = 'message-edited-label';
            label.textContent = 'изм.';
            timeRow.insertBefore(label, timeRow.firstChild);
        }
    }
    return true;
}

function appendMessage(msg) {
    if (msg.messageId && document.querySelector(`[data-mid="${msg.messageId}"]`)) {
        if (msg.messageText !== undefined && msg.messageText !== null) {
            updateExistingMessageText(msg.messageId, msg.messageText);
        }
        return;
    }

    const isMyMessage = String(msg.senderId) === String(me);

    updateChatLastMessagePreview(
        msg.chatId,
        msg.messageText || '',
        msg.attachments || [],
        msg.sentAt || msg.sentTime,
        isMyMessage,
        msg.status || msg.deliveryStatus || msg.messageStatus || 'sent'
    );

    removeEmptyStateIfNeeded();

    let displayText = msg.messageText || "";
    const _parsedReply = parseReplyPayload(displayText);
    displayText = _parsedReply.text || '';
    const replyMeta = _parsedReply.reply;

    if (displayText && displayText.length > 20 && !displayText.includes(' ') && !displayText.includes('\n') &&
        /^[A-Za-z0-9+/=]+$/.test(displayText)) {
        displayText = "[Сообщение]";
    }

    const isGroupChat = currentChatInfo?.type === 'group';

    appendDateSeparatorIfNeeded(msg.sentAt || msg.sentTime);

    const senderId = msg.senderId || msg.SenderId || '';
    const grouped = isSameSenderAsPrevious(senderId);
    const showAvatar = !!isGroupChat;
    const avatarUrl = showAvatar ? getSenderAvatarUrl(senderId) : '';

    const div = document.createElement('div');
    div.className = `message-row ${isMyMessage ? 'outgoing-row' : 'incoming-row'}${grouped ? ' grouped' : ''}`;
    if (msg.messageId) div.dataset.mid = msg.messageId;
    if (senderId) div.dataset.senderId = String(senderId);

    const time = formatTimeOnly(msg.sentAt || msg.sentTime);

    let senderNameHtml = '';
    if (isGroupChat && !isMyMessage && !grouped) {
        senderNameHtml = `<span class="sender-name font-medium text-gray-700 dark:text-gray-300 text-xs mb-1 block px-1">Загрузка...</span>`;
    }

    const statusHtml = isMyMessage
        ? getStatusIconHtml(msg.status || msg.deliveryStatus || 'sent', true)
        : '';

    const avatarHtml = showAvatar
        ? `<img class="msg-avatar" src="${avatarUrl}" alt="" loading="lazy" onerror="this.src='${DEFAULT_MSG_AVATAR}'">`
        : '';

    const reactionsHtml = renderReactionsHtml(msg.reactions || msg.Reactions || [], msg.messageId);

    div.innerHTML = `
        ${!isMyMessage ? avatarHtml : ''}
        <div class="message-col">
            ${senderNameHtml}
            <div class="message-bubble ${isMyMessage ? 'outgoing' : 'incoming'} px-3.5 py-2 shadow-sm">
                ${replyMeta ? `<div class="reply-quote" data-reply-to="${replyMeta.messageId || ''}"><div class="reply-quote-name"></div><div class="reply-quote-text"></div></div>` : ''}
                ${displayText ? `<p class="break-words whitespace-pre-wrap leading-relaxed text-[15px]">${displayText.replace(/\n/g, '<br>')}</p>` : ''}
                ${renderAttachments(msg.attachments || [], isMyMessage)}
                <div class="text-[11px] mt-1 ${isMyMessage ? 'text-blue-100 text-right' : 'text-gray-500 text-right'} opacity-80 flex items-center justify-end gap-1">
                    ${time}
                    ${statusHtml}
                    ${isGroupChat && isMyMessage ? ' · Вы' : ''}
                </div>
            </div>
            ${reactionsHtml}
        </div>
        ${isMyMessage ? avatarHtml : ''}
    `;

    messagesContainer.appendChild(div);
    if (replyMeta) fillReplyQuoteInRow(div, replyMeta);
    feather.replace();

    if (isGroupChat && !isMyMessage && !grouped) {
        loadUserName(msg.senderId).then(name => {
            const nameEl = div.querySelector('.sender-name');
            if (nameEl) nameEl.textContent = name;
        });
    }

    scrollToBottom();
}

async function loadUserName(userId) {
    if (!userId) return "Пользователь";

    const normalizedId = String(userId).trim();

    if (normalizedId !== String(me || "").trim()) {
        userNameCache.delete(normalizedId);
    }

    if (userNameCache.has(normalizedId)) {
        const cachedName = userNameCache.get(normalizedId);
        console.log(`[loadUserName] Из кэша для ${normalizedId}: ${cachedName}`);
        return cachedName;
    }

    console.log(`[loadUserName] Запрашиваем имя с сервера для ${normalizedId}`);

    try {
        const res = await fetch(`${API_BASE}/users/${normalizedId}/name`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Cache-Control': 'no-cache'
            }
        });

        if (res.ok) {
            const json = await res.json();
            if (json.isSuccess && json.data) {
                const name = json.data.trim();
                userNameCache.set(normalizedId, name);
                console.log(`[loadUserName] Успешно получено: ${name} для ${normalizedId}`);
                return name;
            }
        }
    } catch (e) {
        console.warn(`[loadUserName] Ошибка запроса имени для ${normalizedId}`, e);
    }

    userNameCache.set(normalizedId, 'Пользователь');
    return 'Пользователь';
}

function scrollToBottom() {
    const container = document.getElementById('messages-container');
    if (!container) return;

    container.scrollTop = container.scrollHeight;

    requestAnimationFrame(() => {
        container.scrollTop = container.scrollHeight;
    });
}

function decodeHtml(html) {
    const txt = document.createElement("textarea");
    txt.innerHTML = html;
    return txt.value;
}

function getStatusIconHtml(status, isMyMessage) {
    if (!isMyMessage) return '';

    const s = (status || '').toLowerCase();

    if (s === 'read') {
        return `<span class="msg-status read" title="Прочитано">
            <i data-feather="check" class="w-3.5 h-3.5"></i>
            <i data-feather="check" class="w-3.5 h-3.5 -ml-2"></i>
        </span>`;
    }
    if (s === 'delivered' || s === 'sent') {
        return `<span class="msg-status sent" title="Доставлено">
            <i data-feather="check" class="w-3.5 h-3.5"></i>
        </span>`;
    }
    return `<span class="msg-status pending">
        <i data-feather="clock" class="w-3 h-3"></i>
    </span>`;
}

function renderReactionsHtml(reactions, messageId) {
    if (!reactions || !reactions.length) {
        return `<div class="message-reactions" data-message-id="${messageId || ''}"></div>`;
    }
    const chips = reactions.map(r => {
        const type = r.reactionType || r.ReactionType || '';
        const count = r.count || r.Count || (r.users || r.Users || []).length || 1;
        const users = r.users || r.Users || [];
        const isMine = users.some(u => String(u.userId || u.UserId) === String(me));
        const title = users.map(u => u.userName || u.UserName || 'Пользователь').join(', ') || type;
        return `<button type="button" class="reaction-chip${isMine ? ' mine' : ''}" data-emoji="${type}" title="${String(title).replace(/"/g, '&quot;')}" onclick="toggleReaction('${messageId}', '${type}')"><span>${type}</span>${count > 1 ? `<span class="reaction-count">${count}</span>` : ''}</button>`;
    }).join('');
    return `<div class="message-reactions" data-message-id="${messageId || ''}">${chips}</div>`;
}

function hideReactionPicker() {
    const picker = document.getElementById('reaction-picker');
    if (picker) picker.style.display = 'none';
}

function showReactionPicker(messageId, x, y) {
    const picker = document.getElementById('reaction-picker');
    if (!picker) return;
    picker.dataset.messageId = messageId;
    picker.style.display = 'flex';
    const pad = 8;
    let left = x, top = y;
    requestAnimationFrame(() => {
        const w = picker.offsetWidth, h = picker.offsetHeight;
        if (left + w > window.innerWidth - pad) left = window.innerWidth - w - pad;
        if (left < pad) left = pad;
        if (top + h > window.innerHeight - pad) top = y - h - 8;
        if (top < pad) top = pad;
        picker.style.left = left + 'px';
        picker.style.top = top + 'px';
    });
}

async function toggleReaction(messageId, emoji) {
    if (!messageId || !emoji || String(messageId).startsWith('temp-')) return;
    hideReactionPicker();
    const row = document.querySelector(`[data-mid="${messageId}"]`);
    const container = row?.querySelector('.message-reactions');
    const existingChip = container?.querySelector(`.reaction-chip.mine[data-emoji="${emoji}"]`);

    try {
        if (existingChip) {
            const res = await fetchWithAuth(`${API_BASE}/reactions/${messageId}`, { method: 'DELETE' });
            if (!res || !res.ok) throw new Error('Не удалось удалить реакцию');
            applyReactionLocally(messageId, me, null, 'remove');
        } else {
            const res = await fetchWithAuth(`${API_BASE}/reactions/${messageId}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ reactionType: emoji })
            });
            if (!res || !res.ok) {
                const err = await res?.json().catch(() => ({}));
                throw new Error(err?.error || err?.Error || 'Не удалось добавить реакцию');
            }
            applyReactionLocally(messageId, me, emoji, 'add');
        }
    } catch (err) {
        console.error('Реакция:', err);
        showToast(err.message || 'Ошибка реакции', 'error');
    }
}

function applyReactionLocally(messageId, userId, reactionType, action) {
    const row = document.querySelector(`[data-mid="${messageId}"]`);
    if (!row) return;
    let container = row.querySelector('.message-reactions');
    if (!container) {
        const col = row.querySelector('.message-col');
        if (!col) return;
        container = document.createElement('div');
        container.className = 'message-reactions';
        container.dataset.messageId = messageId;
        col.appendChild(container);
    }

    if (String(userId) === String(me)) {
        container.querySelectorAll('.reaction-chip.mine').forEach(chip => {
            let countEl = chip.querySelector('.reaction-count');
            let count = countEl ? parseInt(countEl.textContent, 10) : 1;
            count -= 1;
            if (count <= 0) chip.remove();
            else {
                chip.classList.remove('mine');
                if (countEl) countEl.textContent = count;
                else {
                    countEl = document.createElement('span');
                    countEl.className = 'reaction-count';
                    countEl.textContent = count;
                    chip.appendChild(countEl);
                }
            }
        });
    }

    if (action === 'add' && reactionType) {
        let chip = container.querySelector(`.reaction-chip[data-emoji="${reactionType}"]`);
        if (chip) {
            let countEl = chip.querySelector('.reaction-count');
            let count = countEl ? parseInt(countEl.textContent, 10) : 1;
            count += 1;
            if (!countEl) {
                countEl = document.createElement('span');
                countEl.className = 'reaction-count';
                chip.appendChild(countEl);
            }
            countEl.textContent = count;
            if (String(userId) === String(me)) chip.classList.add('mine');
        } else {
            chip = document.createElement('button');
            chip.type = 'button';
            chip.className = 'reaction-chip' + (String(userId) === String(me) ? ' mine' : '');
            chip.dataset.emoji = reactionType;
            chip.onclick = () => toggleReaction(messageId, reactionType);
            chip.innerHTML = `<span>${reactionType}</span>`;
            container.appendChild(chip);
        }
    }
}

async function refreshMessageReactions(messageId) {
    try {
        const res = await fetchWithAuth(`${API_BASE}/reactions/${messageId}`);
        if (!res || !res.ok) return;
        const json = await res.json();
        const list = json?.data || json?.Data || [];
        const map = {};
        for (const r of list) {
            const type = r.reactionType || r.ReactionType;
            if (!type) continue;
            if (!map[type]) map[type] = { reactionType: type, count: 0, users: [] };
            map[type].count++;
            map[type].users.push({
                userId: r.userId || r.UserId,
                userName: r.userName || r.UserName
            });
        }
        const row = document.querySelector(`[data-mid="${messageId}"]`);
        if (!row) return;
        let container = row.querySelector('.message-reactions');
        if (!container) {
            const col = row.querySelector('.message-col');
            if (!col) return;
            container = document.createElement('div');
            container.className = 'message-reactions';
            col.appendChild(container);
        }
        const html = renderReactionsHtml(Object.values(map), messageId);
        const tmp = document.createElement('div');
        tmp.innerHTML = html;
        const newEl = tmp.firstElementChild;
        container.replaceWith(newEl);
    } catch (e) {
        console.warn('refreshMessageReactions', e);
    }
}

document.getElementById('reaction-picker')?.addEventListener('click', (e) => {
    const btn = e.target.closest('button[data-emoji]');
    if (!btn) return;
    e.preventDefault();
    e.stopPropagation();
    const picker = document.getElementById('reaction-picker');
    const mid = picker?.dataset.messageId;
    const emoji = btn.dataset.emoji;
    if (mid && emoji) toggleReaction(mid, emoji);
});

document.addEventListener('click', (e) => {
    const picker = document.getElementById('reaction-picker');
    if (picker && picker.style.display !== 'none' && !picker.contains(e.target) && !e.target.closest('#ctx-react-btn')) {
        hideReactionPicker();
    }
});

const MONTHS_RU = [
    'января', 'февраля', 'марта', 'апреля', 'мая', 'июня',
    'июля', 'августа', 'сентября', 'октября', 'ноября', 'декабря'
];

let lastRenderedDateKey = null;

function parseMessageDate(dateValue) {
    if (!dateValue) return null;
    try {
        let date;
        if (typeof dateValue === 'string') {
            let str = dateValue.trim();
            if (!str.endsWith('Z') && !str.includes('+') && !str.includes('-', 10)) {
                str += 'Z';
            }
            date = new Date(str);
        } else {
            date = new Date(dateValue);
        }
        if (isNaN(date.getTime())) return null;
        return date;
    } catch {
        return null;
    }
}

function getDateKey(date) {
    if (!date) return null;
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
}

function formatDateLabel(date) {
    if (!date) return '';

    const now = new Date();
    const todayKey = getDateKey(now);
    const yesterday = new Date(now);
    yesterday.setDate(yesterday.getDate() - 1);
    const yesterdayKey = getDateKey(yesterday);
    const key = getDateKey(date);

    if (key === todayKey) return 'Сегодня';
    if (key === yesterdayKey) return 'Вчера';

    const day = date.getDate();
    const month = MONTHS_RU[date.getMonth()];
    const year = date.getFullYear();

    if (year === now.getFullYear()) {
        return `${day} ${month}`;
    }
    return `${day} ${month} ${year}`;
}

function formatTimeOnly(dateValue) {
    const date = parseMessageDate(dateValue);
    if (!date) return '—';
    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');
    return `${hours}:${minutes}`;
}

function formatChatListTime(dateValue) {
    const date = parseMessageDate(dateValue);
    if (!date) return '—';

    const now = new Date();
    const dateKey = getDateKey(date);
    const todayKey = getDateKey(now);

    const yesterday = new Date(now);
    yesterday.setDate(yesterday.getDate() - 1);
    const yesterdayKey = getDateKey(yesterday);

    if (dateKey === todayKey) {
        return formatTimeOnly(date);
    }

    if (dateKey === yesterdayKey) {
        return 'вчера';
    }

    const day = String(date.getDate()).padStart(2, '0');
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const year = date.getFullYear();

    return `${day}.${month}.${year}`;
}

function formatDateTime(dateValue) {
    if (!dateValue) return '—';

    const date = parseMessageDate(dateValue);
    if (!date) {
        console.warn("Не удалось распарсить дату:", dateValue);
        return '—';
    }

    const day = String(date.getDate()).padStart(2, '0');
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const year = date.getFullYear();
    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');

    return `${day}.${month}.${year} в ${hours}:${minutes}`;
}

function appendDateSeparatorIfNeeded(dateValue) {
    const date = parseMessageDate(dateValue) || new Date();
    const key = getDateKey(date);
    if (!key || key === lastRenderedDateKey) return;

    lastRenderedDateKey = key;

    const sep = document.createElement('div');
    sep.className = 'date-separator';
    sep.dataset.dateKey = key;
    sep.innerHTML = `
        <span>${formatDateLabel(date)}</span>
    `;
    messagesContainer.appendChild(sep);
}