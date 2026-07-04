using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.Kamal;

internal sealed class KamalEnvironmentContext(KamalEnvironmentResource environment, ILogger logger)
{
    public async Task<KamalServiceResource> CreateKamalServiceResourceAsync(
        IResource resource,
        DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (environment.ResourceMapping.TryGetValue(resource, out var existing))
        {
            return existing;
        }

        logger.LogInformation("Creating Kamal deployment target for {ResourceName}", resource.Name);

        var serviceResource = new KamalServiceResource(resource.Name, resource, environment);
        environment.ResourceMapping[resource] = serviceResource;

        ProcessEndpoints(serviceResource);
        await ProcessEnvironmentVariablesAsync(serviceResource, executionContext, cancellationToken).ConfigureAwait(false);
        await ProcessArgumentsAsync(serviceResource, executionContext, cancellationToken).ConfigureAwait(false);

        return serviceResource;
    }

    private void ProcessEndpoints(KamalServiceResource serviceResource)
    {
        var resolvedEndpoints = serviceResource.TargetResource.ResolveEndpoints(environment.PortAllocator);

        foreach (var resolved in resolvedEndpoints)
        {
            var endpoint = resolved.Endpoint;

            // Projects have no fixed target port by default; the generated Dockerfile
            // makes the app listen on the default app port.
            var targetPort = resolved.TargetPort.Value ?? KamalServiceResource.DefaultAppPort;

            var exposedPort = (resolved.ExposedPort.IsAllocated || resolved.ExposedPort.IsImplicit)
                ? null
                : resolved.ExposedPort.Value;

            serviceResource.EndpointMappings[endpoint.Name] = new(
                serviceResource.TargetResource,
                endpoint.UriScheme,
                targetPort,
                exposedPort,
                endpoint.IsExternal,
                endpoint.Name);
        }
    }

    private static async Task ProcessEnvironmentVariablesAsync(
        KamalServiceResource serviceResource,
        DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (serviceResource.TargetResource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var callbacks))
        {
            var context = new EnvironmentCallbackContext(
                executionContext,
                serviceResource.TargetResource,
                serviceResource.EnvironmentVariables,
                cancellationToken: cancellationToken);

            foreach (var callback in callbacks)
            {
                await callback.Callback(context).ConfigureAwait(false);
            }

            // Drop https service-discovery variables: TLS termination happens at kamal-proxy,
            // container-to-container traffic on the kamal network is plain http.
            var httpsDiscoveryKeys = context.EnvironmentVariables
                .Where(kvp => kvp.Value is EndpointReference { Scheme: "https" } &&
                              kvp.Key.StartsWith("services__", StringComparison.Ordinal))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in httpsDiscoveryKeys)
            {
                context.EnvironmentVariables.Remove(key);
            }
        }
    }

    private static async Task ProcessArgumentsAsync(
        KamalServiceResource serviceResource,
        DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (serviceResource.TargetResource.TryGetAnnotationsOfType<CommandLineArgsCallbackAnnotation>(out var callbacks))
        {
            var context = new CommandLineArgsCallbackContext(serviceResource.Args, serviceResource.TargetResource, cancellationToken)
            {
                ExecutionContext = executionContext
            };

            foreach (var callback in callbacks)
            {
                await callback.Callback(context).ConfigureAwait(false);
            }
        }
    }
}
