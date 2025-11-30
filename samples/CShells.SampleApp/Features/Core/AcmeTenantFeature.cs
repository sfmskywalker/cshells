namespace CShells.SampleApp.Features.Core;

/// <summary>
/// Tenant-specific feature for Acme Corp that registers tenant information.
/// </summary>
[ShellFeature("AcmeTenant", DisplayName = "Acme Tenant Information")]
public class AcmeTenantFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ITenantInfo>(new TenantInfo
        {
            TenantId = "acme",
            TenantName = "Acme Corp",
            Tier = "Premium"
        });
    }
}
