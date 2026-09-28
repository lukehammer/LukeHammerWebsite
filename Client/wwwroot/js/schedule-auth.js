window.scheduleAuth = {
  storageKey: 'scheduleAdminFirstName',

  getName: function () {
    try {
      return sessionStorage.getItem(this.storageKey) || '';
    } catch {
      return '';
    }
  },

  setName: function (name) {
    try {
      if (name) {
        sessionStorage.setItem(this.storageKey, name);
      } else {
        sessionStorage.removeItem(this.storageKey);
      }
    } catch {
      // sessionStorage unavailable
    }
  },

  clearName: function () {
    try {
      sessionStorage.removeItem(this.storageKey);
    } catch {
      // sessionStorage unavailable
    }
  },

  confirmDelete: function (message) {
    return window.confirm(message);
  }
};
