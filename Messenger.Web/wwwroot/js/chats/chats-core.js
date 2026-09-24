feather.replace();

const API_BASE = window.API_BASE_URL;

let myBlockedUsers = new Set();
let allChats = [];
let unreadCounts = new Map();

let messagesHasMore = false;
let oldestSequence = null;
let isLoadingOlder = false;
let historyScrollObserver = null;

let lastTypingSentAt = 0;
const TYPING_THROTTLE_MS = 2500;
console.log("Access Token:", token.substring(0, 20) + "...");

const chatsList = document.getElementById('chats-list');
const messagesContainer = document.getElementById('messages-container');
const messageInput = document.getElementById('message-input');
const sendButton = document.getElementById('send-button');
const fileInput = document.getElementById('file-input');
const messageContextMenu = document.getElementById('message-context-menu');
const editModeBar = document.getElementById('edit-mode-bar');
let editingMessageId = null;
let modalChatId = null;

window.addEventListener('resize', () => { if (typeof updateChatSubtitleMarquee === 'function') updateChatSubtitleMarquee(); });
function updateChatSubtitleMarquee() {
    const el = document.getElementById('chat-subtitle');
    const area = document.getElementById('chat-status-area');
    if (!el || !area) return;
    el.classList.remove('is-marquee');
    el.style.removeProperty('--marquee-shift');
    el.style.transform = '';
    if (window.innerWidth >= 768) return;
    requestAnimationFrame(() => {
        const overflow = el.scrollWidth - area.clientWidth;
        if (overflow > 4) {
            const sec = Math.min(12, Math.max(4, overflow / 25));
            el.style.animationDuration = sec + 's';
            el.style.setProperty('--marquee-shift', `-${overflow}px`);
            el.classList.add('is-marquee');
        }
    });
}

let selectMode = false;
const selectedMessageIds = new Set();

function enterSelectMode(initialMessageId) {
    if (!currentChatId) return;
    selectMode = true;
    selectedMessageIds.clear();
    const container = document.getElementById('messages-container');
    container?.classList.add('select-mode');
    document.body.classList.add('select-mode-active');
    document.getElementById('selection-toolbar')?.classList.add('show');
    hideMessageContextMenu();
    hideChatContextMenu();
    if (initialMessageId && !String(initialMessageId).startsWith('temp-')) {
        const row = document.querySelector(`[data-mid="${CSS.escape(String(initialMessageId))}"]`);
        if (row && isOwnMessageRow(row)) {
            toggleMessageSelection(initialMessageId, true);
        } else {
            updateSelectionToolbar();
            if (row && !isOwnMessageRow(row)) {
                showToast('Можно удалять только свои сообщения — выберите свои', 'info');
            }
        }
    } else {
        updateSelectionToolbar();
    }
    if (typeof feather !== 'undefined') feather.replace();
}

function exitSelectMode() {
    selectMode = false;
    selectedMessageIds.clear();
    const container = document.getElementById('messages-container');
    container?.classList.remove('select-mode');
    container?.querySelectorAll('.message-row.selected').forEach(el => el.classList.remove('selected'));
    document.body.classList.remove('select-mode-active');
    document.getElementById('selection-toolbar')?.classList.remove('show');
    updateSelectionToolbar();
}

function isOwnMessageRow(row) {
    if (!row) return false;
    if (row.classList.contains('outgoing-row')) return true;
    if (row.querySelector('.message-bubble.outgoing')) return true;
    return false;
}

function toggleMessageSelection(messageId, forceOn) {
    if (!messageId || String(messageId).startsWith('temp-')) return;
    const id = String(messageId);
    const row = document.querySelector(`[data-mid="${CSS.escape(id)}"]`);
    if (!row) return;
    if (!isOwnMessageRow(row)) {
        showToast('Можно удалять только свои сообщения', 'warning');
        return;
    }

    const isSelected = selectedMessageIds.has(id);
    if (forceOn === true || !isSelected) {
        selectedMessageIds.add(id);
        row.classList.add('selected');
    } else {
        selectedMessageIds.delete(id);
        row.classList.remove('selected');
    }
    updateSelectionToolbar();
}

function updateSelectionToolbar() {
    const label = document.getElementById('sel-count-label');
    const delBtn = document.getElementById('sel-delete-btn');
    const n = selectedMessageIds.size;
    if (label) label.textContent = n === 0 ? 'Выберите сообщения' : (n === 1 ? 'Выбрано: 1' : `Выбрано: ${n}`);
    if (delBtn) delBtn.disabled = n === 0;
}

