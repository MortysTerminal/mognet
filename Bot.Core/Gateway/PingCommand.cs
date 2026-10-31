using Discord;

namespace Mognet.Bot.Core.Gateway
{
    /// <summary>What to send back for a command: private to the caller and without any mention.</summary>
    public record PingReply(string Text, bool Ephemeral, AllowedMentions AllowedMentions);

    /// <summary>The guild command <c>/ping</c>, for administrators only.</summary>
    public static class PingCommand
    {
        public const string Name = "ping";
        public const string AllowedText = "Pong.";
        public const string RefusedText = "You need the Administrator permission to use this command.";

        /// <summary>Hidden from everyone by default (permission value 0) and limited to guilds.</summary>
        public static SlashCommandProperties Build() =>
            new SlashCommandBuilder()
                .WithName(Name)
                .WithDescription("Check that the bot is running.")
                .WithDefaultMemberPermissions((GuildPermission)0)
                .WithContextTypes(InteractionContextType.Guild)
                .Build();

        /// <summary>The registration alone is not trusted: the caller is checked at runtime, too.</summary>
        public static PingReply Decide(bool inConfiguredGuild, bool isAdministrator) =>
            new(inConfiguredGuild && isAdministrator ? AllowedText : RefusedText, Ephemeral: true, AllowedMentions.None);
    }
}
