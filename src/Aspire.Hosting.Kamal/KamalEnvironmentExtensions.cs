using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Kamal;
using Aspire.Hosting.Kamal.Model;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for adding a Kamal (https://kamal-deploy.org) deployment
/// environment to the application model.
/// </summary>
public static class KamalEnvironmentExtensions
{
    /// <summary>
    /// Adds a Kamal deployment environment to the application model. During
    /// <c>aspire publish</c> the environment emits Kamal-ready artifacts
    /// (<c>config/deploy.yml</c>, <c>.kamal/secrets</c>, Dockerfiles) to the output path.
    /// Project resources become Kamal apps; containers become Kamal accessories.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the Kamal environment resource.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{KamalEnvironmentResource}"/>.</returns>
    public static IResourceBuilder<KamalEnvironmentResource> AddKamalEnvironment(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var resource = new KamalEnvironmentResource(name);

        if (builder.ExecutionContext.IsRunMode)
        {
            // Not part of the running application model; only relevant for publish/deploy.
            return builder.CreateResourceBuilder(resource);
        }

        return builder.AddResource(resource);
    }

    /// <summary>
    /// Sets the default hosts that Kamal apps (role <c>web</c>) and accessories are deployed to.
    /// </summary>
    public static IResourceBuilder<KamalEnvironmentResource> WithServers(
        this IResourceBuilder<KamalEnvironmentResource> builder,
        params string[] hosts)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(hosts);

        builder.Resource.DefaultServers.Clear();
        foreach (var host in hosts)
        {
            builder.Resource.DefaultServers.Add(host);
        }

        return builder;
    }

    /// <summary>
    /// Configures the container registry the app images are pushed to.
    /// The registry password is always referenced as the <c>KAMAL_REGISTRY_PASSWORD</c> secret.
    /// </summary>
    /// <param name="builder">The Kamal environment resource builder.</param>
    /// <param name="server">Registry server, e.g. <c>ghcr.io</c>. Null means Docker Hub.</param>
    /// <param name="username">
    /// Literal registry username. When null, the config references the
    /// <c>KAMAL_REGISTRY_USERNAME</c> secret instead.
    /// </param>
    public static IResourceBuilder<KamalEnvironmentResource> WithRegistry(
        this IResourceBuilder<KamalEnvironmentResource> builder,
        string? server,
        string? username = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Resource.RegistryServer = server;
        builder.Resource.RegistryUsername = username;
        return builder;
    }

    /// <summary>Sets the domain suffix used for default proxy hosts (<c>{service}.{suffix}</c>).</summary>
    public static IResourceBuilder<KamalEnvironmentResource> WithProxyHostSuffix(
        this IResourceBuilder<KamalEnvironmentResource> builder,
        string suffix)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(suffix);

        builder.Resource.DefaultProxyHostSuffix = suffix;
        return builder;
    }

    /// <summary>Allows setting the properties of the Kamal environment resource.</summary>
    public static IResourceBuilder<KamalEnvironmentResource> WithProperties(
        this IResourceBuilder<KamalEnvironmentResource> builder,
        Action<KamalEnvironmentResource> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        configure(builder.Resource);
        return builder;
    }

    /// <summary>
    /// Registers a callback that can modify every generated <see cref="KamalDeployConfig"/>
    /// before it is written to disk.
    /// </summary>
    public static IResourceBuilder<KamalEnvironmentResource> ConfigureDeployConfig(
        this IResourceBuilder<KamalEnvironmentResource> builder,
        Action<KamalDeployConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Resource.ConfigureDeployConfig += configure;
        return builder;
    }
}

/// <summary>
/// Provides extension methods for customizing how individual resources are published to Kamal.
/// </summary>
public static class KamalServiceExtensions
{
    /// <summary>
    /// Customizes the Kamal <c>deploy.yml</c> generated for this resource, e.g. to set the
    /// proxy host, add server roles, or tune the health check.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.AddProject&lt;Projects.Web&gt;("web")
    ///     .PublishAsKamalService((_, config) =&gt;
    ///     {
    ///         config.Proxy!.Host = "app.contoso.com";
    ///         config.Proxy.Healthcheck = new() { Path = "/health" };
    ///     });
    /// </code>
    /// </example>
    public static IResourceBuilder<T> PublishAsKamalService<T>(
        this IResourceBuilder<T> builder,
        Action<KamalServiceResource, KamalDeployConfig> configure)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        if (!builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return builder;
        }

        return builder.WithAnnotation(new KamalServiceCustomizationAnnotation(configure));
    }

    /// <summary>
    /// Customizes the Kamal accessory definition generated for this container resource,
    /// e.g. to publish a port, pin the host, or add extra volumes.
    /// </summary>
    public static IResourceBuilder<T> PublishAsKamalAccessory<T>(
        this IResourceBuilder<T> builder,
        Action<KamalServiceResource, KamalAccessory> configure)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        if (!builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return builder;
        }

        return builder.WithAnnotation(new KamalAccessoryCustomizationAnnotation(configure));
    }
}
