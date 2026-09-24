function getSearchNav() {
    return document.getElementById('search-navigation');
}

function getSearchEmptyState() {
    return document.getElementById('search-empty-state');
}

function setSearchEmptyState(visible, text = 'Ничего не найдено') {
    const el = getSearchEmptyState();
    const textEl = document.getElementById('search-empty-text');
    if (textEl) textEl.textContent = text;
    if (el) el.classList.toggle('hidden', !visible);
    if (visible && typeof feather !== 'undefined') feather.replace();
}

function getMessageTimestamp(msg) {
    return msg?.sentAt
        ?? msg?.SentAt
        ?? msg?.sentTime
        ?? msg?.SendTime
        ?? msg?.sendTime
        ?? msg?.createdAt
        ?? msg?.CreatedAt
        ?? msg?.date
        ?? msg?.timestamp
        ?? null;
}

function getMessageSenderId(msg) {
    return msg?.senderId ?? msg?.SenderId ?? msg?.authorId ?? msg?.userId ?? msg?.UserId ?? null;
}

function getMessageSenderName(msg) {
    return msg?.senderName
        ?? msg?.SenderName
        ?? msg?.sender?.name
        ?? msg?.sender?.fullName
        ?? msg?.authorName
        ?? '';
}

function getSearchResultText(result) {
    return result?.messageText ?? result?.MessageText ?? result?.text ?? result?.Text ?? result?.content ?? result?.Content ?? '';
}

function getSearchResultTime(result) {
    return getMessageTimestamp(result);
}

function getSearchResultChatId(result) {
    return result?.chatId ?? result?.ChatId ?? result?.conversationId ?? result?.conversationID
        ?? result?.chat?.id ?? result?.chat?.chatId ?? null;
}

function getSearchResultChatName(result) {
    return result?.chatName
        ?? result?.ChatName
        ?? result?.chat?.name
        ?? result?.chat?.title
        ?? result?.conversationName
        ?? 'Чат';
}

function getSearchResultSenderName(result) {
    return getMessageSenderName(result) || 'Пользователь';
}

function getSearchResultHasAttachments(result) {
    const attachments = result?.attachments ?? result?.Attachments ?? result?.files ?? result?.Files;
    return Array.isArray(attachments) && attachments.length > 0;
}

function isMessageUnread(msg) {
    const unreadFields = ['isUnread', 'IsUnread', 'unread', 'Unread'];
    for (const field of unreadFields) {
        if (typeof msg?.[field] === 'boolean') return msg[field];
    }

    const readByMeFields = ['readByMe', 'ReadByMe', 'isReadByCurrentUser', 'IsReadByCurrentUser', 'readByCurrentUser'];
    for (const field of readByMeFields) {
        if (typeof msg?.[field] === 'boolean') return !msg[field];
    }

    const readFields = ['isRead', 'IsRead', 'read', 'Read'];
    for (const field of readFields) {
        if (typeof msg?.[field] === 'boolean') return !msg[field];
    }

    const status = String(
        msg?.readStatus
        ?? msg?.ReadStatus
        ?? msg?.messageReadStatus
        ?? msg?.MessageReadStatus
        ?? msg?.readState
        ?? msg?.ReadState
        ?? ''
    ).trim().toLowerCase();
    if (['unread', 'new', 'notread', 'not_read'].includes(status)) return true;
    if (['read', 'seen', 'viewed'].includes(status)) return false;

    for (const field of ['isSeen', 'IsSeen', 'seen', 'Seen']) {
        if (typeof msg?.[field] === 'boolean') return !msg[field];
    }

    if (Object.prototype.hasOwnProperty.call(msg || {}, 'readAt')) return !msg.readAt;
    if (Object.prototype.hasOwnProperty.call(msg || {}, 'ReadAt')) return !msg.ReadAt;

    return false;
}

