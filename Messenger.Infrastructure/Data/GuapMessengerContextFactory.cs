using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Messenger.Infrastructure.Data;

public sealed class GuapMessengerContextFactory : IDesignTimeDbContextFactory<GuapMessengerContext>
{
    public GuapMessengerContext CreateDbContext(string[] args)
    {
        var apiDirectory = FindApiProjectDirectory();
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Не задана строка подключения DefaultConnection. Укажите ConnectionStrings__DefaultConnection.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<GuapMessengerContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new GuapMessengerContext(optionsBuilder.Options);
    }

    private static string FindApiProjectDirectory()
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

        throw new DirectoryNotFoundException(
            "Не удалось найти Messenger.API.csproj. Запустите dotnet ef из каталога решения/проекта или рядом с ним.");
    }
}
