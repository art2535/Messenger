/**
 * Карточка профиля пользователя (из «Переслано от», из инфо чата).
 * Кнопка «сообщение»: открывает существующий личный чат или создаёт новый.
 */
(function () {
    'use strict';

    const DEFAULT_AVATAR = 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png';

    function apiBase() {
        return (typeof API_BASE !== 'undefined' && API_BASE) ||
            (typeof API_BASE_URL !== 'undefined' && API_BASE_URL) ||
            '/api/v1.0';
    }

    function ensureModal() {
        let modal = document.getElementById('user-profile-modal');
        if (modal) return modal;

        modal = document.createElement('div');
        modal.id = 'user-profile-modal';
        modal.className = 'user-profile-modal';
        modal.innerHTML =
            '<div class="user-profile-sheet">' +
            '  <div class="user-profile-topbar">' +
            '    <button type="button" class="user-profile-icon-btn" id="user-profile-close" aria-label="Закрыть">' +
            '      <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6 6 18M6 6l12 12"/></svg>' +
            '    </button>' +
            '    <span class="user-profile-top-title">Информация</span>' +
            '    <span class="user-profile-top-spacer"></span>' +
            '  </div>' +
            '  <div class="user-profile-hero">' +
            '    <img id="user-profile-avatar" class="user-profile-avatar" src="' + DEFAULT_AVATAR + '" alt="">' +
            '    <h2 id="user-profile-name" class="user-profile-name">—</h2>' +
            '    <p id="user-profile-status" class="user-profile-status"></p>' +
            '    <div class="user-profile-actions">' +
            '      <button type="button" class="user-profile-action" data-action="message">' +
            '        <span class="upa-icon">💬</span><span>сообщение</span>' +
            '      </button>' +
            '      <button type="button" class="user-profile-action" data-action="call" title="Недоступно">' +
            '        <span class="upa-icon">📞</span><span>позвонить</span>' +
            '      </button>' +
            '      <button type="button" class="user-profile-action" data-action="mute">' +
            '        <span class="upa-icon">🔔</span><span>выкл. звук</span>' +
            '      </button>' +
            '      <button type="button" class="user-profile-action" data-action="more">' +
            '        <span class="upa-icon">⋮</span><span>ещё</span>' +
            '      </button>' +
            '    </div>' +
            '  </div>' +
            '  <div class="user-profile-card">' +
            '    <div class="user-profile-row" id="user-profile-login-row">' +
            '      <span class="upr-icon">@</span>' +
            '      <div><div id="user-profile-login" class="upr-value">—</div><div class="upr-label">Имя пользователя</div></div>' +
            '    </div>' +
            '  </div>' +
            '</div>';
        document.body.appendChild(modal);

        modal.querySelector('#user-profile-close').addEventListener('click', closeUserProfile);
        modal.addEventListener('click', (e) => {
            if (e.target === modal) closeUserProfile();
        });
        modal.querySelector('.user-profile-actions').addEventListener('click', onActionClick);
        return modal;
    }

    function formatLastSeen(iso) {
        if (!iso) return '';
        try {
            const d = new Date(iso);
            if (isNaN(d.getTime())) return '';
            const diff = (Date.now() - d.getTime()) / 1000;
            if (diff < 300) return 'был(а) недавно';
            if (diff < 86400) {
                return 'был(а) в ' + d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
            }
            return 'был(а) ' + d.toLocaleDateString('ru-RU', { day: 'numeric', month: 'short' });
        } catch (_) {
            return '';
        }
    }

    async function fetchProfile(userId) {
        const url = apiBase().replace(/\/$/, '') + '/users/' + encodeURIComponent(userId) + '/profile';
        const fetchFn = typeof fetchWithAuth === 'function' ? fetchWithAuth : fetch;
        const res = await fetchFn(url, { headers: { 'Accept': 'application/json' } });
        if (!res || !res.ok) throw new Error('profile ' + (res && res.status));
        const json = await res.json();
        return json.data || json.Data || json;
    }

    function readLiveStatus(userId) {
        if (!userId) return null;
        if (typeof getUserStatus === 'function') return getUserStatus(userId);
        if (typeof userStatuses === 'undefined' || !userStatuses) return null;
        const raw = String(userId).trim();
        return userStatuses.get(raw)
            || userStatuses.get(raw.toLowerCase())
            || userStatuses.get(raw.toUpperCase())
            || null;
    }

    function applyStatusToProfileEl(statusEl, online, lastSeen) {
        if (!statusEl) return;
        if (online) {
            statusEl.textContent = 'в сети';
            statusEl.classList.add('online');
        } else {
            statusEl.textContent = formatLastSeen(lastSeen) || 'не в сети';
            statusEl.classList.remove('online');
        }
    }

    function fillProfile(data, userIdHint) {
        const name = data.fullName || data.FullName ||
            [data.firstName || data.FirstName, data.lastName || data.LastName].filter(Boolean).join(' ') ||
            'Пользователь';
        const login = data.login || data.Login || '';
        const avatar = data.avatar || data.Avatar || DEFAULT_AVATAR;

        // приоритет: live SignalR-статус → поля API
        const uid = userIdHint
            || document.getElementById('user-profile-modal')?.dataset?.userId
            || data.userId || data.UserId || data.id || data.Id
            || '';
        const live = readLiveStatus(uid);
        let online = live
            ? live.isOnline === true
            : (data.isOnline === true || data.IsOnline === true || data.online === true || data.Online === true);
        let lastSeen = (live && live.lastActivity)
            || data.lastSeen || data.LastSeen
            || data.lastActivity || data.LastActivity
            || data.lastOnline || data.LastOnline
            || null;

        const avatarEl = document.getElementById('user-profile-avatar');
        const nameEl = document.getElementById('user-profile-name');
        const statusEl = document.getElementById('user-profile-status');
        const loginEl = document.getElementById('user-profile-login');
        const loginRow = document.getElementById('user-profile-login-row');

        if (avatarEl) avatarEl.src = avatar || DEFAULT_AVATAR;
        if (nameEl) nameEl.textContent = name;
        applyStatusToProfileEl(statusEl, online, lastSeen);
        if (loginEl) loginEl.textContent = login || '—';
        if (loginRow) loginRow.style.display = login ? '' : 'none';
    }

    function updateOpenUserProfileStatus(userId, isOnline, lastActivity) {
        const modal = document.getElementById('user-profile-modal');
        if (!modal || !modal.classList.contains('show')) return;
        const openId = String(modal.dataset.userId || '').trim().toLowerCase();
        if (!openId || openId !== String(userId || '').trim().toLowerCase()) return;
        const statusEl = document.getElementById('user-profile-status');
        applyStatusToProfileEl(statusEl, !!isOnline, lastActivity || null);
    }
    window.updateOpenUserProfileStatus = updateOpenUserProfileStatus;

    async function openUserProfile(userId, fallback) {
        if (!userId) return;
        const modal = ensureModal();
        modal.dataset.userId = String(userId);
        // сразу показываем live-статус, если уже есть
        const live = readLiveStatus(userId);
        fillProfile(Object.assign({
            fullName: 'Загрузка…',
            login: '',
            isOnline: live ? live.isOnline : undefined,
            lastActivity: live ? live.lastActivity : undefined
        }, fallback || {}), userId);
        modal.classList.add('show');

        // запросить актуальный статус через SignalR
        if (typeof requestPartnerStatus === 'function') {
            try { requestPartnerStatus(String(userId)); } catch (_) {}
        }

        try {
            const data = await fetchProfile(userId);
            if (modal.dataset.userId === String(userId)) fillProfile(data, userId);
        } catch (e) {
            console.warn('[user-profile]', e);
            if (fallback) fillProfile(fallback, userId);
            else if (live) fillProfile({ isOnline: live.isOnline, lastActivity: live.lastActivity }, userId);
        }
    }

    function closeUserProfile() {
        document.getElementById('user-profile-modal')?.classList.remove('show');
    }

    function participantMatchesUser(p, userId) {
        const id = String(p.id || p.userId || p.UserId || p.Id || '').trim().toLowerCase();
        return id && id === String(userId).trim().toLowerCase();
    }

    function isPrivateChat(chat) {
        const t = (chat.type || chat.Type || '').toString().toLowerCase();
        return t === 'private' || chat.isPrivate === true;
    }

    function chatIdOf(chat) {
        return String(chat.chatId || chat.ChatId || chat.id || chat.Id || '');
    }

    /** Нормализация id для сравнения */
    function normId(v) {
        return String(v || '').trim().toLowerCase();
    }

    function myUserId() {
        return normId(typeof me !== 'undefined' ? me : '');
    }

    function isSelfUserId(userId) {
        const a = normId(userId);
        const b = myUserId();
        return !!(a && b && a === b);
    }

    /** Найти чат «Избранное» (личный чат с самим собой) */
    async function findFavoritesChatId() {
        const myId = myUserId();
        // 1) помеченные элементы списка
        const items = document.querySelectorAll('.chat-item[data-chat-id]');
        for (const el of items) {
            if (el.dataset.isSelfChat === '1' || el.dataset.isFavorites === '1') {
                return el.dataset.chatId || null;
            }
            // partnerId === me
            if (normId(el.dataset.partnerId) === myId && (el.dataset.chatType || '') === 'private') {
                return el.dataset.chatId || null;
            }
        }
        // 2) кэш
        const map = window.__privatePartnerMap || {};
        for (const [chatId, partnerId] of Object.entries(map)) {
            if (normId(partnerId) === myId) return chatId;
        }
        // 3) private-чаты без другого участника (GET detail)
        return await findExistingPrivateChatId(myId);
    }

    function markChatAsFavorites(chatId) {
        if (!chatId) return;
        const id = String(chatId);
        window.__privatePartnerMap = window.__privatePartnerMap || {};
        window.__privatePartnerMap[id] = myUserId() || 'self';
        window.__favoritesChatId = id;
        const el = document.querySelector(`.chat-item[data-chat-id="${CSS.escape(id)}"]`);
        if (el) {
            el.dataset.isSelfChat = '1';
            el.dataset.isFavorites = '1';
            el.dataset.chatType = 'private';
            const h3 = el.querySelector('h3');
            if (h3) h3.textContent = 'Избранное';
        }
        const title = document.getElementById('chat-title');
        if (title && String(currentChatId) === id) title.textContent = 'Избранное';
    }

    /**
     * Ищем личный чат с userId.
     * В GET /chats нет participants — поэтому:
     *  1) data-partner-id в DOM
     *  2) кэш window.__privatePartnerMap (chatId -> partnerId)
     *  3) для private-чатов без partnerId — GET /chats/{id} (с кэшем)
     */
    async function findExistingPrivateChatId(userId) {
        const uid = normId(userId);
        if (!uid) return null;

        // 1) Список слева по data-partner-id
        const items = document.querySelectorAll('.chat-item[data-chat-id]');
        for (const el of items) {
            if (normId(el.dataset.partnerId) === uid) {
                return el.dataset.chatId || null;
            }
        }

        // 2) Локальный кэш partner map
        const map = window.__privatePartnerMap || {};
        for (const [chatId, partnerId] of Object.entries(map)) {
            if (normId(partnerId) === uid) return chatId;
        }

        // 3) Детали private-чатов: сначала с type=private, иначе все без известного partner
        const candidates = [...items].filter(el => (el.dataset.chatType || '').toLowerCase() === 'private');
        const toCheck = candidates.length ? candidates : [...items];

        const fetchFn = (typeof fetchWithAuth === 'function')
            ? fetchWithAuth
            : (url, opts) => fetch(url, {
                ...opts,
                headers: {
                    ...(opts && opts.headers || {}),
                    'Authorization': `Bearer ${typeof token !== 'undefined' ? token : ''}`
                }
            });

        const baseUrl = (typeof API_BASE !== 'undefined' && API_BASE)
            ? API_BASE.replace(/\/$/, '')
            : apiBase().replace(/\/$/, '');

        for (const el of toCheck) {
            const chatId = el.dataset.chatId;
            if (!chatId) continue;
            if (normId(el.dataset.partnerId) === uid) return chatId;

            // already know partner from map
            if (map[chatId] && normId(map[chatId]) === uid) return chatId;
            if (map[chatId]) continue; // known other partner

            try {
                const res = await fetchFn(`${baseUrl}/chats/${encodeURIComponent(chatId)}`, {
                    headers: { 'Accept': 'application/json', 'Authorization': `Bearer ${typeof token !== 'undefined' ? token : ''}` }
                });
                if (!res || !res.ok) continue;
                const json = await res.json();
                const data = json.data || json.Data || json;
                const type = (data.type || data.Type || '').toString().toLowerCase();
                if (type && type !== 'private') continue;
                const parts = data.participants || data.Participants || [];
                const myId = normId(typeof me !== 'undefined' ? me : '');
                for (const p of parts) {
                    const pid = normId(p.id || p.userId || p.UserId || p.Id);
                    if (!pid || pid === myId) continue;
                    window.__privatePartnerMap = window.__privatePartnerMap || {};
                    window.__privatePartnerMap[String(chatId)] = pid;
                    if (el) el.dataset.partnerId = pid;
                    if (pid === uid) return chatId;
                }
            } catch (e) {
                console.warn('[user-profile] chat detail', chatId, e);
            }
        }
        return null;
    }

    async function openChatById(chatId) {
        if (!chatId) return;
        closeUserProfile();
        const sel = `.chat-item[data-chat-id="${CSS.escape(String(chatId))}"]`;
        let item = document.querySelector(sel);
        if (!item && typeof loadChats === 'function') {
            try { await loadChats(); } catch (_) {}
            item = document.querySelector(sel);
        }
        if (item && typeof openChat === 'function') {
            await openChat(item);
            return;
        }
        // fallback: navigate
        const base = (typeof CHATS_BASE_PATH !== 'undefined' && CHATS_BASE_PATH)
            ? CHATS_BASE_PATH.replace(/\/$/, '')
            : '/Inside/Chats';
        window.location.href = base + '/' + encodeURIComponent(String(chatId));
    }

    async function openOrCreatePrivateChat(userId) {
        if (!userId) return;
        closeUserProfile();
        if (typeof closeChatInfoModal === 'function') {
            try { closeChatInfoModal(); } catch (_) {}
        }

        const selfChat = isSelfUserId(userId);

        // 1) Уже есть чат — только переход
        try {
            const existingId = selfChat
                ? await findFavoritesChatId()
                : await findExistingPrivateChatId(userId);
            if (existingId) {
                if (selfChat) markChatAsFavorites(existingId);
                await openChatById(existingId);
                if (selfChat) markChatAsFavorites(existingId);
                return;
            }
        } catch (e) {
            console.warn('[user-profile] find existing failed', e);
        }

        // 2) Создаём (для себя — тот же private API, UI = «Избранное»)
        try {
            const baseUrl = (typeof API_BASE !== 'undefined' && API_BASE)
                ? API_BASE.replace(/\/$/, '')
                : apiBase().replace(/\/$/, '');
            const authHeaders = {
                'Content-Type': 'application/json',
                'Accept': 'application/json',
                'Authorization': `Bearer ${typeof token !== 'undefined' ? token : ''}`
            };
            // API требует ровно 1 userId в private; для избранного передаём свой id
            const res = await fetch(`${baseUrl}/chats/create-chat`, {
                method: 'POST',
                headers: authHeaders,
                body: JSON.stringify({ type: 'private', userIds: [userId] })
            });
            const json = await res.json().catch(() => ({}));
            if (!res.ok || json.isSuccess === false) {
                const again = selfChat
                    ? await findFavoritesChatId()
                    : await findExistingPrivateChatId(userId);
                if (again) {
                    if (selfChat) markChatAsFavorites(again);
                    await openChatById(again);
                    if (selfChat) markChatAsFavorites(again);
                    return;
                }
                throw new Error(json.error || json.Error || ('Не удалось создать чат (' + res.status + ')'));
            }
            const chat = json.data || json.Data || json;
            const chatId = chat.chatId || chat.ChatId || chat.id;
            if (!chatId) throw new Error('Сервер не вернул chatId');

            window.__privatePartnerMap = window.__privatePartnerMap || {};
            window.__privatePartnerMap[String(chatId)] = String(userId);
            if (selfChat) {
                window.__favoritesChatId = String(chatId);
            }

            if (typeof loadChats === 'function') {
                try { await loadChats(); } catch (_) {}
            }
            if (selfChat) markChatAsFavorites(chatId);
            await openChatById(chatId);
            if (selfChat) markChatAsFavorites(chatId);
        } catch (e) {
            console.error('[user-profile] openOrCreate', e);
            if (typeof showToast === 'function') showToast(e.message || 'Не удалось открыть чат', 'error');
            else alert(e.message || 'Не удалось открыть чат');
        }
    }

    function onActionClick(e) {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;
        const action = btn.dataset.action;
        const userId = document.getElementById('user-profile-modal')?.dataset.userId;
        if (action === 'message' && userId) {
            openOrCreatePrivateChat(userId);
            return;
        }
        if (action === 'call') {
            if (typeof showToast === 'function') showToast('Звонки пока недоступны', 'info');
            return;
        }
        if (action === 'mute') {
            if (typeof showToast === 'function') showToast('Уведомления можно настроить в параметрах чата', 'info');
            return;
        }
        if (action === 'more') {
            if (typeof showToast === 'function') showToast('Дополнительные действия скоро появятся', 'info');
        }
    }

    function nameMatchScore(fullName, query) {
        const a = String(fullName || '').trim().toLowerCase().replace(/\s+/g, ' ');
        const b = String(query || '').trim().toLowerCase().replace(/\s+/g, ' ');
        if (!a || !b) return 0;
        if (a === b) return 100;
        if (a.startsWith(b) || b.startsWith(a)) return 80;
        if (a.includes(b) || b.includes(a)) return 60;
        // сравнение по словам
        const aw = a.split(' ');
        const bw = b.split(' ');
        let hits = 0;
        for (const w of bw) {
            if (w.length > 1 && aw.some(x => x.startsWith(w) || w.startsWith(x))) hits++;
        }
        return hits ? 40 + hits * 10 : 0;
    }

    /** Резолв userId автора пересылки: dataset → кэш → участники → поиск API */
    async function resolveForwardSenderId(row, displayName) {
        let uid = row?.dataset?.forwardSenderId || '';
        if (uid) return uid;

        const nameEl = row?.querySelector?.('.forward-label-name');
        if (nameEl?.dataset?.userId) return nameEl.dataset.userId;

        if (row?.dataset?.forwardOf) {
            const orig = document.querySelector(`.message-row[data-mid="${CSS.escape(row.dataset.forwardOf)}"]`);
            if (orig?.dataset?.senderId) return orig.dataset.senderId;
            try {
                const cached = window.__messageForwardCache?.get?.(String(row.dataset.forwardOf));
                if (cached?.senderId) return String(cached.senderId);
            } catch (_) {}
        }

        const q = String(displayName || nameEl?.textContent || '').trim();
        if (!q || q === 'Пользователь') return '';

        // участники текущего чата
        const parts = (typeof currentChatInfo !== 'undefined' && currentChatInfo)
            ? (currentChatInfo.participants || currentChatInfo.Participants || [])
            : [];
        let best = null;
        let bestScore = 0;
        for (const p of parts) {
            const nm = p.fullName || p.name || p.FullName ||
                [p.firstName, p.lastName].filter(Boolean).join(' ') || '';
            const sc = nameMatchScore(nm, q);
            if (sc > bestScore) {
                bestScore = sc;
                best = p;
            }
        }
        if (best && bestScore >= 40) {
            return String(best.id || best.userId || best.UserId || best.Id || '');
        }

        // список чатов: private partner с таким именем
        const items = document.querySelectorAll('.chat-item[data-partner-id]');
        for (const el of items) {
            const title = el.querySelector('h3')?.textContent || '';
            if (nameMatchScore(title, q) >= 80) {
                return el.dataset.partnerId;
            }
        }

        // API search
        try {
            const baseUrl = (typeof API_BASE !== 'undefined' && API_BASE)
                ? API_BASE.replace(/\/$/, '')
                : apiBase().replace(/\/$/, '');
            const res = await fetch(`${baseUrl}/users/search?query=${encodeURIComponent(q)}`, {
                headers: {
                    'Accept': 'application/json',
                    'Authorization': `Bearer ${typeof token !== 'undefined' ? token : ''}`
                }
            });
            if (res && res.ok) {
                const json = await res.json();
                const list = json.data || json.Data || [];
                if (Array.isArray(list) && list.length) {
                    let top = list[0];
                    let topSc = 0;
                    for (const u of list) {
                        const nm = u.name || u.fullName || u.FullName || u.login || '';
                        const sc = nameMatchScore(nm, q);
                        if (sc > topSc) { topSc = sc; top = u; }
                    }
                    const id = top.id || top.userId || top.Id || top.UserId;
                    if (id) return String(id);
                }
            }
        } catch (err) {
            console.warn('[user-profile] resolve forward sender', err);
        }
        return '';
    }

    document.addEventListener('click', async (e) => {
        // Клик по имени или по всей плашке «Переслано от»
        const label = e.target.closest('.forward-label-name, .forward-label-name-link, .forward-label');
        if (!label) return;
        // не мешаем другим контролам внутри пузыря
        if (e.target.closest('a, button, input, textarea')) return;

        e.preventDefault();
        e.stopPropagation();

        const nameEl = label.classList?.contains('forward-label-name') || label.classList?.contains('forward-label-name-link')
            ? label
            : label.querySelector('.forward-label-name, .forward-label-name-link');
        const row = label.closest('[data-mid], .message-row');
        const displayName = (nameEl?.textContent || row?.dataset?.forwardFrom || '').trim();

        let uid = row?.dataset?.forwardSenderId || nameEl?.dataset?.userId || '';
        if (!uid) {
            uid = await resolveForwardSenderId(row, displayName);
        }
        if (!uid) {
            console.warn('[user-profile] не удалось определить автора пересылки:', displayName);
            return;
        }

        // кэшируем на элементе для следующих кликов
        if (row) row.dataset.forwardSenderId = uid;
        if (nameEl) nameEl.dataset.userId = uid;

        openUserProfile(uid, { fullName: displayName || 'Пользователь' });
    }, true); // capture — до обработчиков строки сообщения

    window.openUserProfile = openUserProfile;
    window.closeUserProfile = closeUserProfile;
    window.openOrCreatePrivateChat = openOrCreatePrivateChat;
    window.findExistingPrivateChatId = findExistingPrivateChatId;
})();
