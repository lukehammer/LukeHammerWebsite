window.scheduleGoingPrefs = {
  storageKeyPrefix: 'scheduleGoingSelection',

  scopeKey: function (signedInName) {
    var scope = (signedInName || '').trim();
    if (!scope) {
      return 'guest';
    }

    return scope.toLowerCase();
  },

  storageKey: function (signedInName) {
    return this.storageKeyPrefix + ':' + this.scopeKey(signedInName);
  },

  load: function (signedInName) {
    try {
      var raw = localStorage.getItem(this.storageKey(signedInName));
      if (!raw) {
        return null;
      }

      return JSON.parse(raw);
    } catch (e) {
      return null;
    }
  },

  save: function (signedInName, prefs) {
    try {
      localStorage.setItem(this.storageKey(signedInName), JSON.stringify(prefs));
    } catch (e) {
      // localStorage unavailable
    }
  }
};
