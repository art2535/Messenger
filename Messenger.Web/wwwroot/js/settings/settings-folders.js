(function () {
    'use strict';

    let folders = [];
    let allChats = [];
    let editingId = null;
    let selectedChatIds = new Set();

    function apiBase() {
        return (window.API_BASE_URL || '/api/v1.0').replace(/\/$/, '');
    }
    function headers() {
        let t = '';
        if (typeof getAuthToken === 'function') t = getAuthToken() || '';
        else t = localStorage.getItem('token') || '';
        t = String(t).replace(/^Bearer\s+/i, '');
        return {
            'Authorization': t ? 'Bearer ' + t : '',
            'Accept': 'application/json',
            'Content-Type': 'application/json'
        };
    }
    async function api(path, opts) {
        return fetch(apiBase() + path, Object.assign({ credentials: 'same-origin', headers: headers() }, opts || {}));
    }

    async function loadFolders() {
        const res = await api('/chat-folders');
        if (!res.ok) return;
        const json = await res.json();
        folders = json.data || json.Data || [];
        renderList();
    }

    async function loadChats() {
        try {
            const res = await api('/chats');
            if (!res.ok) return;
            const json = await res.json();
            allChats = json.data || json.Data || json || [];
            if (!Array.isArray(allChats)) allChats = [];
        } catch (_) {
            allChats = [];
        }
    }

    function chatTitle(c) {
        return c.name || c.Name || c.title || c.Title ||
            c.partnerName || c.PartnerName ||
            (c.participants && c.participants[0] && (c.participants[0].userName || c.participants[0].name)) ||
            'Чат';
    }

    function renderList() {
        const list = document.getElementById('folders-list');
        const empty = document.getElementById('folders-empty');
        if (!list) return;
        list.innerHTML = '';
        if (!folders.length) {
            empty?.classList.remove('hidden');
            return;
        }
        empty?.classList.add('hidden');
        folders.forEach(f => {
            const id = f.folderId || f.FolderId;
            const name = f.name || f.Name;
            const count = (f.chatIds || f.ChatIds || []).length;
            const row = document.createElement('div');
            row.className = 'settings-row';
            row.innerHTML =
                '<div class="settings-row-text">' +
                '<span class="settings-row-label"></span>' +
                '<span class="settings-row-hint"></span></div>' +
                '<div class="flex items-center gap-2">' +
                '<button type="button" class="folder-edit-btn text-sm text-blue-600 dark:text-blue-400 px-2 py-1">Изменить</button>' +
                '<button type="button" class="folder-del-btn text-sm text-red-600 dark:text-red-400 px-2 py-1">Удалить</button>' +
                '</div>';
            row.querySelector('.settings-row-label').textContent = name;
            row.querySelector('.settings-row-hint').textContent = count + ' чат(ов)';
            row.querySelector('.folder-edit-btn').onclick = () => openModal(f);
            row.querySelector('.folder-del-btn').onclick = () => deleteFolder(id, name);
            list.appendChild(row);
        });
    }

    function openModal(folder) {
        editingId = folder ? (folder.folderId || folder.FolderId) : null;
        selectedChatIds = new Set((folder ? (folder.chatIds || folder.ChatIds || []) : []).map(String));
        document.getElementById('folder-modal-title').textContent = editingId ? 'Изменить папку' : 'Новая папка';
        document.getElementById('folder-name-input').value = folder ? (folder.name || folder.Name || '') : '';
        document.getElementById('folder-chat-search').value = '';
        renderPicker('');
        const modal = document.getElementById('folder-edit-modal');
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        if (typeof feather !== 'undefined') feather.replace();
    }

    function closeModal() {
        const modal = document.getElementById('folder-edit-modal');
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        editingId = null;
    }

    function renderPicker(filter) {
        const box = document.getElementById('folder-chats-picker');
        if (!box) return;
        const q = (filter || '').toLowerCase().trim();
        box.innerHTML = '';
        const list = allChats.filter(c => {
            if (!q) return true;
            return chatTitle(c).toLowerCase().includes(q);
        });
        if (!list.length) {
            box.innerHTML = '<p class="text-sm text-gray-500 p-2">Нет чатов</p>';
            return;
        }
        list.forEach(c => {
            const id = String(c.chatId || c.ChatId || c.id || c.Id);
            const label = document.createElement('label');
            label.className = 'flex items-center gap-2 px-2 py-1.5 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700 cursor-pointer';
            const cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.checked = selectedChatIds.has(id);
            cb.onchange = () => {
                if (cb.checked) selectedChatIds.add(id);
                else selectedChatIds.delete(id);
            };
            const span = document.createElement('span');
            span.className = 'text-sm text-gray-800 dark:text-gray-100 truncate';
            span.textContent = chatTitle(c);
            label.appendChild(cb);
            label.appendChild(span);
            box.appendChild(label);
        });
    }

    async function saveFolder() {
        const name = (document.getElementById('folder-name-input').value || '').trim();
        if (!name) {
            alert('Введите название папки');
            return;
        }
        const chatIds = Array.from(selectedChatIds);
        try {
            let res;
            if (editingId) {
                res = await api('/chat-folders/' + encodeURIComponent(editingId), {
                    method: 'PUT',
                    body: JSON.stringify({ name, chatIds })
                });
            } else {
                res = await api('/chat-folders', {
                    method: 'POST',
                    body: JSON.stringify({ name, chatIds })
                });
            }
            if (!res.ok) {
                const err = await res.json().catch(() => ({}));
                throw new Error(err.error || err.Error || 'Ошибка сохранения');
            }
            closeModal();
            await loadFolders();
        } catch (e) {
            alert(e.message || 'Не удалось сохранить');
        }
    }

    async function deleteFolder(id, name) {
        const ok = typeof showConfirm === 'function'
            ? await showConfirm('Удалить папку «' + name + '»? Чаты не удалятся.', { title: 'Удаление папки', okText: 'Удалить', danger: true })
            : window.confirm('Удалить папку «' + name + '»? Чаты не удалятся.');
        if (!ok) return;
        const res = await api('/chat-folders/' + encodeURIComponent(id), { method: 'DELETE' });
        if (!res.ok) {
            alert('Не удалось удалить');
            return;
        }
        await loadFolders();
    }

    function init() {
        document.getElementById('folder-create-btn')?.addEventListener('click', () => openModal(null));
        document.getElementById('folder-modal-close')?.addEventListener('click', closeModal);
        document.getElementById('folder-modal-cancel')?.addEventListener('click', closeModal);
        document.getElementById('folder-modal-save')?.addEventListener('click', saveFolder);
        document.getElementById('folder-chat-search')?.addEventListener('input', (e) => renderPicker(e.target.value));
        document.getElementById('folder-edit-modal')?.addEventListener('click', (e) => {
            if (e.target.id === 'folder-edit-modal') closeModal();
        });
        Promise.all([loadChats(), loadFolders()]);
    }

    window.SettingsFolders = { init: init };
})();
