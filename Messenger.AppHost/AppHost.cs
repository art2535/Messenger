using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose");

var ports = builder.Configuration.GetSection("Ports");
var credentials = builder.Configuration.GetSection("Credentials");

var postgresPassword = builder.AddParameter("postgres-password",
    credentials["Postgres:Password"] ?? "postgres",
    secret: true);

var rabbitUser = builder.AddParameter("rabbitmq-username",
    credentials["RabbitMQ:Username"] ?? "guest");

var rabbitPassword = builder.AddParameter("rabbitmq-password",
    credentials["RabbitMQ:Password"] ?? "guest",
    secret: true);

var grafanaAdminUser = credentials["Grafana:AdminUser"] ?? "admin";
var grafanaAdminPassword = credentials["Grafana:AdminPassword"] ?? "admin";

var dbName = builder.Configuration["Database:Name"] ?? "GUAP_Messenger";

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithImage("postgres:17")
    .WithDataVolume()
    .WithPgAdmin(pgAdmin =>
    {
        pgAdmin.WithUrlForEndpoint("http", u => u.DisplayText = "pgAdmin");
    });

var messengerDb = postgres.AddDatabase("DefaultConnection", dbName);

var rabbitmq = builder.AddRabbitMQ("RabbitMQ", rabbitUser, rabbitPassword)
    .WithDataVolume()
    .WithManagementPlugin(ports.GetValue("RabbitMQManagement", 15672))
    .WithUrlForEndpoint("management", u => u.DisplayText = "RabbitMQ Management");

var redis = builder.AddRedis("Redis")
    .WithImage("redis:7.4")
    .WithDataVolume();

var rabbitmqExporter = builder.AddContainer("rabbitmq-exporter", "kbudde/rabbitmq-exporter", "latest")
    .WithHttpEndpoint(port: ports.GetValue("RabbitMQExporter", 9419), targetPort: 9419, name: "metrics")
    .WithUrlForEndpoint("metrics", u => u.DisplayText = "RabbitMQ Metrics")
    .WithEnvironment("RABBIT_URL", "http://RabbitMQ:15672")
    .WithEnvironment("RABBIT_USER", rabbitUser)
    .WithEnvironment("RABBIT_PASSWORD", rabbitPassword)
    .WithEnvironment("PUBLISH_PORT", "9419")
    .WithEnvironment("RABBIT_CAPABILITIES", "bert,no_sort")
    .WithEnvironment("RABBIT_EXPORTERS", "exchange,node,overview,queue")
    .WaitFor(rabbitmq);

var redisExporter = builder.AddContainer("redis-exporter", "oliver006/redis_exporter", "latest")
    .WithHttpEndpoint(port: ports.GetValue("RedisExporter", 9121), targetPort: 9121, name: "metrics")
    .WithUrlForEndpoint("metrics", u => u.DisplayText = "Redis Metrics")
    .WithEnvironment("REDIS_ADDR", "redis://Redis:6379")
    .WaitFor(redis);

var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "latest")
    .WithBindMount("./monitoring/prometheus.yml", "/etc/prometheus/prometheus.yml", isReadOnly: true)
    .WithVolume("prometheus-data", "/prometheus")
    .WithArgs(
        "--config.file=/etc/prometheus/prometheus.yml",
        "--storage.tsdb.path=/prometheus",
        "--storage.tsdb.retention.time=15d")
    .WithHttpEndpoint(port: ports.GetValue("Prometheus", 9090), targetPort: 9090, name: "http")
    .WithUrlForEndpoint("http", u => u.DisplayText = "Prometheus")
    .WithContainerRuntimeArgs("--add-host=host.docker.internal:host-gateway")
    .WaitFor(rabbitmqExporter)
    .WaitFor(redisExporter);

var grafana = builder.AddContainer("grafana", "grafana/grafana", "latest")
    .WithBindMount("./monitoring/grafana/provisioning", "/etc/grafana/provisioning", isReadOnly: true)
    .WithVolume("grafana-data", "/var/lib/grafana")
    .WithEnvironment("GF_SECURITY_ADMIN_USER", grafanaAdminUser)
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", grafanaAdminPassword)
    .WithEnvironment("GF_USERS_ALLOW_SIGN_UP", "false")
    .WithHttpEndpoint(port: ports.GetValue("Grafana", 3000), targetPort: 3000, name: "http")
    .WithUrlForEndpoint("http", u => u.DisplayText = "Grafana")
    .WaitFor(prometheus);

var apiPorts = ports.GetSection("API");

var api = builder.AddProject<Projects.Messenger_API>("messenger-api")
    .WithHttpsEndpoint(port: apiPorts.GetValue("HTTPS", 7001), isProxied: false)
    .WithEndpoint("http", e => e.Port = apiPorts.GetValue("HTTP", 5245))
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

builder.AddProject<Projects.Messenger_Web>("messenger-web")
    .WithHttpsEndpoint(port: webPorts.GetValue("HTTPS", 7010), isProxied: false)
    .WithEndpoint("http", e => e.Port = webPorts.GetValue("HTTP", 5207))
    .WithUrlForEndpoint("https", url => url.DisplayText = "Messenger Web")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Web HTTP")
    .WithReference(api).WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();