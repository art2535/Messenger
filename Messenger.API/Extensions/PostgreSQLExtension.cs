using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Messenger.API.Extensions
{
    public static class PostgreSQLExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddPostgreSQL(IConfiguration? configuration = null)
            {
                var connectionString = Environment.GetEnvironmentVariable("PostgresConnectionString",
                    EnvironmentVariableTarget.Machine) ?? string.Empty;

                if (string.IsNullOrEmpty(connectionString))
                {
                    if (configuration is null)
                    {
                        throw new InvalidOperationException(
                            "Не указана строка подключения к PostgreSQL. " +
                            "Добавьте переменную окружения 'PostgresConnectionString' " +
                            "или настройте 'DefaultConnection' в appsettings.json."
                        );
                    }

                    connectionString = configuration.GetConnectionString("DefaultConnection");
                }

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        "Строка подключения PostgreSQL пуста после всех попыток чтения.");
                }

                connectionString = EnsurePoolSettings(connectionString);

                var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
                var dataSource = dataSourceBuilder.Build();

                services.AddSingleton(dataSource);

                services.AddDbContext<GuapMessengerContext>((sp, options) =>
                {
                    var ds = sp.GetRequiredService<NpgsqlDataSource>();
                    options.UseNpgsql(ds, npgsql =>
                    {
                        npgsql.CommandTimeout(15);
                    });
                });

                return services;
            }
        }

        private static string EnsurePoolSettings(string connectionString)
        {
            var csb = new NpgsqlConnectionStringBuilder(connectionString);
            if (!connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase) &&
                csb.MaxPoolSize == 100)
            {                
            }

            if (!connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
                csb.MaxPoolSize = 100;
            if (!connectionString.Contains("Minimum Pool Size", StringComparison.OrdinalIgnoreCase) &&
                !connectionString.Contains("Min Pool Size", StringComparison.OrdinalIgnoreCase))
                csb.MinPoolSize = 5;

            return csb.ConnectionString;
        }

        public static void SetTheEnvironmentVariable(bool forMachine = false)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.Development.json", true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Не удалось найти строку подключения 'DefaultConnection' в appsettings.Development.json!"
                );
            }

            var target = forMachine ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User;

            Environment.SetEnvironmentVariable("PostgresConnectionString", connectionString, target);
        }
    }
}
