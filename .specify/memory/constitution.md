<!--
  Sync Impact Report
  ===================
  Version change: 1.0.2 → 1.1.0
  Modified principles:
    - None
  Added sections:
    - VII. Lifecycle & Concurrency Contracts (new principle)
  Removed sections:
    - None
  Templates requiring updates:
    - .specify/templates/plan-template.md ✅ (Constitution Check is dynamic; picks up new principle automatically)
    - .specify/templates/spec-template.md ✅ (no principle references)
    - .specify/templates/tasks-template.md ✅ (no principle references)
  Follow-up TODOs:
    - Ensure implementation of 006-shell-drain-lifecycle applies Principle VII throughout
-->

# CShells Constitution

## Core Principles

### I. Abstraction-First Architecture

Every new public interface or consumer-extensibility contract MUST be
defined in a dedicated `*.Abstractions` project before implementation.
Implementations MUST reside in a separate project that references the
abstractions.

- Abstractions projects (`CShells.Abstractions`,
  `CShells.AspNetCore.Abstractions`) MUST depend only on
  `Microsoft.Extensions.*` abstractions — never on concrete frameworks.
- Internal framework-only interface seams that are not intended for
  external consumption MAY remain in implementation projects when doing
  so preserves a simpler public API surface.
- Framework-owned public notification message records MAY remain in
  implementation projects when they are emitted by the framework,
  consumed via existing notification abstractions, and are not intended
  to define third-party implementation contracts.
- Feature libraries SHOULD reference only abstractions packages, keeping
  them lightweight and decoupled from the full framework.
- Each shell MUST have its own isolated DI container. Root-level services
  are copied into shell containers; features register shell-scoped
  services via `IShellFeature.ConfigureServices`.
- Constructor dependencies in features are resolved from the root
  `IServiceProvider` — features MUST NOT depend on shell-scoped services
  in their constructors.

### II. Feature Modularity

Functionality MUST be delivered as features that implement
`IShellFeature` (services only) or `IWebShellFeature` (services +
endpoints). Features are the unit of composition.

- Features are discovered via reflection from loaded assemblies.
  The `[ShellFeature]` attribute is optional — use it only when an
  explicit name, display name, or dependency declaration is needed.
- Feature dependencies MUST be declared via the `DependsOn` property
  (string names or `typeof()` references). The framework resolves
  dependencies using topological ordering; circular dependencies MUST
  NOT exist.
- Features MUST be self-contained: a feature's services, endpoints,
  and configuration belong to that feature alone. Cross-feature
  coupling MUST go through shared abstractions or dependency
  declarations, never direct type references to another feature's
  internals.
- Shell configuration MUST be declarative — driven by
  `appsettings.json`, external JSON files, or the code-first fluent
  API. Feature sets per shell MUST be expressible as configuration
  without code changes.

### III. Modern C# Style

