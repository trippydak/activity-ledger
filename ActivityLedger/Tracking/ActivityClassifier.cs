using ActivityLedger.Browser;
using ActivityLedger.Core;
using ActivityLedger.Native;

namespace ActivityLedger.Tracking;

/// <summary>Converts foreground metadata into a privacy-filtered activity observation.</summary>
public static class ActivityClassifier
{
    public static ActivitySample? Classify(WindowSample raw, BrowserSnapshot? browser,
        AppSettings settings, int ownProcessId)
    {
        if (raw.SessionLocked || string.IsNullOrWhiteSpace(raw.ProcessName)) return null;
        if (raw.IdleSeconds >= settings.IdleThresholdSeconds) return new("idle", "", "Idle");
        if (raw.ProcessId == ownProcessId)
        {
            // A constant title prevents password-dialog content from becoming activity metadata.
            var own = new ActivitySample("app", "ActivityLedger.exe", settings.CaptureWindowTitles ? "Activity Ledger" : "");
            return PrivacyRules.IsExcluded(own, settings) ? null : own;
        }

        var supportedBrowser = WindowsActivitySensor.GetSupportedBrowser(raw.ProcessName);
        if (browser is not null && supportedBrowser is not null && browser.Browser == supportedBrowser)
        {
            var age = raw.CapturedAtUtc - browser.ReceivedAtUtc;
            // A heartbeat can arrive just after the native observation was captured. Reject older
            // data and implausibly future timestamps instead of attributing an unrelated website.
            if (browser.IsPrivate)
            {
                // This is a mode flag from an explicitly permitted extension, never private page
                // metadata. Do not inspect the snapshot URL/title or the native browser caption.
                if (age <= TimeSpan.FromSeconds(3) && age >= TimeSpan.FromSeconds(-1))
                {
                    var label = supportedBrowser == "msedge"
                        ? "Private browsing (Edge InPrivate)"
                        : "Private browsing (Chrome Incognito)";
                    var mode = new ActivitySample("private", raw.ProcessName, label);
                    return PrivacyRules.IsExcluded(mode, settings) ? null : mode;
                }
            }
            else if (age <= TimeSpan.FromSeconds(8) && age >= TimeSpan.FromSeconds(-1) &&
                BrowserTitleMatcher.Matches(raw.ProcessName, raw.WindowTitle, browser.Title) &&
                Uri.TryCreate(browser.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                try
                {
                    var clean = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
                    var url = settings.CaptureFullUrls ? clean.Uri.AbsoluteUri : clean.Uri.GetLeftPart(UriPartial.Authority);
                    var website = new ActivitySample("website", raw.ProcessName,
                        settings.CaptureWindowTitles ? browser.Title : "", url, uri.IdnHost.ToLowerInvariant());
                    return PrivacyRules.IsExcluded(website, settings) ? null : website;
                }
                catch (UriFormatException)
                {
                    // An invalid internationalized hostname must not interrupt activity collection.
                }
            }
        }

        // Native browser titles can reveal private tabs. Only the extension supplies browser titles.
        var process = PrivacyRules.NormalizeProcess(raw.ProcessName).ToLowerInvariant();
        bool isBrowser = process is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi" or "iexplore";
        var app = new ActivitySample("app", raw.ProcessName, settings.CaptureWindowTitles && !isBrowser ? raw.WindowTitle : "");
        return PrivacyRules.IsExcluded(app, settings) ? null : app;
    }
}
