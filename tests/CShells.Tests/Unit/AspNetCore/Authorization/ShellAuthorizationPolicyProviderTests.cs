using CShells.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CShells.Tests.AspNetCore.Authorization;

public class ShellAuthorizationPolicyProviderTests
{
    private const string ShellPolicyName = "epPolicy:Test.Endpoint";
    private readonly HttpContextAccessor _httpContextAccessor = new();
    private readonly ShellAuthorizationPolicyProvider _provider;
    private readonly AuthorizationPolicy _shellPolicy;

    public ShellAuthorizationPolicyProviderTests()
    {
        var rootOptions = new AuthorizationOptions();
        _provider = new(Options.Create(rootOptions), _httpContextAccessor);

        _shellPolicy = new AuthorizationPolicyBuilder()
            .RequireClaim("permissions", "read:test")
            .Build();

        var shellOptions = new AuthorizationOptions();
        shellOptions.AddPolicy(ShellPolicyName, _shellPolicy);

        var shellServices = new ServiceCollection()
            .AddSingleton<IOptions<AuthorizationOptions>>(Options.Create(shellOptions))
            .BuildServiceProvider();

        _httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = shellServices
        };
    }

    [Fact]
    public async Task GetPolicyAsync_UsesShellAuthorizationOptions_WhenShellProviderIsUnavailable()
    {
        var policy = await _provider.GetPolicyAsync(ShellPolicyName);

        Assert.Same(_shellPolicy, policy);
    }
}
