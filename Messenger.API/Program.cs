using Messenger.API.Extensions;
using Serilog;
using System.Text;

namespace Messenger.API;

/// <summary>
/// Точка композиции и запуска HTTP API.
/// Регистрация зависимостей, pipeline и endpoint'ы сгруппированы в API extensions.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        ConfigureConsoleOutput();

        var builder = WebApplication.CreateBuilder(args);

        builder.ConfigureDevelopmentCertificate();
        builder.AddMessengerApiLogging();
        builder.EnsureSharedDevelopmentEncryptionKey();
        builder.AddMessengerApiServices();

        var app = builder.Build();

        app.ApplyMessengerDatabaseMigrations();
        app.UseMessengerApiPipeline();
        app.MapMessengerApiEndpoints();

        RunApplication(app);
    }

    private static void ConfigureConsoleOutput()
    {
        if (Console.IsOutputRedirected)
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
    }

    private static void RunApplication(WebApplication app)
    {
        try
        {
            Log.Information("Запуск Messenger.API...");
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Messenger.API неожиданно завершил работу");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
