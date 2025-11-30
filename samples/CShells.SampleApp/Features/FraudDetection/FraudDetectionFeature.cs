using CShells.AspNetCore;
using CShells.SampleApp.Features.Core;

namespace CShells.SampleApp.Features.FraudDetection;

/// <summary>
/// Fraud detection feature - a premium feature available only to premium/enterprise tenants.
/// Exposes /fraud-check endpoint.
/// </summary>
[ShellFeature("FraudDetection", DependsOn = ["Core"], DisplayName = "Fraud Detection")]
public class FraudDetectionFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IFraudDetectionService, FraudDetectionService>();
    }

    public void Configure(IApplicationBuilder app, IHostEnvironment? environment)
    {
        // Expose /fraud-check endpoint
        app.Map("/fraud-check", fraudCheckApp =>
        {
            fraudCheckApp.Run(async context =>
            {
                // Only accept POST requests
                if (!context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 405; // Method Not Allowed
                    return;
                }

                // Parse request body
                var request = await context.Request.ReadFromJsonAsync<FraudCheckRequest>();
                if (request == null)
                {
                    context.Response.StatusCode = 400; // Bad Request
                    await context.Response.WriteAsJsonAsync(new { Error = "Invalid request body" });
                    return;
                }

                var tenantInfo = context.RequestServices.GetRequiredService<ITenantInfo>();
                var fraudDetection = context.RequestServices.GetRequiredService<IFraudDetectionService>();

                var result = fraudDetection.AnalyzeTransaction(request.Amount, request.Currency, request.IpAddress);

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    Tenant = tenantInfo.TenantName,
                    Analysis = result
                });
            });
        });
    }
}

/// <summary>
/// Fraud check request DTO.
/// </summary>
public record FraudCheckRequest(decimal Amount, string Currency, string IpAddress);
