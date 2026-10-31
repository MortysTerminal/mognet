using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mognet.Bot.Core.Gateway;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core
{
    /// <summary>
    /// Start-up and shutdown order. Start: migrations (core and every registered module), gateway, enabled modules.
    /// Stop: modules, then gateway. A failed migration stops the host with exit code 1 before anything connects.
    /// </summary>
    public sealed class BotRuntime : IHostedService
    {
        private readonly IGateway _gateway;
        private readonly MigrationRunner _migrations;
        private readonly ModuleManager _modules;
        private readonly IReadOnlyList<IBotModule> _registeredModules;
        private readonly IFatalShutdown _shutdown;
        private readonly ILogger<BotRuntime> _logger;

        public BotRuntime(
            IGateway gateway,
            MigrationRunner migrations,
            ModuleManager modules,
            IEnumerable<IBotModule> registeredModules,
            IFatalShutdown shutdown,
            ILogger<BotRuntime> logger)
        {
            _gateway = gateway;
            _migrations = migrations;
            _modules = modules;
            _registeredModules = registeredModules.ToList();
            _shutdown = shutdown;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var sets = new List<MigrationSet> { CoreMigrations.Set };
            sets.AddRange(_registeredModules.Select(m => m.Migrations).OfType<MigrationSet>());

            try
            {
                await _migrations.ApplyAsync(sets, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogCritical(ex, "The database could not be prepared. The bot stops and the container will be restarted.");
                _shutdown.Shutdown();
                return;
            }

            await _gateway.StartAsync(cancellationToken);
            await _modules.StartEnabledAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _modules.StopAllAsync(cancellationToken);
            await _gateway.StopAsync(cancellationToken);
        }
    }
}
