const form = document.querySelector("#settings");
const token = document.querySelector("#token");
const port = document.querySelector("#port");
const browser = document.querySelector("#browser");
const status = document.querySelector("#status");
const button = form.querySelector("button");
const stateLabels = {
  active: "Active website delivered", unfocused: "Browser is not the focused window", private: "Private-mode flag delivered — no address or title",
  "private-permission-required": "Private-mode flags unavailable — allow the extension in private mode first",
  "private-stale": "Private-mode observation expired before delivery",
  "unsupported-page": "Browser or extension page excluded", "no-active-tab": "No active tab available", "unsupported-window": "This browser window type is excluded",
  unpaired: "Pairing token is not configured"
};
function setStatus(message, kind = "") { status.textContent = message; status.className = kind; }

async function load() {
  const value = await chrome.storage.local.get(["token", "port", "browser", "connectionStatus", "lastConnected"]);
  token.value = value.token || "";
  port.value = value.port || 47831;
  browser.value = value.browser || (navigator.userAgent.includes("Edg/") ? "msedge" : "chrome");
  if (value.lastConnected) setStatus(`Last activity connection: ${new Date(value.lastConnected).toLocaleString()}`);
  await refreshDiagnostics();
}

async function refreshDiagnostics() {
  const value = await chrome.storage.local.get(["lastReportAt", "lastReportState", "lastReportBrowser", "lastActiveDomain", "lastActiveReportAt", "lastError"]);
  document.querySelector("#report-browser").textContent = value.lastReportBrowser === "msedge" ? "Microsoft Edge" : value.lastReportBrowser === "chrome" ? "Google Chrome" : "Not reported yet";
  const stale = value.lastReportAt && Date.now() - value.lastReportAt > 8000;
  document.querySelector("#report-at").textContent = value.lastReportAt ? `${new Date(value.lastReportAt).toLocaleString()}${stale ? " — heartbeat is stale" : ""}` : "No activity report received";
  document.querySelector("#report-state").textContent = stateLabels[value.lastReportState] || "Not reported yet";
  document.querySelector("#active-domain").textContent = value.lastActiveDomain || "No active website reported yet";
  document.querySelector("#active-at").textContent = value.lastActiveReportAt ? new Date(value.lastActiveReportAt).toLocaleString() : "Not reported yet";
  document.querySelector("#delivery-error").textContent = value.lastError ? `Last delivery error: ${value.lastError}` : "";
  let privateAllowed = false;
  try { privateAllowed = await chrome.extension.isAllowedIncognitoAccess(); } catch {}
  document.querySelector("#incognito-allowed").checked = privateAllowed;
  document.querySelector("#incognito-access").textContent = privateAllowed ? "Enabled — flags only, no private page details" : "Off — enable Allow in InPrivate / Incognito in extension Details";
}
document.querySelector("#refresh-status").addEventListener("click", async () => {
  await chrome.runtime.sendMessage({ type: "activity-ledger-report-now" }).catch(() => {});
  await refreshDiagnostics();
});
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && (changes.lastReportAt || changes.lastReportState || changes.lastError)) void refreshDiagnostics();
});
setInterval(refreshDiagnostics, 2000);
form.addEventListener("submit", async event => {
  event.preventDefault();
  const secret = token.value.trim();
  const localPort = Number(port.value);
  if (secret.length < 24 || !Number.isInteger(localPort) || localPort < 1024 || localPort > 65535) {
    setStatus("Enter the desktop pairing token and a valid local port.", "error"); return;
  }
  button.disabled = true;
  setStatus("Connecting to your desktop app…");
  try {
    await chrome.storage.local.set({ token: secret, port: localPort, browser: browser.value });
    const response = await fetch(`http://127.0.0.1:${localPort}/health`, {
      // Privileged extension GET requests can omit Origin. POST consistently includes it,
      // allowing the desktop app to retain strict extension-origin validation.
      method: "POST", mode: "cors", cache: "no-store",
      headers: { Authorization: `Bearer ${secret}`, "Content-Type": "application/json" },
      body: "{}", signal: AbortSignal.timeout(3500)
    });
    if (response.status === 401) throw new Error("Pairing token does not match. Copy it again from the desktop app.");
    if (!response.ok) throw new Error(`Desktop app returned ${response.status}.`);
    const data = await response.json();
    if (data.app !== "ActivityLedger") throw new Error("This port belongs to another application.");
    setStatus("Paired with the desktop app. Check Activity delivery below after visiting a website.", "good");
    await chrome.runtime.sendMessage({ type: "activity-ledger-report-now" }).catch(() => {});
    await refreshDiagnostics();
  } catch (error) {
    setStatus(error.message?.includes("token") ? error.message : "Could not connect. Open Activity Ledger, check the token and port, then try again.", "error");
  } finally { button.disabled = false; }
});
void load();
