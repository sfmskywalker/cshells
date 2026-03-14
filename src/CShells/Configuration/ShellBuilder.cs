using System.Reflection;
using CShells.Features;
using Microsoft.Extensions.Configuration;

namespace CShells.Configuration;

/// <summary>
/// Fluent API for building shell configurations.
/// </summary>
public class ShellBuilder
{
    private readonly ShellSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellBuilder"/> class.
    /// </summary>
    /// <param name="id">The shell identifier.</param>
    public ShellBuilder(ShellId id)
    {
        _settings = new ShellSettings(id);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellBuilder"/> class.
    /// </summary>
    /// <param name="id">The shell identifier as a string.</param>
    public ShellBuilder(string id) : this(new ShellId(id))
    {
    }

    /// <summary>
    /// Adds features to the shell by string identifier.
    /// </summary>
    public ShellBuilder WithFeatures(params string[] featureIds)
    {
        Guard.Against.Null(featureIds);
        var current = _settings.EnabledFeatures.ToList();
        foreach (var id in featureIds)
            if (!current.Contains(id, StringComparer.OrdinalIgnoreCase))
                current.Add(id);
        _settings.EnabledFeatures = [..current];
        return this;
    }

    /// <summary>
    /// Adds features to the shell from a mixed array.
    /// Each element may be:
    /// <list type="bullet">
    ///   <item>A <see cref="string"/> — used directly as the feature identifier.</item>
    ///   <item>A <see cref="Type"/> implementing <see cref="IShellFeature"/> — the feature name is resolved from
    ///         <see cref="ShellFeatureAttribute"/> or the class name.</item>
    ///   <item>A <see cref="FeatureEntry"/> — enables the named feature and applies its settings.</item>
    /// </list>
    /// </summary>
    /// <example>
    /// <code>
    /// shell.WithFeatures(
    ///     "AdminUser",
    ///     typeof(IdentityFeature),
    ///     new FeatureEntry { Name = "FastEndpoints", Settings = { ["EndpointRoutePrefix"] = "elsa/api" } });
    /// </code>
    /// </example>
    public ShellBuilder WithFeatures(params object[] features)
    {
        Guard.Against.Null(features);
        foreach (var feature in features)
        {
            switch (feature)
            {
                case string featureId:
                    WithFeature(featureId);
                    break;
                case Type featureType:
                    WithFeature(featureType);
                    break;
                case FeatureEntry featureEntry:
                    WithFeature(featureEntry);
                    break;
                case null:
                    throw new ArgumentException("Feature array must not contain null elements.");
                default:
                    throw new ArgumentException(
                        $"Unsupported feature descriptor type '{feature.GetType().FullName}'. " +
                        "Each element must be a string, Type, or FeatureEntry.");
            }
        }
        return this;
    }

    /// <summary>
    /// Adds a single feature to the shell by string identifier.
    /// </summary>
    public ShellBuilder WithFeature(string featureId)
    {
        Guard.Against.Null(featureId);
        var current = _settings.EnabledFeatures.ToList();
        if (!current.Contains(featureId, StringComparer.OrdinalIgnoreCase))
            current.Add(featureId);
        _settings.EnabledFeatures = [..current];
        return this;
    }

    /// <summary>
    /// Adds a feature to the shell by its type.
    /// The feature name is resolved from <see cref="ShellFeatureAttribute"/> or derived from the class name.
    /// </summary>
    public ShellBuilder WithFeature<TFeature>() where TFeature : IShellFeature
        => WithFeature(typeof(TFeature));

    /// <summary>
    /// Adds a feature to the shell by its type, with a strongly-typed configure action that is
    /// applied to the live feature instance at shell-build time — after config binding but before
    /// <c>ConfigureServices</c> is called, so code-first values always win over appsettings.
    /// </summary>
    /// <example>
    /// <code>
    /// shell.WithFeature&lt;QuartzFeature&gt;(feature =>
    /// {
    ///     feature.WaitForJobsToComplete = false;
    /// });
    /// </code>
    /// </example>
    public ShellBuilder WithFeature<TFeature>(Action<TFeature> configure) where TFeature : IShellFeature
    {
        Guard.Against.Null(configure);
        var featureId = ResolveFeatureName(typeof(TFeature));

        // Enable the feature
        WithFeature(featureId);

        // Wrap the typed delegate as Action<IShellFeature> and store it.
        // If the same feature is configured multiple times, compose the delegates so all run.
        Action<IShellFeature> untyped = feature =>
        {
            if (feature is TFeature typed)
                configure(typed);
            else
                throw new InvalidOperationException(
                    $"Feature configurator for '{featureId}' expected an instance of '{typeof(TFeature).Name}' " +
                    $"but received '{feature.GetType().Name}'.");
        };

        if (_settings.FeatureConfigurators.TryGetValue(featureId, out var existing))
            _settings.FeatureConfigurators[featureId] = existing + untyped;
        else
            _settings.FeatureConfigurators[featureId] = untyped;

        return this;
    }

    /// <summary>
    /// Adds a feature to the shell by its type.
    /// The feature name is resolved from <see cref="ShellFeatureAttribute"/> or derived from the class name.
    /// </summary>
    public ShellBuilder WithFeature(Type featureType)
    {
        Guard.Against.Null(featureType);
        return WithFeature(ResolveFeatureName(featureType));
    }

    /// <summary>
    /// Adds a feature to the shell by its type, with settings configured via a <see cref="FeatureSettingsBuilder"/>.
    /// The feature name is resolved from <see cref="ShellFeatureAttribute"/> or derived from the class name.
    /// </summary>
    public ShellBuilder WithFeature(Type featureType, Action<FeatureSettingsBuilder> configure)
    {
        Guard.Against.Null(featureType);
        Guard.Against.Null(configure);
        return WithFeature(ResolveFeatureName(featureType), configure);
    }

    /// <summary>
    /// Adds a feature with settings to the shell.
    /// </summary>
    /// <param name="featureId">The feature identifier.</param>
    /// <param name="configure">Action to configure the feature settings.</param>
    /// <returns>This builder for method chaining.</returns>
    public ShellBuilder WithFeature(string featureId, Action<FeatureSettingsBuilder> configure)
    {
        Guard.Against.Null(featureId);
        Guard.Against.Null(configure);

        var current = _settings.EnabledFeatures.ToList();
        if (!current.Contains(featureId, StringComparer.OrdinalIgnoreCase))
            current.Add(featureId);
        _settings.EnabledFeatures = [..current];

        var settingsBuilder = new FeatureSettingsBuilder(featureId);
        configure(settingsBuilder);
        settingsBuilder.ApplyTo(_settings.ConfigurationData);

        return this;
    }

    /// <summary>
    /// Adds a feature entry (with optional settings) to the shell.
    /// </summary>
    public ShellBuilder WithFeature(FeatureEntry feature)
    {
        Guard.Against.Null(feature);

        var current = _settings.EnabledFeatures.ToList();
        if (!current.Contains(feature.Name, StringComparer.OrdinalIgnoreCase))
            current.Add(feature.Name);
        _settings.EnabledFeatures = [..current];

        ConfigurationHelper.PopulateFeatureSettings([feature], _settings.ConfigurationData);

        return this;
    }

    /// <summary>
    /// Resolves the feature name from a feature type, using the same logic as <see cref="FeatureDiscovery"/>.
    /// </summary>
    public static string ResolveFeatureName(Type featureType)
    {
        Guard.Against.Null(featureType);

        if (!typeof(IShellFeature).IsAssignableFrom(featureType))
            throw new ArgumentException($"Type '{featureType.FullName}' does not implement {nameof(IShellFeature)}.", nameof(featureType));

        var attribute = featureType.GetCustomAttribute<ShellFeatureAttribute>();
        return attribute?.Name ?? StripSuffixes(featureType.Name, "ShellFeature", "Feature");
    }

    private static string StripSuffixes(string source, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (!string.IsNullOrEmpty(suffix) &&
                source.EndsWith(suffix, StringComparison.Ordinal) &&
                source.Length > suffix.Length)
                return source[..^suffix.Length];
        }
        return source;
    }

