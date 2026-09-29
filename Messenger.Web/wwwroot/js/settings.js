const API_URL = window.API_BASE_URL;
let connection = null;
const avatarInput = document.getElementById('avatarInput');
let currentAvatar = document.getElementById('current-avatar');
const previewContainer = document.getElementById('avatar-preview-container');
const previewImg = document.getElementById('avatar-preview');
const cancelPreviewBtn = document.getElementById('cancel-preview');
const deleteAvatarTrigger = document.getElementById('delete-avatar-trigger');
const deleteAvatarFlag = document.getElementById('delete-avatar-flag');
let hasOriginalAvatar = '@hasAvatar'.toLowerCase() === 'true';
let pushToggle = null;
let currentSubscription = null;
let pushToggleBusy = false;

const sidebar = document.getElementById('sidebar');
const overlay = document.getElementById('sidebar-overlay');
const menuToggle = document.getElementById('menu-toggle');

function openSidebar() {
    sidebar.classList.add('open');
    overlay.classList.add('active');
    document.body.style.overflow = 'hidden';
}

function closeSidebar() {
    sidebar.classList.remove('open');
    overlay.classList.remove('active');
    document.body.style.overflow = '';
}

menuToggle?.addEventListener('click', openSidebar);
overlay?.addEventListener('click', closeSidebar);

document.querySelectorAll('.sidebar-link').forEach(link => {
    link.addEventListener('click', () => {
        if (window.innerWidth < 768) closeSidebar();
    });
});

function cleanToken(t) {
    return (t || '').trim().replace(/^Bearer\s+/i, '');
}

function jwtExpiresAtMs(token) {
    try {
        const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
        const json = JSON.parse(atob(payload + '='.repeat((4 - payload.length % 4) % 4)));
        return json.exp ? json.exp * 1000 : 0;
    } catch (_) {
        return 0;
    }
}

function getJwtToken() {
    const candidates = [
        document.querySelector('meta[name="access-token"]')?.content,
        localStorage.getItem('token'),
        sessionStorage.getItem('token')
    ].map(cleanToken).filter(Boolean);

    if (!candidates.length) return '';
    candidates.sort((a, b) => jwtExpiresAtMs(b) - jwtExpiresAtMs(a));
    return candidates[0];
}

function getAuthToken() {
    const token = getJwtToken();
    return token ? 'Bearer ' + token : '';
}

async function fetchFreshToken() {
    try {
        const res = await fetch('/Account/Settings?handler=Token', {
            credentials: 'same-origin',
            cache: 'no-store',
            headers: { 'Accept': 'application/json' }
        });
        if (!res.ok) return '';
        const data = await res.json();
        const fresh = cleanToken(data?.accessToken);
        if (!fresh) return '';

        try { localStorage.setItem('token', fresh); } catch (_) { }
        const meta = document.querySelector('meta[name="access-token"]');
        if (meta) meta.content = fresh;
        return fresh;
    } catch (_) {
        return '';
    }
}

async function ensureFreshToken() {
    const token = getJwtToken();
    const exp = token ? jwtExpiresAtMs(token) : 0;
    if (!token || (exp && exp - Date.now() < 30000)) {
        await fetchFreshToken();
    }
}

async function apiFetch(endpoint, options = {}, retried = false) {
    await ensureFreshToken();
    const token = getAuthToken();
    const headers = {
        'Authorization': token,
        'Content-Type': 'application/json',
        ...options.headers
    };
    const response = await fetch(`${API_URL}${endpoint}`, { ...options, headers });

    if (response.status === 401 && !retried && await fetchFreshToken()) {
        return apiFetch(endpoint, options, true);
    }

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || `Ошибка: ${response.statusText}`);
    }
    return response.json();
}

function showToast(message, type = 'info', duration = 4000) {
    if (!message) return;

    let container = document.getElementById('toast-container');

    if (!container) {
        container = document.createElement('div');
        container.id = 'toast-container';
        document.body.appendChild(container);
    }

    const icons = {
        success: '✓',
        error: '✕',
        warning: '!',
        info: 'i'
    };

    const toast = document.createElement('div');
    toast.className = `toast toast-${type}`;

    toast.innerHTML = `
        <div class="toast-icon">${icons[type] || icons.info}</div>
        <div class="toast-content">
            <div class="toast-message"></div>
        </div>
        <button class="toast-close" aria-label="Закрыть">×</button>
        <div class="toast-progress"></div>
    `;

    toast.querySelector('.toast-message').textContent = message;

    container.appendChild(toast);

    requestAnimationFrame(() => {
        toast.classList.add('toast-show');
    });

    const removeToast = () => {
        if (!toast.parentElement) return;

        toast.classList.remove('toast-show');
        toast.classList.add('toast-hide');

        setTimeout(() => {
            toast.remove();

            if (container.children.length === 0) {
                container.remove();
            }
        }, 300);
    };

    toast.querySelector('.toast-close').addEventListener('click', removeToast);

    const timer = setTimeout(removeToast, duration);

    let remaining = duration;
    let startTime = Date.now();

    toast.addEventListener('mouseenter', () => {
        clearTimeout(timer);
        remaining -= Date.now() - startTime;
    });

    toast.addEventListener('mouseleave', () => {
        startTime = Date.now();
        setTimeout(removeToast, remaining);
    });
}

function openConfirm(title, message) {
    document.getElementById('confirmTitle').textContent = title;
    document.getElementById('confirmMessage').textContent = message;
    document.getElementById('confirmModal').classList.add('active');
    return new Promise(resolve => confirmCallback = resolve);
}

function closeConfirm(result = false) {
    document.getElementById('confirmModal').classList.remove('active');
    if (confirmCallback) {
        confirmCallback(result);
        confirmCallback = null;
    }
}

async function initSignalR() {
    await ensureFreshToken();
    if (!getJwtToken()) {
        console.warn("⚠️ SignalR: Токен не найден");
        return;
    }

    connection = new signalR.HubConnectionBuilder()
        .withUrl(HUB_URL, { accessTokenFactory: () => getJwtToken() })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    const myUserId = '@(Model.CurrentUser?.UserId ?? "")';
    const myDisplayName = '@fullName'.replace(/&#x27;/g, "'");

    connection.on('UserBlockStatusChanged', (data) => {
        const me = myUserId.toLowerCase();
        if (data.targetId?.toLowerCase() === me) {
            showToast(data.isBlocked ? "Вас добавили в чёрный список" : "Вас убрали из чёрного списка",
                data.isBlocked ? "warning" : "info");
        }
    });

    function applyProfileUpdate(data) {
        if (!data?.userId) return;
        if (String(data.userId).toLowerCase() !== myUserId.toLowerCase()) return;

        const url = data.avatarUrl ?? data.AvatarUrl ?? null;
        updateAvatarDisplay(url);
        hasOriginalAvatar = !!url;
        if (deleteAvatarTrigger) {
            deleteAvatarTrigger.classList.toggle('hidden', !hasOriginalAvatar);
        }
    }

    connection.on("ProfileUpdated", applyProfileUpdate);
    connection.on("AvatarUpdated", applyProfileUpdate);

    try {
        await connection.start();
        console.log("SignalR успешно подключён!");

        if (window.__profileJustSaved) {
            const avatarUrl = window.__savedAvatarUrl || null;
            const displayName = window.__savedDisplayName || myDisplayName;
            try {
                await connection.invoke("NotifyProfileUpdated", myUserId, avatarUrl, displayName);
                await connection.invoke("NotifyAvatarUpdated", myUserId, avatarUrl || "");
                console.log("ProfileUpdated/AvatarUpdated разосланы через SignalR");
            } catch (e) {
                console.warn("Не удалось разослать ProfileUpdated:", e);
            }
            window.__profileJustSaved = false;
        }
    } catch (err) {
        console.error("SignalR НЕ подключился:", err);
    }
}

function updateAvatarDisplay(avatarUrl) {
    const fallback = '/images/logo.png';
    const urlWithCache = avatarUrl ? `${avatarUrl}?t=${new Date().getTime()}` : fallback;

    if (currentAvatar.tagName === 'IMG') {
        currentAvatar.src = urlWithCache;
    } else {
        const img = document.createElement('img');
        img.id = 'current-avatar';
        img.className = currentAvatar.className;
        img.src = urlWithCache;
        currentAvatar.replaceWith(img);
        currentAvatar = img;
    }
}

avatarInput.addEventListener('change', function (e) {
    const file = e.target.files[0];
    if (!file) return hidePreview();

    if (!file.type.startsWith('image/') || file.size > 2 * 1024 * 1024) {
        showToast('Выберите изображение до 2 МБ', 'error');
        this.value = '';
        return;
    }

    const reader = new FileReader();
    reader.onload = (ev) => {
        previewImg.onload = () => {
            previewImg.style.opacity = '1';
        };
        previewImg.src = ev.target.result;
        previewImg.style.opacity = '1';
        previewImg.style.display = 'block';
        previewContainer.classList.remove('hidden');
        if (currentAvatar) currentAvatar.style.visibility = 'hidden';
        const defAv = document.getElementById('default-avatar');
        if (defAv) defAv.classList.add('hidden');
        cancelPreviewBtn.classList.remove('hidden');
        deleteAvatarTrigger.classList.add('hidden');
        showToast('Фото будет установлено после сохранения изменений. Нажмите «Сохранить».', 'info');
    };
    reader.onerror = () => {
        showToast('Не удалось прочитать файл', 'error');
        hidePreview();
    };
    reader.readAsDataURL(file);
});

function hidePreview() {
    previewContainer.classList.add('hidden');
    previewImg.src = '';
    cancelPreviewBtn.classList.add('hidden');
    if (currentAvatar) currentAvatar.style.visibility = '';
    if (hasOriginalAvatar) deleteAvatarTrigger.classList.remove('hidden');
}

cancelPreviewBtn.onclick = () => {
    avatarInput.value = '';
    hidePreview();
};

deleteAvatarTrigger.onclick = async () => {
    const ok = await showConfirm('Удалить аватар?', {
        title: 'Удаление фото',
        okText: 'Удалить',
        cancelText: 'Отмена',
        danger: true
    });
    if (!ok) return;
    deleteAvatarFlag.value = 'true';
    updateAvatarDisplay(null);
    hidePreview();
    showToast('Фото будет удалено после сохранения изменений. Нажмите «Сохранить».', 'info');
};

function isIOS() {
    return /iPad|iPhone|iPod/.test(navigator.userAgent)
        || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
}

function isStandalonePWA() {
    return window.matchMedia('(display-mode: standalone)').matches
        || window.navigator.standalone === true
        || document.referrer.includes('android-app://');
}

function updatePushHints() {
    const iosHint = document.getElementById('ios-push-hint');
    const unsupportedHint = document.getElementById('push-unsupported-hint');
    const statusText = document.getElementById('push-status-text');
    const hasPush = ('PushManager' in window) && ('serviceWorker' in navigator);

    if (!hasPush) {
        unsupportedHint?.classList.remove('hidden');
        iosHint?.classList.add('hidden');
        return;
    }

    unsupportedHint?.classList.add('hidden');

    if (isIOS() && !isStandalonePWA()) {
        iosHint?.classList.remove('hidden');
        if (statusText) {
            statusText.textContent = 'Сначала установите приложение на экран «Домой»';
        }
    } else {
        iosHint?.classList.add('hidden');
        if (statusText && Notification.permission !== 'denied') {
            statusText.textContent = 'Получать уведомления даже когда приложение закрыто';
        }
    }
}

function setPushToggleUnavailable(unavailable) {
    const slider = document.getElementById('push-toggle-slider');
    if (!pushToggle) pushToggle = document.getElementById('push-toggle');
    if (pushToggle) pushToggle.disabled = !!unavailable;
    if (!slider) return;
    if (unavailable) {
        slider.classList.add('!bg-gray-400', 'cursor-not-allowed', 'opacity-60');
    } else {
        slider.classList.remove('!bg-gray-400', 'cursor-not-allowed', 'opacity-60');
    }
}

function setNotificationTypesVisible(visible) {
    const typesContainer = document.getElementById('notification-types');
    if (typesContainer) typesContainer.classList.toggle('hidden', !visible);
}

async function ensureServiceWorker() {
    if (!('serviceWorker' in navigator)) {
        throw new Error('Service Worker не поддерживается');
    }

    let reg = await navigator.serviceWorker.getRegistration();
    if (reg) {
        await navigator.serviceWorker.ready;
        return reg;
    }

    const candidates = ['/service-worker.js', '/sw.js', '/js/sw.js'];
    let lastError = null;
    for (const path of candidates) {
        try {
            reg = await navigator.serviceWorker.register(path, { scope: '/' });
            await navigator.serviceWorker.ready;
            console.log('SW зарегистрирован:', path);
            return reg;
        } catch (e) {
            lastError = e;
        }
    }
    throw lastError || new Error('Не удалось зарегистрировать Service Worker');
}

async function checkPushStatus() {
    pushToggle = document.getElementById('push-toggle');
    if (!pushToggle) return;

    updatePushHints();

    if (!('PushManager' in window) || !('serviceWorker' in navigator)) {
        setPushToggleUnavailable(true);
        return;
    }

    if (isIOS() && !isStandalonePWA()) {
        setPushToggleUnavailable(true);
        return;
    }

    if (Notification.permission === 'denied') {
        setPushToggleUnavailable(true);
        const statusText = document.getElementById('push-status-text');
        if (statusText) {
            statusText.textContent = 'Разрешение отклонено. Включите уведомления в настройках системы';
        }
        return;
    }

    setPushToggleUnavailable(false);

    try {
        const registration = await ensureServiceWorker();
        currentSubscription = await registration.pushManager.getSubscription();
    } catch (e) {
        console.error('Ошибка проверки статуса push:', e);
    }
}

async function subscribeToPush() {
    try {
        if (isIOS() && !isStandalonePWA()) {
            if (pushToggle) pushToggle.checked = false;
            document.getElementById('ios-push-hint')?.classList.remove('hidden');
            showToast('На iPhone откройте приложение с экрана «Домой»', 'error');
            return false;
        }

        if (!('Notification' in window)) {
            throw new Error('Notification API недоступен');
        }

        if (Notification.permission === 'denied') {
            if (pushToggle) pushToggle.checked = false;
            setPushToggleUnavailable(true);
            showToast('Уведомления запрещены в настройках браузера/системы', 'error');
            return false;
        }

        if (Notification.permission === 'default') {
            const permission = await Notification.requestPermission();
            if (permission !== 'granted') {
                if (pushToggle) pushToggle.checked = false;
                showToast('Разрешение на уведомления отклонено', 'error');
                return false;
            }
        }

        const registration = await ensureServiceWorker();
        const keyRes = await fetch(`${API_URL}/push/vapid-public-key`);
        if (!keyRes.ok) throw new Error('Не удалось получить VAPID-ключ');
        const vapidPublicKey = (await keyRes.text()).trim();
        if (!vapidPublicKey) throw new Error('Пустой VAPID-ключ');

        let subscription = await registration.pushManager.getSubscription();
        if (!subscription) {
            subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: urlBase64ToUint8Array(vapidPublicKey)
            });
        }

        await ensureFreshToken();
        const token = getAuthToken();
        if (!token) throw new Error('Нет токена авторизации');

        const subJson = subscription.toJSON();
        const res = await fetch(`${API_URL}/push/subscribe`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': token
            },
            body: JSON.stringify({
                endpoint: subscription.endpoint,
                p256dh: subJson.keys?.p256dh,
                auth: subJson.keys?.auth
            })
        });

        if (res.ok) {
            currentSubscription = subscription;
            if (pushToggle) pushToggle.checked = true;
            showToast('Push-уведомления включены', 'success');
            return true;
        }

        const errText = await res.text().catch(() => '');
        throw new Error(errText || `Не удалось сохранить подписку (${res.status})`);
    } catch (err) {
        console.error(err);
        if (pushToggle) pushToggle.checked = false;
        currentSubscription = null;
        const msg = (err && err.message) ? String(err.message) : 'Не удалось включить push-уведомления';
        showToast(msg.length > 120 ? 'Не удалось включить push-уведомления' : msg, 'error');
        return false;
    }
}

async function unsubscribeFromPush() {
    try {
        if (!currentSubscription) {
            try {
                const registration = await ensureServiceWorker();
                currentSubscription = await registration.pushManager.getSubscription();
            } catch (_) {
                currentSubscription = null;
            }
        }

        if (currentSubscription) {
            const endpoint = currentSubscription.endpoint;
            try {
                await currentSubscription.unsubscribe();
            } catch (e) {
                console.warn('Browser unsubscribe:', e);
            }
            const token = getAuthToken();
            if (token && endpoint) {
                try {
                    await fetch(`${API_URL}/push/unsubscribe`, {
                        method: 'DELETE',
                        headers: {
                            'Content-Type': 'application/json',
                            'Authorization': token
                        },
                        body: JSON.stringify(endpoint)
                    });
                } catch (e) {
                    console.warn('Server unsubscribe:', e);
                }
            }
            currentSubscription = null;
        }

        if (pushToggle) pushToggle.checked = false;
        showToast('Push-уведомления отключены', 'info');
        return true;
    } catch (err) {
        console.error(err);
        if (pushToggle) pushToggle.checked = false;
        currentSubscription = null;
        showToast('Push-уведомления отключены', 'info');
        return true;
    }
}

function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - base64String.length % 4) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const rawData = window.atob(base64);
    const outputArray = new Uint8Array(rawData.length);
    for (let i = 0; i < rawData.length; ++i) {
        outputArray[i] = rawData.charCodeAt(i);
    }
    return outputArray;
}

async function loadPushSettings() {
    try {
        const settings = await apiFetch('/push/settings');
        pushToggle = document.getElementById('push-toggle');
        const isEnabled = !!settings.pushEnabled;

        if (pushToggle) pushToggle.checked = isEnabled;

        setNotificationTypesVisible(isEnabled);

        const nm = document.getElementById('notifyMessages');
        const ng = document.getElementById('notifyGroup');
        const nmen = document.getElementById('notifyMentions');
        if (nm) nm.checked = !!settings.notifyMessages;
        if (ng) ng.checked = !!settings.notifyGroupChats;
        if (nmen) nmen.checked = !!settings.notifyMentions;
    } catch (e) {
        console.error('Не удалось загрузить настройки push:', e);
    }
}

async function togglePushNotifications() {
    if (!pushToggle) pushToggle = document.getElementById('push-toggle');
    if (!pushToggle) return;

    if (pushToggleBusy) {
        pushToggle.checked = !pushToggle.checked;
        return;
    }

    pushToggleBusy = true;
    pushToggle.disabled = true;

    try {
        const wantEnable = pushToggle.checked;

        if (wantEnable) {
            const ok = await subscribeToPush();
            if (!ok) {
                pushToggle.checked = false;
                setNotificationTypesVisible(false);
                await saveNotificationSettings();
                return;
            }
            setNotificationTypesVisible(true);
        } else {
            await unsubscribeFromPush();
            pushToggle.checked = false;
            setNotificationTypesVisible(false);
        }

        await saveNotificationSettings();
    } finally {
        pushToggleBusy = false;
        if (Notification.permission !== 'denied' &&
            !((isIOS() && !isStandalonePWA())) &&
            ('PushManager' in window) && ('serviceWorker' in navigator)) {
            pushToggle.disabled = false;
        }
    }
}

async function saveNotificationSettings() {
    const dto = {
        pushEnabled: !!(document.getElementById('push-toggle')?.checked),
        notifyMessages: !!(document.getElementById('notifyMessages')?.checked),
        notifyGroupChats: !!(document.getElementById('notifyGroup')?.checked),
        notifyMentions: !!(document.getElementById('notifyMentions')?.checked)
    };

    try {
        await apiFetch('/push/settings', {
            method: 'POST',
            body: JSON.stringify(dto)
        });
    } catch (err) {
        console.error('Ошибка сохранения настроек:', err);
        showToast('Не удалось сохранить настройки уведомлений', 'error');
    }
}

document.addEventListener('DOMContentLoaded', async () => {
    feather.replace();

    const pushToggleEl = document.getElementById('push-toggle');
    if (pushToggleEl) {
        pushToggleEl.addEventListener('change', togglePushNotifications);
    }

    document.querySelectorAll('#notification-types input[type="checkbox"]').forEach(cb => {
        cb.addEventListener('change', saveNotificationSettings);
    });

    initSignalR();
    await loadPushSettings();
    await checkPushStatus();

    document.getElementById('confirmYesBtn').onclick = () => closeConfirm(true);

    document.querySelectorAll('.modal-overlay').forEach(m => {
        m.onclick = (e) => {
            if (e.target === m) {
                m.classList.remove('active');
                if (m.id === 'confirmModal') closeConfirm(false);
            }
        };
    });

    function updateThemeUI() {
        const isDark = document.documentElement.classList.contains('dark');
        document.querySelectorAll('.theme-icon-dark').forEach(el => el.classList.toggle('hidden', !isDark));
        document.querySelectorAll('.theme-icon-light').forEach(el => el.classList.toggle('hidden', isDark));
        const label = document.getElementById('theme-label');
        if (label) label.textContent = isDark ? 'Тёмная' : 'Светлая';

        const meta = document.querySelector('meta[name="theme-color"]:not([media])') ||
            document.querySelector('meta[name="theme-color"]');
        if (meta) meta.setAttribute('content', isDark ? '#111827' : '#ffffff');
    }

    function toggleTheme() {
        const html = document.documentElement;
        const isDark = html.classList.toggle('dark');
        localStorage.setItem('theme', isDark ? 'dark' : 'light');
        updateThemeUI();
        feather.replace();
    }

    document.getElementById('theme-toggle-settings')?.addEventListener('click', toggleTheme);
    updateThemeUI();
});
