#pragma warning disable ASPIREPIPELINES001

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Kamal.Model;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.Kamal;

/// <summary>
/// Generates the Kamal artifacts (<c>config/deploy*.yml</c>, <c>.kamal/secrets</c>,
/// Dockerfiles and a README) for a <see cref="KamalEnvironmentResource"/>.
/// </summary>
internal sealed class KamalPublishingContext(
    DistributedApplicationExecutionContext executionContext,
    string outputPath,
    ILogger logger,
    IReportingStep reportingStep,
    CancellationToken cancellationToken = default)
{
    public string OutputPath { get; } = outputPath;

    internal async Task WriteModelAsync(DistributedApplicationModel model, KamalEnvironmentResource environment)
    {
        if (!executionContext.IsPublishMode)
        {
            return;
        }

        var services = model.Resources
            .Select(r => r.GetDeploymentTargetAnnotation(environment)?.DeploymentTarget)
            .OfType<KamalServiceResource>()
            .ToList();

        var apps = services.Where(s => s.IsApp).ToList();
        var accessories = services.Where(s => !s.IsApp).ToList();

        if (apps.Count == 0)
        {
            throw new InvalidOperationException(
                $"The Kamal environment '{environment.Name}' has no deployable app. " +
                "Kamal requires at least one project resource (or a container built from a Dockerfile). " +
                "Containers with prebuilt images are published as Kamal accessories and cannot be deployed on their own.");
        }

        var writeTask = await reportingStep.CreateTaskAsync(
            "Writing Kamal deployment artifacts.", cancellationToken).ConfigureAwait(false);

        await using (writeTask.ConfigureAwait(false))
        {
            Directory.CreateDirectory(Path.Combine(OutputPath, "config"));
            Directory.CreateDirectory(Path.Combine(OutputPath, ".kamal"));

            RegisterRegistrySecrets(environment);

            var accessoryModels = new Dictionary<string, KamalAccessory>();
            foreach (var accessory in accessories)
            {
                accessoryModels[accessory.ServiceName] = await BuildAccessoryAsync(environment, accessory).ConfigureAwait(false);
            }

            var configFiles = new List<string>();
            for (var i = 0; i < apps.Count; i++)
            {
                var app = apps[i];
                var isPrimary = i == 0;

                var config = await BuildAppConfigAsync(environment, app).ConfigureAwait(false);

                if (isPrimary && accessoryModels.Count > 0)
                {
                    config.Accessories = accessoryModels;
                }

                ApplyCustomizations(environment, app, config);

                var fileName = isPrimary ? "deploy.yml" : $"deploy.{app.ServiceName}.yml";
                var filePath = Path.Combine(OutputPath, "config", fileName);
                await File.WriteAllTextAsync(filePath, KamalYamlWriter.Serialize(config), cancellationToken).ConfigureAwait(false);
                configFiles.Add($"config/{fileName}");

                logger.LogInformation("Wrote Kamal config for {Service} to {Path}", app.ServiceName, filePath);
            }

            await WriteSecretsFileAsync(environment).ConfigureAwait(false);
            await WriteReadmeAsync(environment, apps, accessories, configFiles).ConfigureAwait(false);

            await writeTask.CompleteAsync(
                $"Kamal artifacts written to {OutputPath} ({string.Join(", ", configFiles)}).",
                CompletionState.Completed,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<KamalDeployConfig> BuildAppConfigAsync(KamalEnvironmentResource environment, KamalServiceResource app)
    {
        var serviceName = app.ServiceName;
        var imageNamespace = environment.ImageNamespace ?? environment.RegistryUsername ?? "apps";
        var hosts = GetDefaultHosts(environment);

        var externalEndpoint = app.EndpointMappings.Values
            .Where(m => m.IsExternal && m.Scheme is "http" or "https")
            .OrderBy(m => m.EndpointName)
            .Cast<KamalServiceResource.EndpointMapping?>()
            .FirstOrDefault();

        var config = new KamalDeployConfig
        {
            Service = serviceName,
            Image = $"{imageNamespace}/{serviceName}",
            Servers = new Dictionary<string, KamalServerRole>
            {
                ["web"] = new()
                {
                    Hosts = [.. hosts],
                    Proxy = externalEndpoint is null ? false : null
                }
            },
            Registry = BuildRegistry(environment),
            Builder = await BuildBuilderAsync(environment, app).ConfigureAwait(false)
        };

        if (externalEndpoint is { } endpoint)
        {
            config.Proxy = new KamalProxy
            {
                Ssl = true,
                Host = $"{serviceName}.{environment.DefaultProxyHostSuffix}",
                AppPort = endpoint.TargetPort
            };
        }

        if (environment.SshUser is not null)
        {
            config.Ssh = new KamalSsh { User = environment.SshUser };
        }

        var (clear, secret) = await ResolveEnvironmentAsync(environment, app).ConfigureAwait(false);
        if (clear.Count > 0 || secret.Count > 0)
        {
            config.Env = new KamalEnv
            {
                Clear = clear.Count > 0 ? clear : null,
                Secret = secret.Count > 0 ? secret : null
            };
        }

        var cmd = await ResolveCommandAsync(app).ConfigureAwait(false);
        if (cmd is not null)
        {
            config.Servers["web"].Cmd = cmd;
        }

        AddAppVolumes(app, config);

        environment.ConfigureDeployConfig?.Invoke(config);

        return config;
    }

    private async Task<KamalAccessory> BuildAccessoryAsync(KamalEnvironmentResource environment, KamalServiceResource accessory)
    {
        if (!accessory.TargetResource.TryGetContainerImageName(out var imageName) || imageName is null)
        {
            throw new InvalidOperationException(
                $"Container resource '{accessory.TargetResource.Name}' has no image name and cannot be published as a Kamal accessory.");
        }

        var model = new KamalAccessory
        {
            Image = imageName,
            Host = GetDefaultHosts(environment)[0]
        };

        var (clear, secret) = await ResolveEnvironmentAsync(environment, accessory).ConfigureAwait(false);
        if (clear.Count > 0 || secret.Count > 0)
        {
            model.Env = new KamalEnv
            {
                Clear = clear.Count > 0 ? clear : null,
                Secret = secret.Count > 0 ? secret : null
            };
        }

        var cmd = await ResolveCommandAsync(accessory).ConfigureAwait(false);
        if (cmd is not null)
        {
            model.Cmd = cmd;
        }

        if (accessory.TargetResource is ContainerResource { Entrypoint: { } entrypoint })
        {
            (model.Options ??= [])["entrypoint"] = entrypoint;
        }

        // Only endpoints explicitly marked external are published on the host;
        // everything else stays reachable through the kamal docker network.
        foreach (var mapping in accessory.EndpointMappings.Values.Where(m => m.IsExternal))
        {
            var hostPort = mapping.ExposedPort ?? mapping.TargetPort;
            model.Port = $"{hostPort}:{mapping.TargetPort}";
            break;
        }

        if (accessory.TargetResource.TryGetContainerMounts(out var mounts))
        {
            foreach (var mount in mounts)
            {
                if (mount.Source is null || mount.Target is null)
                {
                    continue;
                }

                if (mount.Type == ContainerMountType.Volume)
                {
                    (model.Volumes ??= []).Add($"{mount.Source}:{mount.Target}");
                }
                else
                {
                    // Bind mounts become Kamal-managed directories relative to the
                    // accessory's data directory on the host.
                    (model.Directories ??= []).Add($"{Path.GetFileName(mount.Source.TrimEnd('/', '\\'))}:{mount.Target}");
                }
            }
        }

        if (accessory.TargetResource.TryGetAnnotationsOfType<KamalAccessoryCustomizationAnnotation>(out var customizations))
        {
            foreach (var customization in customizations)
            {
                customization.Configure(accessory, model);
            }
        }

        return model;
    }

    private static void ApplyCustomizations(KamalEnvironmentResource environment, KamalServiceResource app, KamalDeployConfig config)
    {
        if (app.TargetResource.TryGetAnnotationsOfType<KamalServiceCustomizationAnnotation>(out var customizations))
        {
            foreach (var customization in customizations)
            {
                customization.Configure(app, config);
            }
        }
    }

    private async Task<(Dictionary<string, string> Clear, List<string> Secret)> ResolveEnvironmentAsync(
        KamalEnvironmentResource environment,
        KamalServiceResource service)
    {
        var clear = new Dictionary<string, string>();
        var secret = new List<string>();

        foreach (var (key, rawValue) in service.EnvironmentVariables)
        {
            var value = await service.ProcessValueAsync(rawValue, cancellationToken).ConfigureAwait(false);

            if (value.IsSecret)
            {
                secret.Add(key);

                // A secret env var resolves inside .kamal/secrets, referencing the
                // underlying secret variables via dotenv interpolation.
                if (!(value.SecretRefs.Count == 1 && value.Text == $"${key}"))
                {
                    environment.SecretsFileEntries[key] = QuoteDotenvValue(value.Text);
                }
            }
            else
            {
                clear[key] = value.Text;
            }
        }

        return (clear, secret);
    }

    private async Task<string?> ResolveCommandAsync(KamalServiceResource service)
    {
        if (service.Args.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var arg in service.Args)
        {
            var value = await service.ProcessValueAsync(arg, cancellationToken).ConfigureAwait(false);
            if (value.IsSecret)
            {
                throw new InvalidOperationException(
                    $"Resource '{service.TargetResource.Name}' uses a secret value as a command line argument. " +
                    "Kamal cannot pass secrets on the command line; move it to an environment variable instead.");
            }

            parts.Add(value.Text.Contains(' ') ? $"\"{value.Text}\"" : value.Text);
        }

        return string.Join(' ', parts);
    }

    private static void AddAppVolumes(KamalServiceResource app, KamalDeployConfig config)
    {
        if (app.TargetResource.TryGetContainerMounts(out var mounts))
        {
            foreach (var mount in mounts)
            {
                if (mount.Source is not null && mount.Target is not null)
                {
                    (config.Volumes ??= []).Add($"{mount.Source}:{mount.Target}");
                }
            }
        }
    }

    private async Task<KamalBuilder> BuildBuilderAsync(KamalEnvironmentResource environment, KamalServiceResource app)
    {
        var builder = new KamalBuilder { Arch = environment.BuilderArch };

        if (app.TargetResource is ProjectResource project)
        {
            var projectPath = project.GetProjectMetadata().ProjectPath;
            var contextPath = DotnetDockerfileGenerator.FindRepositoryRoot(projectPath);
            var dockerfileName = $"Dockerfile.{app.ServiceName}";

            var dockerfile = DotnetDockerfileGenerator.Generate(projectPath, contextPath, app.InternalPort);
            await File.WriteAllTextAsync(Path.Combine(OutputPath, dockerfileName), dockerfile, cancellationToken).ConfigureAwait(false);

            // Paths are resolved from the publish output directory, where kamal is expected to run.
            builder.Dockerfile = dockerfileName;
            builder.Context = Path.GetRelativePath(OutputPath, contextPath).Replace('\\', '/');
        }
        else if (app.TargetResource.TryGetLastAnnotation<DockerfileBuildAnnotation>(out var dockerfileBuild))
        {
            builder.Dockerfile = Path.GetRelativePath(OutputPath, dockerfileBuild.DockerfilePath).Replace('\\', '/');
            builder.Context = Path.GetRelativePath(OutputPath, dockerfileBuild.ContextPath).Replace('\\', '/');
        }

        return builder;
    }

    private static KamalRegistry BuildRegistry(KamalEnvironmentResource environment)
    {
        return new KamalRegistry
        {
            Server = environment.RegistryServer,
            Username = environment.RegistryUsername is not null
                ? environment.RegistryUsername
                : new List<string> { "KAMAL_REGISTRY_USERNAME" },
            Password = new List<string> { "KAMAL_REGISTRY_PASSWORD" }
        };
    }

    private static void RegisterRegistrySecrets(KamalEnvironmentResource environment)
    {
        environment.SecretsFileEntries.TryAdd("KAMAL_REGISTRY_PASSWORD", "$KAMAL_REGISTRY_PASSWORD");
        if (environment.RegistryUsername is null)
        {
            environment.SecretsFileEntries.TryAdd("KAMAL_REGISTRY_USERNAME", "$KAMAL_REGISTRY_USERNAME");
        }
    }

    private static IReadOnlyList<string> GetDefaultHosts(KamalEnvironmentResource environment) =>
        environment.DefaultServers.Count > 0 ? [.. environment.DefaultServers] : ["YOUR-SERVER-IP"];

    private static string QuoteDotenvValue(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private async Task WriteSecretsFileAsync(KamalEnvironmentResource environment)
    {
        var lines = new List<string>
        {
            "# Generated by Aspire.Hosting.Kamal — DO NOT commit real secret values to source control.",
            "# Values are read from the deployer's environment via dotenv interpolation.",
            "# See https://kamal-deploy.org/docs/commands/secrets/ for secret manager adapters.",
            ""
        };

        // Simple $VAR pass-throughs first so later composite entries can interpolate them.
        foreach (var (key, value) in environment.SecretsFileEntries.OrderBy(e => e.Value.Contains(' ') ? 1 : 0).ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            lines.Add($"{key}={value}");
        }

        var path = Path.Combine(OutputPath, ".kamal", "secrets");
        await File.WriteAllLinesAsync(path, lines, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteReadmeAsync(
        KamalEnvironmentResource environment,
        List<KamalServiceResource> apps,
        List<KamalServiceResource> accessories,
        List<string> configFiles)
    {
        var secretVars = environment.SecretsFileEntries
            .Where(e => e.Value == $"${e.Key}")
            .Select(e => e.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var deployCommands = configFiles
            .Select(f => f == "config/deploy.yml" ? "kamal deploy" : $"kamal deploy -c {f}");

        var accessoryLines = accessories.Count > 0
            ? $"""

              ## Accessories

              {string.Join("\n", accessories.Select(a => $"- `{a.ServiceName}` → container `{apps[0].ServiceName}-{a.ServiceName}` on the kamal docker network"))}

              Accessories are defined in `config/deploy.yml` and are booted with:

              ```sh
              kamal accessory boot all
              ```
              """
            : "";

        var readme = $"""
            # Kamal deployment artifacts

            Generated by `aspire publish` from the Aspire environment **{environment.Name}**.

            ## Layout

            {string.Join("\n", configFiles.Select(f => $"- `{f}`"))}
            - `.kamal/secrets` — secret lookups (no literal values)
            {string.Join("\n", apps.Where(a => a.TargetResource is ProjectResource).Select(a => $"- `Dockerfile.{a.ServiceName}` — generated multi-stage .NET build"))}

            ## Before the first deploy

            1. Install Kamal: `gem install kamal` (or use the docker alias from kamal-deploy.org).
            2. Edit the config files: replace placeholder server IPs{(environment.DefaultServers.Count == 0 ? " (`YOUR-SERVER-IP`)" : "")} and proxy hosts (`*.{environment.DefaultProxyHostSuffix}`) with real values.
            3. Export the secret values referenced by `.kamal/secrets`:

            ```sh
            {string.Join("\n", secretVars.Select(v => $"export {v}=..."))}
            ```

            4. First-time server setup: `kamal setup` (per config file).

            ## Deploy

            ```sh
            {string.Join("\n", deployCommands)}
            ```

            Run all commands from this directory{(apps.Count > 1 ? "; each app has its own config file" : "")}.{accessoryLines}

            ## Notes

            - kamal-proxy only routes traffic once the app answers 200 on the health check path
              (`/up` unless a `proxy.healthcheck.path` is set in the config). Make sure the app
              serves that endpoint in production.
            - TLS terminates at kamal-proxy (Let's Encrypt); containers talk plain http on the `kamal` docker network.
            - Cross-service references resolve to container DNS names (`<service>-web`, `<service>-<accessory>`); all services must share a host (or a docker network spanning hosts) for that to work.
            - `aspire deploy` runs these `kamal deploy` commands for you when the Kamal CLI is installed.
            """;

        await File.WriteAllTextAsync(Path.Combine(OutputPath, "README.md"), readme, cancellationToken).ConfigureAwait(false);
    }
}
