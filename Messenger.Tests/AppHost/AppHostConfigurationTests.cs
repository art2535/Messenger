using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Messenger.Tests.AppHost
{
    public class AppHostConfigurationTests
    {
        private static IConfiguration BuildConfig(Dictionary<string, string?> values)
            => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        [Fact]
        public void UseDocker_CanBeReadAsBool()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["UseDocker"] = "true"
            });

            config.GetValue<bool>("UseDocker").Should().BeTrue();
        }

        [Fact]
        public void EndpointPorts_AreReadable()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Endpoints:HTTPS:API:Port"] = "7443",
                ["Endpoints:HTTP:API:Port"] = "7080",
                ["Endpoints:HTTPS:Web:Port"] = "7444",
                ["Endpoints:HTTP:Web:Port"] = "7081"
            });

            config.GetValue<int>("Endpoints:HTTPS:API:Port").Should().Be(7443);
            config.GetValue<int>("Endpoints:HTTP:API:Port").Should().Be(7080);
            config.GetValue<int>("Endpoints:HTTPS:Web:Port").Should().Be(7444);
            config.GetValue<int>("Endpoints:HTTP:Web:Port").Should().Be(7081);
        }

        [Fact]
        public void MissingPorts_DefaultToZero()
        {
            var config = BuildConfig(new Dictionary<string, string?>());
            config.GetValue<int>("Endpoints:HTTPS:API:Port").Should().Be(0);
        }
    }
}
