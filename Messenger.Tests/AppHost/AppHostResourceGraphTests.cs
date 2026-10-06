using FluentAssertions;

namespace Messenger.Tests.AppHost
{
    public class AppHostResourceGraphTests
    {
        private static string ReadAppHostSource()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "AppHostSource", "AppHost.cs"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Messenger.AppHost", "AppHost.cs"),
                Path.Combine(Directory.GetCurrentDirectory(), "Messenger.AppHost", "AppHost.cs"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "Messenger.AppHost", "AppHost.cs"),
                "/tmp/Messenger/Messenger.AppHost/AppHost.cs"
            };

            foreach (var path in candidates)
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full))
                    return File.ReadAllText(full);
            }

            throw new FileNotFoundException("Messenger.AppHost/AppHost.cs не найден для структурных тестов");
        }

        [Fact]
        public void AppHost_DefinesExpectedProjectResources()
        {
            var source = ReadAppHostSource();

            source.Should().Contain("AddProject<Projects.Messenger_API>(\"messenger-api\")");
            source.Should().Contain("AddProject<Projects.Messenger_Web>(\"messenger-web\")");
        }

        [Fact]
        public void AppHost_DockerMode_DefinesPostgresRedisRabbit()
        {
            var source = ReadAppHostSource();

            source.Should().Contain("AddPostgres(\"postgres\")");
            source.Should().Contain("AddDatabase(\"DefaultConnection\"");
            source.Should().Contain("AddRabbitMQ(\"RabbitMQ\"");
            source.Should().Contain("AddRedis(\"Redis\")");
        }

        [Fact]
        public void AppHost_APIWaitsForInfrastructure()
        {
            var source = ReadAppHostSource();

            source.Should().Contain(".WaitFor(messengerDb)");
            source.Should().Contain(".WaitFor(redis)");
            source.Should().Contain(".WaitFor(rabbitmq)");
            source.Should().Contain(".WithReference(api)");
            source.Should().Contain(".WaitFor(api)");
        }

        [Fact]
        public void AppHost_ExposesScalarDocsUrl()
        {
            var source = ReadAppHostSource();
            source.Should().Contain("/scalar");
            source.Should().Contain("Scalar API Docs");
        }

        [Fact]
        public void AppHost_SupportsNonDockerConnectionStrings()
        {
            var source = ReadAppHostSource();
            source.Should().Contain("AddConnectionString(\"DefaultConnection\")");
            source.Should().Contain("AddConnectionString(\"RabbitMQ\")");
            source.Should().Contain("AddConnectionString(\"Redis\")");
        }

        [Fact]
        public void AppHostCsproj_ReferencesAPIAndWeb()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "AppHostSource", "Messenger.AppHost.csproj"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Messenger.AppHost", "Messenger.AppHost.csproj"),
                Path.Combine(Directory.GetCurrentDirectory(), "Messenger.AppHost", "Messenger.AppHost.csproj"),
                "/tmp/Messenger/Messenger.AppHost/Messenger.AppHost.csproj"
            };

            var path = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
            path.Should().NotBeNull("Messenger.AppHost.csproj должен быть доступен");

            var csproj = File.ReadAllText(path!);
            csproj.Should().Contain("Messenger.API");
            csproj.Should().Contain("Messenger.Web");
            csproj.Should().Contain("Aspire.Hosting.PostgreSQL");
            csproj.Should().Contain("Aspire.Hosting.Redis");
            csproj.Should().Contain("Aspire.Hosting.RabbitMQ");
        }
    }
}
