using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core.Tests
{
    public class MigrationTests : IDisposable
    {
        private readonly TempDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private Task Apply(params MigrationSet[] sets) => new MigrationRunner(_db.Store()).ApplyAsync(sets);

        [Fact]
        public async Task Empty_database_gets_the_core_tables()
        {
            await Apply(CoreMigrations.Set);

            Assert.True(await _db.TableExistsAsync("settings"));
            Assert.True(await _db.TableExistsAsync("sent_alerts"));
            var rows = await _db.QueryAsync("SELECT scope, version, name FROM schema_migrations");
            Assert.Equal(new object?[] { "core", 1L, "settings_and_sent_alerts" }, Assert.Single(rows));
        }

        [Fact]
        public async Task Second_run_applies_nothing_and_keeps_data()
        {
            await Apply(CoreMigrations.Set);
            await _db.ExecuteAsync("INSERT INTO settings VALUES ('a', 'b', 'c', 'now')");
            var before = await _db.QueryAsync("SELECT * FROM schema_migrations");

            await Apply(CoreMigrations.Set);

            Assert.Equal(before[0], Assert.Single(await _db.QueryAsync("SELECT * FROM schema_migrations")));
            Assert.Single(await _db.QueryAsync("SELECT * FROM settings"));
        }

        [Fact]
        public async Task Failed_migration_rolls_back_stops_later_ones_and_can_be_retried()
        {
            var first = new Migration(1, "create_first", "CREATE TABLE first (id INTEGER)");
            var broken = new Migration(2, "broken", "CREATE TABLE partial (id INTEGER); THIS IS NOT SQL;");
            var later = new Migration(3, "create_later", "CREATE TABLE later (id INTEGER)");

            var ex = await Assert.ThrowsAsync<MigrationException>(() =>
                Apply(new MigrationSet("mod", [first, broken, later])));

            Assert.Contains("'mod'", ex.Message);
            Assert.Contains("2", ex.Message);
            Assert.Contains("broken", ex.Message);
            Assert.True(await _db.TableExistsAsync("first"));
            Assert.False(await _db.TableExistsAsync("partial"));
            Assert.False(await _db.TableExistsAsync("later"));
            var versions = await _db.QueryAsync("SELECT version FROM schema_migrations WHERE scope = 'mod'");
            Assert.Equal(1L, Assert.Single(versions)[0]);

            var fixedTwo = new Migration(2, "broken", "CREATE TABLE partial (id INTEGER)");
            await Apply(new MigrationSet("mod", [first, fixedTwo, later]));

            Assert.True(await _db.TableExistsAsync("partial"));
            Assert.True(await _db.TableExistsAsync("later"));
            Assert.Equal(3, (await _db.QueryAsync("SELECT version FROM schema_migrations WHERE scope = 'mod'")).Count);
        }

        [Fact]
        public async Task Scopes_count_versions_independently()
        {
            await Apply(
                new MigrationSet("one", [new Migration(1, "a", "CREATE TABLE t_one (id INTEGER)")]),
                new MigrationSet("two", [new Migration(1, "b", "CREATE TABLE t_two (id INTEGER)")]));

            Assert.True(await _db.TableExistsAsync("t_one"));
            Assert.True(await _db.TableExistsAsync("t_two"));
            Assert.Equal(2, (await _db.QueryAsync("SELECT 1 FROM schema_migrations WHERE version = 1")).Count);
        }

        [Fact]
        public async Task Database_with_a_newer_version_fails_clearly_and_changes_nothing()
        {
            await Apply(new MigrationSet("mod", [new Migration(1, "a", "CREATE TABLE a (id INTEGER)"), new Migration(2, "b", "CREATE TABLE b (id INTEGER)")]));

            var older = new MigrationSet("mod", [new Migration(1, "a", "CREATE TABLE a (id INTEGER)")]);
            var other = new MigrationSet("other", [new Migration(1, "x", "CREATE TABLE x (id INTEGER)")]);
            var ex = await Assert.ThrowsAsync<MigrationException>(() => Apply(other, older));

            Assert.Contains("'mod'", ex.Message);
            Assert.Contains("2", ex.Message);
            Assert.False(await _db.TableExistsAsync("x"));
        }

        [Fact]
        public void Duplicate_and_non_positive_versions_are_rejected_without_a_database()
        {
            Assert.Throws<ArgumentException>(() => new MigrationSet("mod",
                [new Migration(1, "a", "SELECT 1"), new Migration(1, "b", "SELECT 1")]));
            Assert.Throws<ArgumentException>(() => new MigrationSet("mod", [new Migration(0, "a", "SELECT 1")]));
            Assert.Throws<ArgumentException>(() => new MigrationSet("mod", [new Migration(-1, "a", "SELECT 1")]));
            Assert.False(File.Exists(_db.FilePath));
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("Core")]
        [InlineData("a b")]
        [InlineData("1abc")]
        [InlineData("a;drop")]
        public void Malformed_scope_is_rejected(string scope)
        {
            Assert.Throws<ArgumentException>(() => new MigrationSet(scope, [new Migration(1, "a", "SELECT 1")]));
        }

        [Fact]
        public async Task Same_scope_given_twice_is_rejected_before_the_database_is_touched()
        {
            var set = new MigrationSet("mod", [new Migration(1, "a", "SELECT 1")]);

            await Assert.ThrowsAsync<ArgumentException>(() => Apply(set, set));
            Assert.False(File.Exists(_db.FilePath));
        }
    }
}
