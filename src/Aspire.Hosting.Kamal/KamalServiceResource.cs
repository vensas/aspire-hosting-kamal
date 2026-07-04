using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Kamal;

/// <summary>
/// The deployment target created for each compute resource assigned to a
/// <see cref="KamalEnvironmentResource"/>. Project resources (and containers built from a
/// Dockerfile) become Kamal <em>apps</em> with their own <c>deploy.yml</c>; other containers
/// become Kamal <em>accessories</em> attached to the primary app's config.
/// </summary>
public class KamalServiceResource : Resource, IResourceWithParent<KamalEnvironmentResource>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KamalServiceResource"/> class.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="resource">The target resource this Kamal service was created for.</param>
    /// <param name="environment">The Kamal environment resource.</param>
    public KamalServiceResource(string name, IResource resource, KamalEnvironmentResource environment) : base(name)
    {
        TargetResource = resource;
        Parent = environment;
        ServiceName = SanitizeName(resource.Name);
        IsApp = resource is ProjectResource || resource.HasAnnotationOfType<DockerfileBuildAnnotation>();
    }

    /// <summary>Gets the resource that is the target of this Kamal service.</summary>
    public IResource TargetResource { get; }

    /// <inheritdoc/>
    public KamalEnvironmentResource Parent { get; }

    /// <summary>The sanitized Kamal service/accessory name derived from the resource name.</summary>
    public string ServiceName { get; }

    /// <summary>True when the resource is published as a Kamal app; false for accessories.</summary>
    public bool IsApp { get; }

    internal Dictionary<string, object> EnvironmentVariables { get; } = [];

    internal List<object> Args { get; } = [];

    internal Dictionary<string, EndpointMapping> EndpointMappings { get; } = [];

    internal record struct EndpointMapping(
        IResource Resource,
        string Scheme,
        int TargetPort,
        int? ExposedPort,
        bool IsExternal,
        string EndpointName);

    /// <summary>
    /// The DNS name under which this workload is reachable from other containers on the
    /// <c>kamal</c> docker network.
    /// </summary>
    internal string InternalHostName => IsApp
        ? $"{ServiceName}-web"
        : $"{Parent.PrimaryAppServiceName ?? "app"}-{ServiceName}";

    /// <summary>The container port other services should use to reach this workload.</summary>
    internal int InternalPort =>
        EndpointMappings.Values.OrderByDescending(m => m.IsExternal).Select(m => m.TargetPort).FirstOrDefault(DefaultAppPort);

    internal const int DefaultAppPort = 8080;

    internal static string SanitizeName(string name)
    {
        var sanitized = new string(name.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray());
        return sanitized.Trim('-');
    }
}
