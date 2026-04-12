# Feature Specification: Fluent Assembly Source Selection

**Feature Branch**: `003-fluent-assembly-selection`  
**Created**: 2026-04-11  
**Status**: Draft  
**Input**: User description: "Refactor AddShells so assembly scanning is configured fluently on the builder, support additive assembly source calls (including explicit host assemblies), keep default host-assembly behavior when no sources are specified, allow breaking changes, and include a naming-focused work item."

## Clarifications

### Session 2026-04-11

- Q: What should `FromHostAssemblies()` include? → A: Exactly the same assembly set that CShells currently scans by default when no assemblies are specified.
- Q: Once any assembly-source method is called, should implicit default host-derived scanning still apply? → A: No. Once any assembly-source method is called, discovery uses only explicitly configured sources; host-derived assemblies are included only by default when no calls are made or when `FromHostAssemblies()` is called.
- Q: How should multiple assembly-source calls compose? → A: All assembly-source calls concatenate their contributions. Multiple explicit assembly-list calls contribute all listed assemblies, and host-assembly source calls contribute the host-derived assembly set alongside them.
- Q: What abstraction should back fluent assembly-source configuration? → A: CShells should expose an assembly provider abstraction that supplies assemblies for discovery. The builder maintains a list of assembly provider registrations, including built-in host-derived and explicit-assembly registrations, and each fluent call appends another registration entry to that list.

### Session 2026-04-12

- Q: Should the new assembly provider abstraction be a supported public extension point for custom implementations? → A: Yes. Developers should be able to supply their own custom assembly provider implementations through a public builder API in addition to the built-in convenience methods.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure Feature Discovery Fluently (Priority: P1)

As an application developer, I configure feature discovery using only fluent builder calls so I can keep all shell setup logic in one readable chain while composing multiple assembly providers declaratively.

**Why this priority**: This is the primary behavior change and the main value of the enhancement.

**Independent Test**: Configure shells using fluent assembly source methods only, then verify expected feature types are discovered without passing assemblies as extra method arguments and that each call appends another source contribution.

**Acceptance Scenarios**:

1. **Given** a shell setup chain with one explicit assembly source call, **When** startup configuration completes, **Then** feature discovery uses that configured source.
2. **Given** a shell setup chain with multiple explicit assembly source calls, **When** startup configuration completes, **Then** feature discovery uses the concatenated contributions from all configured calls.
3. **Given** a shell setup chain with an explicit custom assembly source call and no host-assemblies source call, **When** startup configuration completes, **Then** discovery uses only the explicitly configured assembly set.
4. **Given** multiple fluent assembly source calls, **When** configuration is built, **Then** each call appends another assembly provider registration to the builder-managed provider list.

---

### User Story 2 - Explicitly Include Host Assemblies (Priority: P2)

As an application developer, I can explicitly include host application assemblies in the fluent chain so default behavior can be declared intentionally and combined with other provider-based sources.

**Why this priority**: It improves clarity and supports additive composition when host assemblies are needed alongside custom sources.

**Independent Test**: Configure one setup with no assembly source calls and another with an explicit host-assemblies call; verify both discover the same host-derived features.

**Acceptance Scenarios**:

1. **Given** no explicit assembly source configuration, **When** shells are initialized, **Then** host-assembly discovery still occurs by default.
2. **Given** an explicit host-assemblies source call, **When** shells are initialized, **Then** discovery includes the host-derived feature set.
3. **Given** explicit host-assemblies and custom assembly source calls together, **When** shells are initialized, **Then** discovery includes the union of both sources.
4. **Given** an explicit custom assembly source call without `FromHostAssemblies()`, **When** shells are initialized, **Then** host-derived assemblies are not included implicitly.
5. **Given** the out-of-the-box host-derived provider and explicit-assembly provider are both configured, **When** discovery runs, **Then** assemblies from both providers are scanned.

---

### User Story 3 - Extend Discovery with Custom Providers (Priority: P3)

