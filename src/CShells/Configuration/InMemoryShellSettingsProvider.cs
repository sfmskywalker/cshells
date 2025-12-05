namespace CShells.Configuration;

/// <summary>
/// In-memory implementation of <see cref="IShellSettingsProvider"/> for code-first shell configuration.
/// </summary>
public class InMemoryShellSettingsProvider(IReadOnlyList<ShellSettings> shells) : IShellSettingsProvider
{
    private readonly IReadOnlyList<ShellSettings> _shells = Guard.Against.Null(shells);

    /// <inheritdoc />
    public Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<ShellSettings>>(_shells);
}
