using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mognet.Bot.Core.Gateway;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core.Tests
{
    public class CoreWiringTests : IDisposable
    {
        private readonly TempDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static ServiceCollection Services(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IHostApplicationLifetime>(new StubLifetime());
            services.AddMognetCore(configuration);
            return services;
        }

        private IConfiguration Config() =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Path"] = _db.FilePath }).Build();

        [Fact]
        public void Core_services_resolve_and_the_runtime_is_the_only_hosted_service()
        {
            using var provider = Services(Config()).BuildServiceProvider();

            Assert.NotNull(provider.GetRequiredService<SqliteStore>());
            Assert.NotNull(provider.GetRequiredService<SettingsStore>());
            Assert.NotNull(provider.GetRequiredService<MigrationRunner>());
            Assert.NotNull(provider.GetRequiredService<ModuleManager>());
            Assert.Same(provider.GetRequiredService<GatewayService>(), provider.GetRequiredService<IGateway>());
            Assert.IsType<BotRuntime>(Assert.Single(provider.GetServices<IHostedService>()));
        }

        [Fact]
        public void A_missing_database_path_fails_naming_the_variable()
        {
            using var provider = Services(new ConfigurationBuilder().Build()).BuildServiceProvider();

            var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<SqliteStore>());

            Assert.Contains("Database__Path", ex.Message);
        }

        private sealed class StubLifetime : IHostApplicationLifetime
        {
            public CancellationToken ApplicationStarted => CancellationToken.None;
            public CancellationToken ApplicationStopping => CancellationToken.None;
            public CancellationToken ApplicationStopped => CancellationToken.None;

            public void StopApplication()
            {
            }
        }
    }
}