As an application developer, I can append my own assembly provider implementations through the public builder API so I can plug in custom discovery sources without forking CShells.

**Why this priority**: The new provider abstraction is much more valuable if it is a supported extension point rather than an internal-only mechanism.

**Independent Test**: Register a custom assembly provider through the public builder API, then verify its contributed assemblies are included additively alongside built-in providers.

**Acceptance Scenarios**:

1. **Given** a custom assembly provider implementation, **When** it is appended through the public builder API, **Then** its assembly contribution is included in discovery.
2. **Given** built-in and custom providers are appended together, **When** discovery runs, **Then** CShells scans the deduplicated union of all contributed assemblies.
3. **Given** multiple custom provider inputs are appended, **When** configuration is built, **Then** each input is retained as a distinct provider registration in the builder-managed provider list.

---

### User Story 4 - Adopt Clear Naming for the New API Surface (Priority: P4)

As a maintainer, I can apply clear and mentally consistent names for this enhancement so developers understand intent without reading implementation details.

**Why this priority**: Naming quality strongly affects API discoverability, onboarding, and long-term maintainability.

**Independent Test**: Review the finalized naming set in a short design pass and verify each name is unambiguous, consistent, and reflected in developer-facing usage examples.

**Acceptance Scenarios**:

1. **Given** the new fluent assembly-source capability, **When** naming review is completed, **Then** one approved naming set is selected and used consistently across the exposed API.
2. **Given** obsolete naming candidates, **When** the enhancement is finalized, **Then** those candidates are not left in developer-facing API surface or examples.

### Edge Cases

- The same assembly is added multiple times across calls; duplicate processing must not alter discovery results.
- An explicit assembly source call is made with an empty set; configuration remains valid, contributes no assemblies, and does not re-enable implicit host-derived scanning.
- A null assembly source input is provided; configuration fails fast with a clear developer-facing error.
- A null custom provider instance or factory is supplied; configuration fails fast with a clear developer-facing error.
- A custom provider returns a null assembly sequence; discovery treats that as invalid instead of partially succeeding.
- `WithAssemblyProvider<TProvider>()` is used for a provider type that cannot be resolved from the root application service provider; discovery fails fast with a clear developer-facing error.
- Host assembly source is combined with custom sources in different call orders; resulting discovered feature set remains equivalent.
- The builder receives many assembly source calls; provider registrations are all retained in call order and none are silently replaced.
- A custom provider returns an assembly already contributed by another provider; discovery remains deduplicated.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The shell setup API MUST allow assembly-source configuration through fluent builder methods within the main configuration chain.
- **FR-002**: CShells MUST define an assembly provider abstraction, expressed as an interface, whose responsibility is to supply assemblies for feature discovery.
- **FR-003**: The fluent builder MUST maintain an internal ordered list of assembly provider registrations used to determine which assemblies CShells scans.
- **FR-004**: Each assembly-source fluent call MUST append one or more assembly provider registrations to that builder-maintained list and MUST NOT replace previously configured registrations.
- **FR-005**: The shell setup API MUST support multiple assembly-source method calls and concatenate their provider contributions into one aggregate discovery set.
- **FR-006**: The system MUST provide a built-in assembly provider that returns the same host-derived assembly set that CShells currently scans by default when no assemblies are specified.
- **FR-007**: The system MUST provide a built-in assembly provider that accepts an explicit set of assemblies through its creation inputs and returns that exact configured set.
- **FR-008**: The system MUST provide a fluent convenience method for adding explicit developer-supplied assemblies as feature discovery sources, and assemblies supplied across multiple calls MUST all be included.
- **FR-009**: The system MUST provide a fluent convenience method for explicitly adding host-application assemblies as feature discovery sources, and that host-derived assembly set MUST be included additively alongside other configured sources.
- **FR-010**: The system MUST provide a public builder API for appending custom assembly provider implementations, and the generic custom-provider entry point MUST resolve the provider from the root application service provider.
- **FR-011**: Custom assembly provider implementations appended through the public builder API MUST participate additively in the same provider list and discovery flow as the built-in providers, and provider evaluation MUST receive the root application service provider used during shell registration.
- **FR-012**: `FromHostAssemblies()` MUST include exactly the same assembly set that CShells currently scans by default when no assemblies are specified.
- **FR-013**: If no assembly-source methods are called, the system MUST preserve current default behavior of scanning that same host-derived assembly set.
- **FR-014**: Once any assembly-source method is called, discovery MUST use only explicitly configured providers and the assemblies they contribute.
- **FR-015**: When explicit, host-derived, and custom providers are combined, discovery MUST use the deduplicated union of all contributed assemblies.
- **FR-016**: The previous non-fluent assembly argument approach MUST be removed; maintaining backward compatibility with legacy overloads is out of scope and prohibited.
- **FR-017**: Null assembly-source inputs, null custom provider registrations, null custom provider factories, and null custom provider output sequences MUST be rejected with clear guidance; empty explicit inputs MUST be accepted as zero-contribution additions that still participate in explicit-source mode.
- **FR-018**: The enhancement MUST include a naming decision work item that results in clean, mentally consistent terminology for the provider abstraction, builder members, fluent methods, and custom-provider entry point.
- **FR-019**: Developer-facing usage guidance and examples for shell setup MUST reflect the new fluent assembly-source pattern, the additive provider model, and the public custom-provider extension point only.

