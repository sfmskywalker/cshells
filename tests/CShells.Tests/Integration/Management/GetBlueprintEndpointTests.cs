using System.Net;
using CShells.Management.Api.Models;

namespace CShells.Tests.Integration.Management;

/// <summary>
/// Integration tests for <c>GET /{name}/blueprint</c> (US3 blueprint portion). Verifies the
/// endpoint returns the registered blueprint with <c>ConfigurationData</c> verbatim
/// (FR-012a) and does not trigger activation as a side effect.
/// </summary>
public class GetBlueprintEndpointTests
{
    [Fact(DisplayName = "GetBlueprint of a registered name returns features and ConfigurationData verbatim")]
    public async Task GetBlueprint_RegisteredName_Returns200_WithFeaturesAndConfigurationData()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("acme", s => s
                .WithConfiguration("WebRouting:Path", "acme")
                .WithConfiguration("Plan", "Enterprise")));

        var bp = await fixture.GetJsonAsync<BlueprintResponse>("/admin/acme/blueprint");
        Assert.NotNull(bp);
        Assert.Equal("acme", bp.Name);
        Assert.Contains("WebRouting:Path", bp.ConfigurationData.Keys);
        Assert.Contains("Plan", bp.ConfigurationData.Keys);
        Assert.Equal("acme", bp.ConfigurationData["WebRouting:Path"].ToString());
        Assert.Equal("Enterprise", bp.ConfigurationData["Plan"].ToString());
    }

    [Fact(DisplayName = "GetBlueprint does not activate the shell as a side effect")]
    public async Task GetBlueprint_DoesNotActivateShell()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("never-activated", _ => { }));

        var bp = await fixture.GetJsonAsync<BlueprintResponse>("/admin/never-activated/blueprint");
        Assert.NotNull(bp);

        Assert.Null(fixture.Registry.GetActive("never-activated"));
    }

    [Fact(DisplayName = "GetBlueprint of an unknown name returns 404")]
    public async Task GetBlueprint_UnknownName_Returns404()
    {
        await using var fixture = new ManagementApiFixture(c => c.AddShell("known", _ => { }));

        var response = await fixture.GetAsync("/admin/does-not-exist/blueprint");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
