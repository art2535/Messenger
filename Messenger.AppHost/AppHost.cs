var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("postgres:17")
    .WithDataVolume()
    .WithPgAdmin();

var messengerDb = postgres.AddDatabase("DefaultConnection", "GUAP_Messenger");

var rabbitUser = builder.AddParameter("rabbitmq-username", "guest");
var rabbitPassword = builder.AddParameter("rabbitmq-password", "guest", secret: true);

var rabbitmq = builder.AddRabbitMQ("RabbitMQ", userName: rabbitUser, password: rabbitPassword)
    .WithDataVolume()
    .WithManagementPlugin(port: 15672);

var redis = builder.AddRedis("Redis")
    .WithImage("redis:7.4")
    .WithDataVolume();

var api = builder.AddProject<Projects.Messenger_API>("messenger-api")
    .WithHttpsEndpoint(port: 7001, isProxied: false)
    .WithEndpoint("http", e => e.Port = 5245)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar API Docs";
        url.Url = "/scalar";
    })
    .WithReference(messengerDb).WaitFor(messengerDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(redis).WaitFor(redis);

builder.AddProject<Projects.Messenger_Web>("messenger-web")
    .WithHttpsEndpoint(port: 5207, isProxied: false)
    .WithEndpoint("http", e => e.Port = 5207)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Messenger Web App";
    })
    .WithReference(api).WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();