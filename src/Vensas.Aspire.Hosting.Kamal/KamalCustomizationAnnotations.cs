using Aspire.Hosting.ApplicationModel;
using Vensas.Aspire.Hosting.Kamal.Model;

namespace Vensas.Aspire.Hosting.Kamal;

/// <summary>
/// Customizes the generated <see cref="KamalDeployConfig"/> for a resource published as a Kamal app.
/// </summary>
/// <param name="configure">The customization callback.</param>
public sealed class KamalServiceCustomizationAnnotation(Action<KamalServiceResource, KamalDeployConfig> configure) : IResourceAnnotation
{
    /// <summary>The customization callback, invoked after the default config has been built.</summary>
    public Action<KamalServiceResource, KamalDeployConfig> Configure { get; } = configure ?? throw new ArgumentNullException(nameof(configure));
}

/// <summary>
/// Customizes the generated <see cref="KamalAccessory"/> for a container resource published as a Kamal accessory.
/// </summary>
/// <param name="configure">The customization callback.</param>
public sealed class KamalAccessoryCustomizationAnnotation(Action<KamalServiceResource, KamalAccessory> configure) : IResourceAnnotation
{
    /// <summary>The customization callback, invoked after the default accessory model has been built.</summary>
    public Action<KamalServiceResource, KamalAccessory> Configure { get; } = configure ?? throw new ArgumentNullException(nameof(configure));
}
