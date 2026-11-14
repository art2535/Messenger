/**
 * Жалоба на сообщение: кнопка «Пожаловаться» в #message-context-menu + модалка.
 * Подключать после chats-menus.js.
 */
(function () {
    'use strict';

    var REPORT_BTN_HTML =
        '<button type="button" id="ctx-report-btn">' +
        '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="ctx-icon">' +
        '<path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"/>' +
        '<line x1="4" y1="22" x2="4" y2="15"/></svg>' +
        '<span>Пожаловаться</span></button>';

    function apiBase() {
        if (typeof API_BASE === 'string' && API_BASE) return API_BASE.replace(/\/$/, '');
        if (typeof window.API_BASE_URL === 'string' && window.API_BASE_URL)
            return window.API_BASE_URL.replace(/\/$/, '');
        var meta = document.querySelector('meta[name="api-base"]');
        return (meta && meta.content ? meta.content : '').replace(/\/$/, '');
    }

    function ensureReportButton() {
        var menu = document.getElementById('message-context-menu');
        if (!menu) return null;
        var btn = document.getElementById('ctx-report-btn');
        if (btn) return btn;
        // вставить после первого пункта или в начало
        var wrap = document.createElement('div');
        wrap.innerHTML = REPORT_BTN_HTML;
        btn = wrap.firstChild;
        var reply = document.getElementById('ctx-reply-btn');
        if (reply && reply.parentNode === menu) {
            menu.insertBefore(btn, reply);
        } else {
            menu.insertBefore(btn, menu.firstChild);
        }
        return btn;
    }

    async function postReport(messageId, reason, comment) {
        var url = apiBase() + '/moderation/report';
        var body = JSON.stringify({ messageId: messageId, reason: reason, comment: comment || null });
        if (typeof fetchWithAuth === 'function') {
            return fetchWithAuth(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: body
            });
        }
        var t = '';
        try { t = localStorage.getItem('token') || ''; } catch (e) {}
        t = String(t).replace(/^Bearer\s+/i, '');
        return fetch(url, {
            method: 'POST',
            credentials: 'include',
            headers: Object.assign(
                { 'Content-Type': 'application/json' },
                t ? { Authorization: 'Bearer ' + t } : {}
            ),
            body: body
        });
    }

    function ensureModal() {
        var el = document.getElementById('moderation-report-modal');
        if (el) return el;
        el = document.createElement('div');
        el.id = 'moderation-report-modal';
        el.className = 'mod-report-overlay hidden';
        el.innerHTML =
            '<div class="mod-report-dialog" role="dialog" aria-modal="true">' +
            '<h3>Пожаловаться на сообщение</h3>' +
            '<p class="mod-report-hint">Жалоба уйдёт модераторам.</p>' +
            '<label class="mod-report-label">Причина</label>' +
            '<select id="mod-report-reason" class="mod-report-input">' +
            '<option value="spam">Спам</option>' +
            '<option value="abuse">Оскорбления</option>' +
            '<option value="harassment">Домогательства</option>' +
            '<option value="illegal">Незаконный контент</option>' +
            '<option value="other">Другое</option>' +
            '</select>' +
            '<label class="mod-report-label">Комментарий</label>' +
            '<textarea id="mod-report-comment" class="mod-report-input" rows="3" maxlength="1000" placeholder="Подробности…"></textarea>' +
            '<div class="mod-report-actions">' +
            '<button type="button" class="mod-report-cancel" id="mod-report-cancel">Отмена</button>' +
            '<button type="button" class="mod-report-submit" id="mod-report-submit">Отправить</button>' +
            '</div></div>';
        document.body.appendChild(el);
        if (!document.getElementById('mod-report-styles')) {
            var st = document.createElement('style');
            st.id = 'mod-report-styles';
            st.textContent =
                '.mod-report-overlay{position:fixed;inset:0;z-index:10000;background:rgba(0,0,0,.45);display:flex;align-items:center;justify-content:center;padding:16px}' +
                '.mod-report-overlay.hidden{display:none!important}' +
                '.mod-report-dialog{background:#fff;color:#111;border-radius:16px;max-width:400px;width:100%;padding:20px;box-shadow:0 20px 50px rgba(0,0,0,.25)}' +
                '.dark .mod-report-dialog,.dark .mod-report-dialog{background:#1f2937;color:#f3f4f6}' +
                'html.dark .mod-report-dialog{background:#1f2937;color:#f3f4f6}' +
                '.mod-report-dialog h3{margin:0 0 8px;font-size:18px;font-weight:700}' +
                '.mod-report-hint{margin:0 0 14px;font-size:13px;opacity:.75}' +
                '.mod-report-label{display:block;font-size:12px;font-weight:600;margin:10px 0 4px}' +
                '.mod-report-input{width:100%;border:1px solid #d1d5db;border-radius:10px;padding:8px 10px;font-size:14px;background:#fff;color:inherit;box-sizing:border-box}' +
                'html.dark .mod-report-input{background:#111827;border-color:#374151;color:#f3f4f6}' +
                '.mod-report-actions{display:flex;gap:10px;justify-content:flex-end;margin-top:16px}' +
                '.mod-report-cancel,.mod-report-submit{border:none;border-radius:10px;padding:8px 14px;font-weight:600;cursor:pointer;font-size:14px}' +
                '.mod-report-cancel{background:#e5e7eb;color:#111}' +
                'html.dark .mod-report-cancel{background:#374151;color:#f3f4f6}' +
                '.mod-report-submit{background:#2563eb;color:#fff}' +
                '.mod-report-submit:disabled{opacity:.6;cursor:wait}' +
                '#ctx-report-btn{display:flex!important;align-items:center;gap:8px;width:100%}';
            document.head.appendChild(st);
        }
        return el;
    }

    var pendingMessageId = null;

    function openReportModal(messageId) {
        pendingMessageId = messageId;
        var modal = ensureModal();
        modal.classList.remove('hidden');
        var reason = document.getElementById('mod-report-reason');
        var comment = document.getElementById('mod-report-comment');
        if (reason) reason.value = 'spam';
        if (comment) comment.value = '';
        document.getElementById('mod-report-cancel').onclick = closeReportModal;
        document.getElementById('mod-report-submit').onclick = submitReport;
        modal.onclick = function (e) { if (e.target === modal) closeReportModal(); };
    }

    function closeReportModal() {
        pendingMessageId = null;
        var m = document.getElementById('moderation-report-modal');
        if (m) m.classList.add('hidden');
    }

    async function submitReport() {
        if (!pendingMessageId) return;
        var reason = (document.getElementById('mod-report-reason') || {}).value || 'other';
        var commentEl = document.getElementById('mod-report-comment');
        var comment = commentEl && commentEl.value ? commentEl.value.trim() : '';
        var btn = document.getElementById('mod-report-submit');
        if (btn) btn.disabled = true;
        try {
            var res = await postReport(pendingMessageId, reason, comment);
            var ok = res && (res.ok === true || res.status === 200 || res.status === 201);
            if (typeof showToast === 'function') {
                showToast(ok ? 'Жалоба отправлена' : 'Не удалось отправить жалобу', ok ? 'success' : 'error');
            } else {
                alert(ok ? 'Жалоба отправлена' : 'Не удалось отправить жалобу');
            }
            if (ok) closeReportModal();
        } catch (e) {
            if (typeof showToast === 'function') showToast('Ошибка отправки жалобы', 'error');
            else alert('Ошибка отправки жалобы');
        } finally {
            if (btn) btn.disabled = false;
        }
    }

    function getContextMessageId() {
        if (window._ctxMessageId) return window._ctxMessageId;
        var menu = document.getElementById('message-context-menu');
        return (menu && (menu.dataset.messageId || menu.getAttribute('data-message-id'))) || null;
    }

    function onReportClick(e) {
        e.preventDefault();
        e.stopPropagation();
        var mid = getContextMessageId();
        if (!mid) {
            if (typeof showToast === 'function') showToast('Сообщение не выбрано', 'error');
            return;
        }
        if (typeof hideMessageContextMenu === 'function') hideMessageContextMenu();
        openReportModal(mid);
    }

    function wireReportButton() {
        var btn = ensureReportButton();
        if (!btn) return;
        btn.style.display = '';
        if (btn.dataset.modWired) return;
        btn.dataset.modWired = '1';
        btn.addEventListener('click', onReportClick);
    }

    // Обёртка showMessageContextMenu — сохраняем messageId и показываем кнопку
    function patchShowMenu() {
        if (typeof window.showMessageContextMenu !== 'function') return false;
        if (window.showMessageContextMenu._modPatched) return true;
        var orig = window.showMessageContextMenu;
        window.showMessageContextMenu = function (x, y, messageId, text) {
            window._ctxMessageId = messageId;
            var menu = document.getElementById('message-context-menu');
            if (menu && messageId) menu.dataset.messageId = String(messageId);
            wireReportButton();
            var result = orig.apply(this, arguments);
            var reportBtn = ensureReportButton();
            if (reportBtn) reportBtn.style.display = '';
            return result;
        };
        window.showMessageContextMenu._modPatched = true;
        return true;
    }

    function init() {
        wireReportButton();
        if (!patchShowMenu()) {
            var n = 0;
            var timer = setInterval(function () {
                n++;
                if (patchShowMenu() || n > 40) clearInterval(timer);
            }, 250);
        }
        // если меню появится позже
        if (document.body) {
            var obs = new MutationObserver(function () {
                wireReportButton();
                patchShowMenu();
            });
            obs.observe(document.body, { childList: true, subtree: true });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.ModerationUI = {
        reportMessage: function (id) { openReportModal(id); },
        openReportModal: openReportModal,
        closeReportModal: closeReportModal,
        ensureReportButton: ensureReportButton
    };
})();
