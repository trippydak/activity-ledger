using System.Text;
using System.Text.RegularExpressions;

namespace ActivityLedger.Native;

/// <summary>
/// Checks that an extension observation is consistent with the currently focused
/// browser window. A delayed heartbeat must not attribute the old tab to a new one.
/// </summary>
public static class BrowserTitleMatcher
{
    // Edge can describe other tabs in its window caption. Anchor this grammar
    // immediately after the complete observed title, before a known separator.
    private static readonly Regex EdgeOtherPages = new(
        @"^ and [1-9][0-9]{0,5} more pages?(?= - )",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public static bool Matches(string processName, string nativeTitle, string tabTitle)
    {
        if (string.IsNullOrWhiteSpace(nativeTitle) || string.IsNullOrWhiteSpace(tabTitle))
            return false;

        var browser = WindowsActivitySensor.GetSupportedBrowser(processName);
        if (browser is null) return false;

        nativeTitle = NormalizeCaption(nativeTitle);
        tabTitle = NormalizeCaption(tabTitle);
        if (nativeTitle.Length == 0 || tabTitle.Length == 0) return false;

        // Some browser configurations omit the product name from the caption.
        if (nativeTitle.Equals(tabTitle, StringComparison.Ordinal)) return true;

        // A prefix alone is insufficient: "Documentary" is a different title
        // from "Document". Only a browser caption suffix may follow the title.
        if (!nativeTitle.StartsWith(tabTitle, StringComparison.Ordinal)) return false;
        var remainder = nativeTitle[tabTitle.Length..];
        if (browser == "msedge")
        {
            var otherPages = EdgeOtherPages.Match(remainder);
            if (otherPages.Success) remainder = remainder[otherPages.Length..];
        }
        if (!remainder.StartsWith(" - ", StringComparison.Ordinal)) return false;

        var product = browser == "chrome" ? "Google Chrome" : "Microsoft Edge";
        var suffix = remainder[3..];
        if (suffix.Equals(product, StringComparison.Ordinal)) return true;

        // Edge can insert the profile name before its product name. Chrome
        // configurations that change the caption are deliberately not guessed.
        if (browser != "msedge" || !suffix.EndsWith(" - Microsoft Edge", StringComparison.Ordinal))
            return false;

        var profile = suffix[..^" - Microsoft Edge".Length].Trim();
        if (profile.Length == 0 || profile.Contains(" - ", StringComparison.Ordinal)) return false;

        // Private-mode captions must not be accepted as ordinary profile labels.
        return !profile.Contains("InPrivate", StringComparison.OrdinalIgnoreCase)
            && !profile.Contains("Incognito", StringComparison.OrdinalIgnoreCase)
            && !profile.Contains("Private", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCaption(string value)
    {
        // Remove display direction markers, not letters or meaningful joiners
        // used by languages such as Persian. Edge captions can contain these
        // marks even when the extension title does not.
        var text = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (character is '\u061c' or '\u200b' or '\u200e' or '\u200f' or '\ufeff'
                || character is >= '\u202a' and <= '\u202e'
                || character is >= '\u2066' and <= '\u2069') continue;

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = text.Length > 0;
                continue;
            }
            if (pendingSpace) text.Append(' ');
            pendingSpace = false;
            text.Append(character);
        }

        var normalized = text.ToString();
        // Normalize only spaced caption separators, on both inputs. Punctuation
        // within a word and zero-width language joiners remain significant.
        foreach (var separator in new[] { " – ", " — ", " • ", " · " })
            normalized = normalized.Replace(separator, " - ", StringComparison.Ordinal);
        return normalized;
    }
}