function getLocalDateKey(dateValue) {
    if (!dateValue) return '';

    if (typeof dateValue === 'string') {
        const raw = dateValue.trim();
        const directDate = raw.match(/^(\d{4})-(\d{2})-(\d{2})(?:$|T)/);
        if (!directDate) return '';

        const hasTimezone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(raw);
        const date = hasTimezone ? new Date(raw) : new Date(raw.replace(' ', 'T'));
        if (isNaN(date.getTime())) return `${directDate[1]}-${directDate[2]}-${directDate[3]}`;

        const y = date.getFullYear();
        const m = String(date.getMonth() + 1).padStart(2, '0');
        const d = String(date.getDate()).padStart(2, '0');
        return `${y}-${m}-${d}`;
    }

    return getLocalDateKey(new Date(dateValue).toISOString());
}

function getMessageSearchFilters() {
    const senderId = document.getElementById('message-search-sender-id')?.value?.trim() || '';
    const senderText = document.getElementById('message-search-sender')?.value?.trim() || '';
    return {
        scope: document.getElementById('message-search-scope')?.value || 'chat',
        from: document.getElementById('message-search-date-from')?.value || '',
        to: document.getElementById('message-search-date-to')?.value || '',
        senderId,
        senderName: senderId ? '' : senderText,
        hasAttachments: !!document.getElementById('message-search-has-attachments')?.checked,
        unreadOnly: !!document.getElementById('message-search-unread-only')?.checked
    };
}

function buildSearchParams(query, filters = getMessageSearchFilters()) {
    const params = new URLSearchParams();
    if (query) params.set('query', query);
    if (filters.from) params.set('from', filters.from);
    if (filters.to) params.set('to', filters.to);
    if (filters.senderId) params.set('senderId', filters.senderId);
    else if (filters.senderName) params.set('senderName', filters.senderName);
    if (filters.hasAttachments) params.set('hasAttachments', 'true');
    if (filters.unreadOnly) params.set('unreadOnly', 'true');
    params.set('limit', '100');
    return params;
}

function toggleMessageSearchFilters() {
    const filters = document.getElementById('message-search-filters');
    if (!filters) return;
    const willOpen = filters.classList.contains('hidden');
    filters.classList.toggle('hidden', !willOpen);
    if (willOpen) {
        updateSearchScopeUi();
        setTimeout(() => document.getElementById('message-search-sender')?.focus(), 40);
    }
}

function updateSearchScopeUi() {
    const scope = document.getElementById('message-search-scope')?.value || 'chat';
    currentSearchScope = scope;
    const input = document.getElementById('message-search-input');
    if (input) input.placeholder = scope === 'global' ? 'Поиск по всем чатам' : 'Поиск по сообщениям';
    const filters = document.getElementById('message-search-filters');
    if (filters) filters.classList.toggle('ring-1', scope === 'global');
}

function toggleMessageSearch() {
    const searchBar = document.getElementById('message-search-bar');
    const searchToggle = document.getElementById('search-toggle-btn');
    const searchInput = document.getElementById('message-search-input');
    if (!searchBar) return;

    if (searchBar.classList.contains('hidden')) {
        searchBar.classList.remove('hidden');
        searchBar.classList.add('flex');
        updateSearchScopeUi();
        if (searchToggle) searchToggle.classList.add('hidden');
        searchInput?.focus();
    } else {
        clearMessageSearch(true);
    }
}

const debouncedSearch = (query) => {
    clearTimeout(searchDebounceTimer);
    searchDebounceTimer = setTimeout(() => performMessageSearch(query), 280);
};

function normalizeSearchResults(json) {
    if (Array.isArray(json)) return json;
    if (Array.isArray(json?.data)) return json.data;
    if (Array.isArray(json?.data?.items)) return json.data.items;
    if (Array.isArray(json?.items)) return json.items;
    if (Array.isArray(json?.results)) return json.results;
    return [];
}

function filterMessagesLocally(results, query, filters) {
    const lowerQuery = (query || '').trim().toLowerCase();
    const from = filters.from || '';
    const to = filters.to || '';

    return (Array.isArray(results) ? results : []).filter(msg => {
        const text = getSearchResultText(msg);
        if (lowerQuery && !String(text).toLowerCase().includes(lowerQuery)) return false;

        const senderId = getMessageSenderId(msg);
        const senderName = getSearchResultSenderName(msg);
        if (filters.senderId && String(senderId ?? '') !== String(filters.senderId)) return false;
        if (filters.senderName && !senderName.toLowerCase().includes(filters.senderName.toLowerCase())) return false;
        if (filters.hasAttachments && !getSearchResultHasAttachments(msg)) return false;
        if (filters.unreadOnly && !isMessageUnread(msg)) return false;

        const dateKey = getLocalDateKey(getSearchResultTime(msg));
        if (from && (!dateKey || dateKey < from)) return false;
        if (to && (!dateKey || dateKey > to)) return false;

        return true;
    });
}

