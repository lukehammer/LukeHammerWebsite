// Photo Shoot planner helpers (loaded as an ES module only by /photoshoot pages).

let wakeLock = null;
let wantAwake = false;

async function requestWakeLock() {
    if (!wantAwake || !('wakeLock' in navigator) || document.visibilityState !== 'visible') {
        return false;
    }
    try {
        wakeLock = await navigator.wakeLock.request('screen');
        wakeLock.addEventListener('release', () => { wakeLock = null; });
        return true;
    } catch {
        return false;
    }
}

export async function keepAwake(on) {
    wantAwake = !!on;
    if (wantAwake) {
        return await requestWakeLock();
    }
    if (wakeLock) {
        try { await wakeLock.release(); } catch { /* already released */ }
        wakeLock = null;
    }
    return false;
}

export function isWakeLockSupported() {
    return 'wakeLock' in navigator;
}

const watchers = new Set();

function notifyAll(reason) {
    for (const ref of watchers) {
        ref.invokeMethodAsync('OnBrowserWake', reason).catch(() => { });
    }
}

document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') {
        requestWakeLock();
        notifyAll('visible');
    }
});
window.addEventListener('online', () => notifyAll('online'));
window.addEventListener('offline', () => notifyAll('offline'));

export function watch(dotnetRef) {
    watchers.add(dotnetRef);
    return navigator.onLine;
}

export function unwatch(dotnetRef) {
    watchers.delete(dotnetRef);
}

export function downloadText(fileName, text, mimeType) {
    const blob = new Blob([text], { type: mimeType || 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function printPage() {
    window.print();
}

export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        try {
            const area = document.createElement('textarea');
            area.value = text;
            area.setAttribute('readonly', '');
            area.style.position = 'fixed';
            area.style.left = '-9999px';
            document.body.appendChild(area);
            area.select();
            const ok = document.execCommand('copy');
            area.remove();
            return ok;
        } catch {
            return false;
        }
    }
}

export function scrollToTop() {
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

let shotKeyHandler = null;

export function bindShotKeys(dotnetRef) {
    unbindShotKeys();
    shotKeyHandler = (event) => {
        const tag = event.target && event.target.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') {
            return;
        }

        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight' || event.key === 'Home' || event.key === 'End') {
            event.preventDefault();
            dotnetRef.invokeMethodAsync('OnShotKey', event.key);
        }
    };
    window.addEventListener('keydown', shotKeyHandler);
}

export function unbindShotKeys() {
    if (shotKeyHandler) {
        window.removeEventListener('keydown', shotKeyHandler);
        shotKeyHandler = null;
    }
}
