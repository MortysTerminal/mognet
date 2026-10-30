using System.Text.RegularExpressions;

namespace Mognet.Bot.Core.Storage
{
    /// <summary>
    /// The ordered migrations of one scope (<c>core</c> or one module). Versions count independently per scope.
    /// Invalid input is rejected here, before any database is touched.
    /// </summary>
    public sealed partial class MigrationSet
    {
        public string Scope { get; }
        public IReadOnlyList<Migration> Migrations { get; }

        public MigrationSet(string scope, IEnumerable<Migration> migrations)
        {
            ArgumentNullException.ThrowIfNull(migrations);

            if (string.IsNullOrWhiteSpace(scope) || !ScopePattern().IsMatch(scope))
            {
                throw new ArgumentException(
                    $"Migration scope '{scope}' is invalid: use lower-case letters, digits and underscores, starting with a letter.",
                    nameof(scope));
            }

            var ordered = migrations.OrderBy(m => m.Version).ToList();
            foreach (var migration in ordered)
            {
                if (migration.Version < 1)
                {
                    throw new ArgumentException(
                        $"Migration '{migration.Name}' in scope '{scope}' has version {migration.Version}: versions start at 1.",
                        nameof(migrations));
                }

                if (string.IsNullOrWhiteSpace(migration.Name) || string.IsNullOrWhiteSpace(migration.Sql))
                {
                    throw new ArgumentException(
                        $"Migration {migration.Version} in scope '{scope}' needs a name and SQL.",
                        nameof(migrations));
                }
            }

            var duplicate = ordered.GroupBy(m => m.Version).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                throw new ArgumentException(
                    $"Scope '{scope}' has more than one migration with version {duplicate.Key}.",
                    nameof(migrations));
            }

            Scope = scope;
            Migrations = ordered;
        }

        [GeneratedRegex("^[a-z][a-z0-9_]*$")]
        private static partial Regex ScopePattern();
    }
}
