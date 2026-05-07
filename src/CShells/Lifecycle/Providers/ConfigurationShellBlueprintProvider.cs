using System.Collections.Immutable;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using Microsoft.Extensions.Configuration;

namespace CShells.Lifecycle.Providers;

/// <summary>
/// <see cref="IShellBlueprintProvider"/> backed by an <see cref="IConfiguration"/> section
/// whose children are individual shell configurations. Read-only — no
/// <see cref="IShellBlueprintManager"/> is attached.
/// </summary>
/// <remarks>
/// <para>
/// Lookup is O(1) — a direct <c>section.GetSection(name)</c> call. Listing returns entries in
/// case-insensitive ordinal order using the last-returned key as the cursor (research R-008).
/// </para>
/// <para>
/// Each returned blueprint wraps a single child section in a <see cref="ConfigurationShellBlueprint"/>
/// that re-reads the configuration on every <c>ComposeAsync</c> call, so configuration edits
/// between reloads are observed.
/// </para>
/// </remarks>
public sealed class ConfigurationShellBlueprintProvider : IShellBlueprintProvider
{
    private readonly IConfiguration _shellsSection;
    private const string ShellsPath = "CShells:Shells";

    /// <summary>Stable identifier emitted as <see cref="BlueprintSummary.SourceId"/>.</summary>
    public const string SourceIdValue = nameof(ConfigurationShellBlueprintProvider);

    public ConfigurationShellBlueprintProvider(IConfiguration shellsSection)
    {
        _shellsSection = Guard.Against.Null(shellsSection);
    }

    /// <inheritdoc />
    public Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);
        ValidateShellEntries();

        var direct = FindShellSection(name);
        if (direct is not null)
            return Task.FromResult<ProvidedBlueprint?>(
                new ProvidedBlueprint(new ConfigurationShellBlueprint(ValidateShellName(direct), direct)));

        return Task.FromResult<ProvidedBlueprint?>(null);
    }

    /// <inheritdoc />
    public Task<BlueprintPage> ListAsync(BlueprintListQuery query, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(query);
        query.EnsureValid();

        ValidateShellEntries();

        var ordered = _shellsSection.GetChildren()
            .Select(ValidateShellName)
            .Where(name => query.NamePrefix is null ||
                           name.StartsWith(query.NamePrefix, StringComparison.OrdinalIgnoreCase))
            .Where(name => query.Cursor is null ||
                           string.Compare(name, query.Cursor, StringComparison.OrdinalIgnoreCase) > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(query.Limit + 1)
            .ToList();

        var hasMore = ordered.Count > query.Limit;
        var items = ordered.Take(query.Limit)
            .Select(name => new BlueprintSummary(
                name,
                SourceIdValue,
                Mutable: false,
                Metadata: ImmutableDictionary<string, string>.Empty))
            .ToList();

        var nextCursor = hasMore && items.Count > 0 ? items[^1].Name : null;
        return Task.FromResult(new BlueprintPage(items, nextCursor));
    }

    private void ValidateShellEntries()
    {
        foreach (var child in _shellsSection.GetChildren())
            ValidateShellName(child);
    }

    private IConfigurationSection? FindShellSection(string name)
    {
        foreach (var child in _shellsSection.GetChildren())
            if (string.Equals(ValidateShellName(child), name, StringComparison.OrdinalIgnoreCase))
                return child;

        return null;
    }

    private static string ValidateShellName(IConfigurationSection shellSection)
    {
        var shellName = shellSection.Key.Trim();

        if (string.IsNullOrWhiteSpace(shellName))
        {
            throw new InvalidOperationException(
                $"Configured shell entry '{shellSection.Path}' under '{ShellsPath}' must use a non-empty shell name as the map key.");
        }

        if (int.TryParse(shellName, out _))
        {
            throw new InvalidOperationException(
                $"Configured shell entry '{shellSection.Path}' under '{ShellsPath}' uses unsupported array syntax. Configure shells as named map entries, for example '{ShellsPath}:Default'.");
        }

        return shellName;
    }
}
