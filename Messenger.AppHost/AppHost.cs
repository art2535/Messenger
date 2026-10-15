using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose");

var useDocker = builder.Configuration.GetValue<bool>("UseDocker");

int apiHttpsPort = builder.Configuration.GetValue<int>("Endpoints:HTTPS:API:Port");
int apiHttpPort = builder.Configuration.GetValue<int>("Endpoints:HTTP:API:Port");
int webHttpsPort = builder.Configuration.GetValue<int>("Endpoints:HTTPS:Web:Port");
int webHttpPort = builder.Configuration.GetValue<int>("Endpoints:HTTP:Web:Port");

IResourceBuilder<IResourceWithConnectionString> messengerDb;
IResourceBuilder<IResourceWithConnectionString> rabbitmq;
IResourceBuilder<IResourceWithConnectionString> redis;
IResourceBuilder<ExecutableResource>? redisReady = null;
IResourceBuilder<ExecutableResource>? rabbitmqReady = null;

if (useDocker)
{
    var postgres = builder.AddPostgres("postgres")
        .WithDataVolume()
        .WithPgAdmin();

    messengerDb = postgres.AddDatabase("DefaultConnection", "GUAP_Messenger");

    var rabbitUser = builder.AddParameter("rabbitmq-username", "guest");
    var rabbitPassword = builder.AddParameter("rabbitmq-password", "guest", secret: true);

    rabbitmq = builder.AddRabbitMQ("RabbitMQ", rabbitUser, rabbitPassword)
        .WithDataVolume()
        .WithManagementPlugin();

    redis = builder.AddRedis("Redis")
        .WithDataVolume()
        .WithRedisInsight();
}
else if (OperatingSystem.IsWindows())
{
    messengerDb = builder.AddConnectionString("DefaultConnection");

    rabbitmqReady = builder.AddExecutable("rabbitmq-wsl", "wsl", Environment.CurrentDirectory,
        "-d", "Ubuntu-26.04", "sh", "-c", "sudo pkill -f rabbitmq-server 2>/dev/null; " +
        "sudo rabbitmq-server");

    rabbitmq = builder.AddConnectionString("RabbitMQ");

    redisReady = builder.AddExecutable("redis-wsl", "wsl", Environment.CurrentDirectory,
        "-d", "Ubuntu-26.04", "sh", "-c",
        "sudo pkill -f redis-server 2>/dev/null; exec redis-server --port 6379");

    redis = builder.AddConnectionString("Redis");
}
else
{
    messengerDb = builder.AddConnectionString("DefaultConnection");
    rabbitmq = builder.AddConnectionString("RabbitMQ");
    redis = builder.AddConnectionString("Redis");
}

var api = builder.AddProject<Projects.Messenger_API>("messenger-api")
    .WithHttpsEndpoint(port: apiHttpsPort, isProxied: false)
    .WithEndpoint("http", e => e.Port = apiHttpPort)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar API Docs";
        url.Url = "/scalar";
    })
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Scalar API Docs (HTTP)";
        url.Url = "/scalar";
        url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
    })
    .WithReference(messengerDb)
    .WithEnvironment("Redis__ConnectionString", redis.Resource.ConnectionStringExpression)
    .WithReference(rabbitmq);

if (useDocker)
{
    api = api.WaitFor(messengerDb)
             .WaitFor(redis)
             .WaitFor(rabbitmq);
}
else
{
    if (redisReady is not null)
        api = api.WaitFor(redisReady);

    if (rabbitmqReady is not null)
        api = api.WaitFor(rabbitmqReady);
}

builder.AddProject<Projects.Messenger_Web>("messenger-web")
    .WithHttpsEndpoint(port: webHttpsPort, isProxied: false)
    .WithEndpoint("http", e => e.Port = webHttpPort)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Messenger Web App";
    })
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Messenger Web (HTTP)";
        url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
    })
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();