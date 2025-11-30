namespace CShells.SampleApp.Features.Core;

/// <summary>
/// Simple console audit logger implementation.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly ITenantInfo _tenantInfo;

    public AuditLogger(ITenantInfo tenantInfo)
    {
        _tenantInfo = tenantInfo;
    }

    public void LogInfo(string message)
    {
        Console.WriteLine($"[{DateTime.UtcNow:u}] [{_tenantInfo.TenantId}] {message}");
    }
}
