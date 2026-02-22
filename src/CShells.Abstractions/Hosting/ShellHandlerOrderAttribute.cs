namespace CShells.Hosting;

/// <summary>
/// Controls the relative execution order of an <see cref="IShellActivatedHandler"/> or
/// <see cref="IShellDeactivatingHandler"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Activation</strong>: handlers are invoked in ascending order of <see cref="Order"/>.
/// Handlers with no attribute are treated as order <c>0</c>.
/// </para>
/// <para>
/// <strong>Deactivation</strong>: handlers are invoked in descending order of <see cref="Order"/>
/// — the natural inverse of activation, ensuring symmetric teardown.
/// </para>
/// <para>
/// Use negative values for handlers that must run early during activation (e.g. database migrations),
/// and positive values for handlers that depend on those (e.g. starting a scheduler that requires
/// the schema to exist). The same values automatically produce correct shutdown ordering.
/// </para>
/// <example>
/// <code>
/// [ShellHandlerOrder(-100)]   // activates first, deactivates last
/// public class EfCoreMigrationHandler : IShellActivatedHandler { ... }
///
/// // no attribute → order 0: activates after -100, deactivates before -100
/// public class QuartzShellLifecycleHandler : IShellActivatedHandler, IShellDeactivatingHandler { ... }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ShellHandlerOrderAttribute(int order) : Attribute
{
    /// <summary>
    /// Gets the execution order. Lower values run first during activation, last during deactivation.
    /// Defaults to <c>0</c> when the attribute is absent.
    /// </summary>
    public int Order { get; } = order;
}

