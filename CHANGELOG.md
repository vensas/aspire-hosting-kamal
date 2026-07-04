# Changelog

## 0.1.0 – 2026-07-05

Initial release.

- `AddKamalEnvironment(...)`: Kamal compute environment for Aspire 13.4+ built on the
  pipeline API (`aspire publish` / `aspire deploy`).
- Emits `config/deploy.yml` (plus `deploy.<name>.yml` per additional app), `.kamal/secrets`
  with dotenv interpolation and zero literal secrets, generated multi-stage Dockerfiles for
  project resources, and a README describing the first-deploy steps.
- Container resources are published as Kamal accessories (env, volumes, entrypoint,
  external ports); secret parameters and connection strings are routed through
  `env.secret`.
- `PublishAsKamalService` / `PublishAsKamalAccessory` customization hooks over the typed
  `deploy.yml` model; environment-level fluent config (`WithServers`, `WithRegistry`,
  `WithProxyHostSuffix`, `ConfigureDeployConfig`).
- `aspire deploy` integration that runs `kamal deploy` per generated config when the CLI
  is available.