async function fetchChatMessagesForSearch(chatId) {
    if (!chatId) return [];
    try {
        const res = await fetchWithAuth(`${API_BASE}/messages/${encodeURIComponent(chatId)}?limit=100`, {
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (!res?.ok) return [];
        const json = await res.json();
        return normalizeSearchResults(json);
    } catch (e) {
        console.warn(`Не удалось загрузить сообщения чата ${chatId} для поиска`, e);
        return [];
    }
}

async function performMessageSearch(query) {
    query = (query ?? document.getElementById('message-search-input')?.value ?? '').trim();
    currentSearchQuery = query;
    currentSearchFilters = getMessageSearchFilters();

    const hasFilters = !!(
        currentSearchFilters.from ||
        currentSearchFilters.to ||
        currentSearchFilters.senderId ||
        currentSearchFilters.senderName ||
        currentSearchFilters.hasAttachments ||
        currentSearchFilters.unreadOnly
    );

    if (!query && !hasFilters) {
        resetSearch();
        return;
    }

    setSearchEmptyState(false);

    if (currentSearchFilters.scope === 'global') {
        await performGlobalMessageSearch(query, currentSearchFilters);
        return;
    }

    if (!currentChatId) {
        resetSearch();
        setSearchEmptyState(true, 'Сначала откройте чат');
        return;
    }

    if (query) {
        try {
            const params = buildSearchParams(query, currentSearchFilters);
            const res = await fetchWithAuth(`${API_BASE}/messages/${currentChatId}/search?${params.toString()}`);
            if (res?.ok) {
                const serverResults = filterMessagesLocally(normalizeSearchResults(await res.json()), query, currentSearchFilters);
                if (serverResults.length) {
                    processSearchResults(serverResults, query);
                    return;
                }
            }
        } catch (e) {
            console.warn('Серверный поиск в чате не удался', e);
        }
    }

    let localResults = filterMessagesLocally(originalMessages, query, currentSearchFilters);

    if (!localResults.length && (currentSearchFilters.from || currentSearchFilters.to || currentSearchFilters.unreadOnly || currentSearchFilters.hasAttachments || !query)) {
        const fetched = await fetchChatMessagesForSearch(currentChatId);
        localResults = filterMessagesLocally(fetched, query, currentSearchFilters);
    }

    processSearchResults(localResults, query);
}

async function performGlobalMessageSearch(query, filters) {
    let results = [];

    try {
        const params = buildSearchParams(query, filters);
        const res = await fetchWithAuth(`${API_BASE}/messages/search?${params.toString()}`);
        if (res?.ok) results = filterMessagesLocally(normalizeSearchResults(await res.json()), query, filters);
    } catch (e) {
        console.warn('Глобальный API-поиск недоступен, выполняем поиск по чатам', e);
    }

    if (!results.length) {
        const chatItems = Array.from(document.querySelectorAll('.chat-item[data-chat-id]'));
        const batches = [];
        for (let i = 0; i < chatItems.length; i += 6) {
            const batch = chatItems.slice(i, i + 6);
            const batchResults = await Promise.all(batch.map(async item => {
                const chatId = item.dataset.chatId;
                const chatName = item.querySelector('h3')?.textContent?.trim() || 'Чат';
                const messages = await fetchChatMessagesForSearch(chatId);
                return messages.map(msg => ({ ...msg, chatId, chatName }));
            }));
            batches.push(...batchResults.flat());
        }
        results = filterMessagesLocally(batches, query, filters);
    }

    const unique = new Map();
    results.forEach(msg => {
        const chatId = getSearchResultChatId(msg) || '';
        const messageId = msg?.messageId ?? msg?.MessageId ?? msg?.id ?? msg?.Id;
        const key = `${chatId}:${messageId ?? JSON.stringify([getSearchResultTime(msg), getSearchResultText(msg)])}`;
        if (!unique.has(key)) unique.set(key, msg);
    });

    globalSearchResults = Array.from(unique.values()).sort((a, b) => {
        const da = parseMessageDate(getSearchResultTime(a))?.getTime() || 0;
        const db = parseMessageDate(getSearchResultTime(b))?.getTime() || 0;
        return db - da;
    });

    renderGlobalSearchResults(globalSearchResults, query);
}

function renderGlobalSearchResults(results, query = '', emptyText = 'Ничего не найдено') {
    const global = document.getElementById('global-search-results');
    if (!global) return;
    document.getElementById('messages-container')?.classList.add('hidden');
    global.classList.remove('hidden');
    setSearchEmptyState(false);

    if (!results.length) {
        setSearchEmptyState(true, emptyText);
        global.innerHTML = '';
        return;
    }

    global.innerHTML = `
        <div class="px-4 py-3 border-b border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-900">
            <div class="max-w-4xl mx-auto text-sm text-gray-500 dark:text-gray-400">
                Найдено: <span class="font-semibold text-gray-700 dark:text-gray-200">${results.length}</span>
            </div>
        </div>
        <div class="max-w-4xl mx-auto divide-y divide-gray-200 dark:divide-gray-700">
            ${results.map((result, index) => {
                const chatName = getSearchResultChatName(result);
                const sender = getSearchResultSenderName(result);
                const text = getSearchResultText(result);
                const dt = parseMessageDate(getSearchResultTime(result));
                const time = dt ? formatDateLabel(dt) + ' · ' + dt.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';
                const attachments = getSearchResultHasAttachments(result);
                const unread = isMessageUnread(result);
                return `<button type="button" class="global-search-result w-full text-left px-4 py-3 transition" data-global-index="${index}">
                    <div class="flex items-center gap-2 mb-1">
                        <span class="font-semibold text-gray-800 dark:text-gray-100 truncate">${escapeHtml(chatName)}</span>
                        ${unread ? '<span class="text-[10px] px-1.5 py-0.5 rounded bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300">Непрочитанное</span>' : ''}
                        <span class="ml-auto text-xs text-gray-400 flex-shrink-0">${escapeHtml(time)}</span>
                    </div>
                    <div class="text-xs text-gray-500 dark:text-gray-400 mb-1">${escapeHtml(sender)}${attachments ? ' · 📎 вложение' : ''}</div>
                    <div class="text-sm text-gray-700 dark:text-gray-200 line-clamp-2">${highlightSearchText(text, query)}</div>
                </button>`;
            }).join('')}
        </div>`;

    global.querySelectorAll('.global-search-result').forEach(btn => {
        btn.addEventListener('click', () => openGlobalSearchResult(Number(btn.dataset.globalIndex)));
    });
    if (typeof feather !== 'undefined') feather.replace();
}

async function openGlobalSearchResult(index) {
    const result = globalSearchResults[index];
    if (!result) return;
    const chatId = getSearchResultChatId(result);
    const messageId = result?.messageId ?? result?.MessageId ?? result?.id ?? result?.Id ?? null;
    if (!chatId) return;

    let item = document.querySelector(`.chat-item[data-chat-id="${CSS.escape(String(chatId))}"]`);
    if (!item) {
        await loadChats();
        item = document.querySelector(`.chat-item[data-chat-id="${CSS.escape(String(chatId))}"]`);
    }
    if (!item) {
        showToast('Чат найден, но открыть его не удалось', 'warning');
        return;
    }

    const scopeEl = document.getElementById('message-search-scope');
    if (scopeEl) scopeEl.value = 'chat';
    updateSearchScopeUi();
    await openChat(item);

    if (messageId) {
        setTimeout(() => scrollToMessageId(messageId), 350);
        setTimeout(() => scrollToMessageId(messageId), 900);
    }
}

function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, ch => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[ch]));
}

