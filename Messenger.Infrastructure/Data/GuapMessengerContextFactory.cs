using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Messenger.Infrastructure.Data;

public sealed class GuapMessengerContextFactory : IDesignTimeDbContextFactory<GuapMessengerContext>
{
    public GuapMessengerContext CreateDbContext(string[] args)
    {
        var apiDirectory = TryFindApiProjectDirectory();
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        var configurationBuilder = new ConfigurationBuilder();

        if (apiDirectory is not null)
        {
            configurationBuilder
                .SetBasePath(apiDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false);
        }

        // Environment variables intentionally have the highest precedence and also
        // allow dotnet-ef to run without the API project/config files being present.
        var configuration = configurationBuilder.AddEnvironmentVariables().Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var configHint = apiDirectory is null
                ? "Каталог Messenger.API не найден, а переменная ConnectionStrings__DefaultConnection не задана."
                : "Не задана строка подключения DefaultConnection в appsettings или переменных окружения.";

            throw new InvalidOperationException($"{configHint} Укажите ConnectionStrings__DefaultConnection.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<GuapMessengerContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new GuapMessengerContext(optionsBuilder.Options);
    }

    private static string? TryFindApiProjectDirectory()
    {
        var startDirectories = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var startDirectory in startDirectories)
        {
            var directory = new DirectoryInfo(startDirectory);
            while (directory is not null)
            {
                var nestedApiDirectory = Path.Combine(directory.FullName, "Messenger.API");
                if (File.Exists(Path.Combine(nestedApiDirectory, "Messenger.API.csproj")))
                    return nestedApiDirectory;
                if (File.Exists(Path.Combine(directory.FullName, "Messenger.API.csproj")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        return null;
    }
}
