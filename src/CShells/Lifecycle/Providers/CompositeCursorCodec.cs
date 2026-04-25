using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CShells.Lifecycle.Providers;

/// <summary>
/// Base64-JSON codec for the composite provider's multi-provider cursor. The cursor is an
/// opaque string from the caller's perspective.
/// </summary>
/// <remarks>
/// Wire format:
/// <code>
/// { "v": 1, "entries": [ { "p": 0, "c": "sub-cursor" }, { "p": 2, "c": "sub-cursor" } ] }
/// </code>
/// where <c>v</c> is the codec version (current: 1), <c>p</c> is the sub-provider's
/// registration index, and <c>c</c> is its own cursor. Sub-providers whose iteration is
/// complete are omitted from <c>entries</c>. When every sub-provider is exhausted the composite
/// emits <c>null</c> rather than an empty-entries doc.
/// </remarks>
internal static class CompositeCursorCodec
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Encodes a set of per-sub-provider cursors into an opaque composite cursor, or returns
    /// <c>null</c> when every sub-provider is exhausted.
    /// </summary>
    public static string? Encode(IReadOnlyList<CompositeCursorEntry> entries)
    {
        if (entries.Count == 0)
            return null;

        var doc = new CompositeCursorDoc(CurrentVersion, [.. entries]);
        var json = JsonSerializer.SerializeToUtf8Bytes(doc, Options);
        return Convert.ToBase64String(json);
    }

    /// <summary>
    /// Decodes a composite cursor previously produced by <see cref="Encode"/>. Returns an empty
    /// list for a null/empty input (first page). Throws on corrupted input or version mismatch.
    /// </summary>
    public static IReadOnlyList<CompositeCursorEntry> Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return [];

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(cursor);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Invalid composite cursor: not valid base64.", ex);
        }

        CompositeCursorDoc? doc;
        try
        {
            doc = JsonSerializer.Deserialize<CompositeCursorDoc>(bytes, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid composite cursor: not well-formed JSON.", ex);
        }

        if (doc is null)
            throw new InvalidOperationException("Invalid composite cursor: empty document.");

        if (doc.V != CurrentVersion)
            throw new InvalidOperationException(
                $"Composite cursor version {doc.V} is not supported by this host (expected {CurrentVersion}). Restart pagination from a null cursor.");

        return doc.Entries ?? [];
    }

    private sealed record CompositeCursorDoc(
        [property: JsonPropertyName("v")] int V,
        [property: JsonPropertyName("entries")] List<CompositeCursorEntry> Entries);
}

/// <summary>One entry in the composite cursor: a sub-provider index + its own cursor string.</summary>
internal sealed record CompositeCursorEntry(
    [property: System.Text.Json.Serialization.JsonPropertyName("p")] int ProviderIndex,
    [property: System.Text.Json.Serialization.JsonPropertyName("c")] string SubCursor);
