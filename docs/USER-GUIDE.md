# Activity Ledger

A local Windows activity journal showing which app or website you used, when you used it, and how long it stayed active. Version **2.0.0** packages the ledger as a standalone installer for multiple Windows computers, with a first-run setup guide and optional private cloud viewing. Private-browser flags remain free of private page addresses and titles. It includes a per-user installer, automatic recording after Windows sign-in, and a password for recording and management controls. It runs on **Windows 10/11, 64-bit Intel or AMD** without administrator access or a separate .NET installation.

## Get started

1. Extract the ZIP and run **ActivityLedgerSetup.exe**.
2. Setup asks you to create a management password before installing for your Windows account. If one already exists, enter it to continue. Keep that password somewhere safe. Setup opens the installed app when finished.
3. Recording begins automatically. Switch to another application to start building your timeline.
4. For website addresses and website totals, connect the included browser extension using the instructions below.

The installed executable is `%LOCALAPPDATA%\Programs\ActivityLedger\ActivityLedger.exe`. Setup adds a Start menu shortcut and an entry in Windows **Installed apps**. Closing the window normally minimizes it to the notification area. Use the tray icon to reopen the journal or access recording controls. The journal remains on your computer after you quit.

Activity Ledger registers itself to run after your Windows account signs in and starts recording on every launch. Startup stays enabled in the app's Settings. A password-authorized pause lasts until you resume or launch the app again. It cannot record while the computer is off, asleep, or before an interactive sign-in, and it does not monitor other Windows accounts.

## Upgrade from an earlier version

Quit the existing copy from its tray icon first; enter its management password if one has already been configured. Then run **ActivityLedgerSetup.exe** from the new ZIP. Setup also checks your current password, or asks you to create one if none exists. It replaces the installed program and preserves your journal, settings, browser pairing token, and existing password.

After upgrading, remove old extracted portable program copies so you do not accidentally run an older version without the new controls. The installer does not find or remove copies you saved elsewhere. Installing an update does not retroactively protect those copies.

Normal website recording continues to work with the previous browser companion. To get **1.2.0 private-window flags**, update the extension files too:

1. Open **Browser setup** with your management password and choose **Extract and open extension folder**.
2. If your browser already loads that same folder, open `edge://extensions` or `chrome://extensions` and choose **Reload** for Activity Ledger. Reloading from the same folder preserves its local pairing settings.
3. If the existing extension was loaded from another folder, copy the five new files (`manifest.json`, `background.js`, `options.html`, `options.js`, `options.css`) into that already loaded folder, then reload it. Alternatively, remove the old extension and load the new folder; a different folder may require pairing again.
4. In the browser's extension **Details**, manually enable **Allow in InPrivate** in Edge or **Allow in Incognito** in Chrome if you want private-window flags. Updating or pairing does not grant this permission automatically.

An updated desktop installation alone does not refresh files in a different extension folder.

## Password-protected controls

Activity Ledger asks for the current management password separately whenever you pause or resume logging, quit, open Settings, open Browser setup, open the data folder from the app, export results, clear the journal, or change the password. Entering it once does not leave management actions unlocked for the rest of the session. Installing an update and uninstalling also require the configured password and ask you to quit the running app first.

You can still view the timeline without entering this password; exporting requires it. Exports cannot overwrite the application's internal files. The password protects the application's own management controls; it does not encrypt the journal or restrict Windows file access. There is no built-in forgotten-password reset. Recovery requires manual management of the local data by someone with sufficient Windows permissions; do not rely on an in-app recovery option.

This application is not a Windows service and has no watchdog. Anyone with sufficient Windows process or filesystem rights may still end its process, disable its Windows startup entry, change or delete local files, or run an older copy. Password prompts cannot prevent those operating-system actions.

The executable is unsigned. Windows may show an unknown-publisher or reputation warning. A locally built executable is also unsigned; inspect the source or obtain a signed build according to your computer's policies.

