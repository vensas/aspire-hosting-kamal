# Changelog

## 0.1.0 – 2026-07-05

Initial release, published as `Vensas.Aspire.Hosting.Kamal` (the `Aspire.` package
ID prefix is reserved by Microsoft on NuGet.org).

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
- CI pipeline: PRs and pushes to `main` are built and tested; merges to `main`
  publish the package to NuGet.org via Trusted Publishing (OIDC, no stored API
  key). Duplicate versions are skipped, so bumping `<Version>` in the csproj
  triggers a release.
- NuGet package metadata: README, project/repository URL.
- Dependabot monitors NuGet packages and GitHub Actions weekly.
- NuGet lock files (`packages.lock.json`) for all projects; CI restores in
  locked mode so builds fail if the dependency graph drifts from the lock files.
