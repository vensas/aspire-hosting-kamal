# Aspire.Hosting.Kamal

A [Kamal](https://kamal-deploy.org) deployment target for [.NET Aspire](https://aspire.dev).

Add one line to your AppHost and `aspire publish -o ./out` emits Kamal-ready artifacts:
`config/deploy.yml`, `.kamal/secrets`, and generated Dockerfiles. `aspire deploy` can then
run `kamal deploy` for you.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddKamalEnvironment("kamal")
    .WithServers("203.0.113.10")
    .WithRegistry("ghcr.io", "my-org")
    .WithProxyHostSuffix("example.com");

var postgres = builder.AddPostgres("postgres").WithDataVolume();
var db = postgres.AddDatabase("appdb");
var cache = builder.AddRedis("cache");

builder.AddProject<Projects.Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(db)
    .WithReference(cache)
    .PublishAsKamalService((_, config) =>
    {
        config.Proxy!.Host = "app.example.com";
        config.Proxy.Healthcheck = new() { Path = "/health" };
    });

builder.Build().Run();
```

```sh
aspire publish -o ./out
cd out
export KAMAL_REGISTRY_PASSWORD=... POSTGRES_PASSWORD=... CACHE_PASSWORD=...
kamal setup   # first time; afterwards: kamal deploy
```

## How the Aspire model maps to Kamal

| Aspire | Kamal |
|---|---|
| Project resource (or container built from a Dockerfile) | App with its own `deploy.yml` (`config/deploy.<name>.yml` for secondary apps) |
| Container resource (postgres, redis, ...) | `accessories:` entry in the primary app's config |
| External http endpoint | `proxy:` (SSL via Let's Encrypt, `app_port` from the endpoint) |
| Secret parameters, connection strings containing secrets | `env.secret` + `.kamal/secrets` entries resolved from the deployer's environment — no literal secret ever lands in a generated file |
| Non-secret parameters and env values | `env.clear` |
| Endpoint references between resources | Container DNS names on the `kamal` docker network (`<service>-web`, `<service>-<accessory>`) |

For project resources a multi-stage `Dockerfile.<name>` is generated (TFM auto-detected)
with `builder.context` pointing at your repository root, so `kamal deploy` builds the image
without any Aspire tooling.

## API

- `AddKamalEnvironment(name)` — adds the environment (publish/deploy mode only).
- `.WithServers(params string[])` — default hosts for apps and accessories.
- `.WithRegistry(server, username = null)` — registry config; the password is always the
  `KAMAL_REGISTRY_PASSWORD` secret, the username falls back to `KAMAL_REGISTRY_USERNAME`.
- `.WithProxyHostSuffix(suffix)` — default proxy hosts are `<service>.<suffix>`.
- `.WithProperties(env => ...)` — everything else (`SshUser`, `BuilderArch`, `ImageNamespace`,
  `DeployWithKamalCli`).
- `.ConfigureDeployConfig(config => ...)` — hook applied to every generated config.
- `resource.PublishAsKamalService((service, config) => ...)` — full control over the typed
  `deploy.yml` model for one app.
- `resource.PublishAsKamalAccessory((service, accessory) => ...)` — same for accessories
  (publish a port, pin a host, extra volumes, ...).

## Notes and limitations

- Requires Aspire 13.4+ (builds on the experimental pipeline APIs, `ASPIREPIPELINES00x`).
- Kamal deploys one app per config file; accessories live in the primary app's config.
  Cross-service references assume all containers share the `kamal` docker network, i.e. a
  single-host (or bridged) setup — the classic Kamal topology.
- TLS terminates at kamal-proxy; container-to-container traffic is plain http, so https
  service-discovery variables are dropped (same behavior as the Docker Compose target).
- `aspire deploy` shells out to the `kamal` CLI when it is installed; disable with
  `.WithProperties(e => e.DeployWithKamalCli = false)`.

## Repository layout

- `src/Aspire.Hosting.Kamal` — the package.
- `samples/KamalSample.AppHost` — sample AppHost (api + postgres + redis); try
  `aspire publish -o ./kamal-out` inside it.
- `tests/Aspire.Hosting.Kamal.Tests` — unit tests.
