# CShells Workbench - Multi-Tenant Payment Platform

This sample application demonstrates CShells' multi-tenancy capabilities through a realistic payment processing SaaS platform scenario. It showcases how to build a modular, feature-driven application where different tenants get different service implementations and features based on their subscription tier.

## Scenario

The sample implements a payment processing platform where different tenants (customers) have access to different features and service implementations based on their subscription tier.

### Tenants

#### 1. Default (Basic Tier) - `/`
- **Payment Processor**: Stripe
- **Notifications**: Email
- **Features**: Core, StripePayment, EmailNotification

#### 2. Acme Corp (Premium Tier) - `/acme/*`
- **Payment Processor**: PayPal
- **Notifications**: SMS
- **Premium Feature**: Fraud Detection
- **Features**: Core, PayPalPayment, SmsNotification, FraudDetection

#### 3. Contoso Ltd (Enterprise Tier) - `/contoso/*`
- **Payment Processor**: Stripe
- **Notifications**: Email + SMS (Multi-channel)
- **Premium Features**: Fraud Detection, Reporting
- **Features**: Core, StripePayment, MultiChannelNotification, FraudDetection, Reporting

## Key Concepts Demonstrated

### 1. **Feature-Based Architecture**
Each feature is self-contained with its own services and can be composed into different shells:
- `Core` - Shared services (audit logging, tenant info)
- `PaymentProcessing` - Interface with Stripe/PayPal implementations
- `Notifications` - Interface with Email/SMS implementations
- `FraudDetection` - Premium feature for risk analysis
- `Reporting` - Enterprise feature with endpoint exposure

### 2. **Multi-Tenant Service Resolution**
Different tenants get different implementations of the same service interface:
- Default and Contoso use `StripePaymentProcessor`
- Acme uses `PayPalPaymentProcessor`
- Default uses `EmailNotificationService`
- Acme uses `SmsNotificationService`
- Contoso uses both (multi-channel)

### 3. **Tier-Based Feature Access**
Premium features are only available to certain tenants:
- Basic tier: Core payment and notification services
- Premium tier: Adds fraud detection
- Enterprise tier: Adds reporting with custom endpoints

### 4. **Feature-Owned Endpoints**
All endpoints are exposed by features via `IWebShellFeature`, demonstrating true modularity:
- `CoreFeature` - Exposes `/` (tenant info)
- `PaymentProcessingFeatureBase` - Exposes `/payments` (inherited by Stripe/PayPal features)
- `NotificationFeatureBase` - Exposes `/notifications` (inherited by Email/SMS features)
- `FraudDetectionFeature` - Exposes `/fraud-check` (premium only)
- `ReportingFeature` - Exposes `/reports` (enterprise only)

**Program.cs is now ultra-clean** - it only contains shell configuration, no business logic!

## API Endpoints

### All Tenants

