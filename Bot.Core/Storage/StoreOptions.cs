using Microsoft.Extensions.Configuration;

namespace Mognet.Bot.Core.Storage
{
    /// <summary>Bootstrap setting for the database file.</summary>
    public record StoreOptions(string Path)
    {
        public const string Key = "Database:Path";

        // Name of the environment variable that sets this key.
        public static string EnvironmentVariable => Key.Replace(":", "__");

        /// <summary>Throws <see cref="InvalidOperationException"/> naming the variable when the path is missing or blank.</summary>
        public static StoreOptions FromConfiguration(IConfiguration configuration)
        {
            var raw = configuration[Key];
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException($"{EnvironmentVariable} is not set: give the path of the SQLite database file.");
            return new StoreOptions(raw.Trim());
        }
    }
}
