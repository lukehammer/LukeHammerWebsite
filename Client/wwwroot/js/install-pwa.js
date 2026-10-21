(function () {
    var deferredPrompt = null;
    var deferredPromptManifest = null;

    function currentManifest() {
        var link = document.querySelector('link[rel="manifest"]');
        return link ? link.href : null;
    }

    function isInstallablePage() {
        return /^\/Sports\/?$/i.test(window.location.pathname)
            || /^\/FamilyEvents(\/|$)/i.test(window.location.pathname);
    }

    function isInstalled() {
        return window.matchMedia('(display-mode: standalone)').matches
            || window.navigator.standalone === true;
    }

    function showBanner() {
        if (!isInstallablePage() || isInstalled() || deferredPromptManifest !== currentManifest()) {
            return;
        }

        var banner = document.getElementById('schedule-pwa-install-banner');
        if (banner) {
            banner.hidden = false;
        }
    }

    function hideBanner() {
        var banner = document.getElementById('schedule-pwa-install-banner');
        if (banner) {
            banner.hidden = true;
        }
    }

    window.addEventListener('beforeinstallprompt', function (event) {
        event.preventDefault();
        deferredPrompt = event;
        deferredPromptManifest = currentManifest();
        showBanner();
    });

    window.schedulePwaInstallClick = function () {
        if (!deferredPrompt || deferredPromptManifest !== currentManifest()) {
            return;
        }

        deferredPrompt.prompt();
        deferredPrompt.userChoice.finally(function () {
            deferredPrompt = null;
            hideBanner();
        });
    };

    window.schedulePwaInstallDismiss = function () {
        hideBanner();
    };

    window.schedulePwaTryShowBanner = function () {
        if (deferredPrompt) {
            showBanner();
        }
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', window.schedulePwaTryShowBanner);
    } else {
        window.schedulePwaTryShowBanner();
    }
})();
