using System.Collections.Immutable;
using CShells.Configuration;
using CShells.Lifecycle;

namespace CShells.Lifecycle.Blueprints;

/// <summary>
/// Fluent-delegate-backed <see cref="IShellBlueprint"/>. Registered via
/// <c>CShellsBuilder.AddShell(name, builder => ...)</c>.
/// </summary>
/// <remarks>
/// The stored delegate is invoked against a fresh <see cref="ShellBuilder"/> on every
/// <see cref="ComposeAsync"/> call, so any delegate-captured mutable state is re-read at reload
/// time. Metadata is snapshot-copied onto the descriptor at generation time.
/// </remarks>
public sealed class DelegateShellBlueprint : IShellBlueprint
{
    private readonly Action<ShellBuilder> _configure;

    public DelegateShellBlueprint(string name, Action<ShellBuilder> configure, IReadOnlyDictionary<string, string>? metadata = null)
    {
        Name = Guard.Against.NullOrWhiteSpace(name);
        _configure = Guard.Against.Null(configure);
        Metadata = metadata is null
            ? ImmutableDictionary<string, string>.Empty
            : ImmutableDictionary.CreateRange(metadata);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <inheritdoc />
    public Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default)
    {
        var builder = new ShellBuilder(new ShellId(Name));
        _configure(builder);
        return Task.FromResult(builder.Build());
    }
}
