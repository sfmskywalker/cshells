namespace CShells.SampleApp.Features.Core;

/// <summary>
/// Tenant-specific feature for Default tenant that registers tenant information.
/// </summary>
[ShellFeature("DefaultTenant", DisplayName = "Default Tenant Information")]
public class DefaultTenantFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ITenantInfo>(new TenantInfo
        {
            TenantId = "default",
            TenantName = "Default Tenant",
            Tier = "Basic"
        });
    }
}
