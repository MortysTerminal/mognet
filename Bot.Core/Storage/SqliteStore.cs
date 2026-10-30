using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Mognet.Bot.Core.Storage
{
    /// <summary>
    /// Opens connections to one SQLite file with foreign keys enforced. The parent directory is created when missing.
    /// Callers get <see cref="DbConnection"/> and do not depend on the driver type.
    /// </summary>
    public sealed class SqliteStore
    {
        private readonly string _connectionString;

        public string Path { get; }

        public SqliteStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Database path must not be empty.", nameof(path));

            Path = path;
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
            }.ToString();
        }

        public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(_connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
    }
}
