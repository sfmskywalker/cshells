using System.Reflection;
using CShells.DependencyInjection;
using CShells.Features;

namespace CShells.Tests.Unit.DependencyInjection;

public class CShellsBuilderNamingGuardrailTests
{
    [Fact]
    public void PublicAssemblyDiscoverySurface_UsesOnlyApprovedMethodNames()
    {
        var methodNames = GetPublicAssemblyDiscoveryMethods()
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(CShellsBuilderExtensions.FromAssemblies),
                nameof(CShellsBuilderExtensions.FromAssemblyContaining),
                nameof(CShellsBuilderExtensions.FromHostAssemblies),
                nameof(CShellsBuilderExtensions.WithAssemblyProvider)
            ],
            methodNames);
    }

    [Fact]
    public void FromAssemblyContaining_ExposesApprovedMarkerOverloadShape()
    {
        var overload = Assert.Single(
            GetPublicAssemblyDiscoveryMethods(),
            method => method.Name == nameof(CShellsBuilderExtensions.FromAssemblyContaining));

        Assert.True(overload.IsGenericMethodDefinition);
        Assert.Single(overload.GetGenericArguments());
        Assert.True(HasParameters(overload, typeof(CShellsBuilder)));
    }

    [Fact]
    public void WithAssemblyProvider_ExposesApprovedOverloadShapes()
    {
        var overloads = GetPublicAssemblyDiscoveryMethods()
            .Where(method => method.Name == nameof(CShellsBuilderExtensions.WithAssemblyProvider))
            .ToArray();

        Assert.Contains(overloads, IsGenericProviderAttachmentOverload);
        Assert.Contains(overloads, method => HasParameters(method, typeof(CShellsBuilder), typeof(IFeatureAssemblyProvider)));
        Assert.Contains(overloads, method => HasParameters(method, typeof(CShellsBuilder), typeof(Func<IServiceProvider, IFeatureAssemblyProvider>)));
    }

    [Theory]
    [InlineData("WithAssemblies")]
    [InlineData("WithHostAssemblies")]
    [InlineData("AddAssemblies")]
    [InlineData("AddHostAssemblies")]
    public void PublicAssemblyDiscoverySurface_DoesNotExposeRejectedAliases(string rejectedMethodName)
    {
        Assert.DoesNotContain(
            GetPublicAssemblyDiscoveryMethods(),
            method => string.Equals(method.Name, rejectedMethodName, StringComparison.Ordinal));
    }

    private static MethodInfo[] GetPublicAssemblyDiscoveryMethods() => typeof(CShellsBuilderExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(method => method.ReturnType == typeof(CShellsBuilder))
        .Where(method => method.GetParameters() is [{ ParameterType: var firstParameterType }, ..] && firstParameterType == typeof(CShellsBuilder))
        .Where(method => method.Name.Contains("Assembl", StringComparison.Ordinal))
        .ToArray();

    private static bool IsGenericProviderAttachmentOverload(MethodInfo method)
    {
        if (!method.IsGenericMethodDefinition || method.GetGenericArguments().Length != 1)
        {
            return false;
        }

        if (!HasParameters(method, typeof(CShellsBuilder)))
        {
            return false;
        }

        var genericArgument = method.GetGenericArguments()[0];
        var constraints = genericArgument.GetGenericParameterConstraints();

        return constraints.Contains(typeof(IFeatureAssemblyProvider))
            && (genericArgument.GenericParameterAttributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0;
    }

    private static bool HasParameters(MethodInfo method, params Type[] parameterTypes) => method
        .GetParameters()
        .Select(parameter => parameter.ParameterType)
        .SequenceEqual(parameterTypes);
}
