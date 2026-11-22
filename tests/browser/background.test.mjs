import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";

const source = fs.readFileSync(new URL("../../browser-extension/background.js", import.meta.url), "utf8");
const manifest = JSON.parse(fs.readFileSync(new URL("../../browser-extension/manifest.json", import.meta.url), "utf8"));
const event = () => ({ listeners: [], addListener(listener) { this.listeners.push(listener); } });
const stored = { token: "a-token-with-at-least-24-characters", instanceId: "test-installation", port: 47831 };
let window = { focused: true, incognito: false, type: "normal", id: 1 };
let tab = { active: true, incognito: false, url: "https://user:secret@example.com/path?q=hello#section", title: "A page" };
const requests = [];
let releaseFetch;
let blockedFetch;
let networkFailure = false;
let incognitoAllowed = true;
let tabQueries = 0;
let clock = 1_700_000_000_000;
class TestDate extends Date { static now() { return ++clock; } }
function sensitivePrivateTab() {
  return { active: true, incognito: true,
    get url() { throw new Error("A private tab URL was accessed"); },
    get title() { throw new Error("A private tab title was accessed"); }
  };
}
const chrome = {
  extension: { async isAllowedIncognitoAccess() { return incognitoAllowed; } },
  storage: { local: { async get() { return { ...stored }; }, async set(value) { Object.assign(stored, value); } }, onChanged: event() },
  action: { async setBadgeText() {}, async setBadgeBackgroundColor() {}, onClicked: event() },
  windows: { async getLastFocused() { return { ...window }; }, onFocusChanged: event(), onRemoved: event() },
  tabs: { async query() { tabQueries++; return tab ? [tab] : []; }, onActivated: event(), onUpdated: event(), onRemoved: event() },
  alarms: { async create() {}, onAlarm: event() },
  runtime: { onInstalled: event(), onStartup: event(), onMessage: event(), async openOptionsPage() {} }
};
const context = vm.createContext({ chrome, crypto: { randomUUID: () => "new-installation" }, navigator: { userAgent: "Chrome/130" }, URL, AbortSignal, Date: TestDate, setInterval() {},
  async fetch(url, options) {
    requests.push({ url, payload: JSON.parse(options.body), headers: options.headers });
    if (blockedFetch) await blockedFetch;
    if (networkFailure) throw new Error("Test desktop unavailable");
    return { ok: true };
  }
});
vm.runInContext(source.replace("void start();", ""), context);
const report = () => vm.runInContext("report()", context);
assert.equal(manifest.incognito, "spanning", "Private-window indicators require the spanning manifest mode");
await report();
assert.equal(requests.length, 1);
assert.equal(requests.at(-1).payload.focused, true);
assert.equal(requests.at(-1).payload.url, "https://example.com/path");
assert.equal(requests.at(-1).headers.Authorization, `Bearer ${stored.token}`);
assert.equal(stored.lastReportState, "active");
assert.equal(stored.lastReportBrowser, "chrome");
assert.equal(stored.lastReportedDomain, "example.com");
assert.equal(stored.lastActiveDomain, "example.com");
assert.equal(typeof stored.lastReportAt, "number");
assert.equal(typeof stored.lastActiveReportAt, "number");
const lastPublicDomain = stored.lastActiveDomain;
const lastPublicAt = stored.lastActiveReportAt;
tab = sensitivePrivateTab();
await report();
assert.equal(requests.at(-1).payload.focused, true);
assert.equal(requests.at(-1).payload.incognito, true);
assert.equal(requests.at(-1).payload.url, "");
assert.equal(requests.at(-1).payload.title, "");
assert.equal(stored.lastReportState, "private");
assert.equal(stored.lastReportedDomain, "");
assert.equal(stored.lastActiveDomain, lastPublicDomain);
assert.equal(stored.lastActiveReportAt, lastPublicAt);
tab = { active: true, incognito: false, url: "https://example.com/path", title: "A page" };
window.focused = false;
await report();
assert.equal(requests.at(-1).payload.focused, false);
assert.equal(stored.lastReportState, "unfocused");
window.focused = true;
tab.url = "chrome://settings";
await report();
assert.equal(requests.at(-1).payload.focused, false);
assert.equal(requests.at(-1).payload.title, "");
assert.equal(stored.lastReportState, "unsupported-page");
window.incognito = true;
tab = sensitivePrivateTab();
const beforePrivateQueries = tabQueries;
await report();
assert.equal(tabQueries, beforePrivateQueries, "A private window must be reported without querying any tabs");
assert.equal(requests.at(-1).payload.focused, true);
assert.equal(requests.at(-1).payload.incognito, true);
assert.equal(requests.at(-1).payload.url, "");
assert.equal(requests.at(-1).payload.title, "");
assert.equal(stored.lastReportState, "private");
incognitoAllowed = false;
await report();
assert.equal(tabQueries, beforePrivateQueries, "A private window without permission must not query tabs");
assert.equal(requests.at(-1).payload.focused, false);
assert.equal(requests.at(-1).payload.incognito, false);
assert.equal(requests.at(-1).payload.url, "");
assert.equal(requests.at(-1).payload.title, "");
assert.equal(stored.lastReportState, "private-permission-required");
assert.equal(stored.lastReportedDomain, "");
incognitoAllowed = true;

