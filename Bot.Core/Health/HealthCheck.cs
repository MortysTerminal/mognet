namespace Mognet.Bot.Core.Health
{
    /// <summary>The container health check: reads the health file and returns a process exit code.</summary>
    public static class HealthCheck
    {
        public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(3);

        /// <summary>Returns 0 when the file is fresh and says ok. Otherwise returns 1 and writes one line saying why. Never throws.</summary>
        public static int Run(string path, DateTimeOffset now, TimeSpan maxAge, TextWriter? output = null)
        {
            output ??= Console.Out;
            try
            {
                var reason = Check(path, now, maxAge);
                if (reason is null)
                    return 0;

                output.WriteLine(reason);
                return 1;
            }
            catch (Exception ex)
            {
                try
                {
                    output.WriteLine("unhealthy: the health file could not be checked (" + ex.GetType().Name + ")");
                }
                catch (Exception)
                {
                    // The exit code is the signal; there is nothing else to do.
                }
                return 1;
            }
        }

        private static string? Check(string path, DateTimeOffset now, TimeSpan maxAge)
        {
            if (!File.Exists(path))
                return "unhealthy: health file not found";

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2 || !HealthFile.TryParseTimestamp(lines[0].Trim(), out var written))
                return "unhealthy: health file is not readable";

            if (now - written > maxAge)
                return "unhealthy: health file is too old (written " + written.ToString("u") + ")";

            var state = lines[1].Trim();
            if (state == HealthFile.Ok)
                return null;

            return state.StartsWith(HealthFile.StalledPrefix, StringComparison.Ordinal)
                ? "unhealthy: stalled modules " + state[HealthFile.StalledPrefix.Length..]
                : "unhealthy: health file is not readable";
        }
    }
}
