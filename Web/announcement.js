(function () {
    if (document.getElementById('jf-announcements-script')) return;
    const marker = document.createElement('div');
    marker.id = 'jf-announcements-script';
    marker.style.display = 'none';
    document.documentElement.appendChild(marker);

    const API_BASE = '/Plugins/Announcements';
    const API_PATH = API_BASE;
    const STORAGE_KEY_PERM = 'announcements.dismissed.permanent.v2';
    const STORAGE_KEY_SESSION = 'announcements.dismissed.session.v2';
    const SESSION_KEY = 'announcements.runtime.sessionId';
    const POLL_INTERVAL = 3000; // 3 seconds for faster visibility updates
    const INIT_DELAY_MS = 400;
    const MIN_POLL_GAP_MS = 700;
    const activeImpressionIds = new Set();
    let pollInFlight = false;
    let lastPollAt = 0;
    let lastViewerFingerprint = null;
    let authGeneration = 0;
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
        try { storage.setItem(key, JSON.stringify(ids.slice(-512))); }
        catch { /* Storage can be unavailable or full; dismissal still closes the card. */ }
    }

    // The server scopes this opaque content revision to the verified viewer.
    function dismissKey(a) {
        return a && /^[A-F0-9]{64}$/i.test(a.dismissalKey || '') ? a.dismissalKey : '';
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

    async function fetchAnnouncements(token) {
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 10000);
        try {
            const res = await fetch(API_PATH, {
                credentials: 'include', headers: getApiAuthHeaders({}, token),
                cache: 'no-store', signal: controller.signal
            });
            if (res.status === 401 || res.status === 403) return [];
            if (!res.ok) return null;
            const list = await res.json();
            return Array.isArray(list) ? list : null;
        } catch {
            return null;
        } finally {
            window.clearTimeout(timeout);
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

    function matchesTargeting(a, context) {
        // Identity and library authorization have already run on the server.
        if (normalizeList(a.includeDeviceTypes).length && !listHasValue(a.includeDeviceTypes, context.deviceType)) return false;
        return !listHasValue(a.excludeDeviceTypes, context.deviceType);
    }

    async function postEvent(path, body) {
        try {
            await fetch(API_BASE + path, {
                method: 'POST',
                credentials: 'include',
                headers: getApiAuthHeaders({ 'Content-Type': 'application/json' }),
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
        card.setAttribute('data-revision', dismissKey(a));
        if (a.id && a.id !== 'preview') card.setAttribute('data-id', a.id);

        const header = document.createElement('header');
        header.className = 'jf-announcement-header';

        const chip = document.createElement('span');
        chip.className = 'jf-announcement-chip';
        chip.textContent = levelLabel(level);

        const close = document.createElement('button');
        close.className = 'jf-announcement-close';
        close.setAttribute('aria-label', 'Dismiss announcement');
        close.textContent = '\u00d7';
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
            ? ('Starts ' + startsAt + (endsAt ? ' • Ends ' + endsAt : ' • No expiry'))
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

    function getApiAuthHeaders(extra, token = getAccessToken()) {
        const headers = Object.assign({}, extra || {});
        if (token) {
            // Jellyfin 12 requires the standard authorization header unless legacy
            // authentication is explicitly enabled. Keep X-Emby-Token for 10.11.
            headers.Authorization = 'MediaBrowser Token="' + encodeURIComponent(token) + '"';
            headers['X-Emby-Token'] = token;
        }
        return headers;
    }

    function getAuthWatcherState() {
        return getAccessToken();
    }

    function startAuthWatcher() {
        if (authWatcherHandle) return;
        lastAuthWatcherState = getAuthWatcherState();
        authWatcherHandle = window.setInterval(() => {
            const nextState = getAuthWatcherState();
            if (nextState !== lastAuthWatcherState) {
                lastAuthWatcherState = nextState;
                authGeneration++;
                clearDisplayedAnnouncements();
                lastPollAt = 0;
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

        const token = getAccessToken();
        const generation = authGeneration;
        try {
        const list = await fetchAnnouncements(token);
        if (token !== getAccessToken() || generation !== authGeneration) {
            clearDisplayedAnnouncements();
            return;
        }
        if (lastViewerFingerprint !== null && token !== lastViewerFingerprint) {
            clearDisplayedAnnouncements();
        }
        lastViewerFingerprint = token;
        if (list === null) return;
        const viewerContext = { deviceType: detectDeviceType() };

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
            const current = loginAwareList.find(a => a.id === id);
            if (!visibleIds.includes(id) || el.getAttribute('data-revision') !== dismissKey(current)) {
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
            if (token !== getAccessToken() || generation !== authGeneration) {
                lastPollAt = 0;
                schedulePoll(0);
            }
        }
    }

    function schedulePoll(delayMs) {
        window.setTimeout(() => {
            pollAnnouncements();
        }, Math.max(0, Number(delayMs) || 0));
    }

    function init() {
        // Remove legacy keys containing complete announcement messages.
        try { localStorage.removeItem('announcements.dismissed.permanent'); } catch {}
        try { sessionStorage.removeItem('announcements.dismissed.session'); } catch {}
        ensureCssLoaded();
        // Start observing authentication before the initial request. On Jellyfin Web,
        // the script loads on the anonymous login route and the SPA then hydrates the
        // user session without a page load. Do not let an anonymous request delay the
        // watcher that handles that transition.
        startAuthWatcher();
        pollAnnouncements();
        setInterval(pollAnnouncements, POLL_INTERVAL);

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
