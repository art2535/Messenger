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
else
{
    messengerDb = builder.AddConnectionString("DefaultConnection");
    rabbitmq = builder.AddConnectionString("RabbitMQ");
    redis = builder.AddConnectionString("Redis");

    redisReady = builder.AddExecutable("redis-ready", "wsl", ".",
        "bash", "-lc", "redis-cli -p 16379 ping 2>/dev/null | grep -q PONG "
        + "|| (sudo service redis-server start 2>/dev/null; sleep 1; redis-cli -p 16379 ping | grep -q PONG)");

    builder.AddExecutable("redis-cli-monitor", "wsl", ".", "redis-cli", "-p", "16379", "monitor");
}

var api = builder.AddProject<Projects.Messenger_API>("messenger-api")
    .WithEndpoint("https", e => e.Port = apiHttpsPort)
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
    .WaitFor(messengerDb)
    .WithEnvironment("Redis__ConnectionString", redis.Resource.ConnectionStringExpression)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

if (redisReady is not null)
{
    api = api.WaitForCompletion(redisReady);
}

builder.AddProject<Projects.Messenger_Web>("messenger-web")
    .WithEndpoint("https", e => e.Port = webHttpsPort)
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