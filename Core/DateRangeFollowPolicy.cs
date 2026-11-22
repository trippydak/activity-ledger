namespace ActivityLedger.Core;

/// <summary>Keeps an unchanged Today filter on the current local calendar day.</summary>
public static class DateRangeFollowPolicy
{
    public static bool ShouldAdvance(DateTime selectedFrom, DateTime selectedTo,
        DateTime previousToday, DateTime currentToday)
    {
        var previousDate = previousToday.Date;
        return previousDate != currentToday.Date
            && selectedFrom.Date == previousDate
            && selectedTo.Date == previousDate;
    }
}
