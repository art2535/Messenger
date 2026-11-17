(function () {
  'use strict';

  const API = () => (typeof API_BASE !== 'undefined' ? API_BASE : '/api/v1');

  let scheduledCache = [];
  let sheetEl = null;
  let panelEl = null;
  let selectedDate = null;
  let selectedHour = 12;
  let selectedMinute = 0;
  let bound = false;

  function $(id) {
    return document.getElementById(id);
  }

  function messageInputEl() {
    return (
      (typeof messageInput !== 'undefined' && messageInput) ||
      $('message-input') ||
      document.querySelector('#message-input')
    );
  }

  function sendButtonEl() {
    return (
      (typeof sendButton !== 'undefined' && sendButton) ||
      $('send-button') ||
      document.querySelector('#send-button')
    );
  }

  function pad(n) {
    return String(n).padStart(2, '0');
  }

  function escapeHtml(s) {
    return String(s || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function formatScheduledLabel(d) {
    const now = new Date();
    const isToday =
      d.getFullYear() === now.getFullYear() &&
      d.getMonth() === now.getMonth() &&
      d.getDate() === now.getDate();
    const tomorrow = new Date(now);
    tomorrow.setDate(tomorrow.getDate() + 1);
    const isTomorrow =
      d.getFullYear() === tomorrow.getFullYear() &&
      d.getMonth() === tomorrow.getMonth() &&
      d.getDate() === tomorrow.getDate();

    const time = `${pad(d.getHours())}:${pad(d.getMinutes())}`;
    if (isToday) return `Сегодня в ${time}`;
    if (isTomorrow) return `Завтра в ${time}`;
    const days = ['Вс', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];
    const months = ['янв', 'фев', 'мар', 'апр', 'мая', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'];
    return `${days[d.getDay()]}, ${d.getDate()} ${months[d.getMonth()]} в ${time}`;
  }

  function buildDateOptions(count = 14) {
    const opts = [];
    const now = new Date();
    for (let i = 0; i < count; i++) {
      opts.push(new Date(now.getFullYear(), now.getMonth(), now.getDate() + i, 0, 0, 0, 0));
    }
    return opts;
  }

  function ensureSheet() {
    if (sheetEl) return sheetEl;
    sheetEl = document.createElement('div');
    sheetEl.className = 'schedule-send-sheet';
    sheetEl.innerHTML = `
      <div class="schedule-send-card" role="dialog" aria-label="Запланировать сообщение">
        <div class="schedule-send-header">
          <button type="button" class="cancel-btn" data-action="close">Отмена</button>
          <h3>Отложить сообщение</h3>
          <span style="width:56px"></span>
        </div>
        <div class="schedule-wheel">
          <div class="schedule-wheel-highlight"></div>
          <div class="schedule-wheel-col" data-col="date"></div>
          <div class="schedule-wheel-col" data-col="hour"></div>
          <div class="schedule-wheel-col" data-col="minute"></div>
        </div>
        <button type="button" class="schedule-primary-btn" data-action="confirm">Отправить …</button>
        <button type="button" class="schedule-secondary-btn" data-action="close">Отмена</button>
      </div>
    `;
    document.body.appendChild(sheetEl);

    sheetEl.addEventListener('click', (e) => {
      if (e.target === sheetEl) closeSheet();
      const btn = e.target.closest('[data-action]');
      if (!btn) return;
      const action = btn.getAttribute('data-action');
      if (action === 'close') closeSheet();
      if (action === 'confirm') confirmSchedule();
    });

    return sheetEl;
  }

  function fillWheel() {
    const card = ensureSheet();
    const dateCol = card.querySelector('[data-col="date"]');
    const hourCol = card.querySelector('[data-col="hour"]');
    const minuteCol = card.querySelector('[data-col="minute"]');

    const dates = buildDateOptions(14);
    if (!selectedDate) {
      selectedDate = new Date(dates[0]);
      const now = new Date();
      selectedHour = now.getHours();
      selectedMinute = Math.ceil((now.getMinutes() + 5) / 5) * 5;
      if (selectedMinute >= 60) {
        selectedMinute = 0;
        selectedHour = (selectedHour + 1) % 24;
      }
    }

    dateCol.innerHTML = dates
      .map((d, i) => {
        const label =
          i === 0
            ? 'Сегодня'
            : i === 1
              ? 'Завтра'
              : (() => {
                  const days = ['Вс', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];
                  const months = ['янв', 'фев', 'мар', 'апр', 'мая', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'];
                  return `${days[d.getDay()]} ${d.getDate()} ${months[d.getMonth()]}`;
                })();
        const active =
          d.getFullYear() === selectedDate.getFullYear() &&
          d.getMonth() === selectedDate.getMonth() &&
          d.getDate() === selectedDate.getDate();
        return `<div class="schedule-wheel-item${active ? ' active' : ''}" data-date="${d.toISOString()}">${label}</div>`;
      })
      .join('');

    hourCol.innerHTML = Array.from({ length: 24 }, (_, h) => {
      return `<div class="schedule-wheel-item${h === selectedHour ? ' active' : ''}" data-hour="${h}">${pad(h)}</div>`;
    }).join('');

    minuteCol.innerHTML = Array.from({ length: 12 }, (_, i) => {
      const m = i * 5;
      return `<div class="schedule-wheel-item${m === selectedMinute ? ' active' : ''}" data-minute="${m}">${pad(m)}</div>`;
    }).join('');

    const bindCol = (col, attr, onPick) => {
      col.querySelectorAll('.schedule-wheel-item').forEach((el) => {
        el.addEventListener('click', () => {
          col.querySelectorAll('.schedule-wheel-item').forEach((x) => x.classList.remove('active'));
          el.classList.add('active');
          onPick(el.getAttribute(attr));
          updateConfirmLabel();
          el.scrollIntoView({ block: 'center', behavior: 'smooth' });
        });
      });
      const active = col.querySelector('.schedule-wheel-item.active');
      if (active) active.scrollIntoView({ block: 'center' });
    };

    bindCol(dateCol, 'data-date', (v) => {
      selectedDate = new Date(v);
    });
    bindCol(hourCol, 'data-hour', (v) => {
      selectedHour = parseInt(v, 10);
    });
    bindCol(minuteCol, 'data-minute', (v) => {
      selectedMinute = parseInt(v, 10);
    });

    updateConfirmLabel();
  }

  function getSelectedDateTime() {
    const d = new Date(selectedDate);
    d.setHours(selectedHour, selectedMinute, 0, 0);
    return d;
  }

  function updateConfirmLabel() {
    const btn = ensureSheet().querySelector('[data-action="confirm"]');
    if (!btn) return;
    const d = getSelectedDateTime();
    btn.textContent = `Отправить ${formatScheduledLabel(d).toLowerCase()}`;
  }

  function openSheet() {
    const input = messageInputEl();
    const files =
      typeof selectedFiles !== 'undefined' && Array.isArray(selectedFiles) ? selectedFiles : [];
    if ((!input || !input.value.trim()) && files.length === 0) {
      if (typeof showToast === 'function') {
        showToast('Сначала напишите сообщение или прикрепите файл', 'warning');
      }
      return;
    }
    ensureSheet();
    fillWheel();
    sheetEl.classList.add('visible');
    if (typeof feather !== 'undefined') try { feather.replace(); } catch (_) {}
  }

  function closeSheet() {
    if (sheetEl) sheetEl.classList.remove('visible');
  }

  async function confirmSchedule() {
    const d = getSelectedDateTime();
    if (d.getTime() <= Date.now() + 25000) {
      if (typeof showToast === 'function') showToast('Выберите время хотя бы на 30 секунд вперёд', 'warning');
      return;
    }
    if (typeof currentChatId === 'undefined' || !currentChatId) {
      if (typeof showToast === 'function') showToast('Чат не выбран', 'error');
      return;
    }

    const input = messageInputEl();
    let text = input ? input.value.trim() : '';
    const files =
      typeof selectedFiles !== 'undefined' && Array.isArray(selectedFiles) ? selectedFiles.slice() : [];

    if (!text && files.length === 0) {
      if (typeof showToast === 'function') showToast('Напишите сообщение или прикрепите файл', 'warning');
      return;
    }

    if (typeof replyingTo !== 'undefined' && replyingTo && typeof buildReplyPayload === 'function') {
      text = buildReplyPayload(replyingTo, text);
    }

    const btn = ensureSheet().querySelector('[data-action="confirm"]');
    if (btn) btn.disabled = true;

    try {
      // Если перепланируем — сначала отменим старое
      if (window.__rescheduleId) {
        try {
          await fetchWithAuth(`${API()}/scheduled-messages/${window.__rescheduleId}`, { method: 'DELETE' });
        } catch (_) {}
        window.__rescheduleId = null;
      }

      const formData = new FormData();
      if (text) formData.append('messageText', text);
      formData.append('scheduledAt', d.toISOString());
      if (typeof replyingTo !== 'undefined' && replyingTo) {
        const rid = replyingTo.messageId || replyingTo.MessageId || replyingTo.id;
        if (rid) formData.append('replyToMessageId', String(rid));
      }
      files.forEach((f) => formData.append('files', f));

      const res = await fetchWithAuth(`${API()}/scheduled-messages/${currentChatId}`, {
        method: 'POST',
        body: formData
      });

      if (!res || !res.ok) {
        const err = await (res ? res.json().catch(() => ({})) : Promise.resolve({}));
        throw new Error(err.error || err.Error || `HTTP ${res ? res.status : 'no response'}`);
      }

      const json = await res.json();
      const dto = json.data || json.Data;
      if (dto) {
        const id = String(dto.scheduledMessageId || dto.ScheduledMessageId);
        scheduledCache = scheduledCache.filter(
          (x) => String(x.scheduledMessageId || x.ScheduledMessageId) !== id
        );
        scheduledCache.push(dto);
        scheduledCache.sort(
          (a, b) =>
            new Date(a.scheduledAt || a.ScheduledAt) - new Date(b.scheduledAt || b.ScheduledAt)
        );
      }

      if (input) input.value = '';
      if (typeof selectedFiles !== 'undefined') selectedFiles.length = 0;
      if (typeof updateSelectedFilesUI === 'function') updateSelectedFilesUI();
      if (typeof cancelReply === 'function') cancelReply();
      else if (typeof replyingTo !== 'undefined') window.replyingTo = null;
      if (typeof autoResizeTextarea === 'function') autoResizeTextarea();

      closeSheet();
      updateBadge();
      if (typeof showToast === 'function') {
        showToast(`Сообщение запланировано на ${formatScheduledLabel(d)}`, 'success');
      }
    } catch (e) {
      console.error('[schedule]', e);
      if (typeof showToast === 'function') showToast(e.message || 'Не удалось запланировать', 'error');
    } finally {
      if (btn) btn.disabled = false;
    }
  }

  function ensurePanel() {
    if (panelEl) return panelEl;
    panelEl = document.createElement('div');
    panelEl.className = 'scheduled-panel';
    panelEl.innerHTML = `
      <div class="scheduled-panel-card">
        <div class="scheduled-panel-header">
          <h3>Запланированные</h3>
          <button type="button" class="scheduled-panel-close" aria-label="Закрыть">&times;</button>
        </div>
        <div class="scheduled-list"></div>
      </div>
    `;
    document.body.appendChild(panelEl);
    panelEl.addEventListener('click', (e) => {
      if (e.target === panelEl || e.target.classList.contains('scheduled-panel-close')) {
        panelEl.classList.remove('visible');
      }
    });
    return panelEl;
  }

  function renderPanel() {
    const list = ensurePanel().querySelector('.scheduled-list');
    const items = scheduledCache.filter(
      (x) => (x.status || x.Status || 'Pending') === 'Pending'
    );
    if (!items.length) {
      list.innerHTML = `<div class="scheduled-empty">Нет запланированных сообщений</div>`;
      return;
    }
    list.innerHTML = items
      .map((item) => {
        const id = item.scheduledMessageId || item.ScheduledMessageId;
        const at = new Date(item.scheduledAt || item.ScheduledAt);
        const text =
          item.messageText ||
          item.MessageText ||
          (item.hasAttachments || item.HasAttachments ? '📎 Вложение' : '');
        const chatName = item.chatName || item.ChatName || '';
        return `
          <div class="scheduled-item" data-id="${id}">
            <div class="scheduled-item-body">
              <div class="scheduled-item-time">${formatScheduledLabel(at)}</div>
              ${chatName ? `<div class="scheduled-item-chat">${escapeHtml(chatName)}</div>` : ''}
              <div class="scheduled-item-text">${escapeHtml(text)}</div>
            </div>
            <div class="scheduled-item-actions">
              <button type="button" data-act="edit" data-id="${id}">Изменить</button>
              <button type="button" class="danger" data-act="cancel" data-id="${id}">Отменить</button>
            </div>
          </div>`;
      })
      .join('');

    list.querySelectorAll('[data-act="cancel"]').forEach((btn) => {
      btn.addEventListener('click', async () => {
        await cancelScheduled(btn.getAttribute('data-id'));
      });
    });
    list.querySelectorAll('[data-act="edit"]').forEach((btn) => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-id');
        const item = scheduledCache.find(
          (x) => String(x.scheduledMessageId || x.ScheduledMessageId) === String(id)
        );
        if (!item) return;
        const at = new Date(item.scheduledAt || item.ScheduledAt);
        selectedDate = new Date(at.getFullYear(), at.getMonth(), at.getDate());
        selectedHour = at.getHours();
        selectedMinute = Math.floor(at.getMinutes() / 5) * 5;
        panelEl.classList.remove('visible');
        const input = messageInputEl();
        if (input) input.value = item.messageText || item.MessageText || '';
        window.__rescheduleId = id;
        openSheet();
      });
    });
  }

  async function cancelScheduled(id) {
    try {
      const res = await fetchWithAuth(`${API()}/scheduled-messages/${id}`, { method: 'DELETE' });
      if (!res || !res.ok) {
        const err = await (res ? res.json().catch(() => ({})) : Promise.resolve({}));
        throw new Error(err.error || err.Error || 'Ошибка');
      }
      scheduledCache = scheduledCache.filter(
        (x) => String(x.scheduledMessageId || x.ScheduledMessageId) !== String(id)
      );
      updateBadge();
      renderPanel();
      if (typeof showToast === 'function') showToast('Отправка отменена', 'success');
    } catch (e) {
      if (typeof showToast === 'function') showToast(e.message || 'Не удалось отменить', 'error');
    }
  }

  function updateBadge() {
    const count = scheduledCache.filter((x) => (x.status || x.Status || 'Pending') === 'Pending').length;
    const badge = $('scheduled-messages-badge');
    if (!badge) return;
    const countEl = badge.querySelector('.count');
    if (countEl) countEl.textContent = String(count);
    if (count > 0) {
      badge.style.display = 'inline-flex';
      badge.classList.add('is-visible');
    } else {
      badge.style.display = 'none';
      badge.classList.remove('is-visible');
    }
    if (typeof feather !== 'undefined') try { feather.replace(); } catch (_) {}
  }

  async function loadScheduled() {
    try {
      if (typeof fetchWithAuth !== 'function') return;
      const res = await fetchWithAuth(`${API()}/scheduled-messages`);
      if (!res || !res.ok) return;
      const json = await res.json();
      const list = json.data || json.Data || [];
      scheduledCache = Array.isArray(list) ? list : [];
      updateBadge();
    } catch (e) {
      console.warn('[scheduled] load failed', e);
    }
  }

  function bindLongPressSend() {
    const btn = sendButtonEl();
    if (!btn || btn.dataset.scheduleBound === '1') return;
    btn.dataset.scheduleBound = '1';

    let timer = null;
    let longPressed = false;

    const start = () => {
      longPressed = false;
      timer = setTimeout(() => {
        longPressed = true;
        openSheet();
        if (navigator.vibrate) try { navigator.vibrate(20); } catch (_) {}
      }, 450);
    };
    const end = (e) => {
      clearTimeout(timer);
      if (longPressed) {
        e.preventDefault();
        e.stopPropagation();
      }
    };
    const cancel = () => clearTimeout(timer);

    btn.addEventListener('mousedown', start);
    btn.addEventListener('touchstart', start, { passive: true });
    btn.addEventListener('mouseup', end);
    btn.addEventListener('mouseleave', cancel);
    btn.addEventListener('touchend', end);
    btn.addEventListener('touchcancel', cancel);
    btn.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      openSheet();
    });
  }

  function bindUiButtons() {
    const scheduleBtn = $('schedule-message-btn');
    if (scheduleBtn && scheduleBtn.dataset.bound !== '1') {
      scheduleBtn.dataset.bound = '1';
      scheduleBtn.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        openSheet();
      });
    }

    const badge = $('scheduled-messages-badge');
    if (badge && badge.dataset.bound !== '1') {
      badge.dataset.bound = '1';
      badge.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        renderPanel();
        ensurePanel().classList.add('visible');
      });
    }

    bindLongPressSend();
  }

  function onSignalR(connection) {
    if (!connection || typeof connection.on !== 'function') return;
    const upsert = (dto) => {
      if (!dto) return;
      const id = String(dto.scheduledMessageId || dto.ScheduledMessageId);
      scheduledCache = scheduledCache.filter(
        (x) => String(x.scheduledMessageId || x.ScheduledMessageId) !== id
      );
      if ((dto.status || dto.Status || 'Pending') === 'Pending') {
        scheduledCache.push(dto);
        scheduledCache.sort(
          (a, b) =>
            new Date(a.scheduledAt || a.ScheduledAt) - new Date(b.scheduledAt || b.ScheduledAt)
        );
      }
      updateBadge();
      if (panelEl && panelEl.classList.contains('visible')) renderPanel();
    };

    connection.on('ScheduledMessageCreated', upsert);
    connection.on('ScheduledMessageUpdated', upsert);
    connection.on('ScheduledMessageCancelled', (payload) => {
      const id = String(payload?.scheduledMessageId || payload?.ScheduledMessageId || '');
      if (!id) return;
      scheduledCache = scheduledCache.filter(
        (x) => String(x.scheduledMessageId || x.ScheduledMessageId) !== id
      );
      updateBadge();
      if (panelEl && panelEl.classList.contains('visible')) renderPanel();
    });
    connection.on('ScheduledMessageSent', (payload) => {
      const id = String(payload?.scheduledMessageId || payload?.ScheduledMessageId || '');
      if (id) {
        scheduledCache = scheduledCache.filter(
          (x) => String(x.scheduledMessageId || x.ScheduledMessageId) !== id
        );
        updateBadge();
        if (panelEl && panelEl.classList.contains('visible')) renderPanel();
      }
      if (typeof showToast === 'function') {
        showToast('Запланированное сообщение отправлено', 'success');
      }
    });
  }

  // Авто-подписка, если connection уже есть
  function tryHookExistingConnection() {
    try {
      if (typeof connection !== 'undefined' && connection) onSignalR(connection);
      if (window.chatConnection) onSignalR(window.chatConnection);
      if (window.signalRConnection) onSignalR(window.signalRConnection);
    } catch (_) {}
  }

  function init() {
    bindUiButtons();
    loadScheduled();
    tryHookExistingConnection();
    if (!bound) {
      bound = true;
      // UI чата может пересобираться — периодически довешиваем обработчики
      setInterval(bindUiButtons, 2500);
    }
    if (typeof feather !== 'undefined') try { feather.replace(); } catch (_) {}
  }

  window.ScheduledMessages = {
    init,
    openSheet,
    openPanel() {
      renderPanel();
      ensurePanel().classList.add('visible');
    },
    onSignalR,
    reload: loadScheduled
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    // Страница уже загружена (скрипт в конце body)
    setTimeout(init, 0);
  }
})();
