namespace ProsperApp.Services;

public interface IFeatureGate
{
    bool IsEnabled(string featureName);
}
