using CShells.Features;

namespace CShells.SampleApp.Features.PaymentProcessing;

/// <summary>
/// Payment processing feature using PayPal.
/// Exposes /payments endpoint via base class.
/// </summary>
[ShellFeature("PayPalPayment", DependsOn = ["Core"], DisplayName = "PayPal Payment Processing")]
public class PayPalPaymentFeature : PaymentProcessingFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPaymentProcessor, PayPalPaymentProcessor>();
    }
}