## What the journal records

- Foreground application sessions, process names, window titles when enabled, start/end times, and duration.
- Active websites in Chrome and Microsoft Edge when the paired extension is running.
- Optional private-browsing sessions with start/end times and approximate duration, using a generic label and no private URL, domain, or page title.
- A searchable timeline and grouped application/website totals for the selected period.
- CSV and JSON exports of the selected results.

Selecting **Websites** selects the website activity filter, so a previously selected Apps filter no longer hides its rows. A range left on **Today** follows the local date after midnight; a manually selected historical or multi-day range stays unchanged.

Private sessions appear as **Private browsing** in **Timeline** (export kind `private`); choose **Private browsing** in the activity filter to find them. The **PRIVATE TIME** card totals these sessions. Their time is also included in active time and application totals, but excluded from website time and the **Websites** view because the site is unknown.

The collector samples the foreground window once per second. It measures the app you are interacting with; it does not enumerate every background process or guarantee that a window opened and closed between samples will appear. It starts recording from now and cannot reconstruct earlier activity. Keeping an app in the foreground counts as active time until the idle limit is reached, even if you are reading or watching a video.

The tracker itself also counts as foreground application time under `ActivityLedger.exe`. Its title is the fixed label `Activity Ledger`; password dialogs and other tracker dialog titles are never captured. Your configured process exclusions still apply.

The default idle limit is **120 seconds** without keyboard or mouse input. Idle time, a locked desktop, a pause, and long gaps such as sleep are not counted as app/site usage. Change the idle threshold in Settings if needed.

## Connect Chrome or Edge

The extension obtains the active tab address using the browser's own API. No browser password database or browser history is read.

1. In Activity Ledger, open **Browser setup**, choose **Copy connection token**, and choose **Extract and open extension folder**. The default bridge port is **47831**. Use Chrome or Edge version 120 or newer.
2. In Chrome, open `chrome://extensions`. In Edge, open `edge://extensions`.
3. Enable **Developer mode**, choose **Load unpacked**, and select the **browser-extension** folder opened by the app (or the one in the download ZIP) containing `manifest.json`.
4. Click the extension's toolbar icon to open its settings. Paste the **Pairing token**, check **Browser** explicitly (**Microsoft Edge** when using Edge; **Google Chrome** when using Chrome), and set **Local port** to match the desktop app. Choose **Save and test connection**.
5. Leave the automatically recording desktop app running, then browse normally. Website rows appear when that browser is in the foreground.

Repeat these steps for each browser profile you want to connect. The extension and application communicate only on your computer through `127.0.0.1`; keep the desktop application running. The token authenticates the connection and should remain private. If a connection fails, check the matching token/port, whether the app is running, and whether another application is using that port.

If another app already uses port 47831, quit Activity Ledger, change `BridgePort` in `%LOCALAPPDATA%\ActivityLedger\settings.json` to an unused port between 1024 and 65535, and reopen the app. Set the same port in the extension options.

Private website addresses, domains, and page titles are never included in the companion's reports or saved journal. With your browser's separate permission, the companion can instead report only that a private window is focused. Internal browser pages, extension pages, and non-HTTP(S) addresses are not recorded as normal websites. Native browser window titles are used transiently for matching normal tabs and are never saved. When title recording is enabled, eligible normal website titles come from the extension. Browsers without the extension are recorded only as applications.

Normal windows, popups, and browser app/PWA windows can report websites when the browser exposes their active HTTP/HTTPS tab to the extension.

## Enable private-window flags

1. Install the updated **1.2.0** desktop app and load/reload the updated companion files as described above.
2. In Edge, open `edge://extensions`, open Activity Ledger's **Details**, and enable **Allow in InPrivate**. In Chrome, use `chrome://extensions` → Activity Ledger **Details** → **Allow in Incognito**. This is an explicit browser permission that you must enable yourself for each profile you use.
3. Keep the paired desktop app running. Focus an InPrivate/Incognito window for several seconds, then return to the tracker and choose **Timeline** → **Private browsing** in the activity filter.

