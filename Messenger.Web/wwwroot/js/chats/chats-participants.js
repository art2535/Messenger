function toggleChatNameField() {
    const isGroup = document.querySelector('input[name="chat-type"]:checked').value === 'group';
    document.getElementById('group-name-container').classList.toggle('hidden', !isGroup);
}

document.getElementById('create-chat-btn').onclick = () => {
    selectedUserIds = [];
    document.getElementById('selected-users').innerHTML = '';
    document.getElementById('chat-name').value = '';
    document.getElementById('user-search').value = '';
    document.querySelector('input[value="private"]').checked = true;
    toggleChatNameField();
    document.getElementById('create-chat-modal').classList.add('show');

    lastQuery = '';
    document.getElementById('user-search-results').classList.remove('show');

    setTimeout(() => document.getElementById('user-search').focus(), 150);
};

document.querySelectorAll('input[name="chat-type"]').forEach(r => r.addEventListener('change', toggleChatNameField));
document.getElementById('close-modal').onclick = document.getElementById('cancel-create').onclick = () => {
    document.getElementById('create-chat-modal').classList.remove('show');
};

async function searchUsers() {
    const query = document.getElementById('user-search').value.trim();
    const results = document.getElementById('user-search-results');

    if (query.length < 2) {
        results.classList.remove('show');
        results.innerHTML = '';
        lastQuery = '';
        return;
    }

    if (query === lastQuery) return;
    lastQuery = query;

    results.innerHTML = `
        <div class="p-4 text-center text-gray-500">
            <i data-feather="loader" class="w-5 h-5 animate-spin inline"></i> Поиск...
        </div>
    `;
    results.classList.add('show');
    feather.replace();

    try {
        const res = await fetch(`${API_BASE}/users/search?query=${encodeURIComponent(query)}`, {
            headers: { 'Authorization': `Bearer ${token}` }
        });
        const json = await res.json();

        results.innerHTML = '';

        if (!json.data || json.data.length === 0) {
            results.innerHTML = '<div class="p-4 text-gray-500 text-sm">Ничего не найдено</div>';
            feather.replace();
            return;
        }

        const availableUsers = json.data
            .filter(u => !selectedUserIds.includes(u.id))
            .slice(0, 8);

        if (availableUsers.length === 0) {
            results.innerHTML = '<div class="p-4 text-gray-500 text-sm">Все уже добавлены</div>';
            feather.replace();
            return;
        }

        availableUsers.forEach(user => {
            const div = document.createElement('div');
            div.className = 'p-3 hover:bg-gray-50 cursor-pointer flex items-center gap-3 transition rounded-lg';
            div.innerHTML = `
                <img src="${user.avatar || 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png'}" class="w-10 h-10 rounded-full object-cover">
                <span class="font-medium">${user.name || 'Без имени'}</span>
            `;

            div.onclick = () => {
                if (selectedUserIds.includes(user.id)) return;

                const isPrivate = document.querySelector('input[name="chat-type"]:checked').value === 'private';
                if (isPrivate && selectedUserIds.length >= 1) {
                    showToast('В приватном чате может быть только один участник', 'error');
                    return;
                }

                selectedUserIds.push(user.id);

                const tag = document.createElement('div');
                tag.className = 'inline-flex items-center gap-2 px-3 py-2 rounded-full bg-blue-100 text-blue-800 text-sm font-medium';
                tag.innerHTML = `
                    ${user.name || 'Пользователь'}
                    <button type="button" class="ml-2 hover:bg-blue-200 rounded-full p-0.5 transition">
                        <i data-feather="x" class="w-4 h-4"></i>
                    </button>
                `;

                tag.querySelector('button').onclick = (e) => {
                    e.stopPropagation();
                    selectedUserIds = selectedUserIds.filter(id => id !== user.id);
                    tag.remove();
                };

                document.getElementById('selected-users').appendChild(tag);
                feather.replace();

                document.getElementById('user-search').value = '';
                results.classList.remove('show');
                lastQuery = '';
            };

            results.appendChild(div);
        });

        feather.replace();

    } catch (err) {
        console.error('Ошибка поиска:', err);
        results.innerHTML = '<div class="p-4 text-red-500 text-sm">Ошибка связи</div>';
        feather.replace();
    }
}

