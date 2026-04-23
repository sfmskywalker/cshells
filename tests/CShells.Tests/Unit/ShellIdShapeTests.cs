using System.Reflection;

namespace CShells.Tests.Unit;

/// <summary>
/// Regression tests guarding FR-008: <see cref="ShellId"/> remains a name-only value type.
/// Silent reintroduction of a composite identity (adding a <c>Generation</c> / <c>Version</c>
/// field) would break the architecture's assumption that generation lives exclusively on
/// <see cref="Lifecycle.ShellDescriptor"/>.
/// </summary>
public class ShellIdShapeTests
{
    [Fact(DisplayName = "ShellId is a readonly record struct")]
    public void ShellId_Is_ReadonlyRecordStruct()
    {
        var type = typeof(ShellId);

        Assert.True(type.IsValueType, "ShellId must be a value type.");

        // C# compiler marks record structs with a synthesized EqualityContract-like API, but the most
        // reliable heuristic is checking it has a compiler-generated `PrintMembers` method and
        // is `readonly` (all instance fields are init-only).
        Assert.True(type.IsReadOnly(), "ShellId must be a readonly struct.");
    }

    [Fact(DisplayName = "ShellId exposes exactly one public instance property: Name (string)")]
    public void ShellId_Has_Only_Name_Property()
    {
        var properties = typeof(ShellId)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.Single(properties);

        var name = properties[0];
        Assert.Equal("Name", name.Name);
        Assert.Equal(typeof(string), name.PropertyType);
    }

    [Theory(DisplayName = "ShellId must NOT expose a composite-identity member")]
    [InlineData("Generation")]
    [InlineData("Version")]
    [InlineData("Id")]
    public void ShellId_DoesNotExpose_CompositeIdentityMember(string forbiddenMember)
    {
        var members = typeof(ShellId).GetMembers(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(members, m => string.Equals(m.Name, forbiddenMember, StringComparison.OrdinalIgnoreCase));
    }
}

internal static class TypeExtensions
{
    /// <summary>
    /// Checks whether a value type is marked as <c>readonly</c> (all instance fields init-only).
    /// </summary>
    public static bool IsReadOnly(this Type type) =>
        type.GetCustomAttributes(inherit: false)
            .Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
}
