using ActivityLedger.Core;

var assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException("FAILED: " + message);
}
bool Advance(DateTime from, DateTime to, DateTime oldDay, DateTime newDay)
    => DateRangeFollowPolicy.ShouldAdvance(from, to, oldDay, newDay);

var yesterday = new DateTime(2026, 11, 21);
var today = yesterday.AddDays(1);
Check(Advance(yesterday, yesterday, yesterday, today), "Today-only range follows midnight");
Check(Advance(new(2026, 11, 30), new(2026, 11, 30), new(2026, 11, 30), new(2026, 12, 1)), "Month boundary follows");
Check(Advance(new(2026, 12, 31), new(2026, 12, 31), new(2026, 12, 31), new(2027, 1, 1)), "Year boundary follows");
Check(Advance(new(2028, 2, 28), new(2028, 2, 28), new(2028, 2, 28), new(2028, 2, 29)), "Leap-day entry follows");
Check(Advance(new(2028, 2, 29), new(2028, 2, 29), new(2028, 2, 29), new(2028, 3, 1)), "Leap-day exit follows");
Check(Advance(yesterday, yesterday, yesterday, today.AddDays(3)), "Sleep spanning several days follows current day");
Check(Advance(today, today, today, yesterday), "Backward calendar change follows when still on old Today");
Check(Advance(yesterday.AddHours(2), yesterday.AddHours(22), yesterday.AddHours(23), today.AddHours(1)), "Filter values compared as dates");
Check(!Advance(yesterday, yesterday, yesterday, yesterday.AddHours(23)), "Passing time within same day does not advance");
Check(!Advance(yesterday.AddDays(-7), yesterday.AddDays(-7), yesterday, today), "Manually selected historical day preserved");
Check(!Advance(yesterday.AddDays(-6), yesterday, yesterday, today), "Multi-day history range preserved");
Check(!Advance(yesterday, today, yesterday, today), "Multi-day range ending on new day preserved");
Check(!Advance(today, today, yesterday, today), "Range already on new Today is not advanced again");
Check(!Advance(today.AddDays(7), today.AddDays(7), yesterday, today), "Manually selected future day preserved");
Check(!Advance(yesterday, yesterday.AddDays(-1), yesterday, today), "Inverted manually selected range preserved");

Console.WriteLine($"Date range checks passed: {assertions} assertions.");
