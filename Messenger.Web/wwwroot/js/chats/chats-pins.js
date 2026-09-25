function loadPinnedChatIds() {
    try {
        const raw = localStorage.getItem(PIN_CHATS_KEY);
        const arr = raw ? JSON.parse(raw) : [];
        return Array.isArray(arr) ? arr.map(String) : [];
    } catch { return []; }
}

function savePinnedChatIds(ids) {
    localStorage.setItem(PIN_CHATS_KEY, JSON.stringify(ids.map(String)));
}

function isChatPinned(chatId) {
    return loadPinnedChatIds().includes(String(chatId));
}

function togglePinChat(chatId) {
    if (!chatId) return;
    const id = String(chatId);
    let ids = loadPinnedChatIds();
    if (ids.includes(id)) {
        ids = ids.filter(x => x !== id);
        savePinnedChatIds(ids);
        applyPinnedChatsOrder();
        showToast('Чат откреплён', 'info');
        return false;
    } else {
        ids = [id, ...ids.filter(x => x !== id)];
        savePinnedChatIds(ids);
        applyPinnedChatsOrder();
        showToast('Чат закреплён', 'success');
        return true;
    }
}

let __chatBaseOrder = [];

function rememberChatBaseOrderFromDom() {
    const container = document.getElementById('chats-container');
    if (!container) return;
    const pinned = new Set(loadPinnedChatIds());
    const all = [...container.querySelectorAll('.chat-item')].map(el => String(el.dataset.chatId));
    if (!__chatBaseOrder.length) {
        __chatBaseOrder = all.slice();
        return;
    }
    all.forEach(id => {
        if (!__chatBaseOrder.includes(id)) __chatBaseOrder.push(id);
    });
}

function setChatBaseOrderFromIds(ids) {
    __chatBaseOrder = (ids || []).map(String);
}

function applyPinnedChatsOrder() {
    const container = document.getElementById('chats-container');
    if (!container) return;
    const ids = loadPinnedChatIds();
    const items = [...container.querySelectorAll('.chat-item')];
    const map = new Map(items.map(el => [String(el.dataset.chatId), el]));

    items.forEach(item => {
        const cid = String(item.dataset.chatId || '');
        const pinned = ids.includes(cid);
        item.classList.toggle('pinned', pinned);
        let icon = item.querySelector('.chat-pin-icon');
        if (pinned) {
            if (!icon) {
                icon = document.createElement('span');
                icon.className = 'chat-pin-icon';
                icon.title = 'Закреплён';
                icon.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 17v5"/><path d="M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z"/></svg>`;
                const titleRow = item.querySelector('h3')?.parentElement;
                if (titleRow) titleRow.appendChild(icon);
                else item.appendChild(icon);
            }
        } else if (icon) {
            icon.remove();
        }
    });

    const pinnedItems = [];
    ids.forEach(id => {
        if (map.has(id)) {
            pinnedItems.push(map.get(id));
            map.delete(id);
        }
    });
    const rest = [];
    const base = __chatBaseOrder.length ? __chatBaseOrder : items.map(el => String(el.dataset.chatId));
    base.forEach(id => {
        if (map.has(id)) {
            rest.push(map.get(id));
            map.delete(id);
        }
    });
    map.forEach(el => rest.push(el));

    [...pinnedItems, ...rest].forEach(el => container.appendChild(el));
    if (typeof cacheChatItems === 'function') cacheChatItems();
}

function loadPinnedMessagesMap() {
    try {
        const raw = localStorage.getItem(PIN_MSGS_KEY);
        const obj = raw ? JSON.parse(raw) : {};
        return obj && typeof obj === 'object' ? obj : {};
    } catch { return {}; }
}

function savePinnedMessagesMap(map) {
    localStorage.setItem(PIN_MSGS_KEY, JSON.stringify(map));
}

function getPinnedMessage(chatId) {
    const list = getPinnedMessages(chatId);
    if (!list.length) return null;
    return list[getPinnedViewIndex(chatId)] || list[0];
}

