using CShells.AspNetCore;
using CShells.Providers.FluentStorage;
using FluentStorage;

var builder = WebApplication.CreateBuilder(args);

// Register CShells services and configure multi-tenant shell resolution.
// This sample demonstrates a payment processing SaaS platform where different tenants
// have different features and service implementations loaded from JSON files in the Shells folder.
//
// All endpoints are now exposed by the features themselves via IWebShellFeature:
// - Core: Exposes / (tenant info)
// - PaymentProcessing: Exposes /payments
// - Notifications: Exposes /notifications
// - FraudDetection: Exposes /fraud-check (premium feature)
// - Reporting: Exposes /reports (enterprise feature)
//
// Shell configurations are loaded from the Shells folder using FluentStorage's disk provider.
// Each JSON file (Default.json, Acme.json, Contoso.json) represents a shell configuration.

// Configure FluentStorage to read shell configurations from the Shells folder
var shellsPath = Path.Combine(builder.Environment.ContentRootPath, "Shells");
var blobStorage = StorageFactory.Blobs.DirectoryFiles(shellsPath);

// Register CShells with FluentStorage provider and configure shell resolution
builder.AddCShells(cshells =>
{
    // Load shell settings from FluentStorage.
    cshells.WithFluentStorageProvider(blobStorage);
    
    // Register shell resolution builder with path mappings
    var resolutionBuilder = new CShells.ShellResolutionBuilder();
    resolutionBuilder.MapPath("", "Default");      // Default tenant - Basic tier
    resolutionBuilder.MapPath("acme", "Acme");      // Acme Corp - Premium tier
    resolutionBuilder.MapPath("contoso", "Contoso"); // Contoso Ltd - Enterprise tier
    cshells.Services.AddSingleton(resolutionBuilder.Build());
}, assemblies: [typeof(Program).Assembly]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Enable shell resolution middleware - resolves tenant based on request path
// and activates the appropriate features with their endpoints.
app.UseCShells();

app.Run();