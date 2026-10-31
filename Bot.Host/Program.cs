using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mognet.Bot.Core;
using Mognet.Bot.Core.Health;
using Mognet.Bot.Core.Modules;
using Mognet.Modules.TwitchSchedule;
using Serilog;

// Container health check: runs before the host and the logger exist, and exits with the check's result.
if (args.Contains("--healthcheck"))
    Environment.Exit(HealthCheck.Run(HealthFile.DefaultPath, DateTimeOffset.UtcNow, HealthCheck.DefaultMaxAge));

// Bootstrap logger active before the host starts (catches startup failures)
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Mognet starting...");

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
            services.AddMognetCore(context.Configuration);
            services.AddMognetHealthFile();
            services.AddSingleton<IBotModule, TwitchScheduleModule>();
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
