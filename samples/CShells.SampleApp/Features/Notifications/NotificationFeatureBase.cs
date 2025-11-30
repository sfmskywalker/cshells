using CShells.AspNetCore;
using CShells.AspNetCore.Features;
using CShells.SampleApp.Features.Core;

namespace CShells.SampleApp.Features.Notifications;

/// <summary>
/// Base class for notification features that exposes the /notifications endpoint.
/// </summary>
public abstract class NotificationFeatureBase : IWebShellFeature
{
    public abstract void ConfigureServices(IServiceCollection services);

    public void Configure(IApplicationBuilder app, IHostEnvironment? environment)
    {
        // Expose /notifications endpoint
        app.Map("/notifications", notificationsApp =>
        {
            notificationsApp.Run(async context =>
            {
                // Only accept POST requests
                if (!context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 405; // Method Not Allowed
                    return;
                }

                // Parse request body
                var request = await context.Request.ReadFromJsonAsync<NotificationRequest>();
                if (request == null)
                {
                    context.Response.StatusCode = 400; // Bad Request
                    await context.Response.WriteAsJsonAsync(new { Error = "Invalid request body" });
                    return;
                }

                var tenantInfo = context.RequestServices.GetRequiredService<ITenantInfo>();

                // For tenants with multi-channel notifications, get all available services
                var notificationServices = context.RequestServices.GetServices<INotificationService>().ToList();

                if (!notificationServices.Any())
                {
                    context.Response.StatusCode = 500; // Internal Server Error
                    await context.Response.WriteAsJsonAsync(new { Error = "No notification service available" });
                    return;
                }

                var results = new List<object>();

                foreach (var service in notificationServices)
                {
                    var result = await service.SendAsync(request.Recipient, request.Message);
                    results.Add(new
                    {
                        Channel = service.Channel,
                        Result = result
                    });
                }

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    Tenant = tenantInfo.TenantName,
                    ChannelsUsed = notificationServices.Select(s => s.Channel).ToArray(),
                    Results = results
                });
            });
        });
    }
}

/// <summary>
/// Notification request DTO.
/// </summary>
public record NotificationRequest(string Recipient, string Message);
