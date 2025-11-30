using CShells.SampleApp.Features.Core;

namespace CShells.SampleApp.Features.PaymentProcessing;

/// <summary>
/// Stripe payment processor implementation.
/// </summary>
public class StripePaymentProcessor : IPaymentProcessor
{
    private readonly IAuditLogger _logger;

    public StripePaymentProcessor(IAuditLogger logger)
    {
        _logger = logger;
    }

    public string ProcessorName => "Stripe";

    public PaymentResult ProcessPayment(decimal amount, string currency)
    {
        _logger.LogInfo($"Processing ${amount} {currency} payment via Stripe");

        // Simulate payment processing
        var transactionId = $"stripe_{Guid.NewGuid():N}";

        return new()
        {
            Success = true,
            TransactionId = transactionId,
            ProcessorName = ProcessorName,
            Message = "Payment processed successfully via Stripe"
        };
    }
}
