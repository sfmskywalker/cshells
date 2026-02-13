namespace CShells.Features;

public class FeatureNotFoundException : Exception
{
    public FeatureNotFoundException(string featureName) : base($"Feature '{featureName}' not found. Did you forget to reference the assembly containing the feature?")
    {
        FeatureName = featureName;
    }
    
    public FeatureNotFoundException(string featureName, string dependentFeatureName) : base($"Feature '{featureName}' not found. Required by feature '{dependentFeatureName}'. Did you forget to reference the assembly containing the feature?")
    {
        FeatureName = featureName;
        DependentFeatureName = dependentFeatureName;
    }
    
    public string FeatureName { get; }
    public string? DependentFeatureName { get; }
}