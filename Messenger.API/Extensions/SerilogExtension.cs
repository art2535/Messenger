using Messenger.API.Services;
using NpgsqlTypes;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Sinks.PostgreSQL;
using Serilog.Sinks.PostgreSQL.ColumnWriters;
using Serilog.Ui.Core.Extensions;
using Serilog.Ui.PostgreSqlProvider.Extensions;
using Serilog.Ui.PostgreSqlProvider.Models;
using Serilog.Ui.Web.Extensions;
using System.Diagnostics;

namespace Messenger.API.Extensions;

public static class SerilogExtension
{
    private static string? GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("DefaultConnection")
        ?? configuration["ConnectionStrings:DefaultConnection"]
        ?? configuration["PostgresConnectionString"];

    private static bool IsEfSqlLog(LogEvent e) =>
        Matching.FromSource("Microsoft.EntityFrameworkCore.Database.Command")(e)
        || Matching.FromSource("Microsoft.EntityFrameworkCore.Database.Connection")(e);

    extension(WebApplicationBuilder builder)
    {
        public void AddMessengerSerilog(string applicationName)
        {
            var connectionString = GetConnectionString(builder.Configuration);

            SelfLog.Enable(msg => Debug.WriteLine(msg));

            var columnOptions = new Dictionary<string, ColumnWriterBase>
            {
                { "Message", new RenderedMessageColumnWriter(NpgsqlDbType.Text) },
                { "MessageTemplate", new MessageTemplateColumnWriter(NpgsqlDbType.Text) },
                { "Level", new LevelColumnWriter(true, NpgsqlDbType.Varchar) },
                { "Timestamp", new TimestampColumnWriter(NpgsqlDbType.TimestampTz) },
                { "Exception", new ExceptionColumnWriter(NpgsqlDbType.Text) },
                { "LogEvent", new LogEventSerializedColumnWriter(NpgsqlDbType.Jsonb) },
                { "Application", new SinglePropertyColumnWriter("Application", PropertyWriteMethod.ToString, NpgsqlDbType.Varchar) },
                { "MachineName", new SinglePropertyColumnWriter("MachineName", PropertyWriteMethod.ToString, NpgsqlDbType.Varchar) },
                { "Environment", new SinglePropertyColumnWriter("EnvironmentName", PropertyWriteMethod.ToString, NpgsqlDbType.Varchar) },
            };

            var hasDb = !string.IsNullOrWhiteSpace(connectionString);

            builder.Host.UseSerilog((context, services, configuration) =>
            {
                configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.WithEnvironmentName()
                    .Enrich.WithMachineName()
                    .Enrich.WithProperty("Application", applicationName)
                    .WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File(
                        path: Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", $"{applicationName.ToLowerInvariant()}-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        shared: true,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

                if (hasDb)
                {
                    configuration.WriteTo.Logger(db => db
                        .Filter.ByExcluding(IsEfSqlLog)
                        .WriteTo.PostgreSQL(
                            connectionString: connectionString!,
                            tableName: "Logs",
                            columnOptions: columnOptions,
                            schemaName: "logging",
                            needAutoCreateTable: false,
                            restrictedToMinimumLevel: LogEventLevel.Information,
                            period: TimeSpan.FromSeconds(5),
                            batchSizeLimit: 50));
                }
            },
            writeToProviders: true);

            if (!hasDb)
            {
                Console.WriteLine($"[WARN] {applicationName}: строка подключения PostgreSQL не найдена — sink PostgreSQL отключён");
            }
        }

        public void AddMessengerSerilogUi()
        {
            var connectionString = GetConnectionString(builder.Configuration);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.WriteLine("[WARN] Serilog.UI не зарегистрирован: нет строки подключения PostgreSQL");
                return;
            }

            builder.Services.AddSerilogUi(options => options
                .UseNpgSql(opts => opts
                    .WithConnectionString(connectionString)
                    .WithSchema("logging")
                    .WithTable("Logs")
                    .WithSinkType(PostgreSqlSinkType.SerilogSinksPostgreSQLAlternative)));
        }
    }

    extension(IServiceCollection services)
    {
        public IServiceCollection AddLogRetention(IConfiguration configuration)
        {
            services.Configure<LogRetentionOptions>(configuration.GetSection(LogRetentionOptions.SectionName));
            services.AddHostedService<LogRetentionService>();
            return services;
        }
    }
}