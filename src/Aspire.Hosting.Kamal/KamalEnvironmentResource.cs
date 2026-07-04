#pragma warning disable ASPIREPIPELINES001
#pragma warning disable ASPIREPIPELINES004

using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Kamal.Model;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.Kamal;

/// <summary>
/// Represents a Kamal (https://kamal-deploy.org) deployment environment.
/// During <c>aspire publish</c> this resource emits Kamal-ready artifacts
/// (<c>config/deploy.yml</c>, <c>.kamal/secrets</c> and Dockerfiles) for all
/// compute resources in the application model. During <c>aspire deploy</c> it
/// can optionally shell out to the <c>kamal</c> CLI.
/// </summary>
public class KamalEnvironmentResource : Resource, IComputeEnvironmentResource
{
    /// <summary>
    /// The hosts that app servers (role <c>web</c>) and accessories are deployed to
    /// unless overridden per resource. Defaults to a placeholder that must be replaced.
    /// </summary>
    public IList<string> DefaultServers { get; } = [];

    /// <summary>Container registry server, e.g. <c>ghcr.io</c>. Null means Docker Hub.</summary>
    public string? RegistryServer { get; set; }

    /// <summary>
    /// Literal registry username. When null, the generated config references the
    /// <c>KAMAL_REGISTRY_USERNAME</c> secret instead.
    /// </summary>
    public string? RegistryUsername { get; set; }

    /// <summary>
    /// Namespace prefix for generated image names (<c>{namespace}/{service}</c>).
    /// Defaults to <see cref="RegistryUsername"/> when set, otherwise <c>apps</c>.
    /// </summary>
    public string? ImageNamespace { get; set; }

    /// <summary>SSH user Kamal connects with. Null keeps Kamal's default (root).</summary>
    public string? SshUser { get; set; }

    /// <summary>Target build architecture for <c>builder.arch</c>. Defaults to <c>amd64</c>.</summary>
    public string BuilderArch { get; set; } = "amd64";

    /// <summary>
    /// When true (default), <c>aspire deploy</c> runs <c>kamal deploy</c> for each generated
    /// config using the locally installed Kamal CLI.
    /// </summary>
    public bool DeployWithKamalCli { get; set; } = true;

    /// <summary>Domain suffix used for default proxy hosts (<c>{service}.{suffix}</c>). Defaults to <c>example.com</c>.</summary>
    public string DefaultProxyHostSuffix { get; set; } = "example.com";

    internal Dictionary<IResource, KamalServiceResource> ResourceMapping { get; } = [];

    internal IPortAllocator PortAllocator { get; } = new PortAllocator();

    /// <summary>
    /// Entries for the generated <c>.kamal/secrets</c> file, keyed by variable name.
    /// Values are dotenv-style expressions (e.g. <c>$POSTGRES_PASSWORD</c>) — never literal secrets.
    /// </summary>
    internal Dictionary<string, string> SecretsFileEntries { get; } = [];

    internal Action<KamalDeployConfig>? ConfigureDeployConfig { get; set; }

    internal string? PrimaryAppServiceName { get; set; }

    /// <param name="name">The name of the Kamal environment resource.</param>
    public KamalEnvironmentResource(string name) : base(name)
    {
        Annotations.Add(new PipelineStepAnnotation(factoryContext =>
        {
            var steps = new List<PipelineStep>
            {
                new()
                {
                    Name = $"prepare-deployment-targets-{Name}",
                    Description = $"Prepares Kamal deployment targets for {Name}.",
                    Action = PrepareDeploymentTargetsAsync,
                    DependsOnSteps = [WellKnownPipelineSteps.ValidateComputeEnvironments],
                    RequiredBySteps = [WellKnownPipelineSteps.BeforeStart]
                }
            };

            var publishStep = new PipelineStep
            {
                Name = $"publish-{Name}",
                Description = $"Generates Kamal deployment artifacts for {Name}.",
                Action = PublishAsync
            };
            publishStep.RequiredBy(WellKnownPipelineSteps.Publish);
            steps.Add(publishStep);

            var kamalDeployStep = new PipelineStep
            {
                Name = $"kamal-deploy-{Name}",
                Description = $"Runs 'kamal deploy' for the generated configs of {Name}.",
                Action = KamalDeployAsync,
                DependsOnSteps = [$"publish-{Name}", WellKnownPipelineSteps.DeployPrereq]
            };
            kamalDeployStep.RequiredBy(WellKnownPipelineSteps.Deploy);
            steps.Add(kamalDeployStep);

            return steps;
        }));
    }

    /// <inheritdoc/>
    [Experimental("ASPIRECOMPUTE002", UrlFormat = "https://aka.ms/aspire/diagnostics/{0}")]
    public ReferenceExpression GetHostAddressExpression(EndpointReference endpointReference)
    {
        // Containers on the "kamal" docker network resolve each other via container name.
        // Accessories are named "{service}-{accessory}"; app containers get a
        // "{service}-{role}" alias.
        var resource = endpointReference.Resource;

        if (ResourceMapping.TryGetValue(resource, out var serviceResource))
        {
            return ReferenceExpression.Create($"{serviceResource.InternalHostName}");
        }

        return ReferenceExpression.Create($"{resource.Name.ToLowerInvariant()}");
    }

    private async Task PrepareDeploymentTargetsAsync(PipelineStepContext context)
    {
        if (context.ExecutionContext.IsRunMode)
        {
            return;
        }

        var logger = context.Services.GetRequiredService<ILogger<KamalEnvironmentResource>>();
        var environmentContext = new KamalEnvironmentContext(this, logger);

        foreach (var resource in context.Model.GetComputeResources())
        {
            var resourceComputeEnvironment = resource.GetComputeEnvironment();
            if (resourceComputeEnvironment is not null && resourceComputeEnvironment != this)
            {
                continue;
            }

            var serviceResource = await environmentContext.CreateKamalServiceResourceAsync(
                resource, context.ExecutionContext, context.CancellationToken).ConfigureAwait(false);

            resource.Annotations.Add(new DeploymentTargetAnnotation(serviceResource)
            {
                ComputeEnvironment = this
            });
        }

        // The first app becomes the "primary" Kamal service: it owns config/deploy.yml
        // and hosts all accessories.
        PrimaryAppServiceName = ResourceMapping.Values
            .FirstOrDefault(s => s.IsApp)?.ServiceName;
    }

    private Task PublishAsync(PipelineStepContext context)
    {
        var outputPath = GetEnvironmentOutputPath(context);
        var publishingContext = new KamalPublishingContext(
            context.ExecutionContext,
            outputPath,
            context.Logger,
            context.ReportingStep,
            context.CancellationToken);

        return publishingContext.WriteModelAsync(context.Model, this);
    }

    private async Task KamalDeployAsync(PipelineStepContext context)
    {
        if (!DeployWithKamalCli)
        {
            await context.ReportingStep.CompleteAsync(
                $"Kamal CLI deployment is disabled for '{Name}'. Deploy manually with 'kamal deploy' from the publish output.",
                CompletionState.Completed,
                context.CancellationToken).ConfigureAwait(false);
            return;
        }

        var outputPath = GetEnvironmentOutputPath(context);
        await KamalCliRunner.DeployAsync(this, outputPath, context).ConfigureAwait(false);
    }

    internal string GetEnvironmentOutputPath(PipelineStepContext context)
    {
        var outputService = context.Services.GetRequiredService<IPipelineOutputService>();
        if (context.Model.Resources.OfType<IComputeEnvironmentResource>().Count() > 1)
        {
            return outputService.GetOutputDirectory(this);
        }

        return outputService.GetOutputDirectory();
    }
}