All code MUST target .NET 10 (C# 14) and use the latest language
features consistently.

- **Nullable reference types**: MUST be enabled (`<Nullable>enable</Nullable>`).
  Use `?` annotations for genuinely nullable values; non-null is the default.
- **Implicit usings**: MUST be enabled. Do not add explicit `using`
  directives for implicitly imported namespaces.
- **File-scoped namespaces**: MUST be used in every file
  (`namespace CShells;` not `namespace CShells { }`).
- **`var`**: MUST be used whenever the type is apparent from context.
  Explicit types are acceptable only for disambiguation or API clarity.
- **Primary constructors**: SHOULD be used for DI injection. Guard all
  injected parameters with `Guard.Against.Null(...)` and assign to
  private readonly fields.
- **Expression-bodied members**: MUST be used for single-expression
  methods and properties.
- **Collection expressions**: MUST be used for initialization
  (`= []`, `[..items]`) instead of `new List<T>()` or `Array.Empty<T>()`.
- **Naming**: PascalCase for public members, types, and namespaces.
  Private fields use `_camelCase` (underscore prefix). Interfaces use
  `I` prefix. No other prefixes or Hungarian notation.
- **Access modifiers**: MUST always be explicit — no implicit `private`.
- **`internal sealed`**: SHOULD be applied to implementation classes that
  are not intended for external inheritance.

### IV. Explicit Error Handling

Errors MUST be surfaced clearly. Silent failures are forbidden.

- Guard clauses (`Guard.Against.Null`, `Guard.Against.NullOrWhiteSpace`,
  `Guard.Against.NullOrEmpty`) MUST be used at public method entry
  points for parameter validation. Use `[CallerArgumentExpression]` for
  automatic parameter name propagation.
- Custom domain exceptions (e.g., `FeatureNotFoundException`,
  `FeatureConfigurationValidationException`) MUST carry structured
  context — the feature name, dependent feature name, or error list —
  to aid debugging.
- Exception messages MUST include actionable guidance (e.g.,
  "Did you forget to reference the assembly?").
- Swallowing exceptions without logging or rethrowing is NOT permitted.

### V. Test Coverage

New functionality MUST be accompanied by tests. The codebase uses xUnit
exclusively.

- **Framework**: xUnit with `Assert.*` static methods. No third-party
  assertion libraries.
- **Organization**: Unit tests in `tests/CShells.Tests/Unit/` mirroring
  the `src/` structure. Integration tests in
  `tests/CShells.Tests/Integration/` grouped by subsystem. End-to-end
  tests in `tests/CShells.Tests.EndToEnd/`.
- **Naming**: Test methods follow `{Method}_{Scenario}_{ExpectedResult}`.
  Use `[Fact(DisplayName = "...")]` and
  `[Theory(DisplayName = "...")]` for readable output.
- **Parameterization**: Use `[Theory]` with `[InlineData]` or
  `[MemberData]` for data-driven tests.
- Tests MUST pass before a PR is merged. The CI pipeline runs
  `dotnet test` on every push to `main`.

### VI. Simplicity & Minimalism

Complexity MUST be justified. Prefer the simplest design that meets
current requirements.

- Do not add abstractions, helpers, or layers for hypothetical future
  needs. Features, services, and types are introduced only when a
  concrete requirement demands them.
- Fluent builder APIs (e.g., `CShellsBuilder`, `ShellBuilder`) are the
  preferred pattern for composable configuration — but MUST NOT be
  introduced where a simple method call suffices.
- XML doc comments with `<summary>`, `<param>`, `<returns>`, and
  `<remarks>` are required on all public API types and members. Use
  `<remarks>` for architectural constraints that are not obvious from
  the signature. Do not add comments to non-public or trivial code.
- Changes MUST be focused and minimal. A bug fix does not include
  surrounding refactors. A feature does not include speculative
  extensibility.

### VII. Lifecycle & Concurrency Contracts

Shell state machines and shared mutable state MUST be correct under
concurrent access. Async coordination primitives replace lock-based
approaches wherever async work is involved.

- **Monotonic state machines**: Shell lifecycle states MUST only advance
  forward (e.g., Active → Deactivating → Draining → Drained → Disposed).
  Backward transitions MUST NOT be possible; any attempt MUST be a
  no-op or throw, never silently corrupt state.
- **Async-safe serialization**: State transitions and registry mutations
  MUST be serialized using async-compatible locks (e.g.,
  `SemaphoreSlim(1,1)`) — never `lock()` around async paths.
- **Idempotent concurrent operations**: When the same logical operation
  (e.g., drain, promote) is initiated concurrently for the same entity,
  all callers MUST receive the same in-flight handle. Duplicate
  operations MUST NOT be started.
- **Cancellation discipline**: Every public async API MUST accept and
  propagate a `CancellationToken`. Long-running internal operations
  (drain handlers, policy callbacks) MUST observe cancellation promptly
  and return rather than throwing `OperationCanceledException` unless
  the caller must be notified of cancellation explicitly.
- **Event subscriber isolation**: When fan-out event notification is
  used, exceptions thrown by one subscriber MUST be caught, logged, and
  swallowed so they cannot block other subscribers or disrupt the
  triggering state transition. Subscriber registration and removal MUST
  be thread-safe.
- **Disposal ordering**: Resources that depend on a shell's
  `IServiceProvider` MUST be fully wound down (via drain) before
  `DisposeAsync` is called on the provider. Disposing a provider while
  services resolved from it are still in active use is NOT permitted.

## Technology Stack & Constraints

- **Runtime**: .NET 10 (C# 14, `LangVersion` latest). Source projects
  multi-target `net8.0;net9.0;net10.0`; test and sample projects target
  `net10.0` only.
- **Build**: MSBuild with Central Package Management
  (`Directory.Packages.props`). All package versions are managed
  centrally — individual `.csproj` files MUST NOT specify versions.
- **CI/CD**: GitHub Actions (`publish.yml`). Versioning is derived from
  `src/Directory.Build.props` `<Version>` tag; release builds use the
  git tag, main-branch pushes produce `-preview.{run}` suffixes.
- **NuGet**: Eight packages published per release. Each src project
  includes a per-project `README.md` embedded in the NuGet package.
- **Testing framework**: xUnit 2.x (`xunit`, `xunit.runner.visualstudio`,
  `Microsoft.NET.Test.Sdk`, `coverlet.collector`).
- **Third-party dependencies**: FastEndpoints (adapter), FluentStorage
  (provider), Swashbuckle (sample). New third-party dependencies MUST
  be justified and added via `Directory.Packages.props`.

## Development Workflow

- **Branching**: Feature branches off `main`. PRs MUST pass CI
  (build + test) before merge.
- **PR scope**: Keep changes focused and minimal. Include tests for new
  functionality. Update documentation (docs/, wiki/, README) when
  adding or changing public-facing behavior.
- **Build commands**:
  - `dotnet build` — build the solution.
  - `dotnet test` — run all tests.
  - `dotnet test tests/CShells.Tests/` — run unit + integration tests.
  - `cd samples/CShells.Workbench && dotnet run` — run the sample app.
- **Documentation**: Markdown docs live in `docs/` and `wiki/`.
  Per-project READMEs in each `src/` project. The root `README.md` is
  the primary entry point for users.
- **Code review**: All PRs MUST verify compliance with this
  constitution. Complexity additions MUST be justified in the PR
  description.

## Governance

This constitution is the authoritative reference for CShells coding
standards, architecture decisions, and development practices. It
supersedes ad-hoc conventions or undocumented habits.

- **Amendments**: Any change to this constitution MUST be documented
  with a version bump, rationale, and sync impact report (prepended as
  an HTML comment). Use semantic versioning: MAJOR for principle
  removals/redefinitions, MINOR for new principles or material
  expansions, PATCH for clarifications and wording fixes.
- **Compliance**: All PRs and code reviews MUST verify adherence to the
  principles above. Violations MUST be flagged and resolved before
  merge.
- **Runtime guidance**: `.github/copilot-instructions.md` provides
  AI-assistant-specific guidance and MUST remain consistent with this
  constitution. Update it when principles change.
- **Conflict resolution**: If a principle conflicts with a practical
  requirement, document the deviation in the PR with a justification.
  Recurring deviations signal that the constitution needs amendment.

**Version**: 1.1.0 | **Ratified**: 2026-03-08 | **Last Amended**: 2026-04-22
