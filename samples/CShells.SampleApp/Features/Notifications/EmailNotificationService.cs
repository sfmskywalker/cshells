using CShells.SampleApp.Features.Core;

namespace CShells.SampleApp.Features.Notifications;

/// <summary>
/// Email-based notification service implementation.
/// </summary>
public class EmailNotificationService : INotificationService
{
    private readonly IAuditLogger _logger;

    public EmailNotificationService(IAuditLogger logger)
    {
        _logger = logger;
    }

    public string Channel => "Email";

    public Task<NotificationResult> SendAsync(string recipient, string message)
    {
        _logger.LogInfo($"Sending email to {recipient}: {message}");

        // Simulate email sending
        var messageId = $"email_{Guid.NewGuid():N}";

        return Task.FromResult(new NotificationResult
        {
            Success = true,
            Channel = Channel,
            MessageId = messageId
        });
    }
}