The flag records the presence and approximate duration of a **focused private browser window**, even though its website is unknown. It does not record private URLs, domains, titles, page contents, passwords, or keystrokes. Enabling full website paths or title recording does not add those details to private sessions. Domain exclusions cannot identify an unknown private site; excluding the browser application also excludes its private flags. In companion options, **Private-mode flag access** shows whether permission is enabled; this checkbox is read-only, so change permission in the browser's extension **Details**. **Browser status** shows whether a private report is reaching the desktop.

Without the updated companion and permission, the desktop cannot reliably tell private and normal browser windows apart; it may show only ordinary browser application usage. The older companion continues normal website recording but does not produce private flags. The app cannot reconstruct past private activity, detect a private window merely opened in the background, or guarantee every brief switch is observed. Foreground sampling is about once per second and browser reports about every two seconds, so session boundaries and durations are approximate. Idle, locked, paused, and excluded application time still follows the normal collection rules.

## When pairing succeeds but websites are missing

**Save and test connection** checks pairing and connectivity; it does not prove an active website was delivered or matched to the Windows foreground window. In the extension options, **Activity delivery** shows the reporting browser, latest report, report state, and last active normal website. Make sure **Reporting as** says **Microsoft Edge** for Edge. A settings or extension page is excluded, so visit a regular website for ten seconds, then return to the options to inspect the last active website.

In the desktop app, choose **Browser status** in the bottom action row or tray menu. This read-only view needs no management password. It distinguishes missing reports, stale/inactive reports, private flags, idle time, title mismatches, and excluded activity. Check today's dates and clear the search before reviewing **Websites**. The desktop matches normal tabs to the native browser caption, including Edge grouped-page/profile captions and display-only Unicode differences. Private flags use a generic label instead of a website match.

Choose **Copy diagnostics** to copy the status for troubleshooting. It includes app names, matched website domains, report timing, filter dates, and status messages. It omits the pairing token, raw window/page titles, full website addresses, and the search text. Browser pairing and other management controls still require your password. These fixes cover several possible causes of missing rows; the status results identify which applies on your computer.

## Control your data

Settings let you exclude application process names, such as `passwordmanager.exe`, and website domains, such as `example.com`. Domain exclusions also cover subdomains. Exclusions affect future collection; they do not remove earlier records.

Website recording uses **domain only** by default. Optional full-address recording includes the path but strips the username, password, query string, and fragment. Paths and ordinary application window titles can still contain private information, so choose the detail you want to retain. Window-title recording can be disabled.

The default retention period is **90 days** and is adjustable in Settings. You can export results or clear the journal. CSV exports escape spreadsheet formula prefixes; JSON preserves structured data. Exports create separate files that remain until you delete them.

Your database and settings are stored under:

```text
%LOCALAPPDATA%\ActivityLedger
```

The SQLite database is **not encrypted**. It relies on your Windows account and filesystem permissions. Cloud sync is optional and off by default; when it stays off there is no remote upload or online account requirement. There is no telemetry. Anyone who can access these local files may be able to read them. To make a consistent manual backup, quit Activity Ledger with your management password before copying its data folder.

**Start with Windows** stays enabled in Settings and creates a per-user Windows `Run` entry. It launches the installed application after your Windows account signs in. If Windows or another tool disables that entry, the app cannot run until it is launched again.

To uninstall, quit Activity Ledger using its password-protected tray action, then use its entry in Windows **Installed apps**. The uninstaller verifies your management password and removes the program, shortcut, and startup entry. It preserves `%LOCALAPPDATA%\ActivityLedger`, including your journal, settings, and password. Reinstalling uses that preserved data and requires the same password for protected controls. Remove the data folder separately only if you intentionally want to erase it.

## Build from source

