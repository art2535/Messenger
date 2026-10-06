const SESSION_CLOSE_DELAY_MS = 30 * 60 * 1000;
let closeSessionTimer = null;
let sessionClosed = false;
let intentionalLogout = false;
let internalNavigation = false;

function getAccessTokenForBeacon() {
    return (typeof token === 'string' && token)
        ? token
        : (localStorage.getItem('token') || '');
}

function isInternalAppPath(href) {
    if (!href || href === '#' || href.startsWith('#')) return true;
    try {
        const u = new URL(href, window.location.origin);
        if (u.origin !== window.location.origin) return false;
        const p = (u.pathname || '').toLowerCase();
        return p.includes('/account/') ||
            p.includes('/authorization/') ||
            p === '/' ||
            p.endsWith('/chats') ||
            p.endsWith('/settings');
    } catch {
        return false;
    }
}

async function closeSessionViaApi(options = {}) {
    if (sessionClosed || intentionalLogout) return;
    if (internalNavigation && !options.force) return;

    sessionClosed = true;

    const accessToken = getAccessTokenForBeacon();
    if (!accessToken) return;

    const payload = JSON.stringify({ online: false });

    try {
        navigator.sendBeacon(
            `${window.API_BASE_URL}/userstatuses`,
            new Blob([payload], { type: 'application/json' })
        );
    } catch (e) { }

    try {
        const headers = {
            'Authorization': accessToken.startsWith('Bearer ') ? accessToken : `Bearer ${accessToken}`
        };
        await fetch(`${window.API_BASE_URL}/logins`, {
            method: 'PATCH',
            headers,
            keepalive: true
        });
    } catch (e) {
        console.warn('Не удалось закрыть login-сессию при уходе со страницы', e);
    }

    if (!internalNavigation || options.force) {
        localStorage.removeItem('token');
    }
}

function markIntentionalLogout() {
    intentionalLogout = true;
    internalNavigation = false;
    sessionClosed = true;
    if (closeSessionTimer) {
        clearTimeout(closeSessionTimer);
        closeSessionTimer = null;
    }
    localStorage.removeItem('token');
}

function markInternalNavigation() {
    internalNavigation = true;
    if (closeSessionTimer) {
        clearTimeout(closeSessionTimer);
        closeSessionTimer = null;
    }
}

document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') {
        if (intentionalLogout || internalNavigation) return;
        closeSessionTimer = setTimeout(() => {
            closeSessionViaApi({ force: true });
        }, SESSION_CLOSE_DELAY_MS);
    } else {
        if (closeSessionTimer) {
            clearTimeout(closeSessionTimer);
            closeSessionTimer = null;
        }
        if (!intentionalLogout) {
            sessionClosed = false;
            internalNavigation = false;
        }
    }
});

window.addEventListener('pagehide', (e) => {
    if (e.persisted || intentionalLogout || internalNavigation) return;
    closeSessionViaApi({ force: true });
});

document.addEventListener('submit', (e) => {
    const form = e.target;
    if (!(form instanceof HTMLFormElement)) return;
    const action = (form.getAttribute('action') || '').toLowerCase();
    if (form.dataset.logout === 'true' || action.includes('/authorization/logout') || action.includes('logout')) {
        markIntentionalLogout();
    }
}, true);

document.addEventListener('click', (e) => {
    const el = e.target instanceof Element ? e.target.closest('[data-logout="true"]') : null;
    if (el) {
        markIntentionalLogout();
        return;
    }
    const link = e.target instanceof Element ? e.target.closest('a[href]') : null;
    if (!link) return;
    const href = link.getAttribute('href') || '';
    if (link.target === '_blank' || link.hasAttribute('download')) return;
    if (isInternalAppPath(href)) {
        markInternalNavigation();
        const t = getAccessTokenForBeacon();
        if (t) {
            try { localStorage.setItem('token', t.replace(/^Bearer\s+/i, '')); } catch (_) {}
        }
    }
}, true);
