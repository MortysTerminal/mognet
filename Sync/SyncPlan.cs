using mortys_twitch_discord_schedulebot.Models;

namespace mortys_twitch_discord_schedulebot.Sync
{
    /// <summary>
    /// Snapshot of a Discord scheduled event, as far as the planner needs it.
    /// </summary>
    public record ExistingEvent(
        ulong Id,
        string Name,
        string? Description,
        DateTimeOffset StartTime,
        DateTimeOffset? EndTime,
        string? Location
    );

    /// <summary>
    /// The Discord event a Twitch schedule entry should be mirrored to.
    /// </summary>
    public record DesiredEvent(
        StreamScheduleEntry Entry,
        string Name,
        string Description,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        string Location
    );

    public record EventUpdate(ExistingEvent Existing, DesiredEvent Desired);

    /// <summary>
    /// What one sync cycle should do on Discord. <see cref="Protected"/> lists bot events that are not in the
    /// fetched schedules but are kept because their channel could not be fetched successfully.
    /// <see cref="Started"/> lists bot events whose start time has passed; they are left for Discord to complete.
    /// </summary>
    public record SyncPlan(
        IReadOnlyList<DesiredEvent> Creates,
        IReadOnlyList<EventUpdate> Updates,
        IReadOnlyList<ExistingEvent> Unchanged,
        IReadOnlyList<ExistingEvent> Deletes,
        IReadOnlyList<ExistingEvent> Protected,
        IReadOnlyList<ExistingEvent> Started
    );
}