**GET /** - Get tenant information
```bash
curl http://localhost:5000/
curl http://localhost:5000/acme
curl http://localhost:5000/contoso
```

**POST /payments** - Process a payment
```bash
curl -X POST http://localhost:5000/payments \
  -H "Content-Type: application/json" \
  -d '{"amount": 100, "currency": "USD", "customerEmail": "customer@example.com"}'

curl -X POST http://localhost:5000/acme/payments \
  -H "Content-Type: application/json" \
  -d '{"amount": 100, "currency": "USD", "customerEmail": "customer@example.com"}'
```

**POST /notifications** - Send notifications
```bash
curl -X POST http://localhost:5000/notifications \
  -H "Content-Type: application/json" \
  -d '{"recipient": "user@example.com", "message": "Your payment was successful"}'
```

### Premium/Enterprise Only

**POST /fraud-check** - Analyze transaction for fraud (Acme, Contoso only)
```bash
curl -X POST http://localhost:5000/acme/fraud-check \
  -H "Content-Type: application/json" \
  -d '{"amount": 15000, "currency": "USD", "ipAddress": "203.0.113.42"}'
```

### Enterprise Only

**GET /reports** - Generate transaction report (Contoso only)
```bash
curl "http://localhost:5000/contoso/reports?startDate=2024-01-01&endDate=2024-12-31"
```

## Shell Configuration

Each tenant is configured as a shell via JSON files in the `Shells` folder. CShells loads these at startup using FluentStorage:

- `Shells/Default.json` - Basic tier configuration
- `Shells/Acme.json` - Premium tier configuration
- `Shells/Contoso.json` - Enterprise tier configuration

Example shell configuration (Acme.json):
```json
{
  "name": "Acme",
  "features": ["Core", "PayPalPayment", "SmsNotification", "FraudDetection"],
  "properties": {
    "CShells.AspNetCore.Path": "acme"
  }
}
```

The `CShells.AspNetCore.Path` property determines the URL path prefix for the shell.

## Project Structure

This sample demonstrates the **recommended approach** for organizing CShells applications with separate feature libraries:

```
samples/
├── CShells.Workbench/                       # Main ASP.NET Core application
│   ├── CShells.Workbench.csproj             # References: CShells, CShells.AspNetCore, CShells.Workbench.Features
│   ├── Program.cs                           # Ultra-clean - only shell configuration
│   ├── Shells/                              # Shell configuration files
│   │   ├── Default.json
│   │   ├── Acme.json
│   │   └── Contoso.json
│   └── Features/                            # Additional features defined in main project
│       ├── PaymentProcessing/
│       ├── Notifications/
│       ├── FraudDetection/
│       └── Reporting/
└── CShells.Workbench.Features/              # Feature library (separate project)
    ├── CShells.Workbench.Features.csproj    # References: CShells.AspNetCore.Abstractions only
    └── Core/                                # Shared services & tenant info endpoint
        ├── CoreFeature.cs                   # IWebShellFeature - exposes / endpoint
        ├── ITenantInfo.cs                   # Tenant information interface
        ├── IAuditLogger.cs                  # Audit logging interface
        └── ITimeService.cs                  # Time service interface
```

### Key Architecture Points

**CShells.Workbench.Features** - The feature library:
- References **only** `CShells.AspNetCore.Abstractions` (lightweight, no implementation dependencies)
- Contains the `CoreFeature` that all shells depend on
- Demonstrates how to build reusable features that can be shared across projects
- Shows the recommended pattern: feature definitions in a separate class library

**CShells.Workbench** - The main application:
- References the full `CShells` and `CShells.AspNetCore` packages
- References the `CShells.Workbench.Features` library
- Contains additional features for demonstration purposes (in the Features/ folder)
- Ultra-clean `Program.cs` with only shell configuration

This separation allows you to:
- Build feature libraries with minimal dependencies
- Share features across multiple applications
- Keep feature code independent of framework implementation details
- Maintain clean boundaries between abstractions and implementations
```

## Program.cs Configuration

The application uses the simplified `AddShells()` API, which reads from `appsettings.json` by default:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddShells(); // Reads from appsettings.json "CShells" section

var app = builder.Build();
app.MapShells(); // Configures middleware and endpoints
app.Run();
```

To use FluentStorage instead (reading from Shells folder), you would use:

```csharp
var shellsPath = Path.Combine(builder.Environment.ContentRootPath, "Shells");
var blobStorage = StorageFactory.Blobs.DirectoryFiles(shellsPath);

builder.AddShells(cshells =>
{
    cshells.WithFluentStorageProvider(blobStorage);
});
```

## Running the Sample

```bash
cd samples/CShells.Workbench
dotnet run
```

Then visit:
- `https://localhost:5001/swagger` - Swagger UI to explore all endpoints
- `https://localhost:5001/` - Default tenant
- `https://localhost:5001/acme` - Acme Corp tenant
- `https://localhost:5001/contoso` - Contoso Ltd tenant

## Learning Points

1. **IWebShellFeature**: Features expose their own endpoints via `MapEndpoints()` - no code in Program.cs
2. **Multi-tenant service resolution**: Different tenants get different `IPaymentProcessor` implementations
3. **Feature composition**: Shells are composed of features defined via JSON configuration
4. **Path-based routing**: `CShells.AspNetCore.Path` property controls URL routing
5. **Tier-based features**: Premium and Enterprise features are only enabled for specific tenants
6. **Endpoint inheritance**: Base feature classes expose endpoints, concrete features register services
7. **Clean Program.cs**: All business logic lives in features, Program.cs is minimal
8. **Graceful degradation**: Optional dependencies via `GetService()` (e.g., fraud detection in payments)
