using ActivityLedger.Native;

var assertions = 0;
void Check(bool condition, string description)
{
    assertions++;
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
}

Check(BrowserTitleMatcher.Matches("chrome.exe", "Document - Google Chrome", "Document"), "Chrome normal caption");
Check(BrowserTitleMatcher.Matches("MSedge.EXE", "Document - Microsoft Edge", "Document"), "Edge normal caption");
Check(BrowserTitleMatcher.Matches("msedge", "Document - Personal - Microsoft Edge", "Document"), "Edge named profile caption");
Check(BrowserTitleMatcher.Matches("chrome", "Document", "Document"), "Unadorned matching caption");
Check(BrowserTitleMatcher.Matches("chrome.exe", "A - B - Google Chrome", "A - B"), "Hyphenated page title");
Check(BrowserTitleMatcher.Matches("chrome.exe", "私のページ - Google Chrome", "私のページ"), "Unicode title");

Check(!BrowserTitleMatcher.Matches("chrome.exe", "New tab - Google Chrome", "Old tab"), "Delayed prior-tab heartbeat rejected");
Check(!BrowserTitleMatcher.Matches("chrome.exe", "Documentary - Google Chrome", "Document"), "Prefix without delimiter rejected");
Check(!BrowserTitleMatcher.Matches("chrome.exe", "Document - Google Chrome (Incognito)", "Document"), "Chrome incognito suffix rejected");
Check(!BrowserTitleMatcher.Matches("chrome.exe", "Document - Incognito - Google Chrome", "Document"), "Chrome private intermediate label rejected");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document - InPrivate - Microsoft Edge", "Document"), "Edge InPrivate caption rejected");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document - inprivate - Microsoft Edge", "Document"), "Private marker checked ignoring case");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document - Private Browsing - Microsoft Edge", "Document"), "Other private profile captions rejected");
Check(!BrowserTitleMatcher.Matches("chrome.exe", "Document - Personal - Google Chrome", "Document"), "Unknown Chrome caption variant rejected");
Check(!BrowserTitleMatcher.Matches("chrome.exe", "Document - Microsoft Edge", "Document"), "Wrong browser suffix rejected");
Check(!BrowserTitleMatcher.Matches("firefox.exe", "Document", "Document"), "Unsupported browser rejected");
Check(!BrowserTitleMatcher.Matches("chrome.exe", "Document - Google Chrome", ""), "Empty tab title rejected");
Check(!BrowserTitleMatcher.Matches("chrome.exe", "", "Document"), "Empty native title rejected");
Check(!BrowserTitleMatcher.Matches("chrome.exe", "   ", "   "), "Whitespace title rejected");
Check(WindowsActivitySensor.GetSupportedBrowser(@"C:\Program Files\Google\Chrome\Application\chrome.exe") == "chrome", "Windows path normalization");
Check(WindowsActivitySensor.GetSupportedBrowser(" msedge.EXE ") == "msedge", "Executable normalization");
Check(WindowsActivitySensor.GetSupportedBrowser("brave.exe") is null, "Only paired browsers supported");

Check(BrowserTitleMatcher.Matches("msedge.exe", "Document and 2 more pages - Profile 1 - Microsoft Edge", "Document"), "Edge multi-tab caption");
Check(BrowserTitleMatcher.Matches("msedge.exe", "Document and 1 more page - Microsoft Edge", "Document"), "Edge singular other page");
Check(BrowserTitleMatcher.Matches("msedge.exe", "Document and 37 more pages - [Work] - Microsoft Edge", "Document"), "Edge bracketed profile");
Check(BrowserTitleMatcher.Matches("msedge.exe", "Document - (Personal) - Microsoft Edge", "Document"), "Edge parenthesized profile");
Check(BrowserTitleMatcher.Matches("msedge.exe", "Document – Profile 1 – Microsoft Edge", "Document"), "Edge en-dash separators");
Check(BrowserTitleMatcher.Matches("msedge.exe", "Document — Profile 1 — Microsoft Edge", "Document"), "Edge em-dash separators");
Check(BrowserTitleMatcher.Matches("msedge.exe", "Document • Work • Microsoft Edge", "Document"), "Edge bullet separators");
Check(BrowserTitleMatcher.Matches("msedge.exe", "Document · Work · Microsoft Edge", "Document"), "Edge middle-dot separators");
Check(BrowserTitleMatcher.Matches("msedge.exe", "\u200eDocument\u200f and 2 more pages – Profile 1 – Microsoft\u200b Edge\u200e", "Document"), "Edge bidi and zero-width product marks");
Check(BrowserTitleMatcher.Matches("msedge.exe", "Document\u00a0 and\t2 more pages  -   Work  - Microsoft Edge", "Document"), "Caption whitespace collapsed");
Check(BrowserTitleMatcher.Matches("msedge.exe", "\u2066Document\u2069 - Work - Microsoft Edge", "\u200eDocument"), "Bidi isolates normalized on both inputs");
Check(BrowserTitleMatcher.Matches("chrome.exe", "Document – Google Chrome", "Document"), "Chrome spaced separator normalization");
Check(BrowserTitleMatcher.Matches("msedge.exe", "A — B and 2 more pages - Work - Microsoft Edge", "A - B"), "Title separators normalized consistently");

Check(!BrowserTitleMatcher.Matches("msedge.exe", "New tab and 2 more pages - Profile 1 - Microsoft Edge", "Old tab"), "Grouped different active title rejected");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Documentary and 2 more pages - Microsoft Edge", "Document"), "Grouped prefix boundary rejected");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document - Another title - Work - Microsoft Edge", "Document"), "Extra page-title segment cannot masquerade as one profile");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document and unrelated content - Microsoft Edge", "Document"), "Arbitrary title continuation rejected");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document and 2 more pages of content - Microsoft Edge", "Document"), "Grouped grammar must end at separator");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document and 0 more pages - Microsoft Edge", "Document"), "Impossible zero-page caption rejected");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document and 2 more pages - InPrivate - Microsoft Edge", "Document"), "Grouped private caption rejected");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document and 2 more pages - [InPrivate] - Microsoft Edge", "Document"), "Bracketed private caption rejected");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document - In\u200bPrivate - Microsoft Edge", "Document"), "Display marks cannot hide private caption");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document and 2 more pages - Microsoft Edge (InPrivate)", "Document"), "Private product suffix rejected");
Check(!BrowserTitleMatcher.Matches("chrome.exe", "Document and 2 more pages - Google Chrome", "Document"), "Edge grouping not guessed for Chrome");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "Document-Profile-Microsoft Edge", "Document"), "Unspaced punctuation not broadly normalized");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "\u200e\u2066 - Microsoft Edge", "\u200f"), "Display-control-only title rejected");
Check(!BrowserTitleMatcher.Matches("msedge.exe", "word\u200dword - Microsoft Edge", "wordword"), "Meaningful Unicode joiner preserved");

if (!OperatingSystem.IsWindows())
{
    var rejected = false;
    try { _ = new WindowsActivitySensor().Sample(); }
    catch (PlatformNotSupportedException) { rejected = true; }
    Check(rejected, "Linux rejects Windows sampling explicitly");
}

Console.WriteLine($"Native checks passed: {assertions} assertions.");
