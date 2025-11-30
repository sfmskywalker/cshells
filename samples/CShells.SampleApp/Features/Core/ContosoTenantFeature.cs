namespace CShells.SampleApp.Features.Core;

/// <summary>
/// Tenant-specific feature for Contoso Ltd that registers tenant information.
/// </summary>
[ShellFeature("ContosoTenant", DisplayName = "Contoso Tenant Information")]
public class ContosoTenantFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ITenantInfo>(new TenantInfo
        {
            TenantId = "contoso",
            TenantName = "Contoso Ltd",
            Tier = "Enterprise"
        });
    }
}
