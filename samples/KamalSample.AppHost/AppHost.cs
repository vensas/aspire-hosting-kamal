var builder = DistributedApplication.CreateBuilder(args);

builder.AddKamalEnvironment("kamal")
    .WithServers("203.0.113.10")
    .WithRegistry("ghcr.io", "my-org")
    .WithProxyHostSuffix("kamalsample.dev");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var db = postgres.AddDatabase("appdb");

var cache = builder.AddRedis("cache");

builder.AddProject<Projects.KamalSample_Api>("api")
    .WithExternalHttpEndpoints()
    .WithReference(db)
    .WithReference(cache)
    .PublishAsKamalService((_, config) =>
    {
        config.Proxy!.Host = "api.kamalsample.dev";
        config.Proxy.Healthcheck = new() { Path = "/health" };
    });

builder.Build().Run();
