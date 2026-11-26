/**
 * Единая система toast-уведомлений (error, warning, success, info).
 * window.MessengerToast.show / .error / .warning / .success / .info
 */
(function (global) {
    const MAX_VISIBLE = 5;
    const DEFAULT_DURATION = {
        error: 6000,
        warning: 5000,
        success: 3500,
        info: 4000
    };

    function ensureContainer() {
        let el = document.getElementById('messenger-toast-root');
        if (el) return el;
        el = document.createElement('div');
        el.id = 'messenger-toast-root';
        el.className = 'messenger-toast-root';
        el.setAttribute('aria-live', 'polite');
        el.setAttribute('aria-relevant', 'additions');
        document.body.appendChild(el);
        return el;
    }

    function show(message, type, duration) {
        if (!message) return;
        type = type || 'info';
        duration = duration != null ? duration : (DEFAULT_DURATION[type] || 4000);

        const root = ensureContainer();
        while (root.children.length >= MAX_VISIBLE) {
            root.removeChild(root.firstChild);
        }

        const item = document.createElement('div');
        item.className = 'messenger-toast messenger-toast--' + type;
        item.setAttribute('role', type === 'error' ? 'alert' : 'status');

        const text = document.createElement('div');
        text.className = 'messenger-toast__text';
        text.textContent = String(message);

        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'messenger-toast__close';
        close.setAttribute('aria-label', 'Закрыть');
        close.innerHTML = '&times;';
        close.onclick = () => dismiss(item);

        item.appendChild(text);
        item.appendChild(close);
        root.appendChild(item);

        requestAnimationFrame(() => item.classList.add('messenger-toast--visible'));

        if (duration > 0) {
            item._timer = setTimeout(() => dismiss(item), duration);
        }
        return item;
    }

    function dismiss(item) {
        if (!item || item._leaving) return;
        item._leaving = true;
        if (item._timer) clearTimeout(item._timer);
        item.classList.remove('messenger-toast--visible');
        item.classList.add('messenger-toast--hide');
        setTimeout(() => {
            if (item.parentNode) item.parentNode.removeChild(item);
        }, 280);
    }

    const api = {
        show,
        error: (m, d) => show(m, 'error', d),
        warning: (m, d) => show(m, 'warning', d),
        success: (m, d) => show(m, 'success', d),
        info: (m, d) => show(m, 'info', d),
        dismiss
    };

    global.MessengerToast = api;
})(typeof window !== 'undefined' ? window : this);
