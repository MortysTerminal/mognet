using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mognet.Bot.Core.Gateway;
using Mognet.Bot.Core.Health;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core
{
    public static class CoreServiceCollectionExtensions
    {
        /// <summary>
        /// Registers storage, module management, the gateway, the runtime that orders their start-up and the health registry.
        /// Modules are registered by the caller as <see cref="IBotModule"/>. The database path is read when the store
        /// is first needed; a missing path stops the host with a message naming the variable.
        /// </summary>
        public static IServiceCollection AddMognetCore(this IServiceCollection services, IConfiguration configuration)
        {
            services.TryAddSingleton(configuration);
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<HealthRegistry>();
            services.AddSingleton(_ => new SqliteStore(StoreOptions.FromConfiguration(configuration).Path));
            services.AddSingleton<SettingsStore>();
            services.AddSingleton<MigrationRunner>();
            services.AddSingleton<ModuleManager>();
            services.AddSingleton<IFatalShutdown, HostFatalShutdown>();
            services.AddSingleton<GatewayService>();
            services.AddSingleton<IGateway>(sp => sp.GetRequiredService<GatewayService>());
            services.AddHostedService<BotRuntime>();
            return services;
        }

        /// <summary>
        /// Starts the service that writes the health file read by the container health check. Kept apart from
        /// <see cref="AddMognetCore"/> so the runtime stays the only hosted service the core registers.
        /// </summary>
        public static IServiceCollection AddMognetHealthFile(this IServiceCollection services)
        {
            services.AddHostedService<HealthFileWriter>();
            return services;
        }
    }
}
