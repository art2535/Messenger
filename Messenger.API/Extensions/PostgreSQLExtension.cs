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
                var connectionString = configuration?.GetConnectionString("DefaultConnection")
                    ?? configuration?["PostgresConnectionString"];

                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException(
                        "Не задана строка подключения PostgreSQL. Настройте ConnectionStrings:DefaultConnection через appsettings, User Secrets или ConnectionStrings__DefaultConnection.");

                var dataSourceBuilder = new NpgsqlDataSourceBuilder(EnsurePoolSettings(connectionString));
                var dataSource = dataSourceBuilder.Build();
                services.AddSingleton(dataSource);
                services.AddDbContext<GuapMessengerContext>((sp, options) =>
                {
                    var ds = sp.GetRequiredService<NpgsqlDataSource>();
                    options.UseNpgsql(ds, npgsql => npgsql.CommandTimeout(15));
                });
                return services;
            }
        }

        private static string EnsurePoolSettings(string connectionString)
        {
            var csb = new NpgsqlConnectionStringBuilder(connectionString);
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
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не найдена ConnectionStrings:DefaultConnection.");
            var target = forMachine ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User;
            Environment.SetEnvironmentVariable("PostgresConnectionString", connectionString, target);
        }
    }
}