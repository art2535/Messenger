/**
 * Синхронизация настроек аккаунта с API + применение на всех страницах.
 * API: GET/PUT /api/v1/account-settings
 * Сессии: GET /api/v1/logins, DELETE /api/v1/logins/{id}
 */
(function (global) {
    const CACHE_KEY = 'guap.accountSettings';

    function apiBase() {
        return (global.API_BASE_URL || global.API_URL || '').replace(/\/$/, '');
    }

    function authHeader() {
        if (typeof getAuthToken === 'function') return getAuthToken();
        const t = (document.querySelector('meta[name="access-token"]')?.content || localStorage.getItem('token') || '').trim();
        return t ? (t.startsWith('Bearer ') ? t : 'Bearer ' + t) : '';
    }

    async function apiFetch(path, options = {}) {
        const url = apiBase() + path;
        const headers = Object.assign({ 'Accept': 'application/json' }, options.headers || {});
        const auth = authHeader();
        if (auth) headers['Authorization'] = auth;
        if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
        const res = await fetch(url, Object.assign({}, options, { headers, credentials: 'same-origin' }));
        return res;
    }

    function readCache() {
        try {
            const raw = localStorage.getItem(CACHE_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch { return null; }
    }

    function writeCache(data) {
        try { localStorage.setItem(CACHE_KEY, JSON.stringify(data)); } catch { /* */ }
    }

    function defaults() {
        return {
            pushEnabled: true,
            notifyMessages: true,
            notifyGroupChats: true,
            notifyMentions: true,
            notifyReactions: true,
            notifySystem: true,
            notifySound: true,
            suppressOpenChat: true,
            notifyPreview: true,
            theme: 'system',
            largeFont: false,
            compactList: false,
            showOnline: true,
            showTyping: true,
            readReceipts: true,
            idleLogout: true
        };
    }

    function normalize(dto) {
        const d = defaults();
        if (!dto) return d;
        return {
            pushEnabled: dto.pushEnabled ?? dto.PushEnabled ?? d.pushEnabled,
            notifyMessages: dto.notifyMessages ?? dto.NotifyMessages ?? d.notifyMessages,
            notifyGroupChats: dto.notifyGroupChats ?? dto.NotifyGroupChats ?? d.notifyGroupChats,
            notifyMentions: dto.notifyMentions ?? dto.NotifyMentions ?? d.notifyMentions,
            notifyReactions: dto.notifyReactions ?? dto.NotifyReactions ?? d.notifyReactions,
            notifySystem: dto.notifySystem ?? dto.NotifySystem ?? d.notifySystem,
            notifySound: dto.notifySound ?? dto.NotifySound ?? d.notifySound,
            suppressOpenChat: dto.suppressOpenChat ?? dto.SuppressOpenChat ?? d.suppressOpenChat,
            notifyPreview: dto.notifyPreview ?? dto.NotifyPreview ?? d.notifyPreview,
            theme: (dto.theme ?? dto.Theme ?? d.theme).toLowerCase(),
            largeFont: dto.largeFont ?? dto.LargeFont ?? d.largeFont,
            compactList: dto.compactList ?? dto.CompactList ?? d.compactList,
            showOnline: dto.showOnline ?? dto.ShowOnline ?? d.showOnline,
            showTyping: dto.showTyping ?? dto.ShowTyping ?? d.showTyping,
            readReceipts: dto.readReceipts ?? dto.ReadReceipts ?? d.readReceipts,
            idleLogout: dto.idleLogout ?? dto.IdleLogout ?? d.idleLogout
        };
    }

    function applyTheme(theme) {
        const mode = theme || 'system';
        let dark = false;
        if (mode === 'dark') dark = true;
        else if (mode === 'light') dark = false;
        else dark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        document.documentElement.classList.toggle('dark', dark);
        try {
            if (mode === 'system') localStorage.removeItem('theme');
            else localStorage.setItem('theme', dark ? 'dark' : 'light');
        } catch { /* */ }
    }

    function applyToDocument(settings) {
        const s = settings || readCache() || defaults();
        applyTheme(s.theme);
        document.documentElement.classList.toggle('settings-large-font', !!s.largeFont);
        document.documentElement.classList.toggle('settings-compact-list', !!s.compactList);
        document.documentElement.dataset.showOnline = s.showOnline ? '1' : '0';
        document.documentElement.dataset.showTyping = s.showTyping ? '1' : '0';
        document.documentElement.dataset.readReceipts = s.readReceipts ? '1' : '0';
        document.documentElement.dataset.notifySound = s.notifySound ? '1' : '0';
        document.documentElement.dataset.suppressOpenChat = s.suppressOpenChat ? '1' : '0';
        document.documentElement.dataset.notifyPreview = s.notifyPreview ? '1' : '0';
        global.__accountSettings = s;
    }

    async function loadSettings() {
        const cached = readCache();
        if (cached) applyToDocument(cached);
        try {
            const res = await apiFetch('/api/v1/account-settings');
            if (!res.ok) return cached || defaults();
            const json = await res.json();
            const data = normalize(json.data || json.Data || json);
            writeCache(data);
            applyToDocument(data);
            return data;
        } catch {
            return cached || defaults();
        }
    }

    async function saveSettings(partial) {
        const current = Object.assign({}, readCache() || defaults(), partial || {});
        writeCache(current);
        applyToDocument(current);
        try {
            const body = {
                pushEnabled: current.pushEnabled,
                notifyMessages: current.notifyMessages,
                notifyGroupChats: current.notifyGroupChats,
                notifyMentions: current.notifyMentions,
                notifyReactions: current.notifyReactions,
                notifySystem: current.notifySystem,
                notifySound: current.notifySound,
                suppressOpenChat: current.suppressOpenChat,
                notifyPreview: current.notifyPreview,
                theme: current.theme,
                largeFont: current.largeFont,
                compactList: current.compactList,
                showOnline: current.showOnline,
                showTyping: current.showTyping,
                readReceipts: current.readReceipts,
                idleLogout: current.idleLogout
            };
            const res = await apiFetch('/api/v1/account-settings', {
                method: 'PUT',
                body: JSON.stringify(body)
            });
            if (res.ok) {
                const json = await res.json();
                const data = normalize(json.data || json.Data || body);
                writeCache(data);
                applyToDocument(data);
                return data;
            }
        } catch { /* offline — локальный кэш уже обновлён */ }
        return current;
    }

    function bindPrefInputs(root) {
        const scope = root || document;
        const map = {
            'notif.messages': 'notifyMessages',
            'notif.groupChats': 'notifyGroupChats',
            'notif.mentions': 'notifyMentions',
            'notif.reactions': 'notifyReactions',
            'notif.system': 'notifySystem',
            'notif.sound': 'notifySound',
            'notif.suppressOpenChat': 'suppressOpenChat',
            'notif.preview': 'notifyPreview',
            'appearance.largeFont': 'largeFont',
            'appearance.compactList': 'compactList',
            'privacy.showOnline': 'showOnline',
            'privacy.typing': 'showTyping',
            'privacy.readReceipts': 'readReceipts',
            'security.idleLogout': 'idleLogout'
        };

        loadSettings().then(s => {
            scope.querySelectorAll('.pref-toggle[data-pref]').forEach(el => {
                const key = map[el.getAttribute('data-pref')];
                if (!key) return;
                el.checked = !!s[key];
                el.addEventListener('change', () => {
                    const patch = {};
                    patch[key] = el.checked;
                    saveSettings(patch);
                });
            });

            const push = scope.querySelector('#push-toggle');
            if (push && !push.dataset.boundApi) {
                push.dataset.boundApi = '1';
                push.checked = !!s.pushEnabled;
            }
        });
    }

    function bindThemePicker(root) {
        const scope = root || document;
        const options = scope.querySelectorAll('.theme-option');
        if (!options.length) return;

        loadSettings().then(s => {
            let current = s.theme || 'system';
            const paint = () => options.forEach(btn => {
                const on = btn.getAttribute('data-theme') === current;
                btn.classList.toggle('is-active', on);
                btn.setAttribute('aria-pressed', on ? 'true' : 'false');
            });
            paint();
            options.forEach(btn => {
                btn.addEventListener('click', () => {
                    current = btn.getAttribute('data-theme') || 'system';
                    saveSettings({ theme: current });
                    paint();
                });
            });
        });
    }

    async function loadSessions(listEl) {
        if (!listEl) return;
        listEl.innerHTML = '<p class="text-sm text-gray-500 dark:text-gray-400">Загрузка…</p>';
        try {
            const res = await apiFetch('/api/v1/logins');
            if (!res.ok) throw new Error('fail');
            const json = await res.json();
            const items = json.data || json.Data || [];
            if (!items.length) {
                listEl.innerHTML = '<p class="text-sm text-gray-500">Нет записей о сессиях</p>';
                return;
            }
            const sorted = items.slice().sort((a, b) => new Date(b.loginTime || b.LoginTime) - new Date(a.loginTime || a.LoginTime));
            listEl.innerHTML = sorted.map(s => {
                const id = s.loginId || s.LoginId;
                const active = s.active ?? s.Active;
                const ip = s.ipAddress || s.IpAddress || '—';
                const t = s.loginTime || s.LoginTime;
                const when = t ? new Date(t).toLocaleString('ru-RU') : '—';
                const badge = active
                    ? '<span class="text-xs font-medium text-emerald-600 dark:text-emerald-400 px-2 py-1 rounded-lg bg-emerald-50 dark:bg-emerald-900/30">Активна</span>'
                    : '<span class="text-xs text-gray-400 px-2 py-1">Завершена</span>';
                const btn = active
                    ? `<button type="button" class="session-revoke text-xs text-red-600 dark:text-red-400 hover:underline" data-id="${id}">Завершить</button>`
                    : '';
                return `<div class="settings-row" data-session="${id}">
                    <div class="settings-row-text">
                        <span class="settings-row-label">${ip}</span>
                        <span class="settings-row-hint">${when}</span>
                    </div>
                    <div class="flex items-center gap-3">${badge}${btn}</div>
                </div>`;
            }).join('');

            listEl.querySelectorAll('.session-revoke').forEach(btn => {
                btn.addEventListener('click', async () => {
                    const id = btn.getAttribute('data-id');
                    if (!id || !confirm('Завершить эту сессию?')) return;
                    const r = await apiFetch('/api/v1/logins/' + id, { method: 'DELETE' });
                    if (r.ok) loadSessions(listEl);
                });
            });
        } catch {
            listEl.innerHTML = '<p class="text-sm text-red-500">Не удалось загрузить сессии</p>';
        }
    }

    // Публичный API
    global.AccountSettings = {
        load: loadSettings,
        save: saveSettings,
        apply: applyToDocument,
        bindPrefs: bindPrefInputs,
        bindTheme: bindThemePicker,
        loadSessions,
        defaults,
        readCache
    };

    // Авто-применение на любой странице
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => applyToDocument(readCache()));
    } else {
        applyToDocument(readCache());
    }
    // Фоновая подгрузка
    setTimeout(() => { loadSettings().catch(() => {}); }, 0);
})(window);