    /// <summary>
    /// Adds a configuration entry to the shell settings.
    /// Configuration data is used to populate the shell-scoped IConfiguration.
    /// </summary>
    public ShellBuilder WithConfiguration(string key, object value)
    {
        Guard.Against.Null(key);
        Guard.Against.Null(value);
        _settings.ConfigurationData[key] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple configuration entries to the shell settings.
    /// Configuration data is used to populate the shell-scoped IConfiguration.
    /// </summary>
    public ShellBuilder WithConfiguration(IDictionary<string, object> configuration)
    {
        Guard.Against.Null(configuration);
        foreach (var (key, value) in configuration)
            _settings.ConfigurationData[key] = value;
        return this;
    }

    /// <summary>
    /// Loads configuration from an <see cref="IConfigurationSection"/> and merges it with existing settings.
    /// Features are merged (combined), while Configuration from the section takes precedence.
    /// </summary>
    /// <param name="section">The configuration section representing a shell.</param>
    /// <returns>The builder for method chaining.</returns>
    public ShellBuilder FromConfiguration(IConfigurationSection section)
    {
        Guard.Against.Null(section);

        // Parse features from configuration (handles array and object-map forms)
        var featuresSection = section.GetSection("Features");
        var features = ConfigurationHelper.ParseFeaturesFromConfiguration(featuresSection, _settings.Id.Name);

        if (features.Count > 0)
        {
            var existingFeatures = _settings.EnabledFeatures.ToList();
            var newFeatureNames = ConfigurationHelper.ExtractFeatureNames(features);
            existingFeatures.AddRange(newFeatureNames);
            _settings.EnabledFeatures = existingFeatures.Distinct().ToArray();

            // Apply feature settings
            ConfigurationHelper.PopulateFeatureSettings(features, _settings.ConfigurationData);
        }

        // Load shell-level configuration
        var configurationSection = section.GetSection("Configuration");
        ConfigurationHelper.LoadConfigurationFromSection(configurationSection, _settings.ConfigurationData);

        return this;
    }

    /// <summary>
    /// Loads configuration from a <see cref="ShellConfig"/> and merges it with existing settings.
    /// Features are merged (combined), while Configuration takes precedence.
    /// </summary>
    /// <param name="config">The shell configuration.</param>
    /// <returns>The builder for method chaining.</returns>
    public ShellBuilder FromConfiguration(ShellConfig config)
    {
        Guard.Against.Null(config);

        // Merge features
        var featureNames = ConfigurationHelper.ExtractFeatureNames(config.Features);

        if (featureNames.Length > 0)
        {
            var existingFeatures = _settings.EnabledFeatures.ToList();
            existingFeatures.AddRange(featureNames);
            _settings.EnabledFeatures = existingFeatures.Distinct().ToArray();
        }

        // Apply feature settings
        ConfigurationHelper.PopulateFeatureSettings(config.Features, _settings.ConfigurationData);

        // Apply shell-level configuration
        ConfigurationHelper.PopulateShellConfiguration(config.Configuration, _settings.ConfigurationData);

        return this;
    }

    /// <summary>
    /// Builds the shell settings.
    /// </summary>
    public ShellSettings Build() => _settings;

    /// <summary>
    /// Implicitly converts a builder to shell settings.
    /// </summary>
    public static implicit operator ShellSettings(ShellBuilder builder) => builder.Build();
}

/// <summary>
/// Builder for feature-specific settings.
/// </summary>
public class FeatureSettingsBuilder
{
    private readonly string _featureName;
    private readonly Dictionary<string, object> _settings = new();

    internal FeatureSettingsBuilder(string featureName)
    {
        _featureName = featureName;
    }

    /// <summary>
    /// Adds a setting for the feature.
    /// </summary>
    public FeatureSettingsBuilder WithSetting(string key, object value)
    {
        Guard.Against.Null(key);
        Guard.Against.Null(value);
        _settings[key] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple settings for the feature.
    /// </summary>
    public FeatureSettingsBuilder WithSettings(IDictionary<string, object> settings)
    {
        Guard.Against.Null(settings);
        foreach (var (key, value) in settings)
            _settings[key] = value;
        return this;
    }

    internal void ApplyTo(IDictionary<string, object> configurationData)
    {
        foreach (var (key, value) in _settings)
        {
            configurationData[$"{_featureName}:{key}"] = value;
        }
    }
}

