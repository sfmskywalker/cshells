using System.Reflection;

namespace CShells.Lifecycle;

internal sealed class ShellInitializerOrderingPlanner
{
    public InitializerOrderingPlan Plan(
        ShellDescriptor shell,
        IReadOnlyList<IShellInitializer> initializers,
        IReadOnlyList<ShellInitializerRegistration> initializerRegistrations)
    {
        Guard.Against.Null(initializers);
        Guard.Against.Null(initializerRegistrations);

        foreach (var registration in initializerRegistrations)
        {
            if (!typeof(IShellInitializer).IsAssignableFrom(registration.InitializerType))
            {
                throw new ShellInitializerOrderException(
                    shell,
                    $"ordering metadata source '{registration.Source}' references type '{registration.InitializerType.FullName}' which does not implement {nameof(IShellInitializer)}.");
            }

            ValidatePhase(shell, registration.Phase, registration.Source);
        }

        var pendingRegistrations = initializerRegistrations.ToList();

        var entries = new List<InitializerOrderingEntry>(initializers.Count);

        for (var i = 0; i < initializers.Count; i++)
        {
            var initializer = initializers[i];
            var type = initializer.GetType();
            var metadata = ResolveMetadata(shell, registrationIndex: i, type, pendingRegistrations);
            entries.Add(new InitializerOrderingEntry(
                initializer,
                type,
                metadata.Phase,
                metadata.Order,
                RegistrationIndex: i,
                metadata.IsExplicit,
                metadata.Source));
        }

        var unmatched = pendingRegistrations
            .Select(r => r.InitializerType.FullName ?? r.InitializerType.Name)
            .ToList();

        if (unmatched.Count > 0)
            throw new ShellInitializerOrderException(
                shell,
                $"ordering metadata was registered for initializer type(s) that were not resolved from DI: {string.Join(", ", unmatched)}.");

        var duplicateGroups = entries
            .GroupBy(e => (e.Phase, e.Order))
            .Where(g => g.Count() > 1 && g.Any(e => e.IsExplicit))
            .Select(g => new InitializerOrderingDiagnostic(
                $"Multiple initializers share phase '{g.Key.Phase}' and order {g.Key.Order}; DI registration index will be used as the deterministic tie-break.",
                [.. g.Select(e => e.InitializerType)]))
            .ToList();

        var ordered = entries
            .OrderBy(e => e.Phase)
            .ThenBy(e => e.Order)
            .ThenBy(e => e.RegistrationIndex)
            .ToList();

        return new InitializerOrderingPlan(ordered, duplicateGroups);
    }

    private static ShellInitializerRegistration ResolveMetadata(
        ShellDescriptor shell,
        int registrationIndex,
        Type initializerType,
        List<ShellInitializerRegistration> pendingRegistrations)
    {
        var registration = TakeFirst(pendingRegistrations, r => r.RegistrationIndex == registrationIndex)
            ?? TakeFirst(pendingRegistrations, r => r.InitializerType == initializerType);

        if (registration is not null)
            return ResolveRegistrationMetadata(shell, registration);

        var attribute = initializerType.GetCustomAttribute<LifecycleOrderAttribute>();
        if (attribute is not null)
            return ResolveAttributeMetadata(shell, initializerType, attribute);

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

    private static ShellInitializerRegistration ResolveRegistrationMetadata(ShellDescriptor shell, ShellInitializerRegistration registration)
    {
        if (!registration.IsExplicit)
        {
            var attribute = registration.InitializerType.GetCustomAttribute<LifecycleOrderAttribute>();
            if (attribute is not null)
                return ResolveAttributeMetadata(shell, registration.InitializerType, attribute);
        }

        ValidatePhase(shell, registration.Phase, registration.Source);
        return registration;
    }

    private static ShellInitializerRegistration ResolveAttributeMetadata(
        ShellDescriptor shell,
        Type initializerType,
        LifecycleOrderAttribute attribute)
    {
        var source = $"{nameof(LifecycleOrderAttribute)} on {initializerType.FullName}";
        ValidatePhase(shell, attribute.Phase, source);
        return new ShellInitializerRegistration(
            initializerType,
            attribute.Phase,
            attribute.Order,
            RegistrationIndex: -1,
            IsExplicit: false,
            Source: source);
    }

    private static ShellInitializerRegistration? TakeFirst(
        List<ShellInitializerRegistration> registrations,
        Func<ShellInitializerRegistration, bool> predicate)
    {
        var index = registrations.FindIndex(r => predicate(r));
        if (index < 0)
            return null;

        var registration = registrations[index];
        registrations.RemoveAt(index);
        return registration;
    }

    private static void ValidatePhase(ShellDescriptor shell, LifecyclePhase phase, string source)
    {
        if (!Enum.IsDefined(typeof(LifecyclePhase), phase))
            throw new ShellInitializerOrderException(shell, $"ordering metadata source '{source}' uses undefined lifecycle phase value '{(int)phase}'.");
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
