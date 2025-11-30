using CShells.SampleApp.Features.Core;

namespace CShells.SampleApp.Features.FraudDetection;

/// <summary>
/// Fraud detection service implementation.
/// </summary>
public class FraudDetectionService(IAuditLogger logger) : IFraudDetectionService
{
    public FraudAnalysisResult AnalyzeTransaction(decimal amount, string currency, string ipAddress)
    {
        logger.LogInfo($"Analyzing transaction: ${amount} {currency} from {ipAddress}");

        // Simulate fraud detection logic
        var flags = new List<string>();
        var riskScore = 0.0;

        // Simple rules for demonstration
        if (amount > 10000)
        {
            flags.Add("High transaction amount");
            riskScore += 0.3;
        }

        if (ipAddress.StartsWith("192.168"))
        {
            flags.Add("Local IP address");
            riskScore += 0.1;
        }
        else
        {
            flags.Add("External IP address");
            riskScore += 0.2;
        }

        var isSuspicious = riskScore > 0.5;

        return new()
        {
            IsSuspicious = isSuspicious,
            RiskScore = Math.Round(riskScore, 2),
            Flags = flags.ToArray(),
            Recommendation = isSuspicious
                ? "Manual review recommended"
                : "Transaction appears safe to process"
        };
    }
}