function getPinnedMessages(chatId) {
    if (!chatId) return [];
    const entry = loadPinnedMessagesMap()[String(chatId)];
    if (!entry) return [];
    if (Array.isArray(entry)) return entry;
    if (entry.messageId) return [entry];
    return [];
}

function setPinnedMessages(chatId, list) {
    const map = loadPinnedMessagesMap();
    if (!list || !list.length) delete map[String(chatId)];
    else map[String(chatId)] = list;
    savePinnedMessagesMap(map);
}

function isMessagePinned(chatId, messageId) {
    return getPinnedMessages(chatId).some(p => String(p.messageId) === String(messageId));
}

const pinnedViewIndex = new Map();
/** Chat IDs where user dismissed the pinned-messages bar (Esc / close without unpin). */
const pinnedBarDismissed = new Set();

function hidePinnedMessageBar() {
    const bar = document.getElementById('pinned-message-bar');
    if (bar) bar.classList.remove('show');
    if (currentChatId) pinnedBarDismissed.add(String(currentChatId));
}

function getPinnedViewIndex(chatId) {
    const list = getPinnedMessages(chatId);
    if (!list.length) return 0;
    let idx = pinnedViewIndex.get(String(chatId)) || 0;
    if (idx < 0 || idx >= list.length) idx = 0;
    return idx;
}

function setPinnedViewIndex(chatId, idx) {
    const list = getPinnedMessages(chatId);
    if (!list.length) return;
    const n = ((idx % list.length) + list.length) % list.length;
    pinnedViewIndex.set(String(chatId), n);
}

function pinMessage(chatId, messageId, text) {
    if (!chatId || !messageId) return;
    let list = getPinnedMessages(chatId);
    if (list.some(p => String(p.messageId) === String(messageId))) {
        showToast('Уже закреплено', 'info');
        updatePinnedMessageBar();
        return;
    }
    list = list.concat([{
        messageId: String(messageId),
        text: (text || '').slice(0, 200),
        pinnedAt: Date.now()
    }]);
    setPinnedMessages(chatId, list);
    setPinnedViewIndex(chatId, list.length - 1);
    pinnedBarDismissed.delete(String(chatId));
    updatePinnedMessageBar();
    highlightPinnedMessageInView();
    showToast('Сообщение закреплено', 'success');
}

function unpinMessage(chatId, messageId) {
    if (!chatId) return;
    let list = getPinnedMessages(chatId);
    if (messageId) {
        list = list.filter(p => String(p.messageId) !== String(messageId));
    } else {
        const idx = getPinnedViewIndex(chatId);
        if (list[idx]) list = list.filter((_, i) => i !== idx);
    }
    setPinnedMessages(chatId, list);
    if (list.length) setPinnedViewIndex(chatId, Math.min(getPinnedViewIndex(chatId), list.length - 1));
    else pinnedViewIndex.delete(String(chatId));
    updatePinnedMessageBar();
    document.querySelectorAll('.message-row-pinned').forEach(el => el.classList.remove('message-row-pinned'));
    highlightPinnedMessageInView();
    showToast('Сообщение откреплено', 'info');
}

function updatePinnedMessageBar() {
    const bar = document.getElementById('pinned-message-bar');
    const textEl = document.getElementById('pinned-message-text');
    const titleEl = document.getElementById('pinned-message-title');
    const segs = document.getElementById('pinned-message-segments');
    if (!bar) return;
    if (!currentChatId) {
        bar.classList.remove('show');
        return;
    }
    const list = getPinnedMessages(currentChatId);
    if (!list.length) {
        bar.classList.remove('show');
        pinnedBarDismissed.delete(String(currentChatId));
        return;
    }
    // User dismissed the bar (Esc) — keep hidden until re-open or new pin
    if (pinnedBarDismissed.has(String(currentChatId))) {
        bar.classList.remove('show');
        return;
    }
    const idx = getPinnedViewIndex(currentChatId);
    const cur = list[idx] || list[0];
    if (textEl) textEl.textContent = cur.text || 'Сообщение';
    if (titleEl) {
        titleEl.textContent = list.length > 1
            ? `Закреплённые сообщения ${idx + 1}/${list.length}`
            : 'Закреплённое сообщение';
    }
    if (segs) {
        segs.innerHTML = list.map((_, i) =>
            `<div class="pin-seg${i === idx ? ' active' : ''}" data-pin-idx="${i}"></div>`
        ).join('');
    }
    bar.classList.add('show');
    if (typeof feather !== 'undefined') feather.replace();
}