function highlightSearchText(text, query) {
    const safe = escapeHtml(text);
    if (!query) return safe;
    const escaped = query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return safe.replace(new RegExp(`(${escaped})`, 'gi'), '<span class="bg-yellow-300 px-1 rounded text-gray-900">$1</span>');
}

function processSearchResults(results, query) {
    const global = document.getElementById('global-search-results');
    if (global) {
        global.classList.add('hidden');
        global.innerHTML = '';
    }
    const container = document.getElementById('messages-container');
    if (container) container.classList.remove('hidden');

    resetHighlights();
    setSearchEmptyState(results.length === 0, 'Ничего не найдено');
    if (results.length === 0) return;

    foundMessageElements = [];
    currentHighlightIndex = -1;

    results.forEach(resultMsg => {
        const id = resultMsg.messageId ?? resultMsg.MessageId ?? resultMsg.id ?? resultMsg.Id;
        if (!id) return;
        const elements = document.querySelectorAll(`[data-mid="${CSS.escape(String(id))}"]`);
        elements.forEach(el => {
            highlightMessage(el, query);
            foundMessageElements.push(el);
        });
    });

    if (!foundMessageElements.length) {
        setSearchEmptyState(true, 'Сообщения найдены, но не загружены в текущую историю');
        return;
    }
    goToHighlight(0);
}

