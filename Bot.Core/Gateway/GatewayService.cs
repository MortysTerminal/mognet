using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mognet.Bot.Core.Gateway
{
    /// <summary>
    /// The Discord gateway connection. The library reconnects by itself after a lost connection. A failure that
    /// reconnecting cannot fix (bad token, invalid or disallowed intents) stops the host with exit code 1.
    /// </summary>
    public sealed class GatewayService : IHostedService, IGateway, IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GatewayService> _logger;
        private readonly IFatalShutdown _shutdown;
        private readonly DiscordSocketClient _client;
        private readonly Func<Task> _registerCommands;
        private ulong _guildId;
        private int _commandsRegistered;
        private volatile bool _stopping;
        private bool _started;

        public GatewayService(IConfiguration configuration, ILogger<GatewayService> logger, IFatalShutdown shutdown)
            : this(configuration, logger, shutdown, null)
        {
        }

        internal GatewayService(IConfiguration configuration, ILogger<GatewayService> logger, IFatalShutdown shutdown, Func<Task>? registerCommands)
        {
            _configuration = configuration;
            _logger = logger;
            _shutdown = shutdown;
            _client = new DiscordSocketClient(GatewayConfig.Create());
            _registerCommands = registerCommands ?? RegisterCommandsAsync;

            _client.Log += OnLogAsync;
            _client.Disconnected += HandleDisconnectedAsync;
            _client.Ready += HandleReadyAsync;
            _client.SlashCommandExecuted += OnSlashCommand;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            string token;
            try
            {
                token = Required("Discord:BotToken");
                var guildIdRaw = Required("Discord:GuildId");
                if (!ulong.TryParse(guildIdRaw, out _guildId))
                    throw new InvalidOperationException("Discord:GuildId is invalid: it must be a numeric value.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogCritical("{Reason} The bot stops and the container will be restarted.", ex.Message);
                RequestShutdown();
                return;
            }

            try
            {
                await _client.LoginAsync(TokenType.Bot, token);
                await _client.StartAsync();
                _started = true;
            }
            catch (Exception ex)
            {
                // Without a gateway session the bot cannot work; stop so the container restarts.
                _logger.LogCritical(ex, "Discord login failed: check Discord__BotToken. The bot stops and the container will be restarted.");
                RequestShutdown();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _stopping = true;
            if (!_started)
                return;

            try
            {
                await _client.StopAsync();
                await _client.LogoutAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The Discord connection did not close cleanly.");
            }
        }

        public void Dispose() => _client.Dispose();

        internal Task HandleDisconnectedAsync(Exception? exception)
        {
            try
            {
                // Closing the connection on purpose also raises this event.
                if (_stopping)
                    return Task.CompletedTask;

                if (GatewayDisconnectPolicy.Classify(exception) == GatewayDisconnectAction.Fatal)
                {
                    _logger.LogCritical(exception, "The Discord gateway closed the connection and reconnecting cannot help. The bot stops and the container will be restarted.");
                    RequestShutdown();
                }
                else
                {
                    _logger.LogWarning(exception, "The Discord gateway connection was lost. The client reconnects by itself.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handling a gateway disconnect failed.");
            }

            return Task.CompletedTask;
        }

        // Ready fires again after a reconnect with a new session, so the commands are registered only once.
        internal async Task HandleReadyAsync()
        {
            if (Interlocked.CompareExchange(ref _commandsRegistered, 1, 0) != 0)
                return;

            try
            {
                _logger.LogInformation("The Discord gateway is ready.");
                await _registerCommands();
            }
            catch (Exception ex)
            {
                // Allow a retry on the next Ready.
                Interlocked.Exchange(ref _commandsRegistered, 0);
                _logger.LogError(ex, "Registering the slash commands failed.");
            }
        }

        // The library waits for event handlers before it reads the next gateway message, so the work runs off the handler.
        private Task OnSlashCommand(SocketSlashCommand command)
        {
            _ = Task.Run(() => HandleSlashCommandAsync(command));
            return Task.CompletedTask;
        }

        private async Task HandleSlashCommandAsync(SocketSlashCommand command)
        {
            try
            {
                if (command.CommandName != PingCommand.Name)
                    return;

                var isAdministrator = command.User is IGuildUser user && user.GuildPermissions.Administrator;
                var reply = PingCommand.Decide(command.GuildId == _guildId, isAdministrator);
                await command.RespondAsync(reply.Text, ephemeral: reply.Ephemeral, allowedMentions: reply.AllowedMentions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handling the /{Command} command failed.", command.CommandName);
            }
        }

        private async Task RegisterCommandsAsync()
        {
            var guild = _client.GetGuild(_guildId)
                ?? throw new InvalidOperationException("The bot is not a member of the configured guild (Discord:GuildId).");
            await guild.CreateApplicationCommandAsync(PingCommand.Build());
            _logger.LogInformation("Registered the /{Command} command for the configured guild.", PingCommand.Name);
        }

        private Task OnLogAsync(LogMessage message)
        {
            var level = message.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Debug,
                _ => LogLevel.Trace,
            };
            _logger.Log(level, message.Exception, "Discord {Source}: {Message}", message.Source, message.Message);
            return Task.CompletedTask;
        }

        private string Required(string key)
        {
            var value = _configuration[key];
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{key.Replace(":", "__")} is not set.");
            return value.Trim();
        }

        private void RequestShutdown()
        {
            try
            {
                _shutdown.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Requesting the shutdown failed.");
            }
        }
    }
}