async function deleteSelectedMessages() {
    if (!currentChatId || selectedMessageIds.size === 0) return;
    const ids = [...selectedMessageIds];
    const count = ids.length;
    const ok = await showConfirm(
        count === 1 ? 'Удалить выбранное сообщение?' : `Удалить выбранные сообщения (${count})?`,
        { title: 'Удаление сообщений', okText: 'Удалить', cancelText: 'Отмена', danger: true }
    );
    if (!ok) return;

    const delBtn = document.getElementById('sel-delete-btn');
    if (delBtn) delBtn.disabled = true;
    showToast('Удаление…', 'info');

    try {
        const res = await fetchWithAuth(`${API_BASE}/messages/bulk-delete`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                chatId: currentChatId,
                messageIds: ids
            })
        });

        if (!res) throw new Error('Нет ответа');
        if (!res.ok) {
            if (res.status === 404 || res.status === 405) {
                let ok = 0;
                for (const mid of ids) {
                    try {
                        const r = await fetchWithAuth(`${API_BASE}/messages/${mid}?chatId=${currentChatId}`, {
                            method: 'DELETE',
                            headers: { 'Authorization': `Bearer ${token}` }
                        });
                        if (r && (r.ok || r.status === 204)) {
                            removeMessageFromUI(mid, currentChatId);
                            ok++;
                        }
                    } catch (_) {}
                }
                exitSelectMode();
                showToast(ok ? `Удалено: ${ok}` : 'Не удалось удалить', ok ? 'success' : 'error');
                return;
            }
            const err = await res.json().catch(() => ({}));
            throw new Error(err.error || err.Error || `HTTP ${res.status}`);
        }

        const data = await res.json().catch(() => ({}));
        const deletedIds = data.messageIds || data.MessageIds || ids;
        (deletedIds || []).forEach(mid => removeMessageFromUI(mid, currentChatId));
        exitSelectMode();
        const n = data.deletedCount ?? data.DeletedCount ?? deletedIds.length;
        showToast(n === 1 ? 'Сообщение удалено' : `Удалено сообщений: ${n}`, 'success');
    } catch (err) {
        console.error('Bulk delete error:', err);
        showToast(err.message || 'Ошибка удаления', 'error');
        if (delBtn) delBtn.disabled = selectedMessageIds.size === 0;
    }
}


        let contextMenuMessageId = null;
let contextMenuMessageText = '';

const REPLY_PREFIX = '\u200BREPLY:';
const REPLY_SUFFIX = '\u200B\n';
let replyingTo = null;

const MESSAGE_DRAFTS_KEY = 'guap_message_drafts_v1_' + encodeURIComponent(String(me || 'anonymous'));
let draftsCache = null;
let draftSaveTimer = null;

function getDrafts() {
    if (draftsCache) return draftsCache;
    try {
        const raw = localStorage.getItem(MESSAGE_DRAFTS_KEY);
        const parsed = raw ? JSON.parse(raw) : {};
        draftsCache = parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : {};
    } catch (e) {
        console.warn('[Drafts] Не удалось прочитать черновики', e);
        draftsCache = {};
    }
    return draftsCache;
}

function saveDrafts() {
    try {
        localStorage.setItem(MESSAGE_DRAFTS_KEY, JSON.stringify(getDrafts()));
    } catch (e) {
        console.warn('[Drafts] Не удалось сохранить черновики', e);
    }
}

function getChatDraft(chatId) {
    if (!chatId) return null;
    const draft = getDrafts()[String(chatId)];
    if (!draft || typeof draft !== 'object') return null;

    const text = typeof draft.text === 'string' ? draft.text : '';
    if (!text.trim()) return null;

    return {
        text,
        reply: draft.reply && draft.reply.messageId
            ? {
                messageId: String(draft.reply.messageId),
                senderName: String(draft.reply.senderName || 'Сообщение'),
                preview: String(draft.reply.preview || '')
            }
            : null,
        updatedAt: draft.updatedAt || null
    };
}

