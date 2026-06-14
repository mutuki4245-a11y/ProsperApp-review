using Microsoft.Extensions.Options;
using ProsperApp.Options;

namespace ProsperApp.Services;

public class FeatureGate(IOptions<AppOptions> options) : IFeatureGate
{
    private readonly HashSet<string> _enabledFeatures = options.Value.EnabledFeatures
        .Where(feature => !string.IsNullOrWhiteSpace(feature))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(string featureName)
    {
        return _enabledFeatures.Contains(featureName);
    }
}
