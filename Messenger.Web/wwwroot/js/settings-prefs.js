(function () {
    const CACHE_KEY = 'guap.accountSettings.v2';
    const PREFIX = 'guap.settings.';
    try { localStorage.removeItem('guap.accountSettings'); } catch (_) { /* */ }

    const PREF_TO_API = {
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
        'appearance.theme': 'theme',
        'privacy.showOnline': 'showOnline',
        'privacy.typing': 'showTyping',
        'privacy.readReceipts': 'readReceipts',
        'security.idleLogout': 'idleLogout'
    };

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

    function apiRoot() {
        var base = (window.API_BASE_URL || '').replace(/\/$/, '');
        if (!base) {
            var m = document.querySelector('meta[name="api-base"]');
            if (m && m.content) base = m.content.trim().replace(/\/$/, '');
        }
        if (!base) return '';
        if (!/\/api\/v\d/i.test(base)) {
            base = base + '/api/v1.0';
        }
        return base;
    }

    function cleanToken(t) {
        return (t || '').trim().replace(/^Bearer\s+/i, '');
    }

    function jwtExpiresAtMs(token) {
        try {
            var payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
            var json = JSON.parse(atob(payload + '='.repeat((4 - payload.length % 4) % 4)));
            return json.exp ? json.exp * 1000 : 0;
        } catch (_) {
            return 0;
        }
    }

    function getJwtToken() {
        if (typeof window.getJwtToken === 'function' && window.getJwtToken !== getJwtToken) {
            try {
                var t = window.getJwtToken();
                if (t) return cleanToken(t);
            } catch (_) { /* */ }
        }
        var candidates = [
            document.querySelector('meta[name="access-token"]') && document.querySelector('meta[name="access-token"]').content,
            localStorage.getItem('token'),
            sessionStorage.getItem('token')
        ].map(cleanToken).filter(Boolean);
        if (!candidates.length) return '';
        candidates.sort(function (a, b) { return jwtExpiresAtMs(b) - jwtExpiresAtMs(a); });
        return candidates[0];
    }

    function getAuthHeader() {
        if (typeof getAuthToken === 'function') {
            var h = getAuthToken();
            if (h) return h.startsWith('Bearer ') ? h : ('Bearer ' + h);
        }
        var token = getJwtToken();
        return token ? ('Bearer ' + token) : '';
    }

    async function fetchFreshToken() {
        if (typeof window.fetchFreshToken === 'function' && window.fetchFreshToken !== fetchFreshToken) {
            try {
                var t = await window.fetchFreshToken();
                if (t) return cleanToken(t);
            } catch (_) { /* */ }
        }
        try {
            var res = await fetch('/inside/settings/profile?handler=Token', {
                credentials: 'same-origin',
                cache: 'no-store',
                headers: { 'Accept': 'application/json' }
            });
            if (!res.ok) return '';
            var data = await res.json();
            var fresh = cleanToken(data && data.accessToken);
            if (!fresh) return '';
            try { localStorage.setItem('token', fresh); } catch (_) { /* */ }
            var meta = document.querySelector('meta[name="access-token"]');
            if (meta) meta.content = fresh;
            return fresh;
        } catch (_) {
            return '';
        }
    }

    async function ensureFreshToken() {
        var token = getJwtToken();
        var exp = token ? jwtExpiresAtMs(token) : 0;
        if (!token || (exp && exp - Date.now() < 30000)) {
            await fetchFreshToken();
        }
    }

    async function apiFetch(path, options, retried) {
        options = options || {};
        await ensureFreshToken();
        var auth = getAuthHeader();
        if (!auth) {
            // нет токена — не дёргаем API (избегаем 401 в консоли)
            return { ok: false, status: 401, json: async function () { return {}; } };
        }
        var headers = Object.assign({ 'Accept': 'application/json' }, options.headers || {});
        headers['Authorization'] = auth;
        if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';

        var url = apiRoot() + path;
        var res = await fetch(url, Object.assign({}, options, { headers: headers, credentials: 'same-origin' }));

        if (res.status === 401 && !retried) {
            var fresh = await fetchFreshToken();
            if (fresh) return apiFetch(path, options, true);
        }
        return res;
    }

    function readCache() {
        try {
            var raw = localStorage.getItem(CACHE_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch { return null; }
    }

    function writeCache(data) {
        try { localStorage.setItem(CACHE_KEY, JSON.stringify(data)); } catch { /* */ }
    }

    function normalize(dto) {
        var d = defaults();
        if (!dto) return d;
        return {
            pushEnabled: !!(dto.pushEnabled ?? dto.PushEnabled ?? d.pushEnabled),
            notifyMessages: !!(dto.notifyMessages ?? dto.NotifyMessages ?? d.notifyMessages),
            notifyGroupChats: !!(dto.notifyGroupChats ?? dto.NotifyGroupChats ?? d.notifyGroupChats),
            notifyMentions: !!(dto.notifyMentions ?? dto.NotifyMentions ?? d.notifyMentions),
            notifyReactions: !!(dto.notifyReactions ?? dto.NotifyReactions ?? d.notifyReactions),
            notifySystem: !!(dto.notifySystem ?? dto.NotifySystem ?? d.notifySystem),
            notifySound: !!(dto.notifySound ?? dto.NotifySound ?? d.notifySound),
            suppressOpenChat: !!(dto.suppressOpenChat ?? dto.SuppressOpenChat ?? d.suppressOpenChat),
            notifyPreview: !!(dto.notifyPreview ?? dto.NotifyPreview ?? d.notifyPreview),
            theme: String(dto.theme ?? dto.Theme ?? d.theme).toLowerCase(),
            largeFont: !!(dto.largeFont ?? dto.LargeFont ?? d.largeFont),
            compactList: !!(dto.compactList ?? dto.CompactList ?? d.compactList),
            showOnline: !!(dto.showOnline ?? dto.ShowOnline ?? d.showOnline),
            showTyping: !!(dto.showTyping ?? dto.ShowTyping ?? d.showTyping),
            readReceipts: !!(dto.readReceipts ?? dto.ReadReceipts ?? d.readReceipts),
            idleLogout: !!(dto.idleLogout ?? dto.IdleLogout ?? d.idleLogout)
        };
    }

    function applyTheme(mode) {
        var m = mode || 'system';
        var dark = false;
        if (m === 'dark') dark = true;
        else if (m === 'light') dark = false;
        else dark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        document.documentElement.classList.toggle('dark', dark);
        try {
            if (m === 'system') localStorage.removeItem('theme');
            else localStorage.setItem('theme', dark ? 'dark' : 'light');
        } catch { /* */ }
    }

    function applyToDocument(s) {
        s = s || readCache() || defaults();
        applyTheme(s.theme);
        document.documentElement.classList.toggle('settings-large-font', !!s.largeFont);
        document.documentElement.classList.toggle('settings-compact-list', !!s.compactList);
        document.documentElement.dataset.showOnline = s.showOnline ? '1' : '0';
        document.documentElement.dataset.showTyping = s.showTyping ? '1' : '0';
        document.documentElement.dataset.readReceipts = s.readReceipts ? '1' : '0';
        document.documentElement.dataset.notifySound = s.notifySound ? '1' : '0';
        document.documentElement.dataset.suppressOpenChat = s.suppressOpenChat ? '1' : '0';
        document.documentElement.dataset.notifyPreview = s.notifyPreview ? '1' : '0';
        window.__accountSettings = s;
        window.__privacyShowTyping = !!s.showTyping;
        window.__privacyShowOnline = !!s.showOnline;
        window.__privacyReadReceipts = !!s.readReceipts;
        window.__notifSound = !!s.notifySound;
        window.__suppressOpenChat = !!s.suppressOpenChat;
        window.__notifyPreview = !!s.notifyPreview;
    }

    async function loadSettings() {
        var cached = readCache();
        if (cached) applyToDocument(cached);
        try {
            var res = await apiFetch('/account-settings');
            if (!res.ok) return cached || defaults();
            var json = await res.json();
            var data = normalize(json.data || json.Data || json);
            writeCache(data);
            applyToDocument(data);
            return data;
        } catch {
            return cached || defaults();
        }
    }

    async function saveSettings(partial) {
        var current = Object.assign({}, readCache() || defaults(), partial || {});
        writeCache(current);
        applyToDocument(current);
        try {
            var res = await apiFetch('/account-settings', {
                method: 'PUT',
                body: JSON.stringify(current)
            });
            if (res.ok) {
                var json = await res.json();
                var data = normalize(json.data || json.Data || current);
                writeCache(data);
                applyToDocument(data);
                return data;
            }
        } catch { /* offline */ }
        return current;
    }

    function storageKey(pref) { return PREFIX + pref; }
    function getPref(pref, defaultValue) {
        var s = readCache() || defaults();
        var apiKey = PREF_TO_API[pref];
        if (apiKey && apiKey in s) return s[apiKey];
        try {
            var raw = localStorage.getItem(storageKey(pref));
            if (raw === null || raw === undefined) return defaultValue;
            if (raw === 'true') return true;
            if (raw === 'false') return false;
            return raw;
        } catch { return defaultValue; }
    }
    function setPref(pref, value) {
        var apiKey = PREF_TO_API[pref];
        if (apiKey) {
            var patch = {};
            patch[apiKey] = value;
            saveSettings(patch);
            return;
        }
        try { localStorage.setItem(storageKey(pref), String(value)); } catch { /* */ }
    }

    window.getSettingPref = getPref;
    window.setSettingPref = setPref;

    window.initPrefToggles = function initPrefToggles() {
        loadSettings().then(function (s) {
            document.querySelectorAll('.pref-toggle').forEach(function (input) {
                var pref = input.getAttribute('data-pref');
                if (!pref) return;
                var apiKey = PREF_TO_API[pref];
                if (apiKey && apiKey in s) input.checked = !!s[apiKey];
                input.addEventListener('change', function () {
                    setPref(pref, input.checked);
                });
            });
            var push = document.getElementById('push-toggle');
            if (push && !push.dataset.boundSettings) {
                push.dataset.boundSettings = '1';
                push.checked = !!s.pushEnabled;
                push.addEventListener('change', function () {
                    saveSettings({ pushEnabled: !!push.checked });
                });
            }
        });
    };

    window.initThemePicker = function initThemePicker() {
        var options = document.querySelectorAll('.theme-option');
        if (!options.length) return;
        loadSettings().then(function (s) {
            var current = s.theme || 'system';
            function paint() {
                options.forEach(function (btn) {
                    var on = btn.getAttribute('data-theme') === current;
                    btn.classList.toggle('is-active', on);
                    btn.setAttribute('aria-pressed', on ? 'true' : 'false');
                });
            }
            paint();
            options.forEach(function (btn) {
                btn.addEventListener('click', function () {
                    current = btn.getAttribute('data-theme') || 'system';
                    saveSettings({ theme: current });
                    paint();
                });
            });
        });
    };

    window.loadSessionsList = async function loadSessionsList(listEl) {
        if (!listEl) return;
        listEl.classList.add('sessions-scroll');
        listEl.innerHTML = '<p class="text-sm text-gray-500 dark:text-gray-400 py-2">Загрузка…</p>';
        try {
            var res = await apiFetch('/Logins');
            if (!res.ok) throw new Error('fail');
            var json = await res.json();
            var items = json.data || json.Data || [];
            items = items.filter(function (s) { return !!(s.active ?? s.Active); });
            if (!items.length) {
                listEl.innerHTML = '<p class="text-sm text-gray-500 dark:text-gray-400 py-2">Нет активных сессий</p>';
                return;
            }
            listEl.innerHTML = items.map(function (s) {
                var id = s.loginId || s.LoginId;
                var ip = s.ipAddress || s.IpAddress || '—';
                var tm = s.loginTime || s.LoginTime;
                var when = tm ? new Date(tm).toLocaleString('ru-RU') : '—';
                return '<div class="settings-row" data-session="' + id + '">' +
                    '<div class="settings-row-text"><span class="settings-row-label">' + ip + '</span>' +
                    '<span class="settings-row-hint">' + when + '</span></div>' +
                    '<div class="flex items-center gap-3">' +
                    '<span class="text-xs font-medium text-emerald-600 dark:text-emerald-400 px-2 py-1 rounded-lg bg-emerald-50 dark:bg-emerald-900/30">Активна</span>' +
                    '<button type="button" class="session-revoke text-xs text-red-600 dark:text-red-400 hover:underline" data-id="' + id + '">Завершить</button>' +
                    '</div></div>';
            }).join('');
            listEl.querySelectorAll('.session-revoke').forEach(function (btn) {
                btn.addEventListener('click', async function (ev) {
                    ev.preventDefault();
                    ev.stopPropagation();
                    var id = btn.getAttribute('data-id');
                    if (!id || id === 'undefined' || id === 'null') {
                        alert('Некорректный идентификатор сессии');
                        return;
                    }
                    var ok = true;
                    if (typeof showConfirm === 'function') {
                        ok = await showConfirm('Завершить эту сессию?', {
                            title: 'Сессия',
                            okText: 'Завершить',
                            cancelText: 'Отмена',
                            danger: true
                        });
                    } else {
                        ok = window.confirm('Завершить эту сессию?');
                    }
                    if (!ok) return;
                    btn.disabled = true;
                    btn.textContent = '…';
                    try {
                        var r = await apiFetch('/Logins/' + encodeURIComponent(id), { method: 'DELETE' });
                        if (!r || !r.ok) {
                            r = await apiFetch('/logins/' + encodeURIComponent(id), { method: 'DELETE' });
                        }
                        if (r && r.ok) {
                            await loadSessionsList(listEl);
                            return;
                        }
                        var body = '';
                        try { body = await (r && r.text ? r.text() : Promise.resolve('')); } catch (_) {}
                        console.warn('Revoke session failed', r && r.status, body);
                        if (typeof showAlert === 'function') await showAlert('Не удалось завершить сессию');
                        else alert('Не удалось завершить сессию' + (r && r.status ? (' (' + r.status + ')') : ''));
                    } catch (e) {
                        console.warn('Revoke session error', e);
                        if (typeof showAlert === 'function') await showAlert('Ошибка при завершении сессии');
                        else alert('Ошибка при завершении сессии');
                    }
                    btn.disabled = false;
                    btn.textContent = 'Завершить';
                });
            });
        } catch (e) {
            console.warn('sessions', e);
            listEl.innerHTML = '<p class="text-sm text-red-500 py-2">Не удалось загрузить сессии</p>';
        }
    };

window.AccountSettings = {
        load: loadSettings,
        save: saveSettings,
        apply: applyToDocument
    };

    applyToDocument(readCache());
    document.addEventListener('DOMContentLoaded', function () {
        setTimeout(function () { loadSettings().catch(function () {}); }, 0);
    });
})();
