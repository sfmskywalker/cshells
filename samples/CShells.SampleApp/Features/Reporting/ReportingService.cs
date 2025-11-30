using CShells.SampleApp.Features.Core;

namespace CShells.SampleApp.Features.Reporting;

/// <summary>
/// Reporting service implementation.
/// </summary>
public class ReportingService : IReportingService
{
    private readonly IAuditLogger _logger;
    private readonly ITenantInfo _tenantInfo;

    public ReportingService(IAuditLogger logger, ITenantInfo tenantInfo)
    {
        _logger = logger;
        _tenantInfo = tenantInfo;
    }

    public TransactionReport GenerateTransactionReport(DateTime startDate, DateTime endDate)
    {
        _logger.LogInfo($"Generating transaction report for {_tenantInfo.TenantName} from {startDate:d} to {endDate:d}");

        // Simulate report generation with mock data
        var random = new Random();
        var totalTransactions = random.Next(50, 500);
        var totalAmount = random.Next(10000, 100000);

        return new()
        {
            StartDate = startDate,
            EndDate = endDate,
            TotalTransactions = totalTransactions,
            TotalAmount = totalAmount,
            Currency = "USD",
            TransactionsByProcessor = new()
            {
                ["Stripe"] = random.Next(20, totalTransactions / 2),
                ["PayPal"] = random.Next(10, totalTransactions / 2)
            }
        };
    }
}
