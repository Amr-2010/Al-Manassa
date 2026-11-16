(function () {
  function hideSplash() {
    var splash = document.getElementById('almanassa-splash');
    if (splash) splash.classList.add('splash-hidden');
  }
  if (document.readyState === 'complete') hideSplash();
  else window.addEventListener('load', hideSplash, { once: true });
  // Safety fallback if optional assets are slow; do not trap the user on the loading overlay.
  setTimeout(hideSplash, 12000);
})();
