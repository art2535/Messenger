const SESSION_CLOSE_DELAY_MS = 30 * 60 * 1000;
let closeSessionTimer = null;
let sessionClosed = false;
let intentionalLogout = false;

function getAccessTokenForBeacon() {
    return (typeof token === 'string' && token)
        ? token
        : (localStorage.getItem('token') || '');
}

async function closeSessionViaApi() {
    if (sessionClosed || intentionalLogout) return;
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

    localStorage.removeItem('token');
}

function markIntentionalLogout() {
    intentionalLogout = true;
    sessionClosed = true;
    if (closeSessionTimer) {
        clearTimeout(closeSessionTimer);
        closeSessionTimer = null;
    }
    localStorage.removeItem('token');
}

document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') {
        if (intentionalLogout) return;
        closeSessionTimer = setTimeout(() => {
            closeSessionViaApi();
        }, SESSION_CLOSE_DELAY_MS);
    } else {
        if (closeSessionTimer) {
            clearTimeout(closeSessionTimer);
            closeSessionTimer = null;
        }
        if (!intentionalLogout) {
            sessionClosed = false;
        }
    }
});

window.addEventListener('pagehide', (e) => {
    if (!e.persisted && !intentionalLogout) {
        closeSessionViaApi();
    }
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
    if (el) markIntentionalLogout();
}, true);
