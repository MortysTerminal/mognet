using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mognet.Bot.Core.Gateway;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core
{
    public static class CoreServiceCollectionExtensions
    {
        /// <summary>
        /// Registers storage, module management, the gateway and the runtime that orders their start-up.
        /// Modules are registered by the caller as <see cref="IBotModule"/>. The database path is read when the store
        /// is first needed; a missing path stops the host with a message naming the variable.
        /// </summary>
        public static IServiceCollection AddMognetCore(this IServiceCollection services, IConfiguration configuration)
        {
            services.TryAddSingleton(configuration);
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
    }
}
