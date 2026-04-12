# Data Model: Fluent Builder Naming Matrix

## Fluent Builder Naming Matrix

Represents the decision table that maps an assembly-discovery builder responsibility to the approved verb family that must remain in the repository.

### Fields

- `Responsibility`: the builder responsibility being named, such as source selection or provider attachment
- `ApprovedVerbFamily`: the required leading verb family, such as `From*` or `With*`
- `IntentStatement`: the plain-language rule that explains why the family applies
- `ApprovedExamples`: the shipped examples that currently satisfy the rule
- `RejectedPatterns`: the competing verb families or aliases that violate the rule

### Validation Rules

- Source-selection responsibilities must use the `From*` family for this feature.
- Provider-attachment responsibilities must use the `With*` family for this feature.
- The matrix must remain stable for the approved assembly-discovery surface throughout feature 004 implementation.
- New exceptions or alternate verb families are out of scope and require a separate approved feature.

## Approved Naming Surface

Represents the public assembly-discovery method groups that must stay shipped and protected.

### Fields

- `ContainingType`: the public type that exposes the methods, currently `CShellsBuilderExtensions`
- `ApprovedSourceSelectionMethods`: `FromAssemblies(...)` and `FromHostAssemblies()`
- `ApprovedProviderAttachmentMethodFamily`: `WithAssemblyProvider(...)`
- `AllowedOverloadShapes`: the supported overload variations that may share the approved name
- `PublicVisibility`: whether the surface is publicly reachable from the supported package entry points

### Validation Rules

- The approved source-selection names must remain `FromAssemblies(...)` and `FromHostAssemblies()`.
- The approved provider-attachment family must remain `WithAssemblyProvider(...)` for all supported overloads.
- The surface must not gain competing public replacement names such as `WithAssemblies`, `WithHostAssemblies`, `AddAssemblies`, or `AddHostAssemblies` for the same responsibilities.
- Overload count may change only if the approved `WithAssemblyProvider(...)` family name is preserved.

## Candidate Name Evaluation

Represents the recorded review outcome for one candidate fluent builder name.

### Fields

- `CandidateName`: the proposed or observed public name under review
- `Responsibility`: the assembly-discovery responsibility the candidate attempts to represent
- `ObservedVerbFamily`: the family implied by the candidate name
- `Outcome`: `Approved` or `Rejected`
- `Rationale`: why the candidate preserves or violates the matrix
- `RegressionRisk`: whether the candidate would be a harmless observation, a conflicting alias, or a breaking replacement

### State Transitions

- `Observed` → `Approved`: the candidate matches the approved matrix and the fixed repository baseline
- `Observed` → `Rejected`: the candidate conflicts with the matrix, introduces drift, or weakens terminology

### Validation Rules

- The required evaluation set must include `FromAssemblies`, `FromHostAssemblies`, `WithAssemblies`, `WithHostAssemblies`, `AddAssemblies`, `AddHostAssemblies`, and `WithAssemblyProvider`.
- Every evaluation must record both an outcome and a rationale.
- Rejected candidates must explain whether the problem is wrong verb family, weaker intent, or competing public aliasing.

## Naming Guardrail

Represents the repository-level verification that protects the approved naming surface.

### Fields

- `GuardrailType`: the verification approach, expected to be a focused xUnit public-surface test or equivalent repository-native check
- `InspectionTarget`: the API surface or files being validated
- `ApprovedMethodFamilies`: the method groups that must be present
- `RejectedMethodNames`: the method names that must not appear as competing public entry points
- `AllowedVariations`: the overload or signature differences that are acceptable while retaining the approved family
- `FailureMessageExpectation`: the review signal produced when drift is detected

### State Transitions

- `Planned` → `Implemented`: the guardrail is added to the repository
- `Implemented` → `Passing`: the approved naming surface is intact
- `Implemented` → `Failing`: a required name is missing or a rejected competing name is introduced

### Validation Rules

- The guardrail must pass when `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)` remain present.
- The guardrail must fail if the approved names are removed, renamed, or replaced by rejected competing names for the same responsibilities.
- The guardrail must not fail merely because `WithAssemblyProvider(...)` has multiple valid overloads.

## Developer-Facing Guidance Asset

Represents one in-scope doc, sample, or comment source that teaches the assembly-discovery builder vocabulary.

### Fields

- `AssetPath`: the file path of the doc, sample, or code comment host
- `AssetType`: `README`, `Doc`, `Wiki`, `Sample`, or `XMLComment`
- `ExpectedApprovedNames`: the names that should appear when the asset describes assembly discovery
- `ExpectedMatrixExplanation`: whether the asset should mention source selection versus provider attachment
- `ReviewStatus`: `Unreviewed`, `Aligned`, or `NeedsUpdate`

### State Transitions

- `Unreviewed` → `Aligned`: the asset already uses the approved names and rationale
- `Unreviewed` → `NeedsUpdate`: the asset contains stale or conflicting terminology
- `NeedsUpdate` → `Aligned`: the terminology is corrected without unrelated rewriting

### Validation Rules

- Assets that describe assembly discovery must use `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)` consistently.
- Assets must not teach rejected competing names for in-scope responsibilities.
- Only assets already describing assembly discovery are in scope for this feature.

## Prior-Art Feature Context

Represents the earlier feature set that established the approved naming direction now being protected.

### Fields

- `ReferenceFeature`: `003-fluent-assembly-selection`
- `ReferenceArtifacts`: the 003 plan, research, quickstart, and contracts that established the baseline
- `ApprovedBaseline`: the approved names inherited by 004
- `AlignmentOutcome`: whether 004 preserves, refines, or overturns the prior-art baseline

### Validation Rules

- Feature 004 must treat 003 as prior art, not a competing design path.
- The approved baseline inherited from 003 must remain preserved for 004.
- Any attempt to overturn the 003-approved names is out of scope for this feature.

