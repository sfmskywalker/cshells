# Contract: Fluent Builder Naming Matrix

## Scope

This contract defines the implementation-backed naming obligations for the CShells assembly-discovery builder surface.

It governs:

- the shipped public method groups on `CShellsBuilderExtensions`,
- the regression checks that protect those method groups,
- the in-scope docs, samples, and comments that describe assembly-discovery configuration, and
- future review of changes that could introduce competing public names for the same responsibilities.

`specs/003-fluent-assembly-selection` remains the prior-art baseline. Feature `004-builder-verb-naming` preserves and protects that approved outcome in the repository.

## Naming Matrix

| Builder responsibility | Approved verb family | Use when | Approved examples | Rejection rule |
|---|---|---|---|---|
| Source selection | `From*` | The method tells CShells where feature discovery gets assemblies from. | `FromAssemblies(...)`, `FromHostAssemblies()` | Reject names that imply provider attachment, raw collection mutation, or weaker source-selection intent. |
| Provider attachment | `With*` | The method attaches a provider instance, factory, or provider type that contributes assemblies. | `WithAssemblyProvider(...)` | Reject names that misrepresent provider attachment as direct source selection or introduce alternative verbs for the same attachment role. |

## Approved Public Naming Surface

| Area | Approved name | Allowed shape | Why |
|---|---|---|---|
| Explicit assembly source | `FromAssemblies(...)` | Public method group on `CShellsBuilderExtensions` | Selects developer-supplied discovery sources and belongs to the `From*` family. |
| Host-derived assembly source | `FromHostAssemblies()` | Public method group on `CShellsBuilderExtensions` | Selects the built-in host-derived discovery source and belongs to the `From*` family. |
| Custom provider attachment | `WithAssemblyProvider(...)` | Public method family that may expose generic, instance, and factory overloads | Attaches provider-based extensibility and belongs to the `With*` family. |

## Required Candidate Evaluation

| Candidate | Outcome | Matrix fit | Repository implication |
|---|---|---|---|
| `FromAssemblies(...)` | Approved | Correct | Must remain publicly available. |
| `FromHostAssemblies()` | Approved | Correct | Must remain publicly available. |
| `WithAssemblies(...)` | Rejected | Incorrect | Must not be introduced as a public alias or replacement for source selection. |
| `WithHostAssemblies()` | Rejected | Incorrect | Must not be introduced as a public alias or replacement for host-derived source selection. |
| `AddAssemblies(...)` | Rejected | Weak | Must not be introduced as a public alias or replacement for explicit source selection. |
| `AddHostAssemblies()` | Rejected | Weak | Must not be introduced as a public alias or replacement for host-derived source selection. |
| `WithAssemblyProvider(...)` | Approved | Correct | Must remain the public provider-attachment family, including valid overload variations. |

## Verification Contract

1. The repository must contain automated or equivalently enforceable verification that the approved method groups remain present.
2. That verification must fail if an approved method name is removed, renamed, or replaced by a rejected competing public name for the same assembly-discovery responsibility.
3. Verification must treat `WithAssemblyProvider(...)` as one approved naming family and allow legitimate overload variations under that exact name.
4. Verification should live in the existing repository testing workflow rather than introducing heavyweight tooling unless a later feature explicitly approves that complexity.

## Guidance Alignment Contract

The following asset categories are in scope when they describe assembly discovery:

- root or package `README.md` files,
- `docs/` and `wiki/` guidance,
- sample application setup code,
- XML documentation or explanatory comments near public entry points.

When those assets mention assembly-discovery builder configuration, they must:

- use `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)` consistently,
- reflect `From*` for source selection and `With*` for provider attachment, and
- avoid teaching rejected alternatives such as `WithAssemblies`, `WithHostAssemblies`, `AddAssemblies`, or `AddHostAssemblies` as valid public names.

## Review Rules

- Preserve `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)` as the fixed approved terminology for feature `004-builder-verb-naming`.
- Do not approve a change that adds competing public aliases for the same assembly-discovery responsibilities, even if the approved names remain available.
- Keep implementation scope minimal: protect the current public surface, add regression guardrails, and align only already relevant guidance assets.
- Any effort to reconsider the approved names or expand the matrix to unrelated builder domains requires a separate approved feature.

