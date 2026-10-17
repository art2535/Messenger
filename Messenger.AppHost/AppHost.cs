using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var ports = builder.Configuration.GetSection("Ports");

var postgresPassword = builder.AddParameter("postgres-password", 
    builder.Configuration["Credentials:Postgres:Password"] ?? "postgres", 
    secret: true);

var rabbitUser = builder.AddParameter("rabbitmq-username", 
    builder.Configuration["Credentials:RabbitMQ:Username"] ?? "guest");

var rabbitPassword = builder.AddParameter("rabbitmq-password", 
    builder.Configuration["Credentials:RabbitMQ:Password"] ?? "guest", 
    secret: true);

var dbName = builder.Configuration["Database:Name"] ?? "GUAP_Messenger";

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithImage("postgres:17")
    .WithDataVolume()
    .WithPgAdmin(); 

var messengerDb = postgres.AddDatabase("DefaultConnection", dbName);

var rabbitMqMgmtPort = ports.GetValue("RabbitMQManagement", 15672);

var rabbitmq = builder.AddRabbitMQ("RabbitMQ", rabbitUser, rabbitPassword)
    .WithDataVolume()
    .WithManagementPlugin(rabbitMqMgmtPort);

var redis = builder.AddRedis("Redis")
    .WithImage("redis:7.4")
    .WithDataVolume();

var apiPorts = ports.GetSection("API");

var api = builder.AddProject<Projects.Messenger_API>("messenger-api")
    .WithHttpsEndpoint(port: apiPorts.GetValue("HTTPS", 7001), isProxied: false)
    .WithEndpoint("http", e => e.Port = apiPorts.GetValue("HTTP", 5245))
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar API Docs";
        url.Url = "/scalar";
    })
    .WithReference(messengerDb).WaitFor(messengerDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(redis).WaitFor(redis);

var webPorts = ports.GetSection("Web");

builder.AddProject<Projects.Messenger_Web>("messenger-web")
    .WithHttpsEndpoint(port: webPorts.GetValue("HTTPS", 7010), isProxied: false) 
    .WithEndpoint("http", e => e.Port = webPorts.GetValue("HTTP", 5207))
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Messenger Web App";
    })
    .WithReference(api).WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();