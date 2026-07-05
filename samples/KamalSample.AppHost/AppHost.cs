var builder = DistributedApplication.CreateBuilder(args);

builder.AddKamalEnvironment("kamal")
    .WithServers("203.0.113.10")
    .WithRegistry("ghcr.io", "my-org")
    .WithProxyHostSuffix("kamalsample.ext");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var db = postgres.AddDatabase("appdb");

var cache = builder.AddRedis("cache");

var api = builder.AddProject<Projects.KamalSample_Api>("api")
    .WithExternalHttpEndpoints()
    .WithReference(db)
    .WithReference(cache)
    .PublishAsKamalService((_, config) =>
    {
        config.Proxy!.Host = "api.kamalsample.ext";
        config.Proxy.Healthcheck = new() { Path = "/health" };
    });

// Internal-only API: no external endpoint, so no proxy — reachable from the other
// apps via its network alias (http://orders-web:8080) only.
var orders = builder.AddProject<Projects.KamalSample_Orders>("orders")
    .WithReference(db);

builder.AddProject<Projects.KamalSample_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithReference(orders)
    .PublishAsKamalService((_, config) =>
    {
        config.Proxy!.Host = "www.kamalsample.ext";
        config.Proxy.Healthcheck = new() { Path = "/health" };
    });

await builder.Build().RunAsync();
