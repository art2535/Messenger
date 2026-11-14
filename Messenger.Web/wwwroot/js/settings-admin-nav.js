/**
 * Показывает ссылку #nav-admin-link, если GET /admin/me вернул isAdmin=true.
 */
(function () {
    const link = document.getElementById('nav-admin-link');
    if (!link) return;

    const API = (typeof window.API_BASE_URL === 'string' && window.API_BASE_URL
        ? window.API_BASE_URL
        : (document.querySelector('meta[name="api-base"]')?.content || '')).replace(/\/$/, '');

    let t = document.querySelector('meta[name="access-token"]')?.content || '';
    try { t = t || localStorage.getItem('token') || ''; } catch (_) {}
    t = String(t).replace(/^Bearer\s+/i, '');

    const headers = t ? { 'Authorization': 'Bearer ' + t } : {};

    fetch(API + '/admin/me', { headers, credentials: 'include' })
        .then(function (r) {
            if (!r.ok) return null;
            return r.json();
        })
        .then(function (json) {
            if (json && (json.isAdmin === true || json.IsAdmin === true)) {
                link.classList.remove('hidden');
            }
        })
        .catch(function () {});
})();
