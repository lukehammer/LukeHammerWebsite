window.scheduleAttendanceShare = {
    /** True when the browser can show the phone’s system share sheet (Messages, WhatsApp, etc.). */
    canUseNativeShare: function () {
        return typeof navigator !== 'undefined'
            && typeof navigator.share === 'function';
    },

    /**
     * Opens the OS share sheet when available; otherwise copies text to the clipboard.
     * This site never sends messages itself.
     */
    shareText: async function (text) {
        if (!text) {
            return 'failed';
        }

        if (window.scheduleAttendanceShare.canUseNativeShare()) {
            try {
                await navigator.share({ text: text });
                return 'shared';
            } catch (err) {
                if (err && err.name === 'AbortError') {
                    return 'cancelled';
                }
            }
        }

        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                await navigator.clipboard.writeText(text);
                return 'copied';
            }
        } catch (e) {
            // fall through
        }

        return 'failed';
    }
};
