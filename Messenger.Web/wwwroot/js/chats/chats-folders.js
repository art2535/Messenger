(function () {
    'use strict';

    const state = {
        folders: [],
        activeFolderId: '',
        loaded: false,
        allChats: [],
        editingId: null,
        selectedChatIds: new Set()
    };

    function apiBase() {
        return (typeof API_BASE !== 'undefined' && API_BASE) ||
            (typeof API_BASE_URL !== 'undefined' && API_BASE_URL) ||
            '/api/v1.0';
    }

    function authHeaders() {
        const t = (typeof token !== 'undefined' && token) ||
            (typeof getAuthToken === 'function' && getAuthToken()) ||
            localStorage.getItem('token') || '';
        const bare = String(t).replace(/^Bearer\s+/i, '');
        return {
            'Authorization': bare ? ('Bearer ' + bare) : '',
            'Accept': 'application/json',
            'Content-Type': 'application/json'
        };
    }

    async function api(path, options) {
        return fetch(apiBase().replace(/\/$/, '') + path, Object.assign({
            credentials: 'same-origin',
            headers: authHeaders()
        }, options || {}));
    }

    async function loadFolders() {
        try {
            const res = await api('/chat-folders');
            if (!res.ok) return;
            const json = await res.json();
            state.folders = json.data || json.Data || [];
            state.loaded = true;
            renderTabs();
            applyFolderFilter();
            if (!document.getElementById('chat-folders-modal')?.classList.contains('hidden')) {
                renderModalList();
            }
        } catch (e) {
            console.warn('[folders] load failed', e);
        }
    }

    function renderTabs() {
        const bar = document.getElementById('chat-folder-tabs');
        if (!bar) return;
        const manage = document.getElementById('chat-folder-manage-btn');
        bar.querySelectorAll('.chat-folder-tab[data-folder-id]').forEach(el => {
            if (el.id !== 'chat-folder-manage-btn') el.remove();
        });
        let allBtn = bar.querySelector('.chat-folder-tab[data-folder-id=""]');
        if (!allBtn) {
            allBtn = document.createElement('button');
            allBtn.type = 'button';
            allBtn.className = 'chat-folder-tab';
            allBtn.dataset.folderId = '';
            allBtn.textContent = 'Все';
            bar.insertBefore(allBtn, manage);
        }
        allBtn.classList.toggle('active', !state.activeFolderId);

        state.folders.forEach(f => {
            const id = f.folderId || f.FolderId;
            const name = f.name || f.Name || 'Папка';
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'chat-folder-tab' + (String(state.activeFolderId) === String(id) ? ' active' : '');
            btn.dataset.folderId = id;
            btn.textContent = name;
            btn.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                e.stopPropagation();
                openManageModal();
                const folder = state.folders.find(x => String(x.folderId || x.FolderId) === String(id));
                if (folder) openEditPanel(folder);
            });
            bar.insertBefore(btn, manage);
        });
        if (typeof feather !== 'undefined') feather.replace();
    }

    function applyFolderFilter() {
        const container = document.getElementById('chats-container');
        if (!container) return;
        const items = container.querySelectorAll('.chat-item, .chat-list-item, [data-chat-id]');
        if (!state.activeFolderId) {
            items.forEach(el => { el.style.display = ''; });
            return;
        }
        const folder = state.folders.find(f => String(f.folderId || f.FolderId) === String(state.activeFolderId));
        const ids = new Set((folder?.chatIds || folder?.ChatIds || []).map(String));
        items.forEach(el => {
            const cid = el.dataset.chatId || el.getAttribute('data-chat-id');
            el.style.display = ids.has(String(cid)) ? '' : 'none';
        });
    }

    function onTabsClick(e) {
        const tab = e.target.closest('.chat-folder-tab');
        if (!tab) return;
        if (tab.id === 'chat-folder-manage-btn' || tab.classList.contains('chat-folder-manage')) {
            openManageModal();
            return;
        }
        state.activeFolderId = tab.dataset.folderId || '';
        document.querySelectorAll('.chat-folder-tab').forEach(t => {
            t.classList.toggle('active', (t.dataset.folderId || '') === state.activeFolderId && t.id !== 'chat-folder-manage-btn');
        });
        applyFolderFilter();
    }

    /* ——— модалка управления ——— */

    function openManageModal() {
        const modal = document.getElementById('chat-folders-modal');
        if (!modal) return;
        hideEditPanel();
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        renderModalList();
        loadFolders();
        ensureChatsLoaded();
        if (typeof feather !== 'undefined') feather.replace();
    }

    function closeManageModal() {
        const modal = document.getElementById('chat-folders-modal');
        if (!modal) return;
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        hideEditPanel();
    }

    function renderModalList() {
        const list = document.getElementById('chat-folders-modal-list');
        const empty = document.getElementById('chat-folders-modal-empty');
        if (!list) return;
        list.innerHTML = '';
        if (!state.folders.length) {
            empty?.classList.remove('hidden');
            return;
        }
        empty?.classList.add('hidden');
        state.folders.forEach(f => {
            const id = f.folderId || f.FolderId;
            const name = f.name || f.Name;
            const count = (f.chatIds || f.ChatIds || []).length;
            const row = document.createElement('div');
            row.className = 'flex items-center justify-between px-4 py-3 gap-2';
            row.innerHTML =
                '<div class="min-w-0"><div class="font-medium text-gray-900 dark:text-white truncate"></div>' +
                '<div class="text-xs text-gray-500 dark:text-gray-400"></div></div>' +
                '<div class="flex items-center gap-1 flex-shrink-0">' +
                '<button type="button" class="cf-edit text-sm text-blue-600 dark:text-blue-400 px-2 py-1">Изменить</button>' +
                '<button type="button" class="cf-del text-sm text-red-600 dark:text-red-400 px-2 py-1">Удалить</button></div>';
            row.querySelector('.font-medium').textContent = name;
            row.querySelector('.text-xs').textContent = count + ' чат(ов)';
            row.querySelector('.cf-edit').onclick = () => openEditPanel(f);
            row.querySelector('.cf-del').onclick = async () => {
                const ok = typeof showConfirm === 'function'
                    ? await showConfirm('Удалить папку «' + name + '»? Чаты не удалятся.', { title: 'Удаление папки', okText: 'Удалить', danger: true })
                    : window.confirm('Удалить папку «' + name + '»?');
                if (!ok) return;
                const res = await api('/chat-folders/' + encodeURIComponent(id), { method: 'DELETE' });
                if (res.ok) {
                    if (String(state.activeFolderId) === String(id)) state.activeFolderId = '';
                    await loadFolders();
                } else if (typeof showToast === 'function') showToast('Не удалось удалить', 'error');
            };
            list.appendChild(row);
        });
    }

    function chatTitle(c) {
        return c.name || c.Name || c.title || c.Title ||
            c.partnerName || c.PartnerName || 'Чат';
    }

    async function ensureChatsLoaded() {
        if (state.allChats.length) return;
        try {
            const res = await api('/chats');
            if (!res.ok) return;
            const json = await res.json();
            let list = json.data || json.Data || json || [];
            if (!Array.isArray(list)) list = [];
            state.allChats = list;
        } catch (_) { /* */ }
    }

    function hideEditPanel() {
        document.getElementById('chat-folders-edit-panel')?.classList.add('hidden');
        state.editingId = null;
        state.selectedChatIds = new Set();
        const title = document.getElementById('chat-folders-modal-title');
        if (title) title.textContent = 'Папки чатов';
    }

    async function openEditPanel(folder) {
        await ensureChatsLoaded();
        state.editingId = folder ? (folder.folderId || folder.FolderId) : null;
        state.selectedChatIds = new Set((folder ? (folder.chatIds || folder.ChatIds || []) : []).map(String));
        const title = document.getElementById('chat-folders-modal-title');
        if (title) title.textContent = state.editingId ? 'Изменить папку' : 'Новая папка';
        const nameInput = document.getElementById('chat-folders-name-input');
        if (nameInput) nameInput.value = folder ? (folder.name || folder.Name || '') : '';
        const search = document.getElementById('chat-folders-chat-search');
        if (search) search.value = '';
        renderPicker('');
        document.getElementById('chat-folders-edit-panel')?.classList.remove('hidden');
    }

    function renderPicker(filter) {
        const box = document.getElementById('chat-folders-chats-picker');
        if (!box) return;
        const q = (filter || '').toLowerCase().trim();
        box.innerHTML = '';
        const list = state.allChats.filter(c => !q || chatTitle(c).toLowerCase().includes(q));
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
            cb.checked = state.selectedChatIds.has(id);
            cb.onchange = () => {
                if (cb.checked) state.selectedChatIds.add(id);
                else state.selectedChatIds.delete(id);
            };
            const span = document.createElement('span');
            span.className = 'text-sm text-gray-800 dark:text-gray-100 truncate';
            span.textContent = chatTitle(c);
            label.appendChild(cb);
            label.appendChild(span);
            box.appendChild(label);
        });
    }

    async function saveFolderFromModal() {
        const name = (document.getElementById('chat-folders-name-input')?.value || '').trim();
        if (!name) {
            if (typeof showToast === 'function') showToast('Введите название папки', 'error');
            else alert('Введите название папки');
            return;
        }
        const chatIds = Array.from(state.selectedChatIds);
        try {
            let res;
            if (state.editingId) {
                res = await api('/chat-folders/' + encodeURIComponent(state.editingId), {
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
            hideEditPanel();
            await loadFolders();
            if (typeof showToast === 'function') showToast('Папка сохранена', 'success');
        } catch (e) {
            if (typeof showToast === 'function') showToast(e.message || 'Не удалось сохранить', 'error');
            else alert(e.message || 'Не удалось сохранить');
        }
    }

    /* ——— ПКМ submenu ——— */

    function hideFolderSubmenu() {
        const sm = document.getElementById('chat-folder-submenu');
        if (sm) {
            sm.classList.add('hidden');
            sm.innerHTML = '';
        }
    }

    async function showAddToFolderMenu(x, y, chatId) {
        await loadFolders();
        const sm = document.getElementById('chat-folder-submenu');
        if (!sm) return;
        sm.innerHTML = '';
        if (!state.folders.length) {
            sm.innerHTML = '<div class="muted">Нет папок</div>' +
                '<button type="button" data-action="open-manage">Создать папку…</button>';
        } else {
            state.folders.forEach(f => {
                const id = f.folderId || f.FolderId;
                const name = f.name || f.Name;
                const ids = (f.chatIds || f.ChatIds || []).map(String);
                const inFolder = ids.includes(String(chatId));
                const btn = document.createElement('button');
                btn.type = 'button';
                btn.dataset.folderId = id;
                btn.dataset.chatId = chatId;
                btn.dataset.action = inFolder ? 'remove' : 'add';
                btn.textContent = (inFolder ? '✓ ' : '') + name;
                sm.appendChild(btn);
            });
            const createBtn = document.createElement('button');
            createBtn.type = 'button';
            createBtn.dataset.action = 'open-manage';
            createBtn.textContent = 'Создать папку…';
            sm.appendChild(createBtn);
        }
        sm.classList.remove('hidden');
        const pad = 8;
        let left = x, top = y;
        const w = sm.offsetWidth || 180, h = sm.offsetHeight || 120;
        if (left + w > window.innerWidth - pad) left = window.innerWidth - w - pad;
        if (top + h > window.innerHeight - pad) top = window.innerHeight - h - pad;
        sm.style.left = left + 'px';
        sm.style.top = top + 'px';
    }

    async function showRemoveFromFolderMenu(x, y, chatId) {
        await loadFolders();
        const sm = document.getElementById('chat-folder-submenu');
        if (!sm) return;
        const containing = state.folders.filter(f => {
            const ids = (f.chatIds || f.ChatIds || []).map(String);
            return ids.includes(String(chatId));
        });
        sm.innerHTML = '';
        if (!containing.length) {
            sm.innerHTML = '<div class="muted">Чат не состоит ни в одной папке</div>';
        } else {
            containing.forEach(f => {
                const id = f.folderId || f.FolderId;
                const btn = document.createElement('button');
                btn.type = 'button';
                btn.dataset.folderId = id;
                btn.dataset.chatId = chatId;
                btn.dataset.action = 'remove';
                btn.textContent = 'Убрать из «' + (f.name || f.Name) + '»';
                sm.appendChild(btn);
            });
        }
        sm.classList.remove('hidden');
        sm.style.left = x + 'px';
        sm.style.top = y + 'px';
    }

    async function onSubmenuClick(e) {
        const btn = e.target.closest('button');
        if (!btn) return;
        if (btn.dataset.action === 'open-manage') {
            hideFolderSubmenu();
            openManageModal();
            openEditPanel(null);
            return;
        }
        const folderId = btn.dataset.folderId;
        const chatId = btn.dataset.chatId;
        const action = btn.dataset.action;
        if (!folderId || !chatId) return;
        try {
            if (action === 'add') {
                const res = await api('/chat-folders/' + encodeURIComponent(folderId) + '/chats', {
                    method: 'POST',
                    body: JSON.stringify({ chatId })
                });
                if (!res.ok) throw new Error('Не удалось добавить');
                if (typeof showToast === 'function') showToast('Чат добавлен в папку', 'success');
            } else if (action === 'remove') {
                const res = await api('/chat-folders/' + encodeURIComponent(folderId) + '/chats/' + encodeURIComponent(chatId), {
                    method: 'DELETE'
                });
                if (!res.ok) throw new Error('Не удалось убрать');
                if (typeof showToast === 'function') showToast('Чат убран из папки', 'success');
            }
            await loadFolders();
        } catch (err) {
            if (typeof showToast === 'function') showToast(err.message || 'Ошибка', 'error');
        }
        hideFolderSubmenu();
        document.getElementById('chat-context-menu')?.classList.remove('show');
    }

    function wireContextMenu() {
        document.getElementById('chat-ctx-folder-add-btn')?.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            const chatId = typeof contextMenuChatId !== 'undefined' ? contextMenuChatId : null;
            if (!chatId) return;
            const menu = document.getElementById('chat-context-menu');
            const r = menu ? menu.getBoundingClientRect() : { right: e.clientX, top: e.clientY };
            showAddToFolderMenu(r.right + 4, r.top, chatId);
        });
        document.getElementById('chat-ctx-folder-remove-btn')?.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            const chatId = typeof contextMenuChatId !== 'undefined' ? contextMenuChatId : null;
            if (!chatId) return;
            const menu = document.getElementById('chat-context-menu');
            const r = menu ? menu.getBoundingClientRect() : { right: e.clientX, top: e.clientY };
            showRemoveFromFolderMenu(r.right + 4, r.top, chatId);
        });
        document.getElementById('chat-folder-submenu')?.addEventListener('click', onSubmenuClick);
        document.addEventListener('click', (e) => {
            if (!e.target.closest('#chat-folder-submenu') &&
                !e.target.closest('#chat-ctx-folder-add-btn') &&
                !e.target.closest('#chat-ctx-folder-remove-btn')) {
                hideFolderSubmenu();
            }
        });
    }

    function wireModal() {
        document.getElementById('chat-folders-modal-close')?.addEventListener('click', closeManageModal);
        document.getElementById('chat-folders-modal')?.addEventListener('click', (e) => {
            if (e.target.id === 'chat-folders-modal') closeManageModal();
        });
        document.getElementById('chat-folders-create-btn')?.addEventListener('click', () => openEditPanel(null));
        document.getElementById('chat-folders-edit-cancel')?.addEventListener('click', hideEditPanel);
        document.getElementById('chat-folders-edit-save')?.addEventListener('click', saveFolderFromModal);
        document.getElementById('chat-folders-chat-search')?.addEventListener('input', (e) => renderPicker(e.target.value));
    }

    function init() {
        document.getElementById('chat-folder-tabs')?.addEventListener('click', onTabsClick);
        document.getElementById('chat-folder-tabs')?.addEventListener('contextmenu', (e) => {
            const tab = e.target.closest('.chat-folder-tab');
            if (!tab || tab.id === 'chat-folder-manage-btn') return;
            e.preventDefault();
            if (!tab.dataset.folderId) {
                openManageModal();
                return;
            }
            openManageModal();
            const folder = state.folders.find(x => String(x.folderId || x.FolderId) === String(tab.dataset.folderId));
            if (folder) openEditPanel(folder);
        });
        wireContextMenu();
        wireModal();
        loadFolders();
        const container = document.getElementById('chats-container');
        if (container && typeof MutationObserver !== 'undefined') {
            const obs = new MutationObserver(() => applyFolderFilter());
            obs.observe(container, { childList: true });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.ChatFolders = {
        reload: loadFolders,
        applyFilter: applyFolderFilter,
        openManage: openManageModal,
        getState: () => state
    };
})();
