# Activity Ledger v2

A Windows activity journal for foreground applications and websites, with local search/export, private-browsing mode flags, password-protected management controls, and optional private cloud viewing.

## Downloads

[Open the v2.0.0 release](https://github.com/trippydak/activity-ledger/releases/tag/v2.0.0).

- **[Download Windows x64 installer](https://github.com/trippydak/activity-ledger/raw/refs/tags/v2.0.0-installers/installers/ActivityLedgerSetup-v2.0.0-Windows-x64.exe)** — most Intel/AMD PCs.
- **[Download Windows ARM64 installer](https://github.com/trippydak/activity-ledger/raw/refs/tags/v2.0.0-installers/installers/ActivityLedgerSetup-v2.0.0-Windows-arm64.exe)** — Windows 11 on ARM.
- [Installer checksums](https://github.com/trippydak/activity-ledger/blob/v2.0.0-installers/installers/ActivityLedger-v2-SHA256SUMS.txt).

This is a private repository; sign in to GitHub with repository access to download. Binaries are kept on the versioned `v2.0.0-installers` tag, separate from the source branch.

Installer filenames:

- `ActivityLedgerSetup-v2.0.0-Windows-x64.exe` — Intel/AMD Windows 10/11.
- `ActivityLedgerSetup-v2.0.0-Windows-arm64.exe` — Windows 11 on ARM.
- `ActivityLedger-v2-SHA256SUMS.txt` — installer checksums.

No separate .NET installation is required. Run the matching installer on each computer. Each Windows account gets its own management password, local history and browser pairing. New installs keep cloud sync off. For remote viewing, create a separate laptop entry and pairing file for each installation. No user passwords, upload keys or recorded activity are included in this repository or the installers.

The first-run guide covers browser setup and cloud pairing. For upgrades, quit the existing tracker with its management password first; existing data and pairings are preserved.

## Features and limits

- App/site sessions with timestamps, durations, filtering, summaries, CSV and JSON export.
- Chrome/Edge companion for normal website addresses; private-mode flags require explicit **Allow in InPrivate / Incognito** permission.
- Private sessions contain only generic mode labels and durations, never private website addresses, titles or content.
- Password checks for pause/resume, quit, settings, export and the registered uninstall command.
- Automatic recording at Windows sign-in and continued tray operation when the journal is closed.
- Optional HTTPS cloud snapshots, encrypted local upload credentials and an owner-private read-only dashboard.

Application password prompts do not override Windows process/file permissions. The tracker cannot record during sleep/shutdown, before sign-in or across unconfigured Windows accounts. Sampling is approximate. Builds are unsigned. Fresh Windows installation, startup, DPAPI and end-to-end multi-machine sync still need runtime validation on Windows.

## Build

Install the .NET 10 SDK and run PowerShell from this directory:

```powershell
.\build.ps1 -Runtime win-x64
.\build.ps1 -Runtime win-arm64
```

Each command creates a standalone installer and support ZIP under `artifacts/`. Installers are release assets rather than Git-tracked binaries. Browser companion version 1.2.0 is embedded in the executables.

## Source layout

- `ActivityLedger/` — Windows UI, native collection, installation, cloud sync and browser bridge.
- `Core/` — SQLite storage, settings, password verification and session accounting.
- `browser-extension/` — Chrome/Edge companion.
- `CloudDashboard/` — private Sites Worker, dashboard and cloud access tests.
- `tests/` — portable .NET checks and extension tests.
- [Full user guide](docs/USER-GUIDE.md) and [quick start](QUICKSTART.txt).

Cloud hosting configuration identifies the owner's existing private Site. A new deployment must use its own Site identity and configure `SITES_SERVICE_TOKEN` through the hosting provider. Its value is not in source. Keep pairing files and runtime credentials out of Git.

## Checks

```powershell
dotnet run --project tests/SetupLaunchChecks/SetupLaunchChecks.csproj
dotnet run --project tests/CoreChecks/CoreChecks.csproj
dotnet run --project tests/PrivateModeChecks/PrivateModeChecks.csproj
dotnet run --project tests/CloudSyncChecks/CloudSyncChecks.csproj
node tests/browser/background.test.mjs
node --test CloudDashboard/tests/cloud.test.mjs
```

Additional focused checks are in the other `tests/*Checks` projects. Automated checks and both architecture builds passed in Linux; this does not substitute for Windows runtime testing.

## License

MIT — see [LICENSE](LICENSE).
