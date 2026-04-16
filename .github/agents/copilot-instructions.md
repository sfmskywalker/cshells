# main Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-08

## Active Technologies
- C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`) + `Microsoft.Extensions.Configuration`, `System.Text.Json`, existing `CShells.Configuration` helpers and converters, FluentStorage JSON provider integration (002-feature-object-map)
- N/A at the feature level; shell definitions originate from configuration providers, in-memory/config models, and FluentStorage JSON blobs (002-feature-object-map)
- C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`) + `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.DependencyModel`, `Microsoft.AspNetCore.Builder`, existing `CShells.Features` discovery code, `CShells.DependencyInjection` builder extensions, and ASP.NET Core registration helpers (003-fluent-assembly-selection)
- C# 14 on .NET 10 for implementation, multi-targeted source projects (`net8.0;net9.0;net10.0`), xUnit for tests, and Markdown planning artifacts + Existing runtime seams in `src/CShells/Hosting/DefaultShellHost.cs`, `src/CShells/Management/DefaultShellManager.cs`, `src/CShells/Configuration/ShellSettingsCache.cs`, `src/CShells/Features/FeatureDiscovery.cs`, `src/CShells.Abstractions/Features/IFeatureAssemblyProvider.cs`, `src/CShells.AspNetCore/Resolution/WebRoutingShellResolver.cs`, `src/CShells/Resolution/DefaultShellResolverStrategy.cs`, `src/CShells.AspNetCore/Notifications/ShellEndpointRegistrationHandler.cs`, and `src/CShells.AspNetCore/Routing/DynamicShellEndpointDataSource.cs` (005-deferred-shell-activation)
- In-memory desired-state and applied-runtime records sourced from `IShellSettingsProvider`; no new external persistence is required for this feature (005-deferred-shell-activation)

- C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`) + `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, ASP.NET Core integration packages in adjacent projects, internal notification pipeline in `CShells.Notifications` (001-shell-reload-semantics)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`)

## Code Style

C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`): Follow standard conventions

## Recent Changes
- 005-deferred-shell-activation: Added C# 14 on .NET 10 for implementation, multi-targeted source projects (`net8.0;net9.0;net10.0`), xUnit for tests, and Markdown planning artifacts + Existing runtime seams in `src/CShells/Hosting/DefaultShellHost.cs`, `src/CShells/Management/DefaultShellManager.cs`, `src/CShells/Configuration/ShellSettingsCache.cs`, `src/CShells/Features/FeatureDiscovery.cs`, `src/CShells.Abstractions/Features/IFeatureAssemblyProvider.cs`, `src/CShells.AspNetCore/Resolution/WebRoutingShellResolver.cs`, `src/CShells/Resolution/DefaultShellResolverStrategy.cs`, `src/CShells.AspNetCore/Notifications/ShellEndpointRegistrationHandler.cs`, and `src/CShells.AspNetCore/Routing/DynamicShellEndpointDataSource.cs`
- 003-fluent-assembly-selection: Added C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`) + `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.DependencyModel`, `Microsoft.AspNetCore.Builder`, existing `CShells.Features` discovery code, `CShells.DependencyInjection` builder extensions, and ASP.NET Core registration helpers
- 002-feature-object-map: Added C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`) + `Microsoft.Extensions.Configuration`, `System.Text.Json`, existing `CShells.Configuration` helpers and converters, FluentStorage JSON provider integration


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
