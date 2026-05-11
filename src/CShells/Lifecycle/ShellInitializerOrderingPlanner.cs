using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Lifecycle;

internal sealed class ShellInitializerOrderingPlanner(ILogger<ShellInitializerOrderingPlanner>? logger = null)
{
    private readonly ILogger<ShellInitializerOrderingPlanner> _logger = logger ?? NullLogger<ShellInitializerOrderingPlanner>.Instance;

    public InitializerOrderingPlan Plan(
        ShellDescriptor shell,
        IReadOnlyList<IShellInitializer> initializers,
        IReadOnlyList<ShellInitializerRegistration> explicitRegistrations)
    {
        Guard.Against.Null(initializers);
        Guard.Against.Null(explicitRegistrations);

        foreach (var registration in explicitRegistrations)
        {
            if (!typeof(IShellInitializer).IsAssignableFrom(registration.InitializerType))
            {
                throw new ShellInitializerOrderException(
                    shell,
                    $"ordering metadata source '{registration.Source}' references type '{registration.InitializerType.FullName}' which does not implement {nameof(IShellInitializer)}.");
            }
        }

        var registrationsByType = explicitRegistrations
            .GroupBy(r => r.InitializerType)
            .ToDictionary(g => g.Key, g => new Queue<ShellInitializerRegistration>(g));

        var entries = new List<InitializerOrderingEntry>(initializers.Count);

        for (var i = 0; i < initializers.Count; i++)
        {
            var initializer = initializers[i];
            var type = initializer.GetType();
            var metadata = ResolveMetadata(shell, type, registrationsByType);
            entries.Add(new InitializerOrderingEntry(
                initializer,
                type,
                metadata.Phase,
                metadata.Order,
                RegistrationIndex: i,
                metadata.IsExplicit,
                metadata.Source));
        }

        var unmatched = registrationsByType
            .SelectMany(kvp => kvp.Value.Select(r => r.InitializerType.FullName ?? r.InitializerType.Name))
            .ToList();

        if (unmatched.Count > 0)
            throw new ShellInitializerOrderException(
                shell,
                $"ordering metadata was registered for initializer type(s) that were not resolved from DI: {string.Join(", ", unmatched)}.");

        var duplicateGroups = entries
            .GroupBy(e => (e.Phase, e.Order))
            .Where(g => g.Count() > 1)
            .Select(g => new InitializerOrderingDiagnostic(
                $"Multiple initializers share phase '{g.Key.Phase}' and order {g.Key.Order}; DI registration index will be used as the deterministic tie-break.",
                [.. g.Select(e => e.InitializerType)]))
            .ToList();

        foreach (var diagnostic in duplicateGroups)
            _logger.LogDebug("{Message} Types: {Types}", diagnostic.Message, string.Join(", ", diagnostic.InitializerTypes.Select(t => t.FullName)));

        var ordered = entries
            .OrderBy(e => e.Phase)
            .ThenBy(e => e.Order)
            .ThenBy(e => e.RegistrationIndex)
            .ToList();

        return new InitializerOrderingPlan(ordered, duplicateGroups);
    }

    private static ShellInitializerRegistration ResolveMetadata(
        ShellDescriptor shell,
        Type initializerType,
        Dictionary<Type, Queue<ShellInitializerRegistration>> explicitRegistrations)
    {
        if (explicitRegistrations.TryGetValue(initializerType, out var queue) && queue.Count > 0)
            return queue.Dequeue();

        var attribute = initializerType.GetCustomAttribute<LifecycleOrderAttribute>();
        if (attribute is not null)
        {
            return new ShellInitializerRegistration(
                initializerType,
                attribute.Phase,
                attribute.Order,
                RegistrationIndex: -1,
                IsExplicit: false,
                Source: $"{nameof(LifecycleOrderAttribute)} on {initializerType.FullName}");
        }

        if (!typeof(IShellInitializer).IsAssignableFrom(initializerType))
            throw new ShellInitializerOrderException(shell, $"type '{initializerType.FullName}' does not implement {nameof(IShellInitializer)}.");

        return new ShellInitializerRegistration(
            initializerType,
            LifecyclePhase.Default,
            Order: 0,
            RegistrationIndex: -1,
            IsExplicit: false,
            Source: "Legacy DI registration");
    }

    internal sealed record InitializerOrderingPlan(
        IReadOnlyList<InitializerOrderingEntry> Entries,
        IReadOnlyList<InitializerOrderingDiagnostic> Diagnostics);

    internal sealed record InitializerOrderingEntry(
        IShellInitializer Initializer,
        Type InitializerType,
        LifecyclePhase Phase,
        int Order,
        int RegistrationIndex,
        bool IsExplicit,
        string Source);

    internal sealed record InitializerOrderingDiagnostic(
        string Message,
        IReadOnlyList<Type> InitializerTypes);
}
