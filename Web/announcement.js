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
    const POLL_INTERVAL = 5000; // 5 seconds
    const activeImpressionIds = new Set();

    function getSessionId() {
        try {
            const existing = sessionStorage.getItem(SESSION_KEY);
            if (existing) return existing;
            const created = 'sess-' + Date.now() + '-' + Math.random().toString(36).slice(2, 10);
            sessionStorage.setItem(SESSION_KEY, created);
            return created;
        } catch {
            return 'sess-fallback';
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

    async function pollAnnouncements() {
        const list = await fetchAnnouncements();
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

        const displayed = getDisplayedIds();
        const toShow = loginAwareList.filter(a => !displayed.includes(a.id));
        toShow.forEach(a => showAnnouncement(a));
    }

    async function init() {
        ensureCssLoaded();
        await pollAnnouncements();
        setInterval(pollAnnouncements, POLL_INTERVAL);
    }

    window.addEventListener('beforeunload', () => {
        Array.from(activeImpressionIds).forEach(id => stopImpression(id));
    });

    // Expose showAnnouncement globally for preview
    window.showAnnouncement = showAnnouncement;

    // Use a longer initial delay so Jellyfin's SPA has time to boot
    if (document.readyState === 'complete' || document.readyState === 'interactive') {
        setTimeout(init, 2000);
    } else {
        document.addEventListener('DOMContentLoaded', () => setTimeout(init, 2000));
    }
})();