function escapeDraftHtml(value) {
    return String(value || '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function getDraftPreviewText(draft) {
    const text = stripReplyForPreview(draft?.text || '')
        .replace(/\s+/g, ' ')
        .trim();
    if (!text) return 'Сообщение';
    return text.length > 38 ? text.slice(0, 35) + '...' : text;
}

function renderDraftPreviewHTML(draft) {
    return `
        <div class="flex items-center min-w-0 gap-1 last-message-preview">
            <span class="draft-preview-label flex-shrink-0">Черновик:</span>
            <span class="truncate">${escapeDraftHtml(getDraftPreviewText(draft))}</span>
        </div>`;
}

function renderChatListDraft(chatId) {
    if (!chatId) return false;

    const chatItem = document.querySelector(`.chat-item[data-chat-id="${chatId}"]`);
    if (!chatItem) return false;

    const draft = getChatDraft(chatId);
    const previewEl = chatItem.querySelector('.last-message-preview');

    if (!draft) {
        return false;
    }

    const html = renderDraftPreviewHTML(draft);
    if (previewEl) {
        previewEl.outerHTML = html;
    } else {
        const metaWrap = chatItem.querySelector('.chat-list-message-meta') ||
                            chatItem.querySelector('.text-sm.text-gray-500');
        if (metaWrap) metaWrap.innerHTML = html;
    }

    return true;
}

function saveDraftForChat(chatId, text, reply = null) {
    if (!chatId) return;

    const id = String(chatId);
    const value = String(text || '');
    const drafts = getDrafts();

    if (!value.trim()) {
        delete drafts[id];
        saveDrafts();
        renderChatListDraft(id);
        refreshChatListPreviewFromDom(id);
        return;
    }

    drafts[id] = {
        text: value,
        reply: reply && reply.messageId
            ? {
                messageId: String(reply.messageId),
                senderName: String(reply.senderName || 'Сообщение'),
                preview: String(reply.preview || '').replace(/\s+/g, ' ').slice(0, 120)
            }
            : null,
        updatedAt: new Date().toISOString()
    };

    saveDrafts();
    renderChatListDraft(id);
}

function saveCurrentDraft() {
    if (!currentChatId || editingMessageId) return;

    clearTimeout(draftSaveTimer);
    draftSaveTimer = null;

    saveDraftForChat(
        currentChatId,
        messageInput?.value || '',
        replyingTo
    );
}

function scheduleCurrentDraftSave() {
    if (!currentChatId || editingMessageId) return;

    clearTimeout(draftSaveTimer);
    draftSaveTimer = setTimeout(() => {
        draftSaveTimer = null;
        saveCurrentDraft();
    }, 1000);
}

function clearChatDraft(chatId) {
    if (!chatId) return;
    const id = String(chatId);
    const drafts = getDrafts();

    if (!Object.prototype.hasOwnProperty.call(drafts, id)) {
        return;
    }

    delete drafts[id];
    saveDrafts();
    renderChatListDraft(id);
    refreshChatListPreviewFromDom(id);
}

function restoreDraftForChat(chatId) {
    if (!chatId || !messageInput) return;

    const draft = getChatDraft(chatId);
    if (!draft) {
        cancelReply();
        return;
    }

    messageInput.value = draft.text || '';

    if (draft.reply?.messageId) {
        replyingTo = {
            messageId: draft.reply.messageId,
            senderName: draft.reply.senderName || 'Сообщение',
            preview: draft.reply.preview || 'Сообщение'
        };

        const bar = document.getElementById('reply-compose-bar');
        const title = document.getElementById('reply-compose-title');
        const prev = document.getElementById('reply-compose-preview');

        if (title) title.textContent = 'Ответ: ' + replyingTo.senderName;
        if (prev) prev.textContent = replyingTo.preview || 'Сообщение';
        if (bar) bar.classList.add('show');
    } else {
        cancelReply();
    }
}

function renderAllDraftPreviews() {
    document.querySelectorAll('.chat-item[data-chat-id]').forEach(item => {
        renderChatListDraft(item.dataset.chatId);
    });
}


function buildReplyPayload(reply, bodyText) {
    if (!reply || !reply.messageId) return bodyText || '';
    const name = (reply.senderName || '').replace(/\|/g, ' ');
    const preview = (reply.preview || '').replace(/\|/g, ' ').replace(/\n/g, ' ').slice(0, 120);
    return REPLY_PREFIX + reply.messageId + '|' + name + '|' + preview + REPLY_SUFFIX + (bodyText || '');
}

function parseReplyPayload(raw) {
    const s = String(raw || '');
    if (!s.startsWith(REPLY_PREFIX)) return { text: s, reply: null };
    const rest = s.slice(REPLY_PREFIX.length);
    const end = rest.indexOf(REPLY_SUFFIX);
    let header, body;
    if (end < 0) {
        const nl = rest.indexOf('\n');
        if (nl < 0) return { text: s, reply: null };
        header = rest.slice(0, nl);
        body = rest.slice(nl + 1);
    } else {
        header = rest.slice(0, end);
        body = rest.slice(end + REPLY_SUFFIX.length);
    }
    const parts = header.split('|');
    return { text: body, reply: { messageId: parts[0] || '', senderName: parts[1] || '', preview: parts[2] || '' } };
}

function stripReplyForPreview(raw) {
    return parseReplyPayload(raw).text || '';
}

function startReply(messageId, previewText, senderName) {
    if (typeof exitEditMode === 'function' && editingMessageId) exitEditMode();
    replyingTo = {
        messageId: String(messageId),
        senderName: senderName || 'Сообщение',
        preview: (previewText || '').replace(/\s+/g, ' ').slice(0, 120)
    };
    const bar = document.getElementById('reply-compose-bar');
    const title = document.getElementById('reply-compose-title');
    const prev = document.getElementById('reply-compose-preview');
    if (title) title.textContent = 'Ответ: ' + replyingTo.senderName;
    if (prev) prev.textContent = replyingTo.preview || 'Сообщение';
    if (bar) bar.classList.add('show');
    if (messageInput) messageInput.focus();
}

function cancelReply() {
    replyingTo = null;
    const bar = document.getElementById('reply-compose-bar');
    if (bar) bar.classList.remove('show');
}

function scrollToMessageId(messageId) {
    if (!messageId) return;
    const row = document.querySelector('[data-mid="' + messageId + '"]');
    if (row) {
        row.scrollIntoView({ behavior: 'smooth', block: 'center' });
        row.classList.add('ring-2', 'ring-blue-400');
        setTimeout(function () { row.classList.remove('ring-2', 'ring-blue-400'); }, 1500);
    } else if (typeof showToast === 'function') {
        showToast('Сообщение не в текущей ленте', 'info');
    }
}
window.scrollToMessageId = scrollToMessageId;

function getMessagePreviewFromRow(row) {
    if (!row) return '';
    const p = row.querySelector('.message-bubble p');
    return (p && (p.innerText || p.textContent) || '').trim();
}

function getSenderNameFromRow(row) {
    if (!row) return 'Сообщение';
    if (row.querySelector('.message-bubble.outgoing')) return 'Вы';
    const n = row.querySelector('.sender-name');
    if (n && n.textContent && n.textContent.indexOf('Загрузка') < 0) return n.textContent.trim();
    const title = document.getElementById('chat-title');
    return (title && title.textContent) || 'Собеседник';
}

function fillReplyQuoteInRow(div, replyMeta) {
    if (!div || !replyMeta) return;
    div.dataset.replyTo = replyMeta.messageId || '';
    if (replyMeta.senderName) div.dataset.replyName = replyMeta.senderName;
    if (replyMeta.preview) div.dataset.replyPreview = replyMeta.preview;
    const q = div.querySelector('.reply-quote');
    if (!q) return;
    const n = q.querySelector('.reply-quote-name');
    const t = q.querySelector('.reply-quote-text');
    if (n) n.textContent = replyMeta.senderName || 'Сообщение';
    if (t) t.textContent = replyMeta.preview || '';
    q.addEventListener('click', function (e) {
        e.stopPropagation();
        scrollToMessageId(replyMeta.messageId);
    });
}

const DEFAULT_MSG_AVATAR = 'https://novgorodskij-r49.gosweb.gosuslugi.ru/netcat_files/9/260/user_test.png';

function getSenderAvatarUrl(senderId) {
    if (!senderId) return DEFAULT_MSG_AVATAR;
    const sid = String(senderId).trim().toLowerCase();
    const myId = String(me || '').trim().toLowerCase();
    if (sid && sid === myId) {
        const mine = document.getElementById('current-user-avatar');
        if (mine?.src) return mine.src;
    }
    const parts = currentChatInfo?.participants || currentChatInfo?.Participants || [];
    for (const p of parts) {
        const pid = String(p.id || p.userId || p.UserId || p.Id || '').trim().toLowerCase();
        if (pid && pid === sid) {
            const av = p.avatar || p.Avatar || p.avatarPath || p.AvatarPath || p.avatarUrl || p.AvatarUrl;
            if (av) return av;
        }
    }
    if (currentChatId) {
        const item = document.querySelector(`.chat-item[data-chat-id="${currentChatId}"]`);
        const img = item?.querySelector('img');
        if (img?.src) return img.src;
    }
    const header = document.getElementById('chat-info-avatar');
    if (header?.src) return header.src;
    return DEFAULT_MSG_AVATAR;
}

function isSameSenderAsPrevious(senderId) {
    const rows = messagesContainer.querySelectorAll('[data-mid]');
    if (!rows.length) return false;
    const last = rows[rows.length - 1];
    return String(last.dataset.senderId || '') === String(senderId || '');
}


const attachedFiles = document.getElementById('attached-files');
const lightbox = document.getElementById('lightbox');
const lightboxImg = document.getElementById('lightbox-img');

let pendingMessages = new Map();
let currentChatId = null;
let userStatuses = new Map();
let selectedFiles = [];
let connection = null;
let isSending = false;
let selectedUserIds = [];
let lastQuery = '';
let searchTimeout = null;
let allChatItems = [];
let hasBeenKickedNotificationShown = false;

const userNameCache = new Map();
userNameCache.clear();

function safeChatId(chatId) {
    if (!chatId || chatId === "null" || chatId === "undefined" || chatId.trim() === "") {
        console.error("Получен некорректный chatId:", chatId);
        return null;
    }
    return chatId;
}

async function fetchWithAuth(url, options = {}) {
    const headers = {
        'Authorization': `Bearer ${token}`,
        ...options.headers
    };

    const response = await fetch(url, { ...options, headers });

    if (response.status === 401 || response.status === 403) {
        console.warn('Токен недействителен — редирект на логин');
        window.location.href = "/Authorization/Authorization";
        return null;
    }

    if (response.status === 429) {
        console.warn('Rate limited:', url);
        try {
            const body = await response.clone().json();
            showToast(body?.error || 'Слишком много запросов. Подождите немного.', 'warning');
        } catch {
            showToast('Слишком много запросов. Подождите немного.', 'warning');
        }
    }

    return response;
}

function isRateLimited(res) {
    return res && res.status === 429;
}

fileInput.onchange = function () {
    const MAX_SIZE_MB = 10;
    const MAX_SIZE_BYTES = MAX_SIZE_MB * 1024 * 1024;

    const validFiles = [];
    let hasError = false;

    for (let file of this.files) {
        if (file.size > MAX_SIZE_BYTES) {
            showToast(`Файл "${file.name}" слишком большой.\nМаксимум: ${MAX_SIZE_MB} МБ`, 'error');
            hasError = true;
            continue;
        }
        validFiles.push(file);
    }

    if (hasError && validFiles.length === 0) {
        fileInput.value = '';
        selectedFiles = [];
        attachedFiles.innerHTML = '';
        return;
    }

    selectedFiles = [...selectedFiles, ...validFiles];
    renderAttachedPreviews();

    fileInput.value = '';

    if (hasError && validFiles.length > 0) {
        showToast(`Некоторые файлы превышают лимит ${MAX_SIZE_MB} МБ и были пропущены`, 'warning');
    }
};

async function loadAvatar() {
    try {
        const res = await fetchWithAuth(`${API_BASE}/users/info`);
        if (!res || !res.ok) return;

        const json = await res.json();
        if (json.data?.account?.avatar) {
            const avatarUrl = json.data.account.avatar;
            const img = document.getElementById("current-user-avatar");
            if (img) {
                img.src = avatarUrl + "?t=" + new Date().getTime();
            } else {
                const container = document.querySelector(".relative.w-10.h-10");
                if (container) {
                    const placeholder = document.getElementById("current-user-avatar-placeholder");
                    if (placeholder) placeholder.remove();
                    const newImg = document.createElement("img");
                    newImg.id = "current-user-avatar";
                    newImg.className = "w-10 h-10 rounded-full object-cover shadow-md";
                    newImg.alt = "Аватар";
                    newImg.src = avatarUrl + "?t=" + new Date().getTime();
                    container.appendChild(newImg);
                }
            }
        }
    } catch (err) {
        console.error("Ошибка загрузки аватара:", err);
    }
}