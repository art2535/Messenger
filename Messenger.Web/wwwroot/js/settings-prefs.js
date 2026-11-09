(function () {
    const PREFIX = 'guap.settings.';

    function storageKey(pref) {
        return PREFIX + pref;
    }

    function getPref(pref, defaultValue) {
        try {
            const raw = localStorage.getItem(storageKey(pref));
            if (raw === null || raw === undefined) return defaultValue;
            if (raw === 'true') return true;
            if (raw === 'false') return false;
            return raw;
        } catch {
            return defaultValue;
        }
    }

    function setPref(pref, value) {
        try {
            localStorage.setItem(storageKey(pref), String(value));
        } catch { /* private mode */ }
    }

    window.getSettingPref = getPref;
    window.setSettingPref = setPref;

    window.initPrefToggles = function initPrefToggles() {
        document.querySelectorAll('.pref-toggle').forEach(input => {
            const pref = input.getAttribute('data-pref');
            if (!pref) return;
            const saved = getPref(pref, input.checked);
            input.checked = !!saved;
            input.addEventListener('change', () => {
                setPref(pref, input.checked);
                if (typeof showToast === 'function') {
                    showToast('Сохранено', 'success');
                }
            });
        });
    };

    function applyTheme(mode) {
        const root = document.documentElement;
        let dark = false;
        if (mode === 'dark') dark = true;
        else if (mode === 'light') dark = false;
        else dark = window.matchMedia('(prefers-color-scheme: dark)').matches;

        root.classList.toggle('dark', dark);
        try {
            if (mode === 'system') localStorage.removeItem('theme');
            else localStorage.setItem('theme', mode === 'dark' ? 'dark' : 'light');
            localStorage.setItem(storageKey('appearance.theme'), mode);
        } catch { /* ignore */ }
    }

    window.initThemePicker = function initThemePicker() {
        const options = document.querySelectorAll('.theme-option');
        if (!options.length) return;

        let current = getPref('appearance.theme', null);
        if (!current) {
            const t = localStorage.getItem('theme');
            current = t === 'dark' || t === 'light' ? t : 'system';
        }

        function paint() {
            options.forEach(btn => {
                const on = btn.getAttribute('data-theme') === current;
                btn.classList.toggle('is-active', on);
                btn.setAttribute('aria-pressed', on ? 'true' : 'false');
            });
        }

        options.forEach(btn => {
            btn.addEventListener('click', () => {
                current = btn.getAttribute('data-theme') || 'system';
                applyTheme(current);
                paint();
                if (typeof showToast === 'function') showToast('Тема обновлена', 'success');
            });
        });

        applyTheme(current);
        paint();

        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
            if (current === 'system') applyTheme('system');
        });
    };
})();
