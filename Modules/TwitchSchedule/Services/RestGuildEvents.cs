using Discord;
using Discord.Rest;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Services
{
    /// <summary>The guild's scheduled events through the Discord REST API.</summary>
    internal sealed class RestGuildEvents : IGuildEvents
    {
        private readonly RestGuild _guild;
        private Dictionary<ulong, RestGuildEvent> _events = [];

        public RestGuildEvents(RestGuild guild)
        {
            _guild = guild;
        }

        public async Task<IReadOnlyList<ExistingEvent>> ListAsync()
        {
            _events = (await _guild.GetEventsAsync()).ToDictionary(e => e.Id);
            return _events.Values
                .Select(e => new ExistingEvent(e.Id, e.Name, e.Description, e.StartTime, e.EndTime, e.Location))
                .ToList();
        }

        public async Task CreateAsync(DesiredEvent desired)
        {
            await _guild.CreateEventAsync(
                name: desired.Name,
                startTime: desired.StartTime,
                type: GuildScheduledEventType.External,
                privacyLevel: GuildScheduledEventPrivacyLevel.Private,
                description: desired.Description,
                endTime: desired.EndTime,
                location: desired.Location
            );
        }

        public async Task UpdateAsync(ExistingEvent existing, DesiredEvent desired)
        {
            await _events[existing.Id].ModifyAsync(props =>
            {
                props.Name = desired.Name;
                props.StartTime = desired.StartTime;
                props.EndTime = desired.EndTime;
                props.Description = desired.Description;
                props.Location = desired.Location;
            });
        }

        public async Task DeleteAsync(ExistingEvent existing)
        {
            await _events[existing.Id].DeleteAsync();
        }
    }
}
