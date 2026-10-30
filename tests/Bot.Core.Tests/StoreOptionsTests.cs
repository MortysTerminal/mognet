using Microsoft.Extensions.Configuration;
using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core.Tests
{
    public class StoreOptionsTests
    {
        private static IConfiguration Config(string? path) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection([new KeyValuePair<string, string?>("Database:Path", path)])
                .Build();

        [Fact]
        public void Missing_path_fails_naming_the_variable()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                StoreOptions.FromConfiguration(new ConfigurationBuilder().Build()));
            Assert.Contains("Database__Path", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Blank_path_fails(string blank)
        {
            Assert.Throws<InvalidOperationException>(() => StoreOptions.FromConfiguration(Config(blank)));
        }

        [Fact]
        public void Valid_path_is_read()
        {
            Assert.Equal("/data/bot.db", StoreOptions.FromConfiguration(Config(" /data/bot.db ")).Path);
        }
    }
}