Install the **.NET 10 SDK** on Windows. From this folder, run:

```powershell
.\build.ps1
```

The script publishes a self-contained Windows x64 executable and creates `artifacts\ActivityLedger-win-x64.zip`, containing **ActivityLedgerSetup.exe**, this guide, `QUICKSTART.txt`, the license, and the browser extension. The installer is a copy of the self-contained executable named `ActivityLedgerSetup.exe`; the program selects installer mode from that name. Setup copies itself to the installation folder as `ActivityLedger.exe` and opens the installed app.

For development, the directly runnable executable remains at `artifacts\win-x64\ActivityLedger.exe`. It uses the same per-account data and management password, asking you to create one on first launch if needed. Launching a directly runnable copy also starts recording automatically and registers that path for Windows sign-in, so keep it in a permanent location or use Setup for a standard installation.

Equivalent publish command:

```powershell
dotnet publish .\ActivityLedger.csproj -r win-x64 -c Release --self-contained true -o .\artifacts\win-x64
```

The project also supports publishing from a non-Windows .NET SDK using Windows targeting. Automated logic checks cover storage, foreground classification, browser delivery, title matching, date rollover, and protected controls. A real Chrome for Testing 143 (Chromium) on Linux was tested with a fresh profile: authenticated pairing, website delivery to the C# bridge with query/fragment stripping, domain/source/timestamp diagnostics, browser-label selection, focused HTTP popup capture, and internal-page clearing passed. Selecting the Edge label in that Chrome test verifies report routing; it is not a native Edge test. Actual Edge on Windows, installation, foreground collection, sign-in startup, password dialogs, notification-area behavior, and uninstallation still need runtime testing on Windows.

Automated checks can be run with:

```text
dotnet run --project tests/CoreChecks/CoreChecks.csproj
dotnet run --project tests/BrowserChecks/BrowserChecks.csproj
dotnet run --project tests/NativeChecks/NativeChecks.csproj
dotnet run --project tests/PasswordChecks/PasswordChecks.csproj
dotnet run --project tests/ExportPathChecks/ExportPathChecks.csproj
dotnet run --project tests/InstallationChecks/InstallationChecks.csproj
dotnet run --project tests/TrackingChecks/TrackingChecks.csproj
dotnet run --project tests/DateRangeChecks/DateRangeChecks.csproj
dotnet run --project tests/WebsitePipelineChecks/WebsitePipelineChecks.csproj
dotnet run --project tests/PrivateModeChecks/PrivateModeChecks.csproj
node tests/browser/background.test.mjs
```

## Technology

WinForms on .NET 10, Windows foreground/input-desktop APIs, SQLite, and a Chrome/Edge Manifest V3 extension. Collection does not use screenshots, microphone recording, keystroke logging, or administrator privileges. Source is provided under the MIT license.

Private-mode validation for 1.2.0: automated bridge, classifier, SQLite/CSV/JSON, and extension guard checks passed. The live Chromium private-window test did not complete after a test-extension reload; private-mode operation still needs verification with a normally installed extension on Windows.

## Optional cloud sync (1.3.0)

Cloud sync is **off by default**. The private dashboard is https://activity-ledger-cloud.realtrippydak.chatgpt.site and uses your ChatGPT sign-in. It shows synced timelines, app/site totals, and private-mode flags. It cannot pause, resume, quit, or reconfigure local recording.

1. In the dashboard choose **Connect a laptop**, enter a name, and download its pairing JSON file. Use a separate file for each Windows installation.
2. In the tracker choose **Cloud sync** and enter your existing management password.
3. Choose **Import pairing file**, select the downloaded JSON, choose 1–90 days of retained history (30 by default), check **Enable optional cloud sync**, and save.
4. Leave the app running. It attempts sync about once per minute. The cloud screen shows success/failure; the dashboard shows the last successful sync, including when the laptop is offline.

