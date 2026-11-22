/* All activity goes to the paired loopback desktop app. Nothing is sent to a remote server. */
const DEFAULT_PORT = 47831;
let sending = false;
let pending = false;

async function config() {
  const value = await chrome.storage.local.get(["token", "port", "browser", "instanceId"]);
  if (!value.instanceId) {
    value.instanceId = crypto.randomUUID();
    await chrome.storage.local.set({ instanceId: value.instanceId });
  }
  value.port = Number(value.port) || DEFAULT_PORT;
  value.browser = value.browser || (navigator.userAgent.includes("Edg/") ? "msedge" : "chrome");
  return value;
}

async function report() {
  if (sending) { pending = true; return; }
  sending = true;
  try {
    const settings = await config();
    if (!settings.token) {
      await chrome.storage.local.set({ connectionStatus: "Not paired", lastError: "", lastReportState: "unpaired" });
      await chrome.action.setBadgeText({ text: "PAIR" });
      await chrome.action.setBadgeBackgroundColor({ color: "#bd8529" });
      return;
    }
    let payload = { browser: settings.browser, instanceId: settings.instanceId, focused: false, url: "", title: "", incognito: false, reason: "unfocused" };
    const window = await chrome.windows.getLastFocused();
    payload.observedAtUnixMs = Date.now();
    const supportedWindow = window && ["normal", "popup", "app"].includes(window.type);
    if (!window?.focused) {
      // Focus loss clears a previous private flag, too. No private tab metadata is read.
    }
    else if (!supportedWindow) payload.reason = "unsupported-window";
    else if (window.incognito) {
      // Check the window flag BEFORE asking for tabs. Private mode never needs a tab's URL/title.
      let allowed = false;
      try { allowed = await chrome.extension.isAllowedIncognitoAccess(); } catch {}
      payload = { ...payload, focused: allowed, incognito: allowed, reason: allowed ? "private" : "private-permission-required" };
    }
    else if (window?.focused && supportedWindow) {
      const [tab] = await chrome.tabs.query({ active: true, windowId: window.id });
      if (!tab) payload.reason = "no-active-tab";
      else if (tab.incognito) {
        // Defensive fallback: never access private tab address/title fields.
        let allowed = false;
        try { allowed = await chrome.extension.isAllowedIncognitoAccess(); } catch {}
        payload = { ...payload, focused: allowed, incognito: allowed, reason: allowed ? "private" : "private-permission-required" };
      }
      else if (tab.url && /^https?:\/\//i.test(tab.url)) {
        const url = new URL(tab.url);
        url.username = "";
        url.password = "";
        url.search = "";
        url.hash = "";
        payload = { ...payload, focused: true, url: url.href, title: (tab.title || "").slice(0, 2048), reason: "active" };
      } else payload.reason = "unsupported-page";
    }
    const response = await fetch(`http://127.0.0.1:${settings.port}/activity`, {
      method: "POST", mode: "cors", cache: "no-store",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${settings.token}` },
      body: JSON.stringify(payload), signal: AbortSignal.timeout(2500)
    });
    if (!response.ok) throw new Error(response.status === 401 ? "Pairing token does not match." : `Desktop app returned ${response.status}.`);
    const now = Date.now();
    const domain = payload.focused && !payload.incognito ? new URL(payload.url).hostname : "";
    const delivery = { connectionStatus: "Activity reports are reaching the desktop app", lastConnected: now, lastError: "",
      lastReportAt: now, lastReportState: payload.reason, lastReportBrowser: payload.browser, lastReportedDomain: domain };
    if (payload.focused && !payload.incognito) Object.assign(delivery, { lastActiveDomain: domain, lastActiveReportAt: now });
    await chrome.storage.local.set(delivery);
    await chrome.action.setBadgeText({ text: "" });
  } catch (error) {
    await chrome.storage.local.set({ connectionStatus: "Desktop app unavailable", lastError: error.message || String(error) });
    await chrome.action.setBadgeText({ text: "!" });
    await chrome.action.setBadgeBackgroundColor({ color: "#bd8529" });
  } finally {
    sending = false;
    if (pending) { pending = false; void report(); }
  }
}

chrome.action.onClicked.addListener(() => chrome.runtime.openOptionsPage());
chrome.tabs.onActivated.addListener(report);
chrome.tabs.onUpdated.addListener((_id, change, tab) => {
  if (tab.incognito) { if (tab.active) void report(); return; }
  if (tab.active && (change.url || change.status === "complete" || change.title)) void report();
});
chrome.tabs.onRemoved.addListener(report);
chrome.windows.onFocusChanged.addListener(report);
chrome.windows.onRemoved.addListener(report);
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && (changes.token || changes.port || changes.browser)) void report();
});
chrome.runtime.onMessage.addListener((message, _sender, respond) => {
  if (message?.type !== "activity-ledger-report-now") return;
  report().then(() => respond({ ok: true })).catch(() => respond({ ok: false }));
  return true;
});
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === "activity-ledger-heartbeat") void report(); });
async function start() {
  await chrome.alarms.create("activity-ledger-heartbeat", { periodInMinutes: 0.5 });
  await report();
}
chrome.runtime.onInstalled.addListener(start);
chrome.runtime.onStartup.addListener(start);
// Extension API calls in report keep the MV3 worker alive while available; alarms recover after suspension.
setInterval(report, 2000);
void start();
