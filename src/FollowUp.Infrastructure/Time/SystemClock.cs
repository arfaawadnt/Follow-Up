using FollowUp.Application.Common.Abstractions;

namespace FollowUp.Infrastructure.Time;

/// <summary>Wall-clock time source with the business calendar pinned to Africa/Cairo (SRS).</summary>
public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo Cairo = ResolveCairo();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateTimeOffset CairoNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Cairo);
    public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow.DateTime);

    private static TimeZoneInfo ResolveCairo()
    {
        foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // Fallback: fixed +02:00 (Cairo standard offset) if the tz database is unavailable.
        return TimeZoneInfo.CreateCustomTimeZone("Cairo", TimeSpan.FromHours(2), "Cairo", "Cairo");
    }
}
