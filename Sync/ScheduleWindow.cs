namespace mortys_twitch_discord_schedulebot.Sync
{
    /// <summary>
    /// The time range of schedule entries that are mirrored to Discord: strictly after now, up to and
    /// including now + look-ahead.
    /// </summary>
    public static class ScheduleWindow
    {
        public static bool Includes(DateTimeOffset startTime, DateTimeOffset now, int lookaheadDays) =>
            startTime > now && startTime <= now.AddDays(lookaheadDays);
    }
}