function highlightMessage(element, query) {
    const textEl = element.querySelector('p');
    if (!textEl) return;
    const source = textEl.textContent || '';
    textEl.innerHTML = highlightSearchText(source, query);
}

function resetHighlights() {
    document.querySelectorAll('.bg-yellow-300').forEach(span => {
        const parent = span.parentElement;
        if (parent) parent.textContent = parent.textContent;
    });
    foundMessageElements = [];
    currentHighlightIndex = -1;

    const nav = getSearchNav();
    if (nav) nav.classList.add('hidden');
    const counter = document.getElementById('search-counter');
    if (counter) counter.classList.add('hidden');
}

function resetSearch() {
    resetHighlights();
    setSearchEmptyState(false);
    const global = document.getElementById('global-search-results');
    if (global) {
        global.classList.add('hidden');
        global.innerHTML = '';
    }
    document.getElementById('messages-container')?.classList.remove('hidden');
}

function goToHighlight(index) {
    if (index < 0 || index >= foundMessageElements.length) return;
    setSearchEmptyState(false);
    currentHighlightIndex = index;
    const target = foundMessageElements[index];
    if (target) target.scrollIntoView({ behavior: 'smooth', block: 'center' });
    updateNavigationUI();
}

function updateNavigationUI() {
    const nav = getSearchNav();
    if (!nav) return;
    const total = foundMessageElements.length;
    nav.classList.toggle('hidden', total <= 1);
    const counter = document.getElementById('search-counter');
    if (counter) {
        counter.classList.toggle('hidden', total <= 1);
        if (total > 0) {
            document.getElementById('current-count').textContent = currentHighlightIndex + 1;
            document.getElementById('total-count').textContent = total;
        }
    }
}

function navigateSearch(direction) {
    if (foundMessageElements.length === 0) return;
    let newIndex = currentHighlightIndex + direction;
    if (newIndex < 0) newIndex = foundMessageElements.length - 1;
    if (newIndex >= foundMessageElements.length) newIndex = 0;
    goToHighlight(newIndex);
}

function clearMessageSearch(hideBar = false) {
    clearTimeout(searchDebounceTimer);
    clearTimeout(senderSearchTimer);
    currentSearchQuery = '';
    currentSearchFilters = {};
    globalSearchResults = [];

    const searchInput = document.getElementById('message-search-input');
    const searchBar = document.getElementById('message-search-bar');
    if (searchInput) searchInput.value = '';

    if (hideBar && searchBar) {
        searchBar.classList.add('hidden');
        searchBar.classList.remove('flex');
        document.getElementById('search-toggle-btn')?.classList.remove('hidden');
    }
    document.getElementById('message-search-filters')?.classList.add('hidden');
    resetMessageSearchFilters();
    resetSearch();
}

