# Quickstart: Protect the Fluent Builder Naming Matrix

## Purpose

Use this guide when implementing feature `004-builder-verb-naming` so the repository keeps the approved assembly-discovery naming surface, adds durable regression protection, and only edits the guidance assets that already describe that surface.

## 1. Confirm the shipped public baseline

Inspect the current builder extension surface before planning any code changes.

- `FromAssemblies(...)` must remain the explicit source-selection entry point.
- `FromHostAssemblies()` must remain the host-derived source-selection entry point.
- `WithAssemblyProvider(...)` must remain the provider-attachment entry point, including any valid overload shapes already shipped.

Primary inspection target:

```bash
cd /Users/sipke/Projects/ValenceWorks/cshells/main
grep -n "FromAssemblies\|FromHostAssemblies\|WithAssemblyProvider" src/CShells/DependencyInjection/CShellsBuilderExtensions.cs
```

## 2. Add a focused naming guardrail in the existing xUnit suite

Implement the smallest repository-native verification that protects the public naming surface.

- Prefer a focused unit test in `tests/CShells.Tests/Unit/DependencyInjection/`.
- Verify the approved method groups are publicly present on `CShellsBuilderExtensions`.
- Allow multiple overloads under `WithAssemblyProvider(...)`.
- Fail if competing public names such as `WithAssemblies`, `WithHostAssemblies`, `AddAssemblies`, or `AddHostAssemblies` are introduced for the same assembly-discovery responsibilities.

## 3. Reuse current behavior tests as supporting regression coverage

Do not redesign assembly-discovery behavior for this feature.

- Keep the existing assembly-source semantics tests as the behavior baseline.
- Only extend integration coverage if the naming guardrail needs additional proof about approved overload usage.
- Avoid touching runtime behavior unless the guardrail exposes a real naming inconsistency.

## 4. Audit only the guidance assets that already describe assembly discovery

Review the small set of in-scope assets already identified by repository search:

- `README.md`
- `docs/getting-started.md`
- `docs/multiple-shell-providers.md`
- `src/CShells.AspNetCore/README.md`
- `src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs`
- `src/CShells.AspNetCore/Extensions/ShellExtensions.cs`
- `wiki/Getting-Started.md`
- `samples/CShells.Workbench/Program.cs`

For each asset:

- keep `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)` intact,
- reinforce `From*` for source selection and `With*` for provider attachment when explanation is present,
- avoid unrelated wording cleanup or broad example rewrites.

## 5. Record the required candidate outcomes

Keep the decision record explicit even though the public names are already approved.

- Approve: `FromAssemblies(...)`, `FromHostAssemblies()`, `WithAssemblyProvider(...)`
- Reject: `WithAssemblies(...)`, `WithHostAssemblies()`, `AddAssemblies(...)`, `AddHostAssemblies()`

If a future contributor wants to revisit those decisions, that work must happen in a separate approved feature rather than inside 004.

## 6. Run focused verification

After implementing the guardrail and any targeted guidance cleanup, run the relevant tests.

```bash
cd /Users/sipke/Projects/ValenceWorks/cshells/main
dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~CShellsBuilder"

dotnet test tests/CShells.Tests/
```

## Expected Outcome

- The repository keeps the approved public naming surface unchanged.
- Automated verification fails if the approved names drift or rejected competing names are introduced.
- In-scope docs, samples, and comments continue to teach the same `From*` versus `With*` matrix.
- Feature 004 remains minimal, implementation-backed, and free of unrelated renames.

