function clearLongPress() {
    if (__lpTimer) {
        clearTimeout(__lpTimer);
        __lpTimer = null;
    }
    document.querySelectorAll('.long-press-active').forEach(el => el.classList.remove('long-press-active'));
}

function bindLongPress(root, selector, onLongPress) {
    if (!root) return;

    const supportsPointer = window.PointerEvent != null;

    const onStart = (e) => {
        if (e.pointerType === 'mouse') return;
        if (supportsPointer && e.type.startsWith('touch')) return;

        const target = e.target.closest(selector);
        if (!target || !root.contains(target)) return;

        __lpTriggered = false;
        clearLongPress();
        const point = (e.touches && e.touches[0]) ? e.touches[0] : e;
        __lpStartX = point.clientX;
        __lpStartY = point.clientY;

        target.classList.add('long-press-active');

        __lpTimer = setTimeout(() => {
            __lpTriggered = true;
            target.classList.remove('long-press-active');
            try { if (navigator.vibrate) navigator.vibrate(30); } catch (_) {}
            onLongPress(target, point.clientX, point.clientY, e);
        }, LONG_PRESS_MS);
    };

    const onMove = (e) => {
        if (!__lpTimer) return;
        if (supportsPointer && e.type.startsWith('touch')) return;
        const point = (e.touches && e.touches[0]) ? e.touches[0] : e;
        if (Math.abs(point.clientX - __lpStartX) > __LP_MOVE_TOLERANCE ||
            Math.abs(point.clientY - __lpStartY) > __LP_MOVE_TOLERANCE) {
            clearLongPress();
        }
    };

    const onEnd = (e) => {
        if (supportsPointer && e.type.startsWith('touch')) return;
        const was = __lpTriggered;
        clearLongPress();
        if (was) {
            e.preventDefault();
            e.stopPropagation();
            const blockClick = (ev) => {
                ev.preventDefault();
                ev.stopPropagation();
                document.removeEventListener('click', blockClick, true);
            };
            document.addEventListener('click', blockClick, true);
            setTimeout(() => document.removeEventListener('click', blockClick, true), 400);
        }
    };

    if (supportsPointer) {
        root.addEventListener('pointerdown', onStart);
        root.addEventListener('pointermove', onMove);
        root.addEventListener('pointerup', onEnd);
        root.addEventListener('pointercancel', clearLongPress);
    } else {
        root.addEventListener('touchstart', onStart, { passive: true });
        root.addEventListener('touchmove', onMove, { passive: true });
        root.addEventListener('touchend', onEnd, { passive: false });
        root.addEventListener('touchcancel', clearLongPress, { passive: true });
    }
}

const chatsListEl = document.getElementById('chats-list') || document.getElementById('chats-container');
if (chatsListEl) {
    chatsListEl.addEventListener('contextmenu', function (e) {
        const item = e.target.closest('.chat-item');
        if (!item) return;
        const chatId = item.dataset.chatId;
        if (!chatId) return;
        e.preventDefault();
        e.stopPropagation();
        showChatContextMenu(e.clientX, e.clientY, chatId);
    });
}

document.getElementById('chat-ctx-edit-btn')?.addEventListener('click', async (e) => {
    e.preventDefault();
    e.stopPropagation();
    const chatId = contextMenuChatId;
    hideChatContextMenu();
    if (!chatId) return;
    if (String(currentChatId) === String(chatId) && currentChatInfo && typeof openChatInfo === 'function') {
        openChatInfo();
    } else {
        await openChatInfoForChatId(chatId);
    }
});

document.getElementById('chat-ctx-pin-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    const chatId = contextMenuChatId;
    hideChatContextMenu();
    if (chatId) togglePinChat(chatId);
});

document.getElementById('ctx-pin-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    const mid = contextMenuMessageId;
    const txt = contextMenuMessageText;
    hideMessageContextMenu();
    if (!mid || !currentChatId) return;
    if (isMessagePinned(currentChatId, mid)) {
        unpinMessage(currentChatId, mid);
    } else {
        pinMessage(currentChatId, mid, txt);
    }
});

document.getElementById('ctx-reply-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    const mid = contextMenuMessageId;
    const txt = contextMenuMessageText;
    hideMessageContextMenu();
    if (!mid) return;
    const row = document.querySelector(`[data-mid="${mid}"]`);
    startReply(mid, txt || getMessagePreviewFromRow(row), getSenderNameFromRow(row));
});

document.getElementById('ctx-react-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    const mid = contextMenuMessageId;
    hideMessageContextMenu();
    if (!mid) return;
    const menu = document.getElementById('message-context-menu');
    const rect = menu ? menu.getBoundingClientRect() : { left: e.clientX, bottom: e.clientY };
    showReactionPicker(mid, rect.left || e.clientX, (rect.bottom || e.clientY) + 4);
});

document.getElementById('reply-compose-cancel')?.addEventListener('click', (e) => {
    e.preventDefault();
    cancelReply();
});

document.getElementById('messages-container')?.addEventListener('dblclick', (e) => {
    const bubble = e.target.closest('.message-bubble');
    if (!bubble) return;
    if (e.target.closest('.reply-quote')) return;
    const row = bubble.closest('[data-mid]');
    if (!row || String(row.dataset.mid || '').startsWith('temp-')) return;
    e.preventDefault();
    startReply(row.dataset.mid, getMessagePreviewFromRow(row), getSenderNameFromRow(row));
});


