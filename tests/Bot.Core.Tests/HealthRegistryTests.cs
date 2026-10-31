using Mognet.Bot.Core.Health;

namespace Mognet.Bot.Core.Tests
{
    public class HealthRegistryTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void A_host_with_no_registered_module_is_healthy()
        {
            var status = new HealthRegistry().Evaluate(Now);

            Assert.True(status.IsOk);
            Assert.Empty(status.Stalled);
        }

        [Fact]
        public void A_future_deadline_is_healthy()
        {
            var registry = new HealthRegistry();
            registry.Report("a", Now.AddMinutes(1));

            Assert.True(registry.Evaluate(Now).IsOk);
        }

        [Fact]
        public void A_deadline_exactly_now_is_still_healthy()
        {
            var registry = new HealthRegistry();
            registry.Report("a", Now);

            Assert.True(registry.Evaluate(Now).IsOk);
        }

        [Fact]
        public void A_past_deadline_is_reported_as_stalled_by_module_id()
        {
            var registry = new HealthRegistry();
            registry.Report("late", Now.AddSeconds(-1));
            registry.Report("fine", Now.AddMinutes(5));

            var status = registry.Evaluate(Now);

            Assert.False(status.IsOk);
            Assert.Equal(["late"], status.Stalled);
        }

        [Fact]
        public void Several_stalled_modules_are_listed_in_order()
        {
            var registry = new HealthRegistry();
            registry.Report("b", Now.AddSeconds(-1));
            registry.Report("a", Now.AddSeconds(-1));

            Assert.Equal(["a", "b"], registry.Evaluate(Now).Stalled);
        }

        [Fact]
        public void A_new_report_moves_the_deadline()
        {
            var registry = new HealthRegistry();
            registry.Report("a", Now.AddSeconds(-1));
            registry.Report("a", Now.AddMinutes(1));

            Assert.True(registry.Evaluate(Now).IsOk);
        }

        [Fact]
        public void A_cleared_module_no_longer_counts()
        {
            var registry = new HealthRegistry();
            registry.Report("a", Now.AddSeconds(-1));
            registry.Clear("a");

            Assert.True(registry.Evaluate(Now).IsOk);
        }

        [Fact]
        public void Clearing_an_unknown_module_does_nothing()
        {
            new HealthRegistry().Clear("nope");
        }
    }
}