function highlightPinnedMessageInView() {
    document.querySelectorAll('.message-row-pinned').forEach(el => el.classList.remove('message-row-pinned'));
    document.querySelectorAll('.msg-pin-badge').forEach(el => el.remove());
    if (!currentChatId) return;
    const list = getPinnedMessages(currentChatId);
    list.forEach(p => {
        const row = document.querySelector(`[data-mid="${p.messageId}"]`);
        if (!row) return;
        row.classList.add('message-row-pinned');
        const timeRow = row.querySelector('.message-bubble [class*="text-[11px]"]');
        if (timeRow && !timeRow.querySelector('.msg-pin-badge')) {
            const badge = document.createElement('span');
            badge.className = 'msg-pin-badge';
            badge.title = 'Закреплено';
            badge.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 17v5"/><path d="M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z"/></svg>`;
            timeRow.insertBefore(badge, timeRow.firstChild);
        }
    });
}

function scrollToPinnedMessage(specificId) {
    if (!currentChatId) return;
    const list = getPinnedMessages(currentChatId);
    if (!list.length) return;
    let targetId = specificId;
    if (!targetId) {
        const idx = getPinnedViewIndex(currentChatId);
        targetId = list[idx]?.messageId;
    }
    if (!targetId) return;
    const row = document.querySelector(`[data-mid="${targetId}"]`);
    if (row) {
        row.scrollIntoView({ behavior: 'smooth', block: 'center' });
        row.classList.add('message-row-pinned');
        setTimeout(() => {
            highlightPinnedMessageInView();
        }, 1800);
    } else {
        showToast('Сообщение не в загруженной истории — прокрутите вверх', 'info');
    }
}

function onPinnedBarClick() {
    if (!currentChatId) return;
    const list = getPinnedMessages(currentChatId);
    if (!list.length) return;
    const idx = getPinnedViewIndex(currentChatId);
    const cur = list[idx];
    if (cur) scrollToPinnedMessage(cur.messageId);
    if (list.length > 1) {
        setPinnedViewIndex(currentChatId, idx + 1);
        updatePinnedMessageBar();
    }
}


async function withFrozenChatListPosition(chatId, fn) {
    const container = document.getElementById('chats-container');
    const item = chatId ? document.querySelector(`.chat-item[data-chat-id="${CSS.escape(String(chatId))}"]`) : null;
    const next = item?.nextElementSibling || null;
    const prev = item?.previousElementSibling || null;
    const wasFirst = !!(item && container && item === container.firstElementChild);
    const indexBefore = item && container
        ? [...container.querySelectorAll('.chat-item')].indexOf(item)
        : -1;

    __suppressChatBump = true;
    try {
        const result = fn();
        if (result && typeof result.then === 'function') {
            await result;
        }
    } finally {
        // Вернуть чат на прежнее место относительно соседей
        if (item && container && item.parentElement === container) {
            if (next && next.parentElement === container) {
                if (item.nextElementSibling !== next) {
                    container.insertBefore(item, next);
                }
            } else if (prev && prev.parentElement === container) {
                // вставить после prev
                if (prev.nextElementSibling !== item) {
                    if (prev.nextElementSibling) container.insertBefore(item, prev.nextElementSibling);
                    else container.appendChild(item);
                }
            } else if (!next && !wasFirst) {
                container.appendChild(item);
            } else if (wasFirst && container.firstElementChild !== item) {
                container.insertBefore(item, container.firstElementChild);
            }

            // Если порядок всё ещё сбился — восстановить по индексу
            const items = [...container.querySelectorAll('.chat-item')];
            const indexAfter = items.indexOf(item);
            if (indexBefore >= 0 && indexAfter >= 0 && indexBefore !== indexAfter) {
                const ref = items[indexBefore] === item
                    ? (items[indexBefore + 1] || null)
                    : items[indexBefore];
                // более надёжно: собрать id-порядок
                const ids = items.map(el => el.dataset.chatId);
                const id = String(chatId);
                ids.splice(indexAfter, 1);
                ids.splice(Math.min(indexBefore, ids.length), 0, id);
                // не перестраиваем весь список — только этот элемент
                if (indexBefore === 0) {
                    container.insertBefore(item, container.firstElementChild);
                } else {
                    const beforeEl = container.querySelector(`.chat-item[data-chat-id="${CSS.escape(ids[indexBefore - 1])}"]`);
                    if (beforeEl && beforeEl.nextElementSibling !== item) {
                        if (beforeEl.nextElementSibling) container.insertBefore(item, beforeEl.nextElementSibling);
                        else container.appendChild(item);
                    }
                }
            }
        }
        // Дольше держим suppress: SignalR MessageDeleted может прийти с задержкой
        setTimeout(() => { __suppressChatBump = false; }, 1500);
    }
}

