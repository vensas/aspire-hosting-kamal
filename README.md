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

## Multiple projects

Kamal deploys one app per config file, so each Aspire project gets its own:

```
config/deploy.yml          # first project ("primary") — also owns all accessories
config/deploy.orders.yml   # every further project
config/deploy.web.yml
```

- Deploy them with `kamal deploy`, then `kamal deploy -c config/deploy.<name>.yml` per app
  (or let `aspire deploy` run all of them). They share the hosts, kamal-proxy, and the
  `kamal` docker network; container/image names are namespaced per service, so nothing collides.
- Apps with `WithExternalHttpEndpoints()` each get their own proxy host
  (`api.example.com`, `www.example.com`, ...) on the shared kamal-proxy.
- Projects without an external endpoint become internal-only apps (`proxy: false`):
  reachable by the other apps but not from the internet.
- Cross-project `WithReference(...)` env vars resolve to stable DNS aliases on the `kamal`
  network — every app role is generated with `options: { network-alias: <service>-web }`
  because Kamal's own container names carry the deploy version and can't be used for
  discovery. So `web` sees `services__api__http__0: http://api-web:8080`.
- Accessories land in the primary app's config only; all apps reference them by the
  primary-scoped container name (e.g. `api-postgres`).

### Accessories-only environments

Kamal's schema requires `service:` and `image:` in every config, so a truly app-free
`deploy.yml` is not possible. If the Aspire model contains only containers (e.g. an
infra-only stack with postgres + redis), the publisher emits a stub
(`service: <environment-name>`, `image: unused/accessories-only`) that is never built or
pulled — manage the stack with `kamal accessory boot|reboot all` (which `aspire deploy`
runs automatically in this mode) instead of `kamal deploy`.

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

## Testing the generated output

Kamal has no `--dry-run` flag, but you can validate everything short of a real deploy:

```sh
aspire publish -o ./out && cd out

# 1. Schema/parse check: Kamal loads deploy.yml, resolves image, roles, accessories.
#    Note: kamal derives the image tag from git, so run this inside a git repository
#    (your app repo, or 'git init' the output directory).
kamal config -c config/deploy.yml

# 2. Secrets check: resolves .kamal/secrets with dotenv interpolation (uses your env vars).
export KAMAL_REGISTRY_PASSWORD=x POSTGRES_PASSWORD=x ...
kamal secrets print -c config/deploy.yml

# 3. Image build check (needs Docker, no server): builds the generated Dockerfile.
docker build -f Dockerfile.<app> <context-from-deploy.yml>
```

For a full end-to-end rehearsal without a cloud server, point `WithServers(...)` at a local
Linux VM that runs Docker and accepts your SSH key (e.g. OrbStack: `orb create ubuntu kamal-test`,
Multipass, or Lima), use a throwaway registry like `localhost:5000` or a free GHCR repo, then
run `kamal setup` from the publish output. Kamal treats the VM exactly like a production host.

## Notes and limitations

- **Your app must answer the proxy health check.** kamal-proxy only routes traffic after a
  200 response from `/up` (Kamal's default) or whatever you set via
  `config.Proxy.Healthcheck = new() { Path = "/health" }`. Note that Aspire ServiceDefaults
  maps `/health` only in the Development environment — expose one explicitly for production,
  e.g. `builder.Services.AddHealthChecks();` + `app.MapHealthChecks("/health");`. Also avoid
  `UseHttpsRedirection()` for the probe path: the proxy probes over plain http.
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
