using mortys_twitch_discord_schedulebot.Models;

namespace mortys_twitch_discord_schedulebot.Sync
{
    /// <summary>
    /// Decides which Discord events to create, update or delete so that Discord mirrors the Twitch schedule.
    /// Pure: works on snapshots, performs no I/O.
    /// </summary>
    /// <remarks>
    /// Deletion rules — a bot event that matches no fetched entry is deleted only when it is safe:
    /// <list type="bullet">
    /// <item>Event tagged with a configured channel: only if that channel was fetched successfully this cycle.</item>
    /// <item>Event without a channel tag (created by an older version), or tagged with a channel that is no longer
    /// configured: only if every configured channel was fetched successfully this cycle.</item>
    /// </list>
    /// Otherwise the event is kept and reported as protected.
    /// <para>
    /// Bot events whose start time has passed are never updated or deleted: Discord completes external events
    /// at their end time on its own.
    /// </para>
    /// </remarks>
    public class EventPlanner
    {
        private readonly EventFormatter _formatter;

        public EventPlanner(EventFormatter formatter)
        {
            _formatter = formatter;
        }

        public SyncPlan Plan(IReadOnlyList<ChannelSchedule> schedules, IReadOnlyList<ExistingEvent> existingEvents, DateTimeOffset now)
        {
            var creates = new List<DesiredEvent>();
            var updates = new List<EventUpdate>();
            var unchanged = new List<ExistingEvent>();
            var started = existingEvents.Where(e => IsBotEvent(e) && e.StartTime <= now).ToList();
            var matchedEventIds = started.Select(e => e.Id).ToHashSet();

            foreach (var entry in schedules.Where(s => s.Succeeded).SelectMany(s => s.Entries))
            {
                var desired = ToDesiredEvent(entry);

                // Events are matched by the segment ID embedded in their description.
                var matchingEvent = existingEvents.FirstOrDefault(e =>
                    e.Description != null &&
                    e.Description.Contains(entry.TwitchSegmentId)
                );

                if (matchingEvent == null)
                {
                    creates.Add(desired);
                    continue;
                }

                if (started.Contains(matchingEvent))
                    continue;

                matchedEventIds.Add(matchingEvent.Id);

                if (HasChanged(matchingEvent, desired))
                    updates.Add(new EventUpdate(matchingEvent, desired));
                else
                    unchanged.Add(matchingEvent);
            }

            var configuredChannels = schedules.Select(s => s.ChannelLogin).ToHashSet();
            var succeededChannels = schedules.Where(s => s.Succeeded).Select(s => s.ChannelLogin).ToHashSet();
            var allSucceeded = schedules.All(s => s.Succeeded);

            var deletes = new List<ExistingEvent>();
            var protectedEvents = new List<ExistingEvent>();

            foreach (var existing in existingEvents.Where(e => !matchedEventIds.Contains(e.Id) && IsBotEvent(e)))
            {
                var channel = EventFormatter.ReadChannelTag(existing.Description);

                var safeToDelete = channel != null && configuredChannels.Contains(channel)
                    ? succeededChannels.Contains(channel)
                    : allSucceeded;

                (safeToDelete ? deletes : protectedEvents).Add(existing);
            }

            return new SyncPlan(creates, updates, unchanged, deletes, protectedEvents, started);
        }

        public DesiredEvent ToDesiredEvent(StreamScheduleEntry entry) =>
            new(
                Entry: entry,
                Name: _formatter.BuildName(entry),
                Description: _formatter.BuildDescription(entry),
                StartTime: entry.StartTime,
                EndTime: EventFormatter.BuildEndTime(entry),
                Location: entry.ChannelUrl
            );

        public static bool IsBotEvent(ExistingEvent existingEvent) =>
            existingEvent.Description != null &&
            existingEvent.Description.Contains(EventFormatter.SegmentTagPrefix);

        private static bool HasChanged(ExistingEvent existing, DesiredEvent desired) =>
            existing.Name != desired.Name ||
            existing.StartTime != desired.StartTime ||
            existing.EndTime != desired.EndTime ||
            existing.Description != desired.Description ||
            existing.Location != desired.Location;
    }
}
