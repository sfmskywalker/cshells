using CShells.Lifecycle.Providers;

namespace CShells.Tests.Unit.Lifecycle.Providers;

public class CompositeCursorCodecTests
{
    [Fact(DisplayName = "Encode(empty) returns null; Decode(null) returns empty list")]
    public void EmptyRoundTrip()
    {
        Assert.Null(CompositeCursorCodec.Encode([]));
        Assert.Empty(CompositeCursorCodec.Decode(null));
        Assert.Empty(CompositeCursorCodec.Decode(""));
    }

    [Fact(DisplayName = "Single-entry round-trip preserves index and cursor")]
    public void Single_RoundTrip()
    {
        var encoded = CompositeCursorCodec.Encode([new(0, "sub-cursor-a")]);
        Assert.NotNull(encoded);

        var decoded = CompositeCursorCodec.Decode(encoded);
        var e = Assert.Single(decoded);
        Assert.Equal(0, e.ProviderIndex);
        Assert.Equal("sub-cursor-a", e.SubCursor);
    }

    [Fact(DisplayName = "Multi-entry round-trip preserves order")]
    public void Multi_RoundTrip_PreservesOrder()
    {
        var input = new CompositeCursorEntry[]
        {
            new(0, "a"),
            new(2, "b"),
            new(5, "c"),
        };
        var encoded = CompositeCursorCodec.Encode(input);
        var decoded = CompositeCursorCodec.Decode(encoded);

        Assert.Equal(3, decoded.Count);
        Assert.Equal(0, decoded[0].ProviderIndex);
        Assert.Equal("a", decoded[0].SubCursor);
        Assert.Equal(2, decoded[1].ProviderIndex);
        Assert.Equal("b", decoded[1].SubCursor);
        Assert.Equal(5, decoded[2].ProviderIndex);
        Assert.Equal("c", decoded[2].SubCursor);
    }

    [Fact(DisplayName = "Decode rejects corrupted base64")]
    public void Decode_Corrupted_Base64_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CompositeCursorCodec.Decode("!!!not-base64!!!"));
        Assert.Contains("base64", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Decode rejects malformed JSON")]
    public void Decode_Malformed_Json_Throws()
    {
        var garbage = Convert.ToBase64String("{this is not json"u8.ToArray());
        var ex = Assert.Throws<InvalidOperationException>(() => CompositeCursorCodec.Decode(garbage));
        Assert.Contains("JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Decode rejects unknown codec version")]
    public void Decode_UnknownVersion_Throws()
    {
        var badVersion = Convert.ToBase64String(
            """{"v":99,"entries":[{"p":0,"c":"x"}]}"""u8.ToArray());
        var ex = Assert.Throws<InvalidOperationException>(() => CompositeCursorCodec.Decode(badVersion));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