The selected session metadata is sent over HTTPS to your owner-private Site. Pairing credentials are encrypted with Windows DPAPI for your current Windows account; your management password is never uploaded. Private-mode sessions are projected to fixed labels with empty address/domain fields, even if local fields were altered. A pairing file contains upload credentials: keep it private and delete your downloaded copy after importing. Upload keys can be revoked in the dashboard; this stops future uploads while retaining the last cloud snapshot and local recording.

Turning sync off stops new attempts; an already submitted request may finish. The last uploaded snapshot stays online. Clearing local history is reflected after the next successful enabled sync; disabling sync or uninstalling does not erase cloud copies. Cloud history is a separately stored snapshot, not an end-to-end encrypted backup. The local SQLite history remains unencrypted as described above.

Uploads reuse content-addressed chunks, then commit a complete snapshot. Failed/incomplete syncs leave the previous visible snapshot intact. The current snapshot covers only retained local records within the selected number of days. A selection above 500,000 sessions requires fewer days instead of silently uploading a partial snapshot. The dashboard shows up to 1,200 matching timeline rows and totals over the selected range; narrow the dates to inspect more. Choose up to 31 days per dashboard view. Times are shown in the viewing device's timezone.

Cloud settings and the imported credentials remain on uninstall so reinstall can reconnect. Import a new pairing file after revoking a key or moving to another Windows account. Do not reuse one laptop's pairing file on multiple installations.

The cloud Worker source and security tests are included in the download source bundle under `CloudDashboard`. `node --test tests/cloud.test.mjs` runs its access/isolation tests; `dotnet run --project tests/CloudSyncChecks/CloudSyncChecks.csproj` checks sync projection, bounded chunks, credential scoping, reuse and disabling. Windows DPAPI/UI behavior and the first real laptop-to-dashboard sync still need Windows runtime testing. Browser preview was unavailable in this execution environment. Production checks confirmed that anonymous, service-only and forged-viewer requests cannot read history.

## v2 distribution to other Windows computers

Download and run the standalone installer matching the computer: **ActivityLedgerSetup-v2.0.0-Windows-x64.exe** for Intel/AMD Windows 10/11, or **ActivityLedgerSetup-v2.0.0-Windows-arm64.exe** for Windows 11 on ARM. No ZIP extraction or separate .NET installation is needed. The Chrome/Edge companion is embedded and can be extracted through Browser setup. The download is unsigned.

The same installer can be copied to other authorized computers. Each Windows account has its own local history, management password, browser pairing token, and encrypted cloud configuration. No user password, Site service token, cloud upload key, or existing history is embedded in these installers. New installs keep cloud sync off. Recording begins automatically after setup and at that account's Windows sign-in.

The first-run guide opens after a manual first launch and can be reopened from **tray menu → Set up this computer**. It links to browser setup, the dashboard, and Cloud sync. In the private dashboard, create a separate laptop entry and download a separate pairing file for every Windows installation. Do not reuse a laptop's pairing file or copy its encrypted cloud settings to another computer. Devices appear separately in the dashboard's Laptop selector after pairing and sync.

To update an existing machine, quit the old tracker with its management password, then run the v2 installer. Its data directory and password format are unchanged, so history, settings, password, browser pairing and existing cloud pairing are preserved. The v2 guide appears once after upgrade. Renaming a download with the usual browser suffix such as '(1)' no longer changes setup into app mode.

Build either architecture from source with `.\build.ps1 -Runtime win-x64` or `.\build.ps1 -Runtime win-arm64`. Each produces a standalone versioned installer beside its ZIP bundle. Setup mode is selected from its ActivityLedgerSetup filename prefix or an explicit --install argument; the installed ActivityLedger.exe opens the journal normally.

Tests cover the setup naming rules, storage and cloud sync. Cross-compilation does not verify a fresh Windows installation: x64/ARM64 setup, first-run dialogs, Windows startup and actual multi-machine cloud sync still need testing on Windows.
