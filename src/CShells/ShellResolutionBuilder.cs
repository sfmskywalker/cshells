namespace CShells;

/// <summary>
/// A builder for configuring shell resolution strategies in a protocol-agnostic way.
/// </summary>
public class ShellResolutionBuilder
{
    private readonly List<IShellResolver> _resolvers = [];
    private readonly Dictionary<string, object> _properties = [];
    private readonly List<Action<ShellResolutionBuilder>> _finalizers = [];

    /// <summary>
    /// Adds a custom shell resolver to the resolution pipeline.
    /// </summary>
    /// <param name="resolver">The resolver to add.</param>
    /// <returns>The builder for method chaining.</returns>
    public ShellResolutionBuilder AddResolver(IShellResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolvers.Add(resolver);
        return this;
    }

    /// <summary>
    /// Adds a custom shell resolver to the resolution pipeline using a factory function.
    /// </summary>
    /// <param name="resolverFactory">A function that creates the resolver.</param>
    /// <returns>The builder for method chaining.</returns>
    public ShellResolutionBuilder AddResolver(Func<IShellResolver> resolverFactory)
    {
        ArgumentNullException.ThrowIfNull(resolverFactory);
        _resolvers.Add(resolverFactory());
        return this;
    }

    /// <summary>
    /// Adds a finalizer that will be invoked before building the resolver.
    /// Finalizers allow extension methods to perform cleanup or add resolvers based on accumulated state.
    /// </summary>
    /// <param name="finalizer">The finalizer action.</param>
    /// <returns>The builder for method chaining.</returns>
    public ShellResolutionBuilder AddFinalizer(Action<ShellResolutionBuilder> finalizer)
    {
        ArgumentNullException.ThrowIfNull(finalizer);
        _finalizers.Add(finalizer);
        return this;
    }

    /// <summary>
    /// Gets or creates a property value for use by extension methods.
    /// </summary>
    /// <typeparam name="T">The type of the property value.</typeparam>
    /// <param name="key">The property key.</param>
    /// <param name="factory">A factory function to create the value if it doesn't exist.</param>
    /// <returns>The property value.</returns>
    public T GetOrCreateProperty<T>(string key, Func<T> factory) where T : notnull
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_properties.TryGetValue(key, out var value))
        {
            value = factory();
            _properties[key] = value;
        }

        return (T)value;
    }

    /// <summary>
    /// Tries to get a property value.
    /// </summary>
    /// <typeparam name="T">The type of the property value.</typeparam>
    /// <param name="key">The property key.</param>
    /// <param name="value">The property value if found.</param>
    /// <returns><c>true</c> if the property exists; otherwise, <c>false</c>.</returns>
    public bool TryGetProperty<T>(string key, out T? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (_properties.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Removes a property from the builder.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns><c>true</c> if the property was removed; otherwise, <c>false</c>.</returns>
    public bool RemoveProperty(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _properties.Remove(key);
    }

    /// <summary>
    /// Builds the final composite shell resolver from all configured resolvers.
    /// </summary>
    /// <returns>An <see cref="IShellResolver"/> that evaluates all configured resolvers in order.</returns>
    public IShellResolver Build()
    {
        // Run all finalizers before building
        foreach (var finalizer in _finalizers)
        {
            finalizer(this);
        }

        if (_resolvers.Count == 0)
        {
            throw new InvalidOperationException("No resolvers have been configured. Add at least one resolver before building.");
        }

        if (_resolvers.Count == 1)
        {
            return _resolvers[0];
        }

        return new CompositeShellResolver(_resolvers.ToArray());
    }

    /// <summary>
    /// A composite shell resolver that tries multiple <see cref="IShellResolver"/> instances in order
    /// and returns the first non-null <see cref="ShellId"/>.
    /// </summary>
    private sealed class CompositeShellResolver : IShellResolver
    {
        private readonly IReadOnlyList<IShellResolver> _resolvers;

        public CompositeShellResolver(IShellResolver[] resolvers)
        {
            _resolvers = resolvers;
        }

        public ShellId? Resolve(ShellResolutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            foreach (var resolver in _resolvers)
            {
                var shellId = resolver.Resolve(context);
                if (shellId.HasValue)
                {
                    return shellId;
                }
            }

            return null;
        }
    }
}
