using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Management.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CShells.Tests.Integration.Management;

/// <summary>
/// Spins up a minimal in-memory <see cref="WebApplication"/> with CShells configured + the
/// management API mapped under <c>/admin</c>. Used by every endpoint integration test class
/// in this folder.
/// </summary>
internal sealed class ManagementApiFixture : IAsyncDisposable
{
    private readonly WebApplication _app;

    public ManagementApiFixture(
        Action<CShellsBuilder>? configureCShells = null,
        Action<RouteGroupBuilder>? conventions = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // Authentication + authorization registered eagerly so test classes can chain
        // RequireAuthorization on the management route group. The default scheme is a no-op
        // handler that always returns NoResult; requests therefore have no identity and the
        // default policy denies them — exactly the unauthenticated-401 path the
        // auth-passthrough tests assert.
        builder.Services
            .AddAuthentication(NoIdentityAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, NoIdentityAuthHandler>(NoIdentityAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddCShells(c =>
        {
            c.WithAssemblies();
            configureCShells?.Invoke(c);
        });

        _app = builder.Build();
        _app.UseAuthorization();
        var group = _app.MapShellManagementApi("/admin");
        conventions?.Invoke(group);

        _app.StartAsync().GetAwaiter().GetResult();

        Client = _app.GetTestClient();
        Registry = _app.Services.GetRequiredService<IShellRegistry>();
    }

    public HttpClient Client { get; }

    public IShellRegistry Registry { get; }

    public IServiceProvider Services => _app.Services;

    public async Task<T?> GetJsonAsync<T>(string path) =>
        await Client.GetFromJsonAsync<T>(path, JsonOptions);

    public async Task<HttpResponseMessage> GetAsync(string path) =>
        await Client.GetAsync(path);

    public async Task<HttpResponseMessage> PostAsync(string path) =>
        await Client.PostAsync(path, content: null);

    public async Task<T?> PostJsonAsync<T>(string path)
    {
        var response = await Client.PostAsync(path, content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Always returns NoResult — every request is anonymous as far as the pipeline is concerned.</summary>
    private sealed class NoIdentityAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
    {
        public const string SchemeName = "Test-NoIdentity";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
