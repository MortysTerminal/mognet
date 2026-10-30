using Discord;
using Discord.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mognet.Bot.Core.Gateway;

namespace Mognet.Bot.Core.Tests
{
    public class GatewayConfigTests
    {
        [Fact]
        public void Only_the_guilds_intent_is_requested()
        {
            Assert.Equal(GatewayIntents.Guilds, GatewayConfig.Intents);
            Assert.Equal(GatewayIntents.Guilds, GatewayConfig.Create().GatewayIntents);
        }

        [Fact]
        public void No_privileged_intent_is_requested()
        {
            var intents = GatewayConfig.Create().GatewayIntents;

            Assert.Equal(GatewayIntents.None, intents & GatewayIntents.GuildPresences);
            Assert.Equal(GatewayIntents.None, intents & GatewayIntents.GuildMembers);
            Assert.Equal(GatewayIntents.None, intents & GatewayIntents.MessageContent);
        }
    }

    public class GatewayDisconnectPolicyTests
    {
        [Theory]
        [InlineData(4000)]
        [InlineData(4001)]
        [InlineData(4002)]
        [InlineData(4003)]
        [InlineData(4005)]
        [InlineData(4007)]
        [InlineData(4008)]
        [InlineData(4009)]
        public void Reconnectable_close_codes_reconnect(int code)
        {
            Assert.Equal(GatewayDisconnectAction.Reconnect, GatewayDisconnectPolicy.Classify(new WebSocketClosedException(code)));
        }

        [Theory]
        [InlineData(4004)]
        [InlineData(4010)]
        [InlineData(4011)]
        [InlineData(4012)]
        [InlineData(4013)]
        [InlineData(4014)]
        [InlineData(4006)]
        public void Close_codes_that_cannot_recover_are_fatal(int code)
        {
            Assert.Equal(GatewayDisconnectAction.Fatal, GatewayDisconnectPolicy.Classify(new WebSocketClosedException(code)));
        }

        [Fact]
        public void A_close_code_wrapped_by_the_library_is_found()
        {
            var wrapped = new System.Net.WebSockets.WebSocketException(
                System.Net.WebSockets.WebSocketError.ConnectionClosedPrematurely, "closed", new WebSocketClosedException(4014));

            Assert.Equal(GatewayDisconnectAction.Fatal, GatewayDisconnectPolicy.Classify(wrapped));
            Assert.Equal(GatewayDisconnectAction.Fatal, GatewayDisconnectPolicy.Classify(new AggregateException(wrapped)));
        }

        [Fact]
        public void Unknown_code_reconnects()
        {
            Assert.Equal(GatewayDisconnectAction.Reconnect, GatewayDisconnectPolicy.Classify(new WebSocketClosedException(1006)));
            Assert.Equal(GatewayDisconnectAction.Reconnect, GatewayDisconnectPolicy.Classify(new WebSocketClosedException(4999)));
        }

        [Fact]
        public void No_code_reconnects()
        {
            Assert.Equal(GatewayDisconnectAction.Reconnect, GatewayDisconnectPolicy.Classify(null));
            Assert.Equal(GatewayDisconnectAction.Reconnect, GatewayDisconnectPolicy.Classify(new InvalidOperationException("x")));
            Assert.Equal(GatewayDisconnectAction.Reconnect, GatewayDisconnectPolicy.Classify(new Discord.WebSocket.GatewayReconnectException("server asked")));
        }
    }

    public class PingCommandTests
    {
        [Fact]
        public void An_administrator_in_the_configured_guild_is_allowed()
        {
            var reply = PingCommand.Decide(inConfiguredGuild: true, isAdministrator: true);

            Assert.Equal("Pong.", reply.Text);
        }

        [Fact]
        public void A_non_administrator_is_refused()
        {
            var reply = PingCommand.Decide(inConfiguredGuild: true, isAdministrator: false);

            Assert.NotEqual("Pong.", reply.Text);
            Assert.Contains("Administrator", reply.Text);
        }

