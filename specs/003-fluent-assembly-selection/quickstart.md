# Quickstart: Validate Fluent Assembly Source Selection

## Prerequisites

- The feature branch `003-fluent-assembly-selection` is checked out.
- The existing CShells solution builds and tests successfully before changes begin.
- You know which assemblies currently provide your shell features so you can express them through the new fluent builder API.

## 1. Introduce the public feature-assembly provider contract

- Add `IFeatureAssemblyProvider` to `src/CShells.Abstractions/Features/`.
- Keep the interface implementation-agnostic so third parties can provide custom discovery sources.
- Document the contract with XML comments because it is part of the supported public API.

## 2. Move assembly-source configuration onto `CShellsBuilder`

- Extend the core builder fluent surface with `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`.
- Store appended provider registrations on the builder in call order.
- Keep this provider list independent from the shell-settings provider pipeline.

## 3. Add built-in providers and aggregate discovery behavior

- Implement the built-in explicit-assemblies provider in `src/CShells/Features/`.
- Implement the built-in host-derived provider by reusing the existing host assembly resolution algorithm.
- Aggregate provider outputs additively and deduplicate assemblies before calling `FeatureDiscovery`.

## 4. Preserve default behavior while enabling explicit mode

- When no assembly-source fluent calls are made, continue scanning the current host-derived assembly set.
- Once any assembly-source call is made, use only explicitly configured providers.
- Require `FromHostAssemblies()` to opt host-derived assemblies back into explicit mode.
- Treat empty explicit-assemblies calls as valid zero-contribution providers that still activate explicit mode.

## 5. Remove the legacy assembly-argument API surface

- Remove direct assembly parameters from `AddCShells`, `AddCShellsAspNetCore`, and `AddShells` overloads.
- Update all in-repo examples to show the fluent builder pattern instead of trailing assembly arguments.
- Ensure migration examples map cleanly from the removed overloads to the new fluent calls.

## 6. Add tests for builder semantics and discovery outcomes

- Add unit tests proving each assembly-source call appends a provider registration.
- Add unit tests for null input rejection and empty explicit input acceptance.
- Add integration tests showing:
  - default behavior matches `FromHostAssemblies()`,
  - explicit custom or explicit assembly sources do not implicitly include host assemblies,
  - built-in and custom providers compose additively, and
  - duplicate assembly contributions are discovered only once.

## 7. Update developer guidance

- Update `README.md` examples that currently pass assemblies directly to `AddShells` or `AddCShells`.
- Update relevant docs in `docs/` and mirrored wiki content if it repeats the old pattern.
- Reflect the approved naming set from the naming decision record consistently.

## 8. Validate behavior

- Run focused unit and integration tests for the new builder and discovery semantics.
- Run the main CShells test project once focused tests pass.

```bash
dotnet test tests/CShells.Tests/
```

## Expected Outcomes

- Developers configure feature-discovery assemblies only through fluent builder calls.
- Default host-derived discovery remains unchanged when no assembly-source call is made.
- Any explicit assembly-source call switches CShells into explicit-provider mode.
- Host-derived, explicit, and custom providers compose additively and discover expected features exactly once.
- Public examples no longer advertise the removed assembly-argument overloads.

