namespace PrintingBooksPortal.Services;

public static class EgyptTime
{
    private static readonly TimeZoneInfo TZ = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    public static DateTime ToEgyptLocal(this DateTime utcDateTime)
    {
        if (utcDateTime.Kind == DateTimeKind.Local)
            utcDateTime = utcDateTime.ToUniversalTime();
        else if (utcDateTime.Kind == DateTimeKind.Unspecified)
            utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, TZ);
    }
}
