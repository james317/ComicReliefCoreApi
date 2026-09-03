// Generic app-launch splash, decoupled from whatever page content loads underneath it -
// originally lived inside app.js (the Comic Vine page, since retired), moved here since the
// splash itself was never Comic-Vine-specific. Exposes window.dismissSplash() for the
// page's own data-loading script to call once its initial load settles, same as before.
(() => {
  const splashEl = document.getElementById("splash");
  const splashSkipBtn = document.getElementById("splashSkip");
  if (!splashEl) return;

  // How long the splash lingers (minimum) before auto-dismissing, in seconds.
  // Defaults to DEFAULT_SPLASH_SECONDS; override once via ?splashSeconds=N in the URL
  // (e.g. bookmark /index.html?splashSeconds=5) and it's remembered from then on via
  // localStorage - no settings page needed for a single-purpose preference like this.
  const DEFAULT_SPLASH_SECONDS = 3;
  const SPLASH_SECONDS_KEY = "splashSeconds";

  function getSplashSeconds() {
    const fromQuery = new URLSearchParams(location.search).get("splashSeconds");
    if (fromQuery !== null) {
      const parsed = Number(fromQuery);
      if (Number.isFinite(parsed) && parsed >= 0) {
        try { localStorage.setItem(SPLASH_SECONDS_KEY, String(parsed)); } catch { /* ignore */ }
        return parsed;
      }
    }
    try {
      const rawStored = localStorage.getItem(SPLASH_SECONDS_KEY);
      const stored = rawStored === null ? NaN : Number(rawStored);
      if (Number.isFinite(stored) && stored >= 0) return stored;
    } catch { /* ignore */ }
    return DEFAULT_SPLASH_SECONDS;
  }

  const SPLASH_MIN_MS = getSplashSeconds() * 1000;
  const splashShownAt = Date.now();
  let splashDismissed = false;

  function hideSplashNow() {
    if (splashDismissed) return;
    splashDismissed = true;
    splashEl.classList.add("splash-hidden");
  }

  window.dismissSplash = function dismissSplash() {
    if (splashDismissed) return;
    const elapsed = Date.now() - splashShownAt;
    const wait = Math.max(0, SPLASH_MIN_MS - elapsed);
    setTimeout(hideSplashNow, wait);
  };

  splashSkipBtn?.addEventListener("click", hideSplashNow);
})();
