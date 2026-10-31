using Discord;
using Discord.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mognet.Modules.TwitchSchedule.Storage;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Services
{
    public class DiscordService : IDiscordSync, IAsyncDisposable
    {
        private readonly DiscordRestClient _client;
        private readonly ulong _guildId;
        private readonly ILogger<DiscordService> _logger;
        private readonly string _configuration_token;
        private readonly TimeProvider _timeProvider;

        public DiscordService(IConfiguration configuration, ILogger<DiscordService> logger, TimeProvider timeProvider)
        {
            _logger = logger;
            _timeProvider = timeProvider;

            var token = configuration["Discord:BotToken"]
                ?? throw new InvalidOperationException("Discord:BotToken is missing from configuration.");

            _configuration_token = token;

            var guildIdRaw = configuration["Discord:GuildId"]
                ?? throw new InvalidOperationException("Discord:GuildId is missing from configuration.");

            if (!ulong.TryParse(guildIdRaw, out _guildId))
                throw new InvalidOperationException("Discord:GuildId is invalid — must be a numeric value.");

            _client = new DiscordRestClient();

            _logger.LogInformation("DiscordService initialized.");
        }

        public async Task LoginAsync()
        {
            await _client.LoginAsync(TokenType.Bot, _configuration_token);
            _logger.LogInformation("Discord login successful.");
        }

        public async Task SyncEventsAsync(IReadOnlyList<ChannelSchedule> schedules, ScheduleConfig config)
        {
            _logger.LogInformation("Starting Discord event synchronization...");

            var guild = await _client.GetGuildAsync(_guildId);

            if (guild == null)
            {
                _logger.LogError("Discord server (GuildId: {GuildId}) not found.", _guildId);
                return;
            }

            await new GuildEventSynchronizer(_logger, _timeProvider).SyncAsync(new RestGuildEvents(guild), schedules, config);

            _logger.LogInformation("Discord event synchronization complete.");
        }

        public async ValueTask DisposeAsync()
        {
            await _client.LogoutAsync();
            _client.Dispose();
        }
    }
}
