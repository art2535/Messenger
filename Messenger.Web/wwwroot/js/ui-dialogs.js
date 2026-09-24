(function (global) {
  'use strict';

  const ICONS = {
    error: '⚠️',
    warning: '⚠️',
    info: 'ℹ️',
    success: '✓',
    confirm: '⚠️'
  };

  function ensureRoot() {
    let root = document.getElementById('ui-dialog-root');
    if (!root) {
      root = document.createElement('div');
      root.id = 'ui-dialog-root';
      document.body.appendChild(root);
    }
    return root;
  }

  function ensureToastContainer() {
    let c = document.getElementById('ui-toast-container');
    if (!c) {
      c = document.createElement('div');
      c.id = 'ui-toast-container';
      document.body.appendChild(c);
    }
    return c;
  }

  function closeOverlay(overlay) {
    overlay.classList.remove('open');
    setTimeout(() => overlay.remove(), 200);
  }

  function showAlert(message, options) {
    const opts = options || {};
    const type = opts.type || 'error';
    const title = opts.title || (type === 'success' ? 'Готово' : type === 'info' ? 'Сообщение' : type === 'warning' ? 'Внимание' : 'Ошибка');
    const btnText = opts.okText || 'ОК';

    return new Promise((resolve) => {
      const root = ensureRoot();
      const overlay = document.createElement('div');
      overlay.className = 'ui-dialog-overlay';
      overlay.setAttribute('role', 'alertdialog');
      overlay.innerHTML =
        '<div class="ui-dialog">' +
          '<div class="ui-dialog-icon ' + type + '">' + (ICONS[type] || ICONS.info) + '</div>' +
          '<h3 class="ui-dialog-title"></h3>' +
          '<p class="ui-dialog-message"></p>' +
          '<div class="ui-dialog-actions">' +
            '<button type="button" class="ui-dialog-btn primary ui-dialog-ok"></button>' +
          '</div>' +
        '</div>';
      overlay.querySelector('.ui-dialog-title').textContent = title;
      overlay.querySelector('.ui-dialog-message').textContent = message == null ? '' : String(message);
      overlay.querySelector('.ui-dialog-ok').textContent = btnText;

      const finish = () => {
        closeOverlay(overlay);
        document.removeEventListener('keydown', onKey);
        resolve();
      };
      const onKey = (e) => {
        if (e.key === 'Escape' || e.key === 'Enter') {
          e.preventDefault();
          finish();
        }
      };
      overlay.querySelector('.ui-dialog-ok').addEventListener('click', finish);
      overlay.addEventListener('click', (e) => {
        if (e.target === overlay) finish();
      });
      document.addEventListener('keydown', onKey);
      root.appendChild(overlay);
      requestAnimationFrame(() => overlay.classList.add('open'));
      overlay.querySelector('.ui-dialog-ok').focus();
    });
  }

  function showConfirm(message, options) {
    const opts = options || {};
    const title = opts.title || 'Подтвердите действие';
    const danger = opts.danger !== false;
    const okText = opts.okText || 'Подтвердить';
    const cancelText = opts.cancelText || 'Отмена';
    const type = opts.type || 'confirm';

    return new Promise((resolve) => {
      const root = ensureRoot();
      const overlay = document.createElement('div');
      overlay.className = 'ui-dialog-overlay';
      overlay.setAttribute('role', 'alertdialog');
      overlay.innerHTML =
        '<div class="ui-dialog">' +
          '<div class="ui-dialog-icon ' + type + '">' + (ICONS[type] || ICONS.confirm) + '</div>' +
          '<h3 class="ui-dialog-title"></h3>' +
          '<p class="ui-dialog-message"></p>' +
          '<div class="ui-dialog-actions">' +
            '<button type="button" class="ui-dialog-btn cancel ui-dialog-no"></button>' +
            '<button type="button" class="ui-dialog-btn ' + (danger ? 'danger' : 'primary') + ' ui-dialog-yes"></button>' +
          '</div>' +
        '</div>';
      overlay.querySelector('.ui-dialog-title').textContent = title;
      overlay.querySelector('.ui-dialog-message').textContent = message == null ? '' : String(message);
      overlay.querySelector('.ui-dialog-no').textContent = cancelText;
      overlay.querySelector('.ui-dialog-yes').textContent = okText;

      const finish = (result) => {
        closeOverlay(overlay);
        document.removeEventListener('keydown', onKey);
        resolve(result);
      };
      const onKey = (e) => {
        if (e.key === 'Escape') {
          e.preventDefault();
          finish(false);
        }
      };
      overlay.querySelector('.ui-dialog-no').addEventListener('click', () => finish(false));
      overlay.querySelector('.ui-dialog-yes').addEventListener('click', () => finish(true));
      overlay.addEventListener('click', (e) => {
        if (e.target === overlay) finish(false);
      });
      document.addEventListener('keydown', onKey);
      root.appendChild(overlay);
      requestAnimationFrame(() => overlay.classList.add('open'));
      overlay.querySelector('.ui-dialog-no').focus();
    });
  }

  function showToast(message, type, duration) {
    type = type || 'info';
    duration = duration == null ? 4000 : duration;
    if (!message) return;

    const container = ensureToastContainer();
    const toast = document.createElement('div');
    toast.className = 'ui-toast ' + type;
    toast.innerHTML =
      '<div class="ui-toast-msg"></div>' +
      '<button type="button" class="ui-toast-close" aria-label="Закрыть">×</button>';
    toast.querySelector('.ui-toast-msg').textContent = String(message);

    const remove = () => {
      toast.classList.remove('show');
      toast.classList.add('hide');
      setTimeout(() => toast.remove(), 250);
    };
    toast.querySelector('.ui-toast-close').addEventListener('click', remove);
    container.appendChild(toast);
    requestAnimationFrame(() => toast.classList.add('show'));
    if (duration > 0) setTimeout(remove, duration);
  }

  // Public API
  global.showAlert = showAlert;
  global.showConfirm = showConfirm;
  global.showToast = showToast;
  global.__uiShowToast = showToast;

  global.alert = function (msg) {
    showAlert(msg == null ? '' : String(msg), { type: 'info', title: 'Сообщение' });
  };

  global.confirm = function (msg) {
    console.warn('[ui-dialogs] Use await showConfirm(...) instead of confirm()');
    showConfirm(msg == null ? '' : String(msg), { title: 'Подтвердите действие' });
    return false;
  };
})(typeof window !== 'undefined' ? window : globalThis);
