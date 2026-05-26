using CShells.AspNetCore.Authorization;
using CShells.FastEndpoints.Features;
using CShells.Features;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.FastEndpoints;

public class FastEndpointsFeatureTests
{
    private readonly ServiceCollection _services = [];
    private readonly FastEndpointsFeature _feature;

    public FastEndpointsFeatureTests()
    {
        var settings = new ShellSettings(new("Default"), ["TestApi"]);
        var descriptors = new[]
        {
            new ShellFeatureDescriptor("TestApi")
            {
                StartupType = typeof(TestApiFeature)
            }
        };
        var context = new ShellFeatureContext(settings, descriptors);
        _feature = new(context);
    }

    [Fact]
    public void ConfigureServices_RegistersAuthorizationPolicyProvider()
    {
        _feature.ConfigureServices(_services);

        using var serviceProvider = _services.BuildServiceProvider();

        var policyProvider = serviceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.NotNull(policyProvider);
        Assert.IsNotType<ShellAuthorizationPolicyProvider>(policyProvider);
    }

    private class TestApiFeature : IFastEndpointsShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }
    }

    private class TestEndpoint : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Get("/test");
            AllowAnonymous();
        }

        public override Task HandleAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
