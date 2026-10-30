using Microsoft.Extensions.Hosting;

namespace Mognet.Bot.Core.Gateway
{
    /// <summary>Sets exit code 1 and stops the host, so the container is restarted.</summary>
    public sealed class HostFatalShutdown(IHostApplicationLifetime lifetime) : IFatalShutdown
    {
        public void Shutdown()
        {
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
    }
}