document.getElementById('pinned-message-bar')?.addEventListener('click', (e) => {
    if (e.target.closest('#pinned-message-close')) return;
    const seg = e.target.closest('.pin-seg');
    if (seg && seg.dataset.pinIdx != null) {
        const i = parseInt(seg.dataset.pinIdx, 10);
        setPinnedViewIndex(currentChatId, i);
        updatePinnedMessageBar();
        const list = getPinnedMessages(currentChatId);
        if (list[i]) scrollToPinnedMessage(list[i].messageId);
        return;
    }
    onPinnedBarClick();
});

document.getElementById('pinned-message-close')?.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (currentChatId) unpinMessage(currentChatId);
});

document.getElementById('chat-ctx-export-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    const chatId = contextMenuChatId;
    hideChatContextMenu();
    if (chatId) openExportChatModal(chatId);
});

document.getElementById('chat-ctx-delete-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    const chatId = contextMenuChatId;
    hideChatContextMenu();
    if (chatId) openDeleteChatConfirm(chatId);
});

document.querySelectorAll('.export-modal-fmt-btn').forEach(btn => {
    btn.addEventListener('click', (e) => {
        e.preventDefault();
        const fmt = btn.getAttribute('data-format') || 'txt';
        exportChat(exportModalChatId || currentChatId, fmt);
    });
});

document.getElementById('export-chat-modal')?.addEventListener('click', (e) => {
    if (e.target.id === 'export-chat-modal') closeExportChatModal();
});

if (messagesContainer) {
    messagesContainer.addEventListener('contextmenu', function (e) {
        const bubble = e.target.closest('.message-bubble');
        if (!bubble) return;
        const row = bubble.closest('[data-mid]');
        if (!row) return;
        const mid = row.dataset.mid;
        if (!mid || String(mid).startsWith('temp-')) return;
        e.preventDefault();
        e.stopPropagation();
        if (selectMode) {
            toggleMessageSelection(mid);
            return;
        }
        const textEl = bubble.querySelector('p');
        const plainText = textEl ? (textEl.innerText || textEl.textContent || '') : '';
        showMessageContextMenu(e.clientX, e.clientY, mid, plainText);
    });
}

bindLongPress(chatsListEl || document.getElementById('chats-container'), '.chat-item', (item, x, y) => {
    const chatId = item.dataset.chatId;
    if (!chatId) return;
    showChatContextMenu(x, y, chatId);
});

bindLongPress(messagesContainer, '.message-bubble', (bubble, x, y) => {
    const row = bubble.closest('[data-mid]');
    if (!row) return;
    const mid = row.dataset.mid;
    if (!mid || String(mid).startsWith('temp-')) return;
    if (selectMode) {
        toggleMessageSelection(mid);
        return;
    }
    const textEl = bubble.querySelector('p');
    const plainText = textEl ? (textEl.innerText || textEl.textContent || '') : '';
    showMessageContextMenu(x, y, mid, plainText);
});

document.getElementById('ctx-edit-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (!contextMenuMessageId) return;
    const id = contextMenuMessageId;
    const txt = contextMenuMessageText;
    hideMessageContextMenu();
    enterEditMode(id, txt);
});
document.getElementById('ctx-delete-btn')?.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (!contextMenuMessageId) return;
    const id = contextMenuMessageId;
    hideMessageContextMenu();
    openDeleteMessageConfirm(id);
});


loadChats();
setTimeout(() => applyPinnedChatsOrder(), 800);
startSignalR();

sendButton.onclick = sendMessage;
messageInput.addEventListener('keydown', e => {
    if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        sendMessage();
    }
});

const observer = new MutationObserver(() => feather.replace());
observer.observe(document.body, { childList: true, subtree: true });

function updateThemeIcons() {
    const isDark = document.documentElement.classList.contains('dark');
    document.querySelectorAll('.theme-icon-dark').forEach(el => el.classList.toggle('hidden', !isDark));
    document.querySelectorAll('.theme-icon-light').forEach(el => el.classList.toggle('hidden', isDark));
}

function toggleTheme() {
    const html = document.documentElement;
    const isDark = html.classList.toggle('dark');
    localStorage.setItem('theme', isDark ? 'dark' : 'light');
    updateThemeIcons();
    feather.replace();
}

document.getElementById('theme-toggle')?.addEventListener('click', toggleTheme);
updateThemeIcons();

window.addEventListener('resize', () => {
    if (!isMobileLayout()) {
        setMobileChatOpen(false);
    } else if (currentChatId) {
        setMobileChatOpen(true);
    }
});

if (window.visualViewport) {
    const updateViewportHeight = () => {
        const vv = window.visualViewport;
        document.documentElement.style.setProperty('--vv-height', vv.height + 'px');
        document.documentElement.style.setProperty('--keyboard-offset', Math.max(0, window.innerHeight - vv.height) + 'px');
    };
    window.visualViewport.addEventListener('resize', updateViewportHeight);
    window.visualViewport.addEventListener('scroll', updateViewportHeight);
    updateViewportHeight();
}

window.addEventListener('orientationchange', () => {
    setTimeout(() => {
        if (window.visualViewport) {
            document.documentElement.style.setProperty('--vv-height', window.visualViewport.height + 'px');
        }
    }, 250);
});