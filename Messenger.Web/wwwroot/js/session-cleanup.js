const SESSION_CLOSE_DELAY_MS = 30 * 60 * 1000;
let closeSessionTimer = null;
let sessionClosed = false;

function getAccessTokenForBeacon() {
    return (typeof token === 'string' && token)
        ? token
        : (localStorage.getItem('token') || '');
}

async function closeSessionViaApi() {
    if (sessionClosed) return;
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
        await fetch(`${window.API_BASE_URL}/logins`, {
            method: 'PATCH',
            headers: {
                'Authorization': accessToken.startsWith('Bearer ') ? accessToken : `Bearer ${accessToken}`,
                'Content-Type': 'application/json'
            },
            keepalive: true
        });
    } catch (e) {
        console.warn('Не удалось закрыть login-сессию при уходе со страницы', e);
    }

    localStorage.removeItem('token');
}

document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') {
        closeSessionTimer = setTimeout(() => {
            closeSessionViaApi();
        }, SESSION_CLOSE_DELAY_MS);
    } else {
        if (closeSessionTimer) {
            clearTimeout(closeSessionTimer);
            closeSessionTimer = null;
        }
        sessionClosed = false;
    }
});

window.addEventListener('pagehide', (e) => {
    if (!e.persisted) {
        closeSessionViaApi();
    }
});