function resetMessageSearchFilters() {
    const fields = {
        'message-search-scope': 'chat',
        'message-search-date-from': '',
        'message-search-date-to': '',
        'message-search-sender': '',
        'message-search-sender-id': ''
    };
    Object.entries(fields).forEach(([id, value]) => {
        const el = document.getElementById(id);
        if (el) el.value = value;
    });
    const a = document.getElementById('message-search-has-attachments');
    const u = document.getElementById('message-search-unread-only');
    if (a) a.checked = false;
    if (u) u.checked = false;
    document.getElementById('message-search-sender-clear')?.classList.add('hidden');
    document.getElementById('message-search-sender-results')?.classList.add('hidden');
    updateSearchScopeUi();
}

async function searchMessageSenders(query) {
    const results = document.getElementById('message-search-sender-results');
    if (!results) return;
    query = (query || '').trim();
    clearTimeout(senderSearchTimer);
    if (query.length < 2) {
        results.classList.add('hidden');
        return;
    }
    senderSearchTimer = setTimeout(async () => {
        try {
            const res = await fetchWithAuth(`${API_BASE}/users/search?query=${encodeURIComponent(query)}`);
            if (!res?.ok) throw new Error('sender search failed');
            const json = await res.json();
            const users = Array.isArray(json?.data) ? json.data : [];
            if (!users.length) {
                results.innerHTML = '<div class="p-3 text-xs text-gray-500">Ничего не найдено</div>';
                results.classList.remove('hidden');
                return;
            }
            results.innerHTML = users.slice(0, 8).map(user => `
                <button type="button" class="w-full text-left px-3 py-2 hover:bg-gray-50 dark:hover:bg-gray-700 rounded-xl" data-sender-id="${escapeHtml(user.id)}" data-sender-name="${escapeHtml(user.name || user.fullName || 'Пользователь')}">
                    <div class="font-medium text-sm text-gray-800 dark:text-gray-100">${escapeHtml(user.name || user.fullName || 'Пользователь')}</div>
                </button>`).join('');
            results.querySelectorAll('[data-sender-id]').forEach(btn => {
                btn.addEventListener('click', () => {
                    document.getElementById('message-search-sender').value = btn.dataset.senderName || '';
                    document.getElementById('message-search-sender-id').value = btn.dataset.senderId || '';
                    document.getElementById('message-search-sender-clear')?.classList.remove('hidden');
                    results.classList.add('hidden');
                    performMessageSearch();
                });
            });
            results.classList.remove('hidden');
        } catch (e) {
            console.warn('Поиск отправителя не удался', e);
        }
    }, 250);
}

document.addEventListener('DOMContentLoaded', () => {
    const searchInput = document.getElementById('message-search-input');
    if (searchInput) {
        searchInput.addEventListener('input', e => debouncedSearch(e.target.value));
        searchInput.addEventListener('keydown', e => {
            if (e.key === 'Escape') {
                e.preventDefault();
                clearMessageSearch(true);
            }
            if (e.key === 'Enter') {
                e.preventDefault();
                clearTimeout(searchDebounceTimer);
                performMessageSearch(e.target.value);
            }
        });
    }

    document.getElementById('message-search-scope')?.addEventListener('change', () => {
        updateSearchScopeUi();
        performMessageSearch();
    });

    ['message-search-date-from', 'message-search-date-to', 'message-search-has-attachments', 'message-search-unread-only'].forEach(id => {
        document.getElementById(id)?.addEventListener('change', () => performMessageSearch());
    });

    const sender = document.getElementById('message-search-sender');
    sender?.addEventListener('input', e => {
        document.getElementById('message-search-sender-id').value = '';
        document.getElementById('message-search-sender-clear')?.classList.toggle('hidden', !e.target.value.trim());
        searchMessageSenders(e.target.value);
    });

    document.getElementById('message-search-sender-clear')?.addEventListener('click', () => {
        document.getElementById('message-search-sender').value = '';
        document.getElementById('message-search-sender-id').value = '';
        document.getElementById('message-search-sender-clear')?.classList.add('hidden');
        document.getElementById('message-search-sender-results')?.classList.add('hidden');
        performMessageSearch();
    });

    updateSearchScopeUi();
});