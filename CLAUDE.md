# main Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-04-22

## Active Technologies
- C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests + `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Hosting.Abstractions`, `Ardalis.GuardClauses`; no new third-party packages (007-blueprint-provider-split)
- N/A — in-memory registry; providers own their own storage (code, configuration, or blob via `FluentStorage`) (007-blueprint-provider-split)

- C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests + `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Ardalis.GuardClauses`; all pinned via `Directory.Packages.props` (006-shell-drain-lifecycle)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests

## Code Style

C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests: Follow standard conventions

## Recent Changes
- 007-blueprint-provider-split: Added C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests + `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Hosting.Abstractions`, `Ardalis.GuardClauses`; no new third-party packages

- 006-shell-drain-lifecycle: Added C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests + `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Ardalis.GuardClauses`; all pinned via `Directory.Packages.props`

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