function bumpChatToTop(chatId) {
    if (__suppressChatBump) return;
    if (!chatId) return;
    // Текущий чат тоже поднимаем — при отправке/пересылке список должен обновиться
    const container = document.getElementById('chats-container');
    const item = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
    if (!container || !item) return;
    if (isChatPinned(chatId)) {
        applyPinnedChatsOrder();
        return;
    }
    const pinnedIds = loadPinnedChatIds();
    let insertBeforeEl = container.firstElementChild;
    const items = [...container.querySelectorAll('.chat-item')];
    for (const el of items) {
        if (pinnedIds.includes(String(el.dataset.chatId))) {
            insertBeforeEl = el.nextElementSibling;
        } else break;
    }
    if (item === insertBeforeEl) return;
    if (insertBeforeEl) container.insertBefore(item, insertBeforeEl);
    else container.appendChild(item);
    const id = String(chatId);
    __chatBaseOrder = [id, ...__chatBaseOrder.filter(x => x !== id)];
}

/** Timestamp последней активности чата (ms). indexFallback сохраняет текущий порядок, если даты нет. */
function getChatLastActivityTs(chatItem, indexFallback = 0) {
    if (!chatItem) return 0;
    const at = chatItem.dataset.lastMessageAt || chatItem.getAttribute('data-last-message-at');
    if (at) {
        const t = new Date(at).getTime();
        if (!isNaN(t) && t > 0) return t;
    }
    // Нет даты — стабильный fallback по текущей позиции (не сбрасывать весь список вниз)
    return 1e15 - indexFallback;
}

/**
 * Пересортировать незакреплённые чаты по дате последнего сообщения (новые сверху).
 * Закреплённые остаются сверху. Чаты без даты сохраняют относительный порядок.
 */
function reorderUnpinnedChatsByLastActivity() {
    const container = document.getElementById('chats-container');
    if (!container) return;

    const pinnedIds = typeof loadPinnedChatIds === 'function' ? loadPinnedChatIds() : [];
    const items = [...container.querySelectorAll('.chat-item')];
    if (!items.length) return;

    const map = new Map(items.map(el => [String(el.dataset.chatId || ''), el]));

    const pinnedOrdered = [];
    for (const id of pinnedIds) {
        if (map.has(id)) {
            pinnedOrdered.push(map.get(id));
            map.delete(id);
        }
    }

    // unpinned в текущем DOM-порядке
    const unpinned = items.filter(el => {
        const id = String(el.dataset.chatId || '');
        return id && !pinnedIds.includes(id);
    });

    const scored = unpinned.map((el, i) => ({
        el,
        ts: getChatLastActivityTs(el, i)
    }));
    scored.sort((a, b) => b.ts - a.ts);

    [...pinnedOrdered, ...scored.map(s => s.el)].forEach(el => container.appendChild(el));

    if (typeof __chatBaseOrder !== 'undefined') {
        __chatBaseOrder = scored.map(s => String(s.el.dataset.chatId || '')).filter(Boolean);
    }
    if (typeof cacheChatItems === 'function') cacheChatItems();
}

