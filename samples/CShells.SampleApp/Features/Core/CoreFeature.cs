using CShells.AspNetCore;
using CShells.AspNetCore.Features;
using CShells.Features;

namespace CShells.SampleApp.Features.Core;

/// <summary>
/// Core feature that registers fundamental services and exposes tenant information endpoint.
/// </summary>
[ShellFeature("Core", DisplayName = "Core Services")]
public class CoreFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<ITimeService, TimeService>();
    }

    public void Configure(IApplicationBuilder app, IHostEnvironment? environment)
    {
        // Expose root endpoint that shows tenant information
        app.Map("", homeApp =>
        {
            homeApp.Run(async context =>
            {
                var tenantInfo = context.RequestServices.GetRequiredService<ITenantInfo>();

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    Tenant = tenantInfo.TenantName,
                    TenantId = tenantInfo.TenantId,
                    Tier = tenantInfo.Tier,
                    Message = "Welcome to the Payment Processing Platform"
                });
            });
        });
    }
}
