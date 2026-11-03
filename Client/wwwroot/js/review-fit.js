// Photo review shot page: sizes the two-panel workspace to the space left under the page's own chrome (sub-nav,
// identity bar, any banner), so the captured photo and the reference strip fit the screen whatever that chrome is.
// The CSS does the rest; this only writes --pr-workspace-h on the workspace element.
let current = null;

const MIN_HEIGHT = 360;
const BOTTOM_GAP = 8;

export function watch(workspace) {
    unwatch();
    const fit = () => {
        const top = workspace.getBoundingClientRect().top;
        const available = Math.floor(window.innerHeight - top - BOTTOM_GAP);
        workspace.style.setProperty('--pr-workspace-h', Math.max(MIN_HEIGHT, available) + 'px');
    };
    const observer = new ResizeObserver(fit);
    observer.observe(workspace.parentElement ?? workspace);
    window.addEventListener('resize', fit);
    current = { observer, fit };
    fit();
}

export function unwatch() {
    if (!current) return;
    current.observer.disconnect();
    window.removeEventListener('resize', current.fit);
    current = null;
}
