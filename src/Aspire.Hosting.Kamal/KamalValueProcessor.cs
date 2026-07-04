using System.Globalization;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Kamal;

/// <summary>
/// The result of resolving an Aspire value expression for Kamal:
/// the resolved text (possibly containing dotenv-style <c>$VAR</c> references),
/// whether it must be treated as a secret, and which secret variables it references.
/// </summary>
internal readonly record struct KamalValue(string Text, bool IsSecret, IReadOnlySet<string> SecretRefs)
{
    public static KamalValue Clear(string text) => new(text, false, EmptySet);

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();
}

internal static class KamalValueProcessor
{
    public static async Task<KamalValue> ProcessValueAsync(
        this KamalServiceResource resource,
        object? value,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            switch (value)
            {
                case null:
                    return KamalValue.Clear(string.Empty);

                case string s:
                    return KamalValue.Clear(s);

                case EndpointReference ep:
                    return KamalValue.Clear(GetEndpointValue(resource, ep.Resource, ep.EndpointName, EndpointProperty.Url));

                case EndpointReferenceExpression epExpr:
                    return KamalValue.Clear(GetEndpointValue(resource, epExpr.Endpoint.Resource, epExpr.Endpoint.EndpointName, epExpr.Property));

                case ParameterResource param:
                    return await ProcessParameterAsync(resource, param, cancellationToken).ConfigureAwait(false);

                case ConnectionStringReference cs:
                    value = cs.Resource.ConnectionStringExpression;
                    continue;

                case IResourceWithConnectionString csrs:
                    value = csrs.ConnectionStringExpression;
                    continue;

                case ReferenceExpression expr:
                    return await ProcessExpressionAsync(resource, expr, cancellationToken).ConfigureAwait(false);

                case IManifestExpressionProvider provider:
                    return ProcessExternalReference(resource, provider);

                case IValueProvider valueProvider:
                    var resolved = await valueProvider.GetValueAsync(cancellationToken).ConfigureAwait(false);
                    return KamalValue.Clear(resolved ?? string.Empty);

                default:
                    return KamalValue.Clear(value.ToString() ?? string.Empty);
            }
        }
    }

    private static async Task<KamalValue> ProcessExpressionAsync(
        KamalServiceResource resource,
        ReferenceExpression expr,
        CancellationToken cancellationToken)
    {
        // Kamal YAML cannot represent conditionals, so the branch is selected at publish time.
        if (expr.IsConditional)
        {
            var conditionValue = await expr.Condition!.GetValueAsync(cancellationToken).ConfigureAwait(false);
            var branch = string.Equals(conditionValue, expr.MatchValue, StringComparison.OrdinalIgnoreCase)
                ? expr.WhenTrue!
                : expr.WhenFalse!;
            return await resource.ProcessValueAsync(branch, cancellationToken).ConfigureAwait(false);
        }

        var args = new object[expr.ValueProviders.Count];
        var isSecret = false;
        var secretRefs = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < expr.ValueProviders.Count; i++)
        {
            var part = await resource.ProcessValueAsync(expr.ValueProviders[i], cancellationToken).ConfigureAwait(false);
            args[i] = part.Text;
            isSecret |= part.IsSecret;
            secretRefs.UnionWith(part.SecretRefs);
        }

        var text = string.Format(CultureInfo.InvariantCulture, expr.Format, args);
        return new KamalValue(text, isSecret, secretRefs);
    }

    private static async Task<KamalValue> ProcessParameterAsync(
        KamalServiceResource resource,
        ParameterResource param,
        CancellationToken cancellationToken)
    {
        if (param.Secret)
        {
            // Secrets never end up in deploy.yml. The generated .kamal/secrets pulls the
            // value from the deployer's environment (or a kamal secrets adapter).
            var varName = ToEnvVarName(param.Name);
            resource.Parent.SecretsFileEntries.TryAdd(varName, $"${varName}");
            return new KamalValue($"${varName}", true, new HashSet<string> { varName });
        }

        var value = await param.GetValueAsync(cancellationToken).ConfigureAwait(false);
        return KamalValue.Clear(value ?? string.Empty);
    }

    private static KamalValue ProcessExternalReference(KamalServiceResource resource, IManifestExpressionProvider provider)
    {
        // Unknown external references (e.g. container image placeholders) are surfaced as
        // secrets-file entries so the deployer supplies them via the environment.
        var varName = ToEnvVarName(provider.ValueExpression.Replace("{", "").Replace("}", "").Replace(".", "_"));
        resource.Parent.SecretsFileEntries.TryAdd(varName, $"${varName}");
        return new KamalValue($"${varName}", true, new HashSet<string> { varName });
    }

    private static string GetEndpointValue(
        KamalServiceResource resource,
        IResource targetResource,
        string endpointName,
        EndpointProperty property)
    {
        if (!resource.Parent.ResourceMapping.TryGetValue(targetResource, out var target))
        {
            throw new InvalidOperationException(
                $"Resource '{targetResource.Name}' is referenced by '{resource.TargetResource.Name}' but is not part of the Kamal environment '{resource.Parent.Name}'.");
        }

        if (!target.EndpointMappings.TryGetValue(endpointName, out var mapping))
        {
            throw new InvalidOperationException(
                $"Endpoint '{endpointName}' on resource '{targetResource.Name}' could not be resolved for the Kamal environment.");
        }

        // TLS terminates at kamal-proxy; traffic on the kamal docker network is plain http.
        var scheme = mapping.Scheme == "https" ? "http" : mapping.Scheme;
        var host = target.InternalHostName;
        var port = mapping.TargetPort;

        return property switch
        {
            EndpointProperty.Url => $"{scheme}://{host}:{port}",
            EndpointProperty.Host or EndpointProperty.IPV4Host => host,
            EndpointProperty.Port => port.ToString(CultureInfo.InvariantCulture),
            EndpointProperty.HostAndPort => $"{host}:{port}",
            EndpointProperty.TargetPort => port.ToString(CultureInfo.InvariantCulture),
            EndpointProperty.Scheme => scheme,
            _ => throw new NotSupportedException($"Endpoint property '{property}' is not supported by the Kamal environment.")
        };
    }

    internal static string ToEnvVarName(string name) =>
        name.ToUpperInvariant().Replace("-", "_").Replace(".", "_");
}
