namespace CShells.Configuration;

/// <summary>
/// In-memory implementation of <see cref="IShellSettingsProvider"/> for code-first shell configuration.
/// </summary>
public class InMemoryShellSettingsProvider : IShellSettingsProvider
{
    private readonly IReadOnlyList<ShellSettings> _shells;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryShellSettingsProvider"/> class.
    /// </summary>
    /// <param name="shells">The shell settings to provide.</param>
    public InMemoryShellSettingsProvider(IReadOnlyList<ShellSettings> shells)
    {
        ArgumentNullException.ThrowIfNull(shells);
        _shells = shells;
    }

    /// <inheritdoc />
    public Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<ShellSettings>>(_shells);
    }
}
