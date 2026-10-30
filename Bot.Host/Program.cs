using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mognet.Modules.TwitchSchedule.Services;
using Mognet.Modules.TwitchSchedule.Sync;
using Serilog;

// Bootstrap logger active before the host starts (catches startup failures)
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Twitch-Discord Schedule Bot starting...");

    var host = Host.CreateDefaultBuilder(args)

        // ── Configuration ──────────────────────────────────────────────────────
        .ConfigureAppConfiguration((context, config) =>
        {
            config
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
        })

        // ── Logging ────────────────────────────────────────────────────────────
        .UseSerilog((context, services, loggerConfig) =>
        {
            loggerConfig
                .MinimumLevel.Information()
                .WriteTo.Console(
                    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                );
        })

        // ── Services ───────────────────────────────────────────────────────────
        .ConfigureServices((context, services) =>
        {
            services.AddSingleton(TimeProvider.System);
            // Validated when the services are created; an invalid value stops the host with exit code 1.
            services.AddSingleton(_ => SyncOptions.FromConfiguration(context.Configuration));
            services.AddSingleton<TwitchService>();
            services.AddSingleton<DiscordService>();
            services.AddHostedService<SyncService>();
        })

        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Bot failed to start.");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
