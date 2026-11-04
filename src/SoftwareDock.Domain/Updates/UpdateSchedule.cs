namespace SoftwareDock.Domain.Updates;

public enum UpdateScheduleMode
{
    Off,
    Daily,
    Weekly,
}

/// <summary>
/// When the next unattended "update everything" run is due. Works in local wall-clock time,
/// because "every night at 3" means 3 on the user's clock, DST included.
/// </summary>
public sealed record UpdateSchedule(UpdateScheduleMode Mode, int Hour = 3, DayOfWeek Day = DayOfWeek.Sunday)
{
    public static readonly UpdateSchedule Off = new(UpdateScheduleMode.Off);

    /// <summary>
    /// The first due time after <paramref name="lastRun"/> (or after <paramref name="now"/> when it
    /// never ran). A run missed while the PC slept is due immediately on wake: it is in the past.
    /// </summary>
    public DateTime? NextRun(DateTime? lastRun, DateTime now)
    {
        if (Mode == UpdateScheduleMode.Off)
        {
            return null;
        }

        var from = lastRun ?? now;
        var candidate = from.Date.AddHours(Math.Clamp(Hour, 0, 23));
        if (candidate <= from)
        {
            candidate = candidate.AddDays(1);
        }

        if (Mode == UpdateScheduleMode.Weekly)
        {
            while (candidate.DayOfWeek != Day)
            {
                candidate = candidate.AddDays(1);
            }
        }

        return candidate;
    }

    public bool IsDue(DateTime? lastRun, DateTime now) => NextRun(lastRun, now) is { } next && next <= now;
}
