using Discord;
using Discord.WebSocket;

namespace Mognet.Bot.Core.Gateway
{
    /// <summary>Client settings. Only guild events are requested; no privileged intent is used.</summary>
    public static class GatewayConfig
    {
        public const GatewayIntents Intents = GatewayIntents.Guilds;

        public static DiscordSocketConfig Create() => new()
        {
            GatewayIntents = Intents,
            LogLevel = LogSeverity.Info,
        };
    }
}
