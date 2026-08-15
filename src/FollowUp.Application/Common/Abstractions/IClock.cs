namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Time source. The system's business calendar is pinned to Africa/Cairo (SRS), so handlers and jobs read
/// "today"/"now" through here rather than <c>DateTime.Now</c>, keeping time testable and timezone-correct.
/// </summary>
public interface IClock
{
    /// <summary>Current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Current instant expressed in Africa/Cairo local time.</summary>
    DateTimeOffset CairoNow { get; }

    /// <summary>Today's date in Africa/Cairo — the board/business day.</summary>
    DateOnly CairoToday { get; }
}
