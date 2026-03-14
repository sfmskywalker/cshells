# main Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-08

## Active Technologies
- C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`) + `Microsoft.Extensions.Configuration`, `System.Text.Json`, existing `CShells.Configuration` helpers and converters, FluentStorage JSON provider integration (002-feature-object-map)
- N/A at the feature level; shell definitions originate from configuration providers, in-memory/config models, and FluentStorage JSON blobs (002-feature-object-map)

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
- 002-feature-object-map: Added C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`) + `Microsoft.Extensions.Configuration`, `System.Text.Json`, existing `CShells.Configuration` helpers and converters, FluentStorage JSON provider integration

- 001-shell-reload-semantics: Added C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`) + `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, ASP.NET Core integration packages in adjacent projects, internal notification pipeline in `CShells.Notifications`

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
