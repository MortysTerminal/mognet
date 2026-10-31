using Microsoft.Extensions.Logging;
using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core.Modules
{
    /// <summary>Outcome of <see cref="ModuleManager.SetEnabledAsync"/>.</summary>
    /// <param name="Succeeded">The module is now in the requested state.</param>
    /// <param name="Error">Why the module could not be brought into that state; <see langword="null"/> on success.</param>
    public record ModuleSwitchResult(bool Succeeded, string? Error);

    /// <summary>
    /// Starts and stops modules and remembers which ones are switched on (scope <c>core</c>, key <c>module.&lt;id&gt;.enabled</c>).
    /// A module that is not in the settings is off. A failing module is logged and never reaches the host or other modules.
    /// All state changes run one at a time.
    /// </summary>
    public sealed class ModuleManager
    {
        public static string SettingKey(string moduleId) => $"module.{moduleId}.enabled";

        private readonly IReadOnlyList<IBotModule> _modules;
        private readonly SettingsStore _settings;
        private readonly ILogger<ModuleManager> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly List<IBotModule> _running = [];

        public ModuleManager(IEnumerable<IBotModule> modules, SettingsStore settings, ILogger<ModuleManager> logger)
        {
            ArgumentNullException.ThrowIfNull(modules);
            _settings = settings;
            _logger = logger;
            _modules = modules.ToList();

            var seen = new HashSet<string>();
            foreach (var module in _modules)
            {
                if (!MigrationSet.IsValidScope(module.Id))
                {
                    throw new ArgumentException(
                        $"Module id '{module.Id}' is invalid: use lower-case letters, digits and underscores, starting with a letter.",
                        nameof(modules));
                }

                if (module.Id == CoreMigrations.Scope)
                    throw new ArgumentException($"Module id '{module.Id}' is reserved.", nameof(modules));

                if (!seen.Add(module.Id))
                    throw new ArgumentException($"Module id '{module.Id}' is registered more than once.", nameof(modules));
            }
        }

        public IReadOnlyList<string> ModuleIds => _modules.Select(m => m.Id).ToList();

        public bool IsRunning(string id)
        {
            return IsRunningUnlocked(Find(id));
        }

        public async Task<bool> IsEnabledAsync(string id, CancellationToken cancellationToken = default)
        {
            Find(id);
            return await _settings.GetAsync(CoreMigrations.Scope, SettingKey(id), cancellationToken) == "true";
        }

        /// <summary>Starts every module that is switched on and not yet running, in registration order.</summary>
        public async Task StartEnabledAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                foreach (var module in _modules)
                {
                    if (IsRunningUnlocked(module) || !await IsEnabledAsync(module.Id, cancellationToken))
                        continue;
                    await TryStartAsync(module, cancellationToken);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Saves the state, then starts or stops the module at once. The state is kept also when the start or stop fails;
        /// the module then counts as stopped and the result carries the reason.
        /// </summary>
        /// <exception cref="ArgumentException">No module has this id.</exception>
        public async Task<ModuleSwitchResult> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default)
        {
            var module = Find(id);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await _settings.SetAsync(CoreMigrations.Scope, SettingKey(id), enabled ? "true" : "false", cancellationToken);

                var running = IsRunningUnlocked(module);
                if (enabled == running)
                    return new ModuleSwitchResult(true, null);

                var error = enabled
                    ? await TryStartAsync(module, cancellationToken)
                    : await TryStopAsync(module, cancellationToken);
                return new ModuleSwitchResult(error is null, error);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Stops all running modules, last started first. The settings stay as they are.</summary>
        public async Task StopAllAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                IBotModule[] running;
                lock (_running)
                    running = _running.AsEnumerable().Reverse().ToArray();
                foreach (var module in running)
                    await TryStopAsync(module, cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<string?> TryStartAsync(IBotModule module, CancellationToken cancellationToken)
        {
            try
            {
                await module.StartAsync(cancellationToken);
                lock (_running)
                    _running.Add(module);
                _logger.LogInformation("Module {ModuleId} started.", module.Id);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module {ModuleId} failed to start and stays stopped.", module.Id);
                return $"Module '{module.Id}' failed to start: {ex.Message}";
            }
        }

        private async Task<string?> TryStopAsync(IBotModule module, CancellationToken cancellationToken)
        {
            // The module counts as stopped even if its stop fails: a half-stopped module must not be stopped again.
            lock (_running)
                _running.Remove(module);
            try
            {
                await module.StopAsync(cancellationToken);
                _logger.LogInformation("Module {ModuleId} stopped.", module.Id);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module {ModuleId} failed to stop cleanly and counts as stopped.", module.Id);
                return $"Module '{module.Id}' failed to stop: {ex.Message}";
            }
        }

        private bool IsRunningUnlocked(IBotModule module)
        {
            lock (_running)
                return _running.Contains(module);
        }

        private IBotModule Find(string id) =>
            _modules.FirstOrDefault(m => m.Id == id)
            ?? throw new ArgumentException($"No module with id '{id}' is registered.", nameof(id));
    }
}