### Key Entities *(include if feature involves data)*

- **Assembly Source Set**: The aggregate set of assemblies configured for feature discovery, built from one or more provider contributions and deduplicated before discovery.
- **IFeatureAssemblyProvider**: The public abstraction CShells uses to obtain assemblies for discovery from different source types.
- **Assembly Provider List**: The builder-maintained ordered list of assembly provider registrations accumulated through fluent configuration calls.
- **HostFeatureAssemblyProvider**: The built-in provider that returns the same host-derived assembly set CShells currently uses by default.
- **ExplicitFeatureAssemblyProvider**: The built-in provider that returns a specific assembly set supplied during its creation.
- **Custom `IFeatureAssemblyProvider` Implementation**: Any developer-supplied implementation of the provider interface appended through the public builder API.
- **Naming Decision Record**: The accepted terminology for this enhancement, including selected method names and rejected alternatives with rationale.

### Assumptions

- Developers may combine source directives in any order and expect equivalent discovery results.
- Existing default host-derived discovery behavior is considered correct, and `FromHostAssemblies()` is expected to target that exact same assembly set through `HostFeatureAssemblyProvider`.
- Calling any assembly-source method switches discovery into explicit-source mode, so host-derived assemblies are no longer included unless the host assemblies provider is explicitly appended.
- Each assembly-source call contributes through one or more provider registrations, and the final discovery set is built by concatenating all provider contributions before deduplication.
- Custom providers are a supported public extension mechanism and are expected to follow the same additive and deduplicated discovery semantics as built-in providers.
- Breaking changes are intentionally acceptable for this enhancement; migration support for legacy overloads is not required.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In acceptance testing, 100% of documented shell setup examples express assembly discovery through fluent builder calls without trailing assembly arguments.
- **SC-002**: In validation scenarios, 100% of feature types expected from combined provider contributions are discovered exactly once.
- **SC-003**: In side-by-side verification, default (no explicit source directives) and explicit host-source configuration produce equivalent host-derived discovery outcomes in all defined test scenarios.
- **SC-004**: In builder-behavior verification, every assembly-source fluent call results in an added provider entry and no previously configured provider entries are lost.
- **SC-005**: In extension-point verification, custom providers appended through the public builder API contribute assemblies successfully in 100% of defined test scenarios.
- **SC-006**: During internal API review, all new assembly-source and provider-related names are approved in one naming decision record with no unresolved naming conflicts.