        [Fact]
        public void Another_guild_is_refused_even_for_an_administrator()
        {
            Assert.NotEqual("Pong.", PingCommand.Decide(inConfiguredGuild: false, isAdministrator: true).Text);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void Every_reply_is_ephemeral_and_mentions_nobody(bool inGuild, bool admin)
        {
            var reply = PingCommand.Decide(inGuild, admin);

            Assert.True(reply.Ephemeral);
            Assert.Same(AllowedMentions.None, reply.AllowedMentions);
        }

        [Fact]
        public void The_registration_is_a_guild_only_command_hidden_from_everyone_by_default()
        {
            var properties = PingCommand.Build();

            Assert.Equal("ping", properties.Name.Value);
            Assert.True(properties.DefaultMemberPermissions.IsSpecified);
            Assert.Equal(0UL, (ulong)properties.DefaultMemberPermissions.Value);
            Assert.Equal([InteractionContextType.Guild], properties.ContextTypes.Value);
        }
    }

    public class GatewayServiceTests
    {
        private static IConfiguration Config(params (string, string)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Item1, v => (string?)v.Item2)).Build();

        private static GatewayService Service(FakeShutdown shutdown, Func<Task>? register = null, IConfiguration? config = null) =>
            new(config ?? Config(), NullLogger<GatewayService>.Instance, shutdown, register);

        [Fact]
        public async Task A_fatal_disconnect_requests_shutdown()
        {
            var shutdown = new FakeShutdown();

            await Service(shutdown).HandleDisconnectedAsync(new WebSocketClosedException(4014));

            Assert.Equal(1, shutdown.Calls);
        }

        [Fact]
        public async Task A_reconnectable_disconnect_does_not_stop_the_host()
        {
            var shutdown = new FakeShutdown();

            await Service(shutdown).HandleDisconnectedAsync(new WebSocketClosedException(4000));
            await Service(shutdown).HandleDisconnectedAsync(null);

            Assert.Equal(0, shutdown.Calls);
        }

        [Fact]
        public async Task The_disconnect_handler_never_throws()
        {
            var shutdown = new FakeShutdown { Throws = true };

            await Service(shutdown).HandleDisconnectedAsync(new WebSocketClosedException(4004));

            Assert.Equal(1, shutdown.Calls);
        }

        [Fact]
        public async Task Commands_are_registered_once_even_when_ready_fires_again()
        {
            var registrations = 0;
            var service = Service(new FakeShutdown(), () => { registrations++; return Task.CompletedTask; });

            await service.HandleReadyAsync();
            await service.HandleReadyAsync();
            await service.HandleReadyAsync();

            Assert.Equal(1, registrations);
        }

        [Fact]
        public async Task A_failed_registration_is_swallowed_and_retried_on_the_next_ready()
        {
            var attempts = 0;
            var service = Service(new FakeShutdown(), () =>
            {
                attempts++;
                return attempts == 1 ? throw new InvalidOperationException("missing access") : Task.CompletedTask;
            });

            await service.HandleReadyAsync();
            await service.HandleReadyAsync();
            await service.HandleReadyAsync();

            Assert.Equal(2, attempts);
        }

        [Theory]
        [InlineData(null, "123")]
        [InlineData("token", null)]
        [InlineData("token", "not-a-number")]
        [InlineData("", "123")]
        public async Task Missing_or_invalid_config_stops_the_host_without_connecting(string? token, string? guild)
        {
            var values = new List<(string, string)>();
            if (token is not null) values.Add(("Discord:BotToken", token));
            if (guild is not null) values.Add(("Discord:GuildId", guild));
            var shutdown = new FakeShutdown();

            await Service(shutdown, config: Config([.. values])).StartAsync(CancellationToken.None);

            Assert.Equal(1, shutdown.Calls);
        }

        private sealed class FakeShutdown : IFatalShutdown
        {
            public int Calls { get; private set; }
            public bool Throws { get; init; }

            public void Shutdown()
            {
                Calls++;
                if (Throws)
                    throw new InvalidOperationException("shutdown failed");
            }
        }
    }
}
