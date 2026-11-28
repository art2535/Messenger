using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
builder.AddDockerComposeEnvironment("compose");

var ports = builder.Configuration.GetSection("Ports");
var credentials = builder.Configuration.GetSection("Credentials");

static string Required(IConfiguration configuration, string key)
{
    var value = configuration[key];
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Не задан обязательный параметр '{key}'. Используйте appsettings.Development.json или User Secrets.");
}

static int Port(IConfiguration configuration, string key, int defaultValue)
{
    var value = configuration.GetValue(key, defaultValue);
    return value is >= 1 and <= 65535
        ? value
        : throw new InvalidOperationException($"Параметр '{key}' должен быть в диапазоне 1..65535.");
}

var postgresUser = builder.AddParameter("postgres-username",
    Required(credentials, "Postgres:Username"));
var postgresPassword = builder.AddParameter("postgres-password",
    Required(credentials, "Postgres:Password"), secret: true);
var rabbitUser = builder.AddParameter("rabbitmq-username",
    Required(credentials, "RabbitMQ:Username"));
var rabbitPassword = builder.AddParameter("rabbitmq-password",
    Required(credentials, "RabbitMQ:Password"), secret: true);
var grafanaAdminUser = Required(credentials, "Grafana:AdminUser");
var grafanaAdminPassword = builder.AddParameter("grafana-admin-password",
    Required(credentials, "Grafana:AdminPassword"), secret: true);
var dbName = Required(builder.Configuration, "Database:Name");

var postgres = builder.AddPostgres("postgres", userName: postgresUser, password: postgresPassword)
    .WithImage("postgres:17")
    .WithContainerName("messenger-postgres")
    .WithDataVolume()
    .WithPgAdmin(pgAdmin =>
    {
        pgAdmin.WithImage("dpage/pgadmin4", "latest");
        pgAdmin.WithContainerName("messenger-pgadmin");
        pgAdmin.WithUrlForEndpoint("http", u => u.DisplayText = "pgAdmin");
    });

var messengerDb = postgres.AddDatabase("DefaultConnection", dbName);

var rabbitmq = builder.AddRabbitMQ("RabbitMQ", rabbitUser, rabbitPassword)
    .WithImageTag("4.3-management")
    .WithContainerName("messenger-rabbitmq")
    .WithDataVolume()
    .WithManagementPlugin(Port(ports, "RabbitMQManagement", 15672))
    .WithUrlForEndpoint("management", u => u.DisplayText = "RabbitMQ Management");

var redis = builder.AddRedis("Redis")
    .WithImage("redis:7.4")
    .WithContainerName("messenger-redis")
    .WithDataVolume();

var rabbitmqExporter = builder.AddContainer("rabbitmq-exporter", "kbudde/rabbitmq-exporter", "latest")
    .WithContainerName("messenger-rabbitmq-exporter")
    .WithHttpEndpoint(port: Port(ports, "RabbitMQExporter", 9419), targetPort: 9419, name: "metrics")
    .WithUrlForEndpoint("metrics", u => u.DisplayText = "RabbitMQ Metrics")
    .WithEnvironment("RABBIT_URL", "http://RabbitMQ:15672")
    .WithEnvironment("RABBIT_USER", rabbitUser)
    .WithEnvironment("RABBIT_PASSWORD", rabbitPassword)
    .WithEnvironment("PUBLISH_PORT", "9419")
    .WithEnvironment("RABBIT_CAPABILITIES", "bert,no_sort")
    .WithEnvironment("RABBIT_EXPORTERS", "exchange,node,overview,queue")
    .WaitFor(rabbitmq);

var redisExporter = builder.AddContainer("redis-exporter", "oliver006/redis_exporter", "latest")
    .WithContainerName("messenger-redis-exporter")
    .WithHttpEndpoint(port: Port(ports, "RedisExporter", 9121), targetPort: 9121, name: "metrics")
    .WithUrlForEndpoint("metrics", u => u.DisplayText = "Redis Metrics")
    .WithEnvironment("REDIS_ADDR", "redis://Redis:6379")
    .WaitFor(redis);

var monitoringPath = Path.Combine(builder.AppHostDirectory, "monitoring");
var prometheus = builder.AddDockerfile("prometheus", monitoringPath)
    .WithImage("messenger-prometheus", "local")
    .WithContainerName("messenger-prometheus")
    .WithVolume("prometheus-data", "/prometheus")
    .WithArgs("--config.file=/etc/prometheus/prometheus.yml", "--storage.tsdb.path=/prometheus", "--storage.tsdb.retention.time=15d")
    .WithHttpEndpoint(port: Port(ports, "Prometheus", 9090), targetPort: 9090, name: "http")
    .WithUrlForEndpoint("http", u => u.DisplayText = "Prometheus")
    .WaitFor(rabbitmqExporter)
    .WaitFor(redisExporter);

var grafanaPath = Path.Combine(monitoringPath, "grafana");
var grafana = builder.AddDockerfile("grafana", grafanaPath)
    .WithImage("messenger-grafana", "local")
    .WithContainerName("messenger-grafana")
    .WithVolume("grafana-data", "/var/lib/grafana")
    .WithEnvironment("GF_SECURITY_ADMIN_USER", grafanaAdminUser)
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", grafanaAdminPassword)
    .WithEnvironment("GF_USERS_ALLOW_SIGN_UP", "false")
    .WithHttpEndpoint(port: Port(ports, "Grafana", 3000), targetPort: 3000, name: "http")
    .WithUrlForEndpoint("http", u => u.DisplayText = "Grafana")
    .WaitFor(prometheus);

var apiPorts = ports.GetSection("API");
var api = builder.AddDotnetProject("messenger-api", "../Messenger.API/Messenger.API.csproj")
    .WithHttpsEndpoint(port: Port(apiPorts, "HTTPS", 7001), isProxied: false)
    .WithEndpoint("http", e => e.Port = Port(apiPorts, "HTTP", 5245))
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar API Docs";
        url.Url = "/scalar";
    })
    .WithUrlForEndpoint("http", url => url.DisplayText = "API HTTP")
    .WithReference(messengerDb).WaitFor(messengerDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(redis).WaitFor(redis);

var webPorts = ports.GetSection("Web");
var web = builder.AddDotnetProject("messenger-web", "../Messenger.Web/Messenger.Web.csproj")
    .WithHttpsEndpoint(port: Port(webPorts, "HTTPS", 7010), isProxied: false)
    .WithEndpoint("http", e => e.Port = Port(webPorts, "HTTP", 5207))
    .WithUrlForEndpoint("https", url => url.DisplayText = "Messenger Web")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Web HTTP")
    .WithReference(api).WaitFor(api)
    .WithReference(redis).WaitFor(redis)
    .WithExternalHttpEndpoints();

prometheus.WithReference(api.GetEndpoint("https"));
prometheus.WithReference(web.GetEndpoint("https"));
builder.Build().Run();
