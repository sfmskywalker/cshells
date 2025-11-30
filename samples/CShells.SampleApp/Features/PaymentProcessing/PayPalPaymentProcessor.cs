using CShells.SampleApp.Features.Core;

namespace CShells.SampleApp.Features.PaymentProcessing;

/// <summary>
/// PayPal payment processor implementation.
/// </summary>
public class PayPalPaymentProcessor : IPaymentProcessor
{
    private readonly IAuditLogger _logger;

    public PayPalPaymentProcessor(IAuditLogger logger)
    {
        _logger = logger;
    }

    public string ProcessorName => "PayPal";

    public PaymentResult ProcessPayment(decimal amount, string currency)
    {
        _logger.LogInfo($"Processing ${amount} {currency} payment via PayPal");

        // Simulate payment processing with PayPal-specific logic
        var transactionId = $"pp_{Guid.NewGuid():N}";

        return new()
        {
            Success = true,
            TransactionId = transactionId,
            ProcessorName = ProcessorName,
            Message = "Payment processed successfully via PayPal"
        };
    }
}
