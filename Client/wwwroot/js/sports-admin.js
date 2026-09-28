window.sportsScheduleAdmin = {
  focusFirst: function (element) {
    if (element && typeof element.focus === 'function') {
      element.focus({ preventScroll: true });
    }
  },
  scrollNearest: function (element) {
    if (element && typeof element.scrollIntoView === 'function') {
      element.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
    }
  },
  scrollToEditGroup: function (eventId) {
    if (!eventId) {
      return;
    }
    var row = document.getElementById('event-row-' + eventId);
    var target = row;
    if (target && typeof target.scrollIntoView === 'function') {
      target.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'auto' });
    }
  }
};
