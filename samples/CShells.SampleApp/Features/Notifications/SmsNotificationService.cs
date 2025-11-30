using CShells.SampleApp.Features.Core;

namespace CShells.SampleApp.Features.Notifications;

/// <summary>
/// SMS-based notification service implementation.
/// </summary>
public class SmsNotificationService : INotificationService
{
    private readonly IAuditLogger _logger;

    public SmsNotificationService(IAuditLogger logger)
    {
        _logger = logger;
    }

    public string Channel => "SMS";

    public Task<NotificationResult> SendAsync(string recipient, string message)
    {
        _logger.LogInfo($"Sending SMS to {recipient}: {message}");

        // Simulate SMS sending
        var messageId = $"sms_{Guid.NewGuid():N}";

        return Task.FromResult(new NotificationResult
        {
            Success = true,
            Channel = Channel,
            MessageId = messageId
        });
    }
}