document.getElementById('user-search').addEventListener('input', () => {
    clearTimeout(searchTimeout);
    searchTimeout = setTimeout(searchUsers, 300);
});

document.getElementById('confirm-create').onclick = async () => {
    const type = document.querySelector('input[name="chat-type"]:checked').value;
    const name = type === 'group' ? document.getElementById('chat-name').value.trim() : null;
    if (type === 'group' && !name)
        return showToast('Введите название группы', 'error');
    if (selectedUserIds.length === 0)
        return showToast('Выберите участников', 'error');
    if (type === 'private' && selectedUserIds.length !== 1)
        return showToast('В приватном чате — один участник', 'error');

    try {
        const res = await fetch(`${API_BASE}/chats/create-chat`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${token}`, 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, type, userIds: selectedUserIds })
        });
        const json = await res.json();
        if (!res.ok || !json.isSuccess) throw new Error(json.error || 'Ошибка');
        document.getElementById('create-chat-modal').classList.remove('show');

        await loadChats();
    } catch (err) {
        showToast('Ошибка: ' + err.message, 'error');
    }
};

let currentChatInfo = null;
let chatNameChanged = false;
let removedParticipantIds = [];
let addedParticipantIds = [];

const ADDED_BY_ME_KEY = 'guap_chat_added_by_me';

function loadAddedByMeMap() {
    try {
        return JSON.parse(localStorage.getItem(ADDED_BY_ME_KEY) || '{}') || {};
    } catch {
        return {};
    }
}

function saveAddedByMeMap(map) {
    try {
        localStorage.setItem(ADDED_BY_ME_KEY, JSON.stringify(map));
    } catch (_) {}
}

function getAddedByMeForChat(chatId) {
    if (!chatId) return new Set();
    const map = loadAddedByMeMap();
    const arr = map[String(chatId)] || [];
    return new Set(arr.map(String));
}

function markAddedByMe(chatId, userId) {
    if (!chatId || !userId) return;
    const map = loadAddedByMeMap();
    const key = String(chatId);
    const set = new Set((map[key] || []).map(String));
    set.add(String(userId));
    map[key] = [...set];
    saveAddedByMeMap(map);
}

function unmarkAddedByMe(chatId, userId) {
    if (!chatId || !userId) return;
    const map = loadAddedByMeMap();
    const key = String(chatId);
    map[key] = (map[key] || []).map(String).filter(id => id !== String(userId));
    saveAddedByMeMap(map);
}

function normalizeParticipantRole(role) {
    return String(role || 'участник').trim().toLowerCase();
}

function isAdminOrOwnerRole(role) {
    const r = normalizeParticipantRole(role);
    return r === 'владелец' || r === 'owner' || r === 'admin' || r === 'админ'
        || r === 'administrator' || r === 'модератор' || r === 'moderator';
}

function getMyRoleInChat(info) {
    const parts = info?.participants || info?.Participants || [];
    const myId = String(me || '').trim().toLowerCase();
    const mine = parts.find(p => String(p.id || p.userId || '').trim().toLowerCase() === myId);
    if (mine) return normalizeParticipantRole(mine.role || mine.Role);
    const creatorId = String(info?.userId || info?.UserId || info?.creatorId || info?.CreatorId || '').trim().toLowerCase();
    if (creatorId && creatorId === myId) return 'владелец';
    return 'участник';
}

function canRemoveParticipant(targetUserId, targetRole, chatInfo) {
    const tid = String(targetUserId || '').trim();
    if (!tid) return false;
    const myId = String(me || '').trim();
    if (tid.toLowerCase() === myId.toLowerCase()) return false;

    const info = chatInfo || currentChatInfo || window.__modalChatInfo || {};
    const myRole = getMyRoleInChat(info);
    const tRole = normalizeParticipantRole(targetRole);

    if (isAdminOrOwnerRole(myRole)) {
        if (myRole !== 'владелец' && myRole !== 'owner' && (tRole === 'владелец' || tRole === 'owner')) {
            return false;
        }
        return true;
    }

    if (isAdminOrOwnerRole(tRole)) return false;

    const chatId = modalChatId || currentChatId || info.chatId || info.ChatId;
    if (addedParticipantIds.map(String).includes(tid)) return true;
    if (getAddedByMeForChat(chatId).has(tid)) return true;
    return false;
}

function roleBadgeHtml(role) {
    const r = normalizeParticipantRole(role);
    if (r === 'владелец' || r === 'owner') {
        return '<p class="text-xs text-amber-600 dark:text-amber-400 mt-0.5">Владелец</p>';
    }
    if (isAdminOrOwnerRole(r)) {
        return '<p class="text-xs text-purple-600 dark:text-purple-400 mt-0.5">Админ</p>';
    }
    return '';
}

async function removeParticipantFromChat(userId, buttonElement) {
    const row = buttonElement?.closest?.('[data-user-id]') || buttonElement?.closest?.('div');
    const targetRole = row?.dataset?.role || 'участник';
    const info = window.__modalChatInfo || currentChatInfo;

    if (!canRemoveParticipant(userId, targetRole, info)) {
        showToast('Нельзя удалить этого участника: только своих добавленных, админов удалять нельзя', 'error');
        return;
    }

    if (!(await showConfirm('Удалить участника из чата?', { title: 'Удаление участника', okText: 'Удалить', cancelText: 'Отмена', danger: true }))) return;

    row?.remove();
    if (!removedParticipantIds.includes(userId)) {
        removedParticipantIds.push(userId);
    }
    unmarkAddedByMe(modalChatId || currentChatId, userId);
    updateParticipantCount();
    toggleSaveButton();
}

function addParticipantToChat(userId, userName, userAvatar) {
    if (document.querySelector(`#participants-list [data-user-id="${userId}"]`)) {
        showToast('Этот пользователь уже в чате', 'error');
        return;
    }

    const list = document.getElementById('participants-list');
    const div = document.createElement('div');
    div.className = 'flex items-center justify-between p-4 bg-gray-50 rounded-xl';
    div.dataset.userId = userId;

    div.dataset.role = 'участник';
    div.innerHTML = `
        <div class="flex items-center gap-4">
            <img src="${userAvatar || 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png'}"
                    class="w-12 h-12 rounded-full object-cover">
            <div>
                <p class="font-semibold">${userName}</p>
            </div>
        </div>
        ${currentChatInfo?.type === 'group' ? `
        <button type="button" class="text-red-600 hover:bg-red-100 dark:hover:bg-red-900/30 rounded-full p-2 transition"
                onclick="removeParticipantFromChat('${userId}', this)">
            <i data-feather="x" class="w-5 h-5"></i>
        </button>` : ''}
    `;
    list.appendChild(div);
    feather.replace();

    if (!addedParticipantIds.includes(userId)) {
        addedParticipantIds.push(userId);
    }
    markAddedByMe(modalChatId || currentChatId, userId);
    updateParticipantCount();
    toggleSaveButton();
}

function updateParticipantCount() {
    const count = document.getElementById('participants-count');
    const base = currentChatInfo.participants.length;
    const newCount = base - removedParticipantIds.length + addedParticipantIds.length;
    count.textContent = `(${newCount})`;
}

function toggleSaveButton() {
    const saveBtn = document.getElementById('save-chat-info');
    const cancelBtn = document.getElementById('chat-info-cancel-btn');
    const hasChanges = chatNameChanged || removedParticipantIds.length > 0 || addedParticipantIds.length > 0;
    if (!saveBtn) return;
    if (hasChanges) {
        saveBtn.classList.remove('hidden');
        saveBtn.classList.remove('opacity-0', 'pointer-events-none');
    } else {
        saveBtn.classList.add('hidden');
    }
    if (cancelBtn) {
        cancelBtn.classList.toggle('flex-1', true);
    }
    saveBtn.classList.toggle('flex-1', true);
}

document.getElementById('add-participant-btn').addEventListener('click', function () {
    const existing = document.getElementById('chat-info-search-container');
    if (existing) {
        existing.querySelector('input').focus();
        return;
    }

    const container = document.createElement('div');
    container.id = 'chat-info-search-container';
    container.className = 'mb-4 relative';

    container.innerHTML = `
        <div class="relative">
            <input type="text" id="chat-info-search-input" placeholder="Введите имя пользователя..."
                    class="w-full px-4 py-3 pl-11 border border-gray-300 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500 transition"
                    autocomplete="off">
            <i data-feather="search" class="absolute left-4 top-3.5 w-5 h-5 text-gray-400 pointer-events-none"></i>
            <div id="chat-info-search-results"
                    class="absolute top-full left-0 right-0 mt-1 bg-white border border-gray-200 rounded-xl shadow-xl max-h-64 overflow-y-auto z-20 hidden"></div>
        </div>
    `;

    document.querySelector('#participants-list').before(container);
    feather.replace();

    const input = document.getElementById('chat-info-search-input');
    const results = document.getElementById('chat-info-search-results');
    input.focus();

    let debounceTimeout;

    input.addEventListener('input', () => {
        clearTimeout(debounceTimeout);
        const query = input.value.trim();

        if (query.length < 2) {
            results.classList.remove('show');
            results.innerHTML = '';
            return;
        }

        debounceTimeout = setTimeout(async () => {
            try {
                const response = await fetch(`${API_BASE}/users/search?query=${encodeURIComponent(query)}`, {
                    headers: { 'Authorization': `Bearer ${token}` }
                });
                const json = await response.json();

                results.innerHTML = '';

                if (!json.data || json.data.length === 0) {
                    results.innerHTML = `<div class="p-4 text-gray-500 text-sm text-center">Ничего не найдено</div>`;
                    results.classList.add('show');
                    return;
                }

                const currentUserIds = Array.from(document.querySelectorAll('#participants-list [data-user-id]'))
                    .map(el => el.dataset.userId);

                const availableUsers = json.data.filter(u => !currentUserIds.includes(u.id));

                if (availableUsers.length === 0) {
                    results.innerHTML = `<div class="p-4 text-gray-500 text-sm text-center">Все уже добавлены</div>`;
                    results.classList.add('show');
                    return;
                }

                availableUsers.forEach(user => {
                    const item = document.createElement('div');
                    item.className = 'flex items-center gap-3 p-3 hover:bg-gray-50 cursor-pointer transition';
                    item.innerHTML = `
                        <img src="${user.avatar || 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png'}"
                                class="w-10 h-10 rounded-full object-cover flex-shrink-0">
                        <div class="min-w-0 flex-1">
                            <p class="font-medium truncate">${user.name || 'Без имени'}</p>
                        </div>
                    `;

                    item.addEventListener('click', () => {
                        addParticipantToChat(user.id, user.name || 'Пользователь', user.avatar);
                        input.value = '';
                        results.classList.remove('show');
                        input.focus();
                    });

                    results.appendChild(item);
                });

                results.classList.add('show');
            } catch (err) {
                results.innerHTML = `<div class="p-4 text-red-500 text-sm text-center">Ошибка поиска</div>`;
                results.classList.add('show');
            }
        }, 300);
    });

    const closeHandler = (e) => {
        if (!container.contains(e.target)) {
            container.remove();
            document.removeEventListener('click', closeHandler);
        }
    };
    setTimeout(() => document.addEventListener('click', closeHandler), 100);
});

document.getElementById('save-chat-info').addEventListener('click', async () => {
    const targetChatId = modalChatId || currentChatId;
    if (!targetChatId || !currentChatInfo) return;

    const newName = document.getElementById('modal-chat-name').value.trim();
    const isGroup = currentChatInfo.type === 'group';
    const tasks = [];

    if (isGroup && chatNameChanged && newName && newName !== currentChatInfo.name) {
        tasks.push(fetch(`${API_BASE}/chats/${targetChatId}`, {
            method: 'PUT',
            headers: { 'Authorization': `Bearer ${token}`, 'Content-Type': 'application/json' },
            body: JSON.stringify({ Name: newName })
        }));
    }

    removedParticipantIds.forEach(userId => {
        tasks.push(fetch(`${API_BASE}/chats/${targetChatId}/${userId}`, {
            method: 'DELETE',
            headers: { 'Authorization': `Bearer ${token}` }
        }));
    });

    addedParticipantIds.forEach(userId => {
        tasks.push(fetch(`${API_BASE}/chats/${targetChatId}/${userId}/participant`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${token}` }
        }));
    });

    if (tasks.length === 0) return;

    try {
        const responses = await Promise.all(tasks);
        const allSuccess = responses.every(r => r.ok);

        if (!allSuccess) throw new Error('Не все изменения сохранены');

        addedParticipantIds.forEach(uid => markAddedByMe(targetChatId, uid));
        removedParticipantIds.forEach(uid => unmarkAddedByMe(targetChatId, uid));

        if (chatNameChanged && isGroup && newName) {
            document.getElementById('chat-title').textContent = newName;
            const chatItem = document.querySelector(`.chat-item[data-chat-id="${targetChatId}"] h3`);
            if (chatItem) chatItem.textContent = newName;
            currentChatInfo.name = newName;
        }

        const newCount = currentChatInfo.participants.length
            - removedParticipantIds.length
            + addedParticipantIds.length;

        currentChatInfo.participants = currentChatInfo.participants.filter(p =>
            !removedParticipantIds.includes(p.id)
        );

        if (typeof closeChatInfoModal === 'function') closeChatInfoModal(); else document.getElementById('chat-info-modal').classList.remove('show');

        showToast('Изменения сохранены!', 'success');

    } catch (err) {
        showToast('Ошибка: ' + err.message, 'error');
    } finally {
        removedParticipantIds = [];
        addedParticipantIds = [];
        chatNameChanged = false;
    }
});

document.getElementById('modal-chat-name').addEventListener('input', () => {
    chatNameChanged = true;
    toggleSaveButton();
});

document.querySelector('#chat-info-modal button[onclick*="classList.remove"]').onclick = () => {
    if (typeof closeChatInfoModal === 'function') closeChatInfoModal(); else document.getElementById('chat-info-modal').classList.remove('show');
    removedParticipantIds = [];
    addedParticipantIds = [];
    chatNameChanged = false;
};

document.getElementById('delete-chat-btn')?.addEventListener('click', function () {
    const targetChatId = modalChatId || currentChatId;
    if (!targetChatId) return;

    if (typeof closeChatInfoModal === 'function') closeChatInfoModal(); else document.getElementById('chat-info-modal').classList.remove('show');

    const overlay = document.createElement('div');
    overlay.id = 'delete-confirm-overlay';
    overlay.className = 'fixed inset-0 bg-black bg-opacity-60 flex items-center justify-center z-50';
    overlay.innerHTML = `
        <div class="bg-white rounded-2xl p-8 max-w-md w-full mx-4 shadow-2xl animate-in">
            <div class="text-center mb-8">
                <div class="w-20 h-20 bg-red-100 rounded-full flex items-center justify-center mx-auto mb-5">
                    <i data-feather="trash-2" class="w-12 h-12 text-red-600"></i>
                </div>
                <h3 class="text-2xl font-bold text-gray-900">Удалить чат навсегда?</h3>
                <p class="text-gray-600 mt-4 leading-relaxed">
                    Все сообщения и файлы будут удалены.<br>
                    Это действие <strong>нельзя отменить</strong>.
                </p>
            </div>
            <div class="flex gap-4">
                <button id="cancel-delete" class="flex-1 px-6 py-3 bg-gray-200 hover:bg-gray-300 text-gray-800 rounded-xl font-medium transition">
                    Отмена
                </button>
                <button id="confirm-delete" class="flex-1 px-6 py-3 bg-red-600 hover:bg-red-700 text-white rounded-xl font-medium transition">
                    Удалить чат
                </button>
            </div>
        </div>
    `;

    document.body.appendChild(overlay);
    feather.replace();

    overlay.querySelector('#cancel-delete').onclick = () => {
        overlay.remove();
    };

    overlay.querySelector('#confirm-delete').onclick = async () => {
        try {
            const res = await fetch(`${API_BASE}/chats/${targetChatId}`, {
                method: 'DELETE',
                headers: { 'Authorization': `Bearer ${token}` }
            });

            if (!res.ok) throw new Error('Не удалось удалить чат');

            overlay.remove();

            document.querySelector(`.chat-item[data-chat-id="${targetChatId}"]`)?.remove();

            currentChatId = null;
            messagesContainer.innerHTML = `
                <div class="flex items-center justify-center h-full text-gray-500">
                    <p>Выберите чат для просмотра сообщений</p>
                </div>
            `;
            document.getElementById('chat-title').textContent = 'Выберите чат';
            document.getElementById('chat-subtitle').textContent = 'Нажмите, чтобы посмотреть информацию';
            if (typeof updateChatSubtitleMarquee === 'function') updateChatSubtitleMarquee();
            if (typeof updateChatSubtitleMarquee === "function") updateChatSubtitleMarquee();
            document.getElementById('chat-info-avatar').src = 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png';

            if (!document.querySelector('.chat-item')) {
                chatsList.innerHTML = '<div class="p-4 text-gray-500">Чатов нет</div>';
            }

            showToast('Чат успешно удалён', 'success');
        } catch (err) {
            overlay.remove();
            showToast('Ошибка: ' + err.message, 'error');
        }
    };

    const escHandler = (e) => {
        if (e.key === 'Escape') {
            overlay.remove();
            document.removeEventListener('keydown', escHandler);
        }
    };
    document.addEventListener('keydown', escHandler);
});

document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;

    const createModal = document.getElementById('create-chat-modal');
    const infoModal = document.getElementById('chat-info-modal');
    const lightboxEl = document.getElementById('lightbox');
    const deleteOverlay = document.getElementById('delete-confirm-overlay');
    const searchContainer = document.getElementById('chat-info-search-container');

    if (deleteOverlay) {
        deleteOverlay.remove();
        return;
    }
    if (searchContainer) {
        searchContainer.remove();
        return;
    }
    if (infoModal && infoModal.classList.contains('show')) {
        infoModal.classList.remove('show');
        removedParticipantIds = [];
        addedParticipantIds = [];
        chatNameChanged = false;
        return;
    }
    if (createModal && createModal.classList.contains('show')) {
        createModal.classList.remove('show');
        return;
    }
    if (lightboxEl && lightboxEl.classList.contains('show')) {
        lightboxEl.classList.remove('show');
        document.getElementById('lightbox-img').src = '';
        return;
    }

    const pinBar = document.getElementById('pinned-message-bar');
    if (pinBar && pinBar.classList.contains('show')) {
        if (typeof hidePinnedMessageBar === 'function') hidePinnedMessageBar();
        else pinBar.classList.remove('show');
        return;
    }

    if (currentChatId !== null) {
        closeCurrentChat();
    }
});

function closeCurrentChat() {
    if (typeof selectMode !== "undefined" && selectMode) exitSelectMode();
    if (currentChatId && connection?.state === 'Connected') {
        leaveChat(currentChatId).catch(() => { });
    }

    currentChatId = null;
    currentChatInfo = null;

    document.getElementById('chat-header').classList.add('hidden');
    document.getElementById('messages-wrapper').classList.add('hidden');
    document.getElementById('messages-container').classList.add('hidden');
    document.getElementById('chat-input-area').classList.add('hidden');
    document.getElementById('empty-right-panel').classList.remove('hidden');

    document.querySelectorAll('.chat-item').forEach(i => i.classList.remove('active'));

    document.getElementById('message-input').value = '';
    document.getElementById('attached-files').innerHTML = '';
    selectedFiles = [];

    // Скрыть вкладку закреплённых при закрытии чата
    const pinBar = document.getElementById('pinned-message-bar');
    if (pinBar) pinBar.classList.remove('show');

    setMobileChatOpen(false);
}

const originalAppendMessage = appendMessage;
appendMessage = function (msg) {
    if (msg.messageText && msg.messageText.includes("Вы были удалены из чата")) {
        if (document.querySelectorAll('.system-kicked-message').length > 0) {
            return;
        }
        const div = document.createElement('div');
        div.className = 'mb-6 flex justify-center system-kicked-message';
        div.innerHTML = `
            <div class="bg-blue-50 border border-blue-200 text-blue-800 px-6 py-4 rounded-2xl text-center">
                <p class="font-medium">${msg.messageText}</p>
            </div>
        `;
        messagesContainer.appendChild(div);
        scrollToBottom();
        return;
    }

    originalAppendMessage.call(this, msg);
};


            
function getActiveChatIdForModal() {
    return modalChatId || currentChatId;
}

async function openChatInfoForChatId(chatId) {
    if (!chatId) return;
    try {
        const res = await fetchWithAuth(`${API_BASE}/chats/${chatId}`);
        if (!res || !res.ok) throw new Error('Не удалось загрузить чат');
        const json = await res.json();
        const info = json.data || json.Data || json;
        if (!info) throw new Error('Пустой ответ');

        modalChatId = String(chatId);

        const modalInfo = {
            type: info.type || info.Type || 'private',
            participants: info.participants || info.Participants || info.chatParticipants || info.ChatParticipants || [],
            name: info.name || info.Name || info.title || ''
        };
        const creatorId = String(info.userId || info.UserId || info.creatorId || info.CreatorId || '').trim().toLowerCase();
        modalInfo.userId = creatorId || modalInfo.userId;
        modalInfo.creatorId = creatorId;
        modalInfo.participants = modalInfo.participants.map(p => {
            const pid = String(p.id || p.userId || p.UserId || p.user_id || '').trim();
            let role = p.role || p.Role || '';
            if (!role || !String(role).trim()) {
                role = (creatorId && pid.toLowerCase() === creatorId) ? 'владелец' : 'участник';
            }
            return {
                id: pid,
                userId: pid,
                fullName: p.fullName || p.FullName || p.name || p.Name || `${p.firstName || p.FirstName || ''} ${p.lastName || p.LastName || ''}`.trim(),
                name: p.name || p.Name || p.fullName,
                avatar: p.avatar || p.Avatar || p.avatarPath || p.AvatarPath,
                role
            };
        });

        if (String(currentChatId) === String(chatId)) {
            currentChatInfo = modalInfo;
        }

        window.__modalChatInfo = modalInfo;

        const item = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
        const avatarSrc = item?.querySelector('img')?.src
            || 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png';
        const title = item?.querySelector('h3')?.textContent || modalInfo.name || 'Чат';

        document.getElementById('modal-chat-avatar').src = avatarSrc;
        document.getElementById('modal-chat-name').value = title;
        document.getElementById('modal-chat-type').textContent =
            modalInfo.type === 'group' ? 'Групповой чат' : 'Личный чат';

        const list = document.getElementById('participants-list');
        list.innerHTML = '';
        modalInfo.participants.forEach(p => {
            const isYou = String(p.id || p.userId || '').trim() === String(me || '').trim();
            const userId = String(p.id || p.userId || '').trim();
            const status = userStatuses.get(userId);
            const isOnline = status?.isOnline === true;
            const div = document.createElement('div');
            div.className = 'flex items-center justify-between p-4 bg-gray-50 dark:bg-gray-700 rounded-xl';
            const pRole = p.role || p.Role || 'участник';
            div.dataset.userId = userId;
            div.dataset.role = pRole;
            const showRemove = modalInfo.type === 'group' && !isYou && canRemoveParticipant(userId, pRole, modalInfo);
            div.innerHTML = `
                <div class="flex items-center gap-4">
                    <div class="relative">
                        <img src="${p.avatar || 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png'}"
                                class="w-12 h-12 rounded-full object-cover">
                        ${modalInfo.type === 'group' ? `
                        <span class="chat-online-dot absolute bottom-0 right-0 w-4 h-4 bg-green-500 rounded-full border-2 border-white ${isOnline ? '' : 'hidden'}"></span>
                        ` : ''}
                    </div>
                    <div>
                        <p class="font-semibold">${p.fullName || p.name || 'Пользователь'}</p>
                        ${isYou ? '<p class="text-sm text-blue-600">Вы</p>' : ''}
                        ${!isYou ? roleBadgeHtml(pRole) : ''}
                    </div>
                </div>
                ${showRemove ? `
                <button type="button" class="text-red-600 hover:bg-red-100 dark:hover:bg-red-900/30 rounded-full p-2 transition"
                        onclick="removeParticipantFromChat('${userId}', this)">
                    <i data-feather="x" class="w-5 h-5"></i>
                </button>` : ''}
            `;
            list.appendChild(div);
        });
        document.getElementById('participants-count').textContent = `(${modalInfo.participants.length})`;

        const addBtn = document.getElementById('add-participant-btn');
        if (addBtn) {
            const isGroup = (modalInfo.type === 'group');
            addBtn.classList.toggle('hidden', !isGroup);
            addBtn.style.display = isGroup ? '' : 'none';
        }
        if (modalInfo.type !== 'group') {
            document.getElementById('chat-info-search-container')?.remove();
        }

        if (String(currentChatId) !== String(chatId)) {
            window.__prevChatInfoBackup = currentChatInfo;
            currentChatInfo = modalInfo;
        }

        if (typeof chatNameChanged !== 'undefined') chatNameChanged = false;
        if (typeof removedParticipantIds !== 'undefined') removedParticipantIds = [];
        if (typeof addedParticipantIds !== 'undefined') addedParticipantIds = [];
        if (typeof toggleSaveButton === 'function') toggleSaveButton();

        document.getElementById('chat-info-modal').classList.add('show');
        if (typeof feather !== 'undefined') feather.replace();
    } catch (err) {
        console.error(err);
        showToast(err.message || 'Не удалось открыть настройки чата', 'error');
    }
}

function closeChatInfoModal() {
    document.getElementById('chat-info-modal')?.classList.remove('show');
    if (modalChatId && String(modalChatId) !== String(currentChatId)) {
        if (window.__prevChatInfoBackup !== undefined) {
            currentChatInfo = window.__prevChatInfoBackup;
            window.__prevChatInfoBackup = undefined;
        }
    }
    modalChatId = null;
    window.__modalChatInfo = null;
}

function openChatInfo() {
    if (!currentChatId) {
        showToast('Сначала выберите чат', 'error');
        return;
    }

    const info = currentChatInfo;
    if (!info) return;

    document.getElementById('modal-chat-avatar').src = document.getElementById('chat-info-avatar').src;
    document.getElementById('modal-chat-name').value = document.getElementById('chat-title').textContent;
    document.getElementById('modal-chat-type').textContent = info.type === 'group' ? 'Групповой чат' : 'Личный чат';

    const list = document.getElementById('participants-list');
    list.innerHTML = '';

    info.participants.forEach(p => {
        const isYou = String(p.id || p.userId || "").trim() === String(me || "").trim();
        const userId = String(p.id || p.userId || "").trim();

        const status = userStatuses.get(userId);
        const isOnline = status?.isOnline === true;

        const div = document.createElement('div');
        div.className = 'flex items-center justify-between p-4 bg-gray-50 rounded-xl';
        const pRole2 = p.role || p.Role || 'участник';
        div.dataset.userId = userId;
        div.dataset.role = pRole2;
        const showRemove2 = info.type === 'group' && !isYou && canRemoveParticipant(userId, pRole2, info);

        div.innerHTML = `
            <div class="flex items-center gap-4">
                <div class="relative">
                    <img src="${p.avatar || 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png'}"
                            class="w-12 h-12 rounded-full object-cover">
                    ${info.type === 'group' ? `
                    <span class="chat-online-dot absolute bottom-0 right-0 w-4 h-4 bg-green-500 rounded-full border-2 border-white ${isOnline ? '' : 'hidden'}"></span>
                    ` : ''}
                </div>
                <div>
                    <p class="font-semibold">${p.fullName || p.name || 'Пользователь'}</p>
                    ${isYou ? '<p class="text-sm text-blue-600">Вы</p>' : ''}
                    ${!isYou ? roleBadgeHtml(pRole2) : ''}
                </div>
            </div>
            ${showRemove2 ? `
            <button type="button" class="text-red-600 hover:bg-red-100 dark:hover:bg-red-900/30 rounded-full p-2 transition"
                    onclick="removeParticipantFromChat('${userId}', this)">
                <i data-feather="x" class="w-5 h-5"></i>
            </button>` : ''}
        `;

        list.appendChild(div);
    });

    document.getElementById('participants-count').textContent = `(${info.participants.length})`;

    const addBtnOpen = document.getElementById('add-participant-btn');
    if (addBtnOpen) {
        const isGroup = (info.type === 'group');
        addBtnOpen.classList.toggle('hidden', !isGroup);
        addBtnOpen.style.display = isGroup ? '' : 'none';
    }
    if (info.type !== 'group') {
        document.getElementById('chat-info-search-container')?.remove();
    }

    chatNameChanged = false;
    removedParticipantIds = [];
    addedParticipantIds = [];
    toggleSaveButton();

    document.getElementById('chat-info-modal').classList.add('show');
    feather.replace();
}

const LONG_PRESS_MS = 480;
let __lpTimer = null;
let __lpTriggered = false;
let __lpStartX = 0;
let __lpStartY = 0;
const __LP_MOVE_TOLERANCE = 12;