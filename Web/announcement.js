(function () {
    if (document.getElementById('jf-announcements-script')) return;
    const marker = document.createElement('div');
    marker.id = 'jf-announcements-script';
    marker.style.display = 'none';
    document.documentElement.appendChild(marker);

    const API_BASE = '/Plugins/Announcements';
    const API_PATH = API_BASE;
    const STORAGE_KEY_PERM = 'announcements.dismissed.permanent';
    const STORAGE_KEY_SESSION = 'announcements.dismissed.session';
    const SESSION_KEY = 'announcements.runtime.sessionId';
    const POLL_INTERVAL = 3000; // 3 seconds for faster visibility updates
    const INIT_DELAY_MS = 400;
    const MIN_POLL_GAP_MS = 700;
    const activeImpressionIds = new Set();
    let cachedLibraryContext = null;
    let cachedLibraryContextAt = 0;
    let cachedViewerUser = null;
    let cachedViewerUserAt = 0;
    let cachedViewerToken = '';

    const LIBRARY_CONTEXT_TTL_MS = 60000;
    const VIEWER_USER_TTL_MS = 10000;
    let pollInFlight = false;
    let lastPollAt = 0;
    let lastViewerFingerprint = null;
    let authWatcherHandle = null;
    let lastAuthWatcherState = 'anon';

    function getSessionId() {
        try {
            const existing = sessionStorage.getItem(SESSION_KEY);
            if (existing) return existing;
            // Use cryptographically random bytes — Math.random() is not secure
            const arr = new Uint8Array(16);
            (window.crypto || window.msCrypto).getRandomValues(arr);
            const created = 'sess-' + Array.from(arr).map(b => b.toString(16).padStart(2, '0')).join('');
            sessionStorage.setItem(SESSION_KEY, created);
            return created;
        } catch {
            // crypto unavailable — use timestamp only (still avoids Math.random)
            return 'sess-' + Date.now().toString(36);
        }
    }

    const sessionId = getSessionId();

    function ensureCssLoaded() {
        if (document.getElementById('jf-announcements-css')) return;
        const link = document.createElement('link');
        link.id = 'jf-announcements-css';
        link.rel = 'stylesheet';
        link.href = API_BASE + '/banner.css';
        document.head.appendChild(link);
    }

    function getDismissed(storage, key) {
        try {
            const parsed = JSON.parse(storage.getItem(key) || '[]');
            return Array.isArray(parsed) ? parsed.filter(x => typeof x === 'string') : [];
        } catch {
            return [];
        }
    }

    function setDismissed(storage, key, ids) {
        storage.setItem(key, JSON.stringify(ids));
    }

    // Use a content-aware key so edited announcements are treated as new items
    // for dismissal purposes and cannot disappear due to stale cache entries.
    function dismissKey(a) {
        return [
            a && a.id ? String(a.id) : '',
            a && a.title ? String(a.title) : '',
            a && a.message ? String(a.message) : '',
            a && a.level ? String(a.level) : '',
            a && a.startsAt ? String(a.startsAt) : '',
            a && a.endsAt ? String(a.endsAt) : '',
            a && a.allowDismiss === false ? '0' : '1',
            a && a.dismissMode ? String(a.dismissMode) : 'permanent',
            a && a.priority != null ? String(a.priority) : '1'
        ].join('|');
    }

    function isDismissed(a) {
        if (a.allowDismiss === false) {
            return false;
        }

        const key = dismissKey(a);
        if (!key) return false;

        const mode = (a.dismissMode || 'permanent').toLowerCase();
        if (mode === 'session') {
            return getDismissed(sessionStorage, STORAGE_KEY_SESSION).includes(key);
        }
        return getDismissed(localStorage, STORAGE_KEY_PERM).includes(key);
    }

    function markDismissed(a) {
        const key = dismissKey(a);
        if (!key) return;

        const mode = (a.dismissMode || 'permanent').toLowerCase();
        if (mode === 'session') {
            const ids = getDismissed(sessionStorage, STORAGE_KEY_SESSION);
            if (!ids.includes(key)) ids.push(key);
            setDismissed(sessionStorage, STORAGE_KEY_SESSION, ids);
            return;
        }

        const ids = getDismissed(localStorage, STORAGE_KEY_PERM);
        if (!ids.includes(key)) ids.push(key);
        setDismissed(localStorage, STORAGE_KEY_PERM, ids);
    }

    async function fetchAnnouncements() {
        try {
            const res = await fetch(API_PATH, { credentials: 'include' });
            if (!res.ok) return [];
            return await res.json();
        } catch {
            return [];
        }
    }

    function normalizeList(values, lower = true) {
        if (!Array.isArray(values)) return [];
        const map = new Map();
        values
            .map(value => String(value || '').trim())
            .filter(value => !!value)
            .forEach(value => {
                const key = lower ? value.toLowerCase() : value;
                if (!map.has(key)) {
                    map.set(key, lower ? key : value);
                }
            });
        return Array.from(map.values());
    }

    function listHasValue(list, value) {
        if (!value) return false;
        const needle = String(value).toLowerCase();
        return normalizeList(list, true).includes(needle);
    }

    function hasAnyIntersection(left, right) {
        const leftNormalized = normalizeList(left, true);
        const rightNormalized = normalizeList(right, true);
        return leftNormalized.some(value => rightNormalized.includes(value));
    }

    function detectDeviceType() {
        const ua = ((navigator && navigator.userAgent) || '').toLowerCase();
        if (/(smart-tv|smarttv|tizen|webos|hbbtv|roku|appletv|android tv|fire tv|aft)/.test(ua)) {
            return 'tv';
        }
        if (/(ipad|tablet|kindle|silk|playbook)/.test(ua)) {
            return 'tablet';
        }
        if (/(mobi|iphone|ipod|android|windows phone)/.test(ua)) {
            return 'mobile';
        }
        return 'desktop';
    }

    function getCurrentUserInfo() {
        try {
            if (window.ApiClient && typeof window.ApiClient.getCurrentUser === 'function') {
                return window.ApiClient.getCurrentUser() || null;
            }
        } catch {
            return null;
        }

        try {
            if (window.ApiClient && window.ApiClient._currentUser) {
                return window.ApiClient._currentUser;
            }
        } catch {
            return null;
        }

        return null;
    }

    function readAny(obj, names) {
        if (!obj) return undefined;
        for (const name of names) {
            if (Object.prototype.hasOwnProperty.call(obj, name)) {
                return obj[name];
            }
        }
        return undefined;
    }

    function normalizeId(value) {
        return String(value || '').trim().replace(/-/g, '').toLowerCase();
    }

    function detectUserRoles(user) {
        const roles = ['guest'];
        const userId = readAny(user, ['Id', 'id']);
        if (!user || !userId) {
            return roles;
        }

        roles.splice(0, roles.length, 'user');

        const policy = readAny(user, ['Policy', 'policy']) || {};
        if (readAny(policy, ['IsAdministrator', 'isAdministrator']) === true) {
            roles.push('admin');
        }

        if (
            readAny(policy, ['IsKid', 'isKid', 'IsKids', 'isKids']) === true ||
            readAny(user, ['IsKid', 'isKid']) === true)
        {
            roles.push('kid');
        }

        return Array.from(new Set(roles));
    }

    async function fetchAccessibleLibraryIds(userId) {
        if (!userId || !window.ApiClient) {
            return [];
        }

        const now = Date.now();
        if (
            cachedLibraryContext &&
            cachedLibraryContext.userId === userId &&
            now - cachedLibraryContextAt < LIBRARY_CONTEXT_TTL_MS
        ) {
            return cachedLibraryContext.libraryIds;
        }

        const path = 'Users/' + encodeURIComponent(userId) + '/Views';
        try {
            const url = window.ApiClient.getUrl(path);
            const response = await fetch(url, {
                credentials: 'include',
                headers: {
                    'X-Emby-Token': typeof window.ApiClient.accessToken === 'function' ? window.ApiClient.accessToken() : ''
                }
            });

            if (!response.ok) {
                return [];
            }

            const payload = await response.json();
            const items = Array.isArray(payload && payload.Items) ? payload.Items : [];
            const libraryIds = items
                .map(item => item && (item.CollectionFolderId || item.Id))
                .filter(id => !!id)
                .map(id => String(id));

            cachedLibraryContext = { userId, libraryIds };
            cachedLibraryContextAt = now;
            return libraryIds;
        } catch {
            return [];
        }
    }

    async function fetchCurrentUserInfo() {
        const now = Date.now();
        const currentToken = getAccessToken();
        if (
            cachedViewerUser &&
            now - cachedViewerUserAt < VIEWER_USER_TTL_MS &&
            cachedViewerToken === currentToken
        ) {
            return cachedViewerUser;
        }

        const immediateUser = getCurrentUserInfo();
        const immediateUserId = readAny(immediateUser, ['Id', 'id']);
        if (immediateUser && immediateUserId) {
            cachedViewerUser = immediateUser;
            cachedViewerUserAt = now;
            cachedViewerToken = currentToken;
            return immediateUser;
        }

        if (!window.ApiClient || typeof window.ApiClient.getUrl !== 'function') {
            return immediateUser;
        }

        try {
            const url = window.ApiClient.getUrl('Users/Me');
            const response = await fetch(url, {
                credentials: 'include',
                headers: {
                    'X-Emby-Token': typeof window.ApiClient.accessToken === 'function' ? window.ApiClient.accessToken() : ''
                }
            });

            if (!response.ok) {
                return immediateUser;
            }

            const user = await response.json();
            const userId = readAny(user, ['Id', 'id']);
            if (user && userId) {
                cachedViewerUser = user;
                cachedViewerUserAt = now;
                cachedViewerToken = currentToken;
                return user;
            }
        } catch {
            return immediateUser;
        }

        return immediateUser;
    }

    async function getViewerContext() {
        const user = await fetchCurrentUserInfo();
        const currentUserId = readAny(user, ['Id', 'id']);
        const isAuthenticated = !!(user && currentUserId);
        const userId = isAuthenticated ? String(currentUserId) : null;
        const userRoles = detectUserRoles(user);
        const libraryIds = isAuthenticated ? await fetchAccessibleLibraryIds(userId) : [];

        return {
            isAuthenticated,
            userId,
            userRoles,
            deviceType: detectDeviceType(),
            libraryIds
        };
    }

    function matchesTargeting(a, context) {
        const audience = String((a && a.audience) || 'all').toLowerCase();
        const isAdmin = context.userRoles.includes('admin');
        const isKid = context.userRoles.includes('kid');
        const includeUserIds = normalizeList(a.includeUserIds, false);
        const excludeUserIds = normalizeList(a.excludeUserIds, false);
        const explicitlyIncludedUser = !!(
            context.userId &&
            includeUserIds.some(id => normalizeId(id) === normalizeId(context.userId))
        );

        if (excludeUserIds.length && context.userId) {
            const excludedUser = excludeUserIds.some(id => normalizeId(id) === normalizeId(context.userId));
            if (excludedUser) return false;
        }

        if (includeUserIds.length && !explicitlyIncludedUser) {
            return false;
        }

        if (audience === 'authenticated' && !context.isAuthenticated) return false;
        if (audience === 'unauthenticated' && context.isAuthenticated) return false;
        if (audience === 'admins' && !isAdmin) return false;
        if (audience === 'nonadmins' && (!context.isAuthenticated || isAdmin)) return false;
        if (audience === 'kids' && !isKid) return false;
        if (audience === 'nonkids' && isKid) return false;

        if (normalizeList(a.includeDeviceTypes).length && !listHasValue(a.includeDeviceTypes, context.deviceType)) return false;
        if (listHasValue(a.excludeDeviceTypes, context.deviceType)) return false;

        if (!explicitlyIncludedUser) {
            if (normalizeList(a.includeUserRoles).length && !hasAnyIntersection(a.includeUserRoles, context.userRoles)) return false;
            if (hasAnyIntersection(a.excludeUserRoles, context.userRoles)) return false;
        }

        const includeLibraries = normalizeList(a.includeLibraryIds, false);
        if (includeLibraries.length && !hasAnyIntersection(includeLibraries, context.libraryIds)) return false;

        const excludeLibraries = normalizeList(a.excludeLibraryIds, false);
        if (excludeLibraries.length && hasAnyIntersection(excludeLibraries, context.libraryIds)) return false;

        return true;
    }

    async function postEvent(path, body) {
        try {
            await fetch(API_BASE + path, {
                method: 'POST',
                credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: body ? JSON.stringify(body) : undefined,
                keepalive: true
            });
        } catch {
            // Ignore analytics transport failures.
        }
    }

    function isLoginPage() {
        const hash = (window.location.hash || '').toLowerCase();
        return hash.indexOf('#/login') >= 0;
    }

    function formatDate(value) {
        if (!value) return null;
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return null;
        return d.toLocaleString();
    }

    function levelLabel(level) {
        const v = (level || 'info').toLowerCase();
        if (v === 'danger') return 'Critical';
        if (v === 'warning') return 'Warning';
        return 'Info';
    }

    function severityRank(level) {
        const v = (level || 'info').toLowerCase();
        if (v === 'danger') return 3;
        if (v === 'warning') return 2;
        return 1;
    }

    function showAnnouncement(a) {
        const level = (a.level || 'info').toLowerCase();
        const startsAt = formatDate(a.startsAt);
        const endsAt = formatDate(a.endsAt);

        const card = document.createElement('section');
        card.className = 'jf-announcement jf-announcement-' + level;
        if (a.id && a.id !== 'preview') card.setAttribute('data-id', a.id);

        const header = document.createElement('header');
        header.className = 'jf-announcement-header';

        const chip = document.createElement('span');
        chip.className = 'jf-announcement-chip';
        chip.textContent = levelLabel(level);

        const close = document.createElement('button');
        close.className = 'jf-announcement-close';
        close.setAttribute('aria-label', 'Dismiss announcement');
        close.textContent = 'x';
        if (a.allowDismiss === false) {
            close.style.display = 'none';
        }
        close.addEventListener('click', () => {
            if (a.id !== 'preview' && a.allowDismiss !== false) {
                markDismissed(a);
                postEvent('/' + encodeURIComponent(a.id) + '/Dismiss');
            }
            stopImpression(a.id);
            card.remove();
            cleanupContainerIfEmpty();
        });

        header.appendChild(chip);
        header.appendChild(close);

        const title = document.createElement('h4');
        title.className = 'jf-announcement-title';
        title.textContent = a.title || 'Announcement';

        const msg = document.createElement('p');
        msg.className = 'jf-announcement-message';
        msg.textContent = a.message || '';

        const meta = document.createElement('div');
        meta.className = 'jf-announcement-meta';
        meta.textContent = startsAt
            ? ('Starts ' + startsAt + (endsAt ? ' | Ends ' + endsAt : ' | No expiry'))
            : (endsAt ? ('Ends ' + endsAt) : 'Active now');

        card.appendChild(header);
        card.appendChild(title);
        card.appendChild(msg);
        card.appendChild(meta);
        
        let container = document.querySelector('.jf-announcement-container');
        if (!container) {
            container = document.createElement('div');
            container.className = 'jf-announcement-container';
            document.body.appendChild(container);
        }
        
        container.insertBefore(card, container.firstChild);

        if (a.id && a.id !== 'preview') {
            postEvent('/' + encodeURIComponent(a.id) + '/View');
            startImpression(a.id);
        }
    }

    function startImpression(id) {
        if (!id || activeImpressionIds.has(id)) return;
        activeImpressionIds.add(id);
        postEvent('/' + encodeURIComponent(id) + '/Impression/Start', { sessionId });
    }

    function stopImpression(id) {
        if (!id || !activeImpressionIds.has(id)) return;
        activeImpressionIds.delete(id);
        postEvent('/' + encodeURIComponent(id) + '/Impression/End', { sessionId });
    }

    function getDisplayedIds() {
        const container = document.querySelector('.jf-announcement-container');
        if (!container) return [];
        return Array.from(container.querySelectorAll('.jf-announcement[data-id]'))
            .map(el => el.getAttribute('data-id'));
    }

    function cleanupContainerIfEmpty() {
        const container = document.querySelector('.jf-announcement-container');
        if (!container) return;
        if (!container.querySelector('.jf-announcement')) {
            container.remove();
        }
    }

    function clearDisplayedAnnouncements() {
        document.querySelectorAll('.jf-announcement[data-id]').forEach(el => {
            const id = el.getAttribute('data-id');
            stopImpression(id);
            el.remove();
        });
        cleanupContainerIfEmpty();
    }

    function getAccessToken() {
        try {
            if (window.ApiClient && typeof window.ApiClient.accessToken === 'function') {
                return String(window.ApiClient.accessToken() || '');
            }
        } catch {
            return '';
        }
        return '';
    }

    function getViewerFingerprint(context) {
        if (!context || !context.isAuthenticated || !context.userId) {
            return 'anon';
        }
        return normalizeId(context.userId) + '|' + getAccessToken();
    }

    function getAuthWatcherState() {
        const token = getAccessToken();
        const user = getCurrentUserInfo();
        const userId = normalizeId(readAny(user, ['Id', 'id']));
        if (!token && !userId) {
            return 'anon';
        }
        return userId + '|' + token;
    }

    function startAuthWatcher() {
        if (authWatcherHandle) return;
        lastAuthWatcherState = getAuthWatcherState();
        authWatcherHandle = window.setInterval(() => {
            const nextState = getAuthWatcherState();
            if (nextState !== lastAuthWatcherState) {
                lastAuthWatcherState = nextState;
                cachedViewerUser = null;
                cachedViewerUserAt = 0;
                cachedViewerToken = '';
                cachedLibraryContext = null;
                cachedLibraryContextAt = 0;
                // Login/logout or token rotation detected: refresh immediately.
                schedulePoll(0);
            }
        }, 1000);
    }

    async function pollAnnouncements() {
        const now = Date.now();
        if (pollInFlight) return;
        if (now - lastPollAt < MIN_POLL_GAP_MS) return;

        pollInFlight = true;
        lastPollAt = now;

        try {
        const list = await fetchAnnouncements();
        const viewerContext = await getViewerContext();
        const viewerFingerprint = getViewerFingerprint(viewerContext);

        if (lastViewerFingerprint !== null && viewerFingerprint !== lastViewerFingerprint) {
            clearDisplayedAnnouncements();
        }
        lastViewerFingerprint = viewerFingerprint;

        const orderedList = list
            .slice()
            .sort((a, b) => {
                const saRank = severityRank(a.level);
                const sbRank = severityRank(b.level);
                if (sbRank !== saRank) return sbRank - saRank;
                const sa = new Date(a.startsAt || 0).getTime();
                const sb = new Date(b.startsAt || 0).getTime();
                return sb - sa;
            });

        // Normalize stored dismiss entries to currently active announcements only.
        // This also drops legacy id-only entries from older script versions.
        const activePermKeys = orderedList
            .filter(a => a.allowDismiss !== false && (a.dismissMode || 'permanent').toLowerCase() !== 'session')
            .map(dismissKey);
        const activeSessionKeys = orderedList
            .filter(a => a.allowDismiss !== false && (a.dismissMode || 'permanent').toLowerCase() === 'session')
            .map(dismissKey);

        const permDismissed = getDismissed(localStorage, STORAGE_KEY_PERM)
            .filter(id => activePermKeys.includes(id));
        setDismissed(localStorage, STORAGE_KEY_PERM, permDismissed);

        const sessionDismissed = getDismissed(sessionStorage, STORAGE_KEY_SESSION)
            .filter(id => activeSessionKeys.includes(id));
        setDismissed(sessionStorage, STORAGE_KEY_SESSION, sessionDismissed);

        const visibleList = orderedList
            .filter(a => matchesTargeting(a, viewerContext))
            .filter(a => !isDismissed(a))
            .filter(a => isLoginPage() ? !!a.showOnLoginPage : true);

        const loginAwareList = isLoginPage()
            ? visibleList.filter(a => !!a.showOnLoginPage)
            : visibleList;
        const visibleIds = loginAwareList.map(a => a.id);

        // Remove banners that are no longer active
        document.querySelectorAll('.jf-announcement[data-id]').forEach(el => {
            const id = el.getAttribute('data-id');
            if (!visibleIds.includes(id)) {
                stopImpression(id);
                el.remove();
            }
        });
        cleanupContainerIfEmpty();

        const displayed = getDisplayedIds();
        const toShow = loginAwareList.filter(a => !displayed.includes(a.id));
        toShow.forEach(a => showAnnouncement(a));
        } finally {
            pollInFlight = false;
        }
    }

    function schedulePoll(delayMs) {
        window.setTimeout(() => {
            pollAnnouncements();
        }, Math.max(0, Number(delayMs) || 0));
    }

    async function init() {
        ensureCssLoaded();
        await pollAnnouncements();
        setInterval(pollAnnouncements, POLL_INTERVAL);
        startAuthWatcher();

        // Startup burst catches post-login hydration without requiring refresh.
        schedulePoll(250);
        schedulePoll(1000);
        schedulePoll(2500);

        // React quickly to SPA route and tab visibility changes.
        window.addEventListener('hashchange', () => schedulePoll(75));
        window.addEventListener('focus', () => schedulePoll(75));
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden) {
                schedulePoll(75);
            }
        });
    }

    window.addEventListener('beforeunload', () => {
        Array.from(activeImpressionIds).forEach(id => stopImpression(id));
        if (authWatcherHandle) {
            window.clearInterval(authWatcherHandle);
            authWatcherHandle = null;
        }
    });

    // Expose showAnnouncement globally for preview
    window.showAnnouncement = showAnnouncement;

    // Keep a short initial delay to avoid racing Jellyfin SPA bootstrap,
    // while still making announcements appear quickly after login/navigation.
    if (document.readyState === 'complete' || document.readyState === 'interactive') {
        setTimeout(init, INIT_DELAY_MS);
    } else {
        document.addEventListener('DOMContentLoaded', () => setTimeout(init, INIT_DELAY_MS));
    }
})();
