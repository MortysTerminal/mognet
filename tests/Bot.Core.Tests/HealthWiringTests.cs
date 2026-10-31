using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mognet.Bot.Core.Health;

namespace Mognet.Bot.Core.Tests
{
    public class HealthWiringTests : IDisposable
    {
        private readonly TempDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private ServiceCollection Services()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostApplicationLifetime, StubLifetime>();
            services.AddMognetCore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Path"] = _db.FilePath }).Build());
            return services;
        }

        [Fact]
        public void The_registry_is_a_singleton_of_the_core()
        {
            using var provider = Services().BuildServiceProvider();

            Assert.Same(provider.GetRequiredService<HealthRegistry>(), provider.GetRequiredService<HealthRegistry>());
        }

        [Fact]
        public void The_health_file_writer_is_a_hosted_service_once_added()
        {
            var services = Services();
            services.AddMognetHealthFile();
            using var provider = services.BuildServiceProvider();

            Assert.Single(provider.GetServices<IHostedService>().OfType<HealthFileWriter>());
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