// Private navigation events must not inspect URL/title changes before reporting a generic flag.
const beforePrivateUpdate = requests.length;
const privateChange = {
  get url() { throw new Error("A private navigation URL was accessed"); },
  get title() { throw new Error("A private navigation title was accessed"); }
};
chrome.tabs.onUpdated.listeners[0](5, privateChange, sensitivePrivateTab());
for (let n = 0; n < 20 && requests.length === beforePrivateUpdate; n++) await new Promise(resolve => setImmediate(resolve));
assert.equal(requests.length, beforePrivateUpdate + 1);
for (let n = 0; n < 20 && stored.lastReportState !== "private"; n++) await new Promise(resolve => setImmediate(resolve));
assert.equal(stored.lastReportState, "private");
await new Promise(resolve => setImmediate(resolve));
window.focused = false;
await report();
assert.equal(tabQueries, beforePrivateQueries, "An unfocused private window must not query any tabs");
assert.equal(requests.at(-1).payload.focused, false);
assert.equal(requests.at(-1).payload.url, "");
assert.equal(stored.lastReportedDomain, "");
window.focused = true;
window.incognito = false;
for (const type of ["normal", "popup", "app"]) {
  window.type = type;
  tab = { active: true, incognito: false, url: "https://example.com/desktop?secret=value#fragment", title: "A page" };
  await report();
  assert.equal(stored.lastReportState, "active", `${type} HTTP(S) windows should report`);
  assert.equal(requests.at(-1).payload.focused, true);
  assert.equal(requests.at(-1).payload.url, "https://example.com/desktop");
  const publicAt = stored.lastActiveReportAt;
  window.incognito = true;
  tab = sensitivePrivateTab();
  const queries = tabQueries;
  await report();
  assert.equal(stored.lastReportState, "private", `${type} private windows should report only an indicator`);
  assert.equal(tabQueries, queries);
  assert.equal(requests.at(-1).payload.focused, true);
  assert.equal(requests.at(-1).payload.incognito, true);
  assert.equal(requests.at(-1).payload.url, "");
  assert.equal(requests.at(-1).payload.title, "");
  assert.equal(stored.lastReportedDomain, "");
  assert.equal(stored.lastActiveReportAt, publicAt);
  window.incognito = false;
  await report();
  assert.equal(stored.lastReportState, "private", `${type} private tabs must not expose URL or title getters`);
  assert.equal(requests.at(-1).payload.focused, true);
  assert.equal(requests.at(-1).payload.incognito, true);
  assert.equal(requests.at(-1).payload.url, "");
  assert.equal(requests.at(-1).payload.title, "");
  assert.equal(stored.lastActiveReportAt, publicAt);
  tab = { active: true, incognito: false, url: "edge://settings", title: "Settings" };
  await report();
  assert.equal(stored.lastReportState, "unsupported-page", `${type} internal pages should clear`);
  assert.equal(requests.at(-1).payload.url, "");
}
window.type = "devtools";
await report();
assert.equal(stored.lastReportState, "unsupported-window");
assert.equal(requests.at(-1).payload.url, "");
window.type = "normal";
tab = null;
await report();
assert.equal(stored.lastReportState, "no-active-tab");
assert.equal(requests.at(-1).payload.url, "");

// Edge source detection must report msedge, otherwise native Edge lookups never find it.
context.navigator.userAgent = "Mozilla/5.0 Chrome/130 Safari/537.36 Edg/130.0.0.0";
delete stored.browser;
tab = { active: true, incognito: false, url: "https://edge.example/path?private=value#secret", title: "Edge page" };
await report();
assert.equal(requests.at(-1).payload.browser, "msedge");
assert.equal(stored.lastReportBrowser, "msedge");
assert.equal(stored.lastReportedDomain, "edge.example");
assert.equal(requests.at(-1).payload.url, "https://edge.example/path");

// Focus changes arriving during an HTTP request must be queued instead of dropped.
const queueStart = requests.length;
blockedFetch = new Promise(resolve => { releaseFetch = resolve; });
const firstReport = report();
while (requests.length === queueStart) await new Promise(resolve => setImmediate(resolve));
window.focused = false;
await report();
blockedFetch = undefined;
releaseFetch();
await firstReport;
for (let n = 0; n < 20 && requests.length < queueStart + 2; n++) await new Promise(resolve => setImmediate(resolve));
assert.equal(requests.length, queueStart + 2);
assert.equal(requests.at(-1).payload.focused, false);
for (let n = 0; n < 20 && stored.lastReportState !== "unfocused"; n++) await new Promise(resolve => setImmediate(resolve));
assert.equal(stored.lastReportState, "unfocused");

// Connection failure must be visible without replacing the last successful activity report.
const lastSuccess = stored.lastReportAt;
networkFailure = true;
await report();
assert.equal(stored.lastReportAt, lastSuccess);
assert.match(stored.lastError, /unavailable/);
networkFailure = false;
const before = requests.length;
stored.token = "";
await report();
assert.equal(requests.length, before);
console.log("Extension: authentication, URL sanitation, diagnostics, normal/popup/app windows, privacy-safe private indicators without URL/title access, inactive/unsupported states, Edge attribution, queued focus changes, and failures passed.");
