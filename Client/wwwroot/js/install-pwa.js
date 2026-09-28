(function () {
    var deferredPrompt = null;

    function isSportsPage() {
        return /^\/Sports\/?$/i.test(window.location.pathname);
    }

    function isInstalled() {
        return window.matchMedia('(display-mode: standalone)').matches
            || window.navigator.standalone === true;
    }

    function showBanner() {
        if (!isSportsPage() || isInstalled()) {
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
        showBanner();
    });

    window.schedulePwaInstallClick = function () {
        if (!deferredPrompt) {
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
