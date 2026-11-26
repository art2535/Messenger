(function () {
    'use strict';

    const state = {
        folders: [],
        activeFolderId: '',
        loaded: false,
        allChats: [],
        editingId: null,
        selectedChatIds: new Set(),
        unreadOnly: false
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
        allBtn.classList.toggle('unread-filter', state.unreadOnly && !state.activeFolderId);

        state.folders.forEach(f => {
            const id = f.folderId || f.FolderId;
            const name = f.name || f.Name || 'Папка';
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'chat-folder-tab' + (String(state.activeFolderId) === String(id) ? ' active' : '');
            if (state.unreadOnly && String(state.activeFolderId) === String(id)) {
                btn.classList.add('unread-filter');
            }
            btn.dataset.folderId = id;
            btn.textContent = name;
            bar.insertBefore(btn, manage);
        });
        if (typeof feather !== 'undefined') feather.replace();
    }

    function chatHasUnread(el) {
        if (!el) return false;
        const cid = el.dataset.chatId || el.getAttribute('data-chat-id');
        if (typeof unreadCounts !== 'undefined' && unreadCounts instanceof Map) {
            const n = unreadCounts.get(String(cid)) || unreadCounts.get(cid);
            if (n > 0) return true;
        }
        const badge = el.querySelector('.unread-badge');
        if (badge && !badge.classList.contains('hidden')) {
            const t = (badge.textContent || '').trim();
            if (t && t !== '0') return true;
        }
        return false;
    }

    function applyFolderFilter() {
        const container = document.getElementById('chats-container');
        if (!container) return;
        const items = container.querySelectorAll('.chat-item, .chat-list-item, [data-chat-id]');
        let allowedIds = null;
        if (state.activeFolderId) {
            const folder = state.folders.find(f => String(f.folderId || f.FolderId) === String(state.activeFolderId));
            allowedIds = new Set((folder?.chatIds || folder?.ChatIds || []).map(String));
        }
        items.forEach(el => {
            const cid = String(el.dataset.chatId || el.getAttribute('data-chat-id') || '');
            let show = !allowedIds || allowedIds.has(cid);
            if (show && state.unreadOnly) show = chatHasUnread(el);
            el.style.display = show ? '' : 'none';
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
            t.classList.toggle('unread-filter', state.unreadOnly && (t.dataset.folderId || '') === state.activeFolderId);
        });
        applyFolderFilter();
    }

    function ensureTabMenu() {
        let menu = document.getElementById('chat-folder-tab-menu');
        if (menu) return menu;
        menu = document.createElement('div');
        menu.id = 'chat-folder-tab-menu';
        menu.className = 'chat-folder-tab-menu hidden';
        document.body.appendChild(menu);
        menu.addEventListener('click', onTabMenuClick);
        return menu;
    }

    function hideTabMenu() {
        const menu = document.getElementById('chat-folder-tab-menu');
        if (menu) {
            menu.classList.add('hidden');
            menu.innerHTML = '';
            delete menu.dataset.folderId;
        }
    }

    function positionMenu(menu, x, y) {
        menu.classList.remove('hidden');
        const pad = 8;
        const w = menu.offsetWidth || 220;
        const h = menu.offsetHeight || 140;
        let left = x;
        let top = y;
        if (left + w > window.innerWidth - pad) left = window.innerWidth - w - pad;
        if (top + h > window.innerHeight - pad) top = window.innerHeight - h - pad;
        if (left < pad) left = pad;
        if (top < pad) top = pad;
        menu.style.left = left + 'px';
        menu.style.top = top + 'px';
    }

    function iconEdit() {
        return '<svg class="cfm-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>';
    }
    function iconUnread() {
        return '<svg class="cfm-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>';
    }
    function iconReadAll() {
        return '<svg class="cfm-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6 7 17l-5-5"/><path d="m22 10-7.5 7.5L13 16"/></svg>';
    }
    function iconTrash() {
        return '<svg class="cfm-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/></svg>';
    }

    function showTabContextMenu(x, y, folderId) {
        hideTabMenu();
        hideFolderSubmenu();
        const menu = ensureTabMenu();
        menu.dataset.folderId = folderId || '';
        const isAll = !folderId;
        const unreadLabel = state.unreadOnly
            ? 'Показать все чаты'
            : 'Показать только непрочитанные';

        let html = '';
        if (isAll) {
            html +=
                '<button type="button" data-action="configure-all">' + iconEdit() + '<span>Настроить папки</span></button>' +
                '<button type="button" data-action="toggle-unread">' + iconUnread() + '<span>' + unreadLabel + '</span></button>' +
                '<button type="button" data-action="read-all">' + iconReadAll() + '<span>Прочитать все</span></button>';
        } else {
            html +=
                '<button type="button" data-action="configure-one">' + iconEdit() + '<span>Настроить папку</span></button>' +
                '<button type="button" data-action="toggle-unread">' + iconUnread() + '<span>' + unreadLabel + '</span></button>' +
                '<button type="button" data-action="read-all">' + iconReadAll() + '<span>Прочитать все</span></button>' +
                '<button type="button" data-action="delete" class="danger">' + iconTrash() + '<span>Удалить</span></button>';
        }
        menu.innerHTML = html;
        positionMenu(menu, x, y);
    }

    async function onTabMenuClick(e) {
        const btn = e.target.closest('button[data-action]');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();
        const action = btn.dataset.action;
        const folderId = document.getElementById('chat-folder-tab-menu')?.dataset.folderId || '';
        hideTabMenu();

        if (action === 'configure-all') {
            openManageModal();
            return;
        }
        if (action === 'configure-one') {
            const folder = state.folders.find(f => String(f.folderId || f.FolderId) === String(folderId));
            openManageModal();
            if (folder) openEditPanel(folder);
            return;
        }
        if (action === 'toggle-unread') {
            state.unreadOnly = !state.unreadOnly;
            renderTabs();
            applyFolderFilter();
            if (typeof showToast === 'function') {
                showToast(state.unreadOnly ? 'Только непрочитанные' : 'Показаны все чаты', 'info');
            }
            return;
        }
        if (action === 'read-all') {
            await markVisibleChatsRead(folderId || null);
            return;
        }
        if (action === 'delete' && folderId) {
            const folder = state.folders.find(f => String(f.folderId || f.FolderId) === String(folderId));
            const name = folder ? (folder.name || folder.Name) : 'папку';
            const ok = typeof showConfirm === 'function'
                ? await showConfirm('Удалить папку «' + name + '»? Чаты не удалятся.', {
                    title: 'Удаление папки',
                    okText: 'Удалить',
                    danger: true
                })
                : window.confirm('Удалить папку «' + name + '»?');
            if (!ok) return;
            const res = await api('/chat-folders/' + encodeURIComponent(folderId), { method: 'DELETE' });
            if (res.ok) {
                if (String(state.activeFolderId) === String(folderId)) state.activeFolderId = '';
                await loadFolders();
                if (typeof showToast === 'function') showToast('Папка удалена', 'success');
            } else if (typeof showToast === 'function') {
                showToast('Не удалось удалить', 'error');
            }
        }
    }

    async function markVisibleChatsRead(folderId) {
        const container = document.getElementById('chats-container');
        if (!container) return;
        let allowed = null;
        if (folderId) {
            const folder = state.folders.find(f => String(f.folderId || f.FolderId) === String(folderId));
            allowed = new Set((folder?.chatIds || folder?.ChatIds || []).map(String));
        }
        const items = Array.from(container.querySelectorAll('.chat-item, .chat-list-item, [data-chat-id]'))
            .filter(el => {
                const cid = String(el.dataset.chatId || el.getAttribute('data-chat-id') || '');
                if (allowed && !allowed.has(cid)) return false;
                if (!folderId && el.style.display === 'none') return false;
                return chatHasUnread(el);
            });
        let ok = 0;
        for (const el of items) {
            const cid = el.dataset.chatId || el.getAttribute('data-chat-id');
            if (!cid) continue;
            try {
                const res = await api('/messages/' + encodeURIComponent(cid) + '/read', { method: 'POST', body: '{}' });
                if (res.ok) {
                    ok++;
                    if (typeof unreadCounts !== 'undefined' && unreadCounts instanceof Map) {
                        unreadCounts.set(String(cid), 0);
                    }
                    if (typeof updateUnreadBadge === 'function') updateUnreadBadge(cid, 0);
                    else {
                        const badge = el.querySelector('.unread-badge');
                        if (badge) {
                            badge.classList.add('hidden');
                            badge.textContent = '';
                        }
                    }
                }
            } catch (_) { /* */ }
        }
        applyFolderFilter();
        if (typeof showToast === 'function') {
            showToast(ok ? ('Прочитано чатов: ' + ok) : 'Нет непрочитанных', ok ? 'success' : 'info');
        }
    }

    function onTabsContextMenu(e) {
        const tab = e.target.closest('.chat-folder-tab');
        if (!tab || tab.id === 'chat-folder-manage-btn') return;
        e.preventDefault();
        e.stopPropagation();
        const folderId = tab.dataset.folderId || '';
        showTabContextMenu(e.clientX, e.clientY, folderId);
    }

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
            if (!e.target.closest('#chat-folder-tab-menu') && !e.target.closest('.chat-folder-tab')) {
                hideTabMenu();
            }
        });
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                hideTabMenu();
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
        document.getElementById('chat-folder-tabs')?.addEventListener('contextmenu', onTabsContextMenu);
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
