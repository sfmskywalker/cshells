# Quickstart: Blueprint-Aware Path Routing

**Feature**: [010-blueprint-aware-routing](spec.md)
**Audience**: host developers upgrading to the CShells release that ships this feature.

The headline change for host developers: **routing now works without `PreWarmShells`.** The route index makes blueprints visible to `WebRoutingShellResolver` whether or not they've been activated, and `ShellMiddleware` activates them lazily on first matching request — exactly as feature `007`'s lazy-activation model promised.

## 1. The expected adoption diff

For most hosts the only change is **deleting one line**:

```diff
 builder.AddShells(shells => shells
     .WithHostAssemblies()
     .WithConfigurationProvider(configuration)
     .WithWebRouting(o => o.EnablePathRouting = true)
     .WithAuthenticationAndAuthorization()
-    .PreWarmShells("Default")
     .ConfigureAllShells(shell => shell.WithFeatures(typeof(MyFeature))));
```

That's it. Path-routed requests will activate `Default` (or any other configured shell) on first hit. `PreWarmShells` is preserved as a perf hint for hosts that want to absorb activation latency at startup; it's no longer required for correctness.

## 2. The cold-start request: what now happens

Before this feature, a host with no `PreWarmShells` call returned 404 for every request:

```
$ curl -i https://localhost:5001/elsa/api/identity/login
HTTP/1.1 404 Not Found
```

After this feature, the same host:

```
$ curl -i https://localhost:5001/elsa/api/identity/login
HTTP/1.1 200 OK
{"isAuthenticated":true,"accessToken":"…"}
```

Logs show:

```
info: CShells.Lifecycle.ShellLifecycleLogger
      Shell Default#1 transitioned Initializing → Active
info: CShells.AspNetCore.Notifications.ShellEndpointRegistrationHandler
      Registering endpoints for active shell 'Default#1'
…
```

The first request pays the activation cost; subsequent requests reuse the active generation as before.

To see per-request match decisions, set `WebRoutingShellResolverOptions.LogMatches = true`
and lower the `CShells.AspNetCore.Resolution.WebRoutingShellResolver` category to `Debug`;
no-match requests already log at `Information` (with `HeaderValue` / `ClaimValue` redacted).

## 3. Reload behaviour: the post-reload 404 is gone

Before this feature, calling `POST /elsa/api/shells/reload` drained the active generation and left the registry empty; the next request 404'd until a process restart:

```
$ curl -X POST https://localhost:5001/elsa/api/shells/reload  -H "Authorization: Bearer …"
HTTP/1.1 200 OK
{"status":"Completed"}

$ curl https://localhost:5001/elsa/api/identity/users  -H "Authorization: Bearer …"
HTTP/1.1 404 Not Found
```

After this feature, the next request lazily activates the new generation:

```
$ curl -X POST https://localhost:5001/elsa/api/shells/reload  -H "Authorization: Bearer …"
HTTP/1.1 200 OK

$ curl https://localhost:5001/elsa/api/identity/users  -H "Authorization: Bearer …"
HTTP/1.1 200 OK
[…users…]
```

You can call `/shells/reload` arbitrarily many times; each is followed by a clean lazy re-activation on the next matched request.

## 4. When you DO want `PreWarmShells`

Pre-warming is still useful when:

- The shell has a slow first-time initialization (e.g., a feature configures its services from a remote system at first access) and you want to absorb that latency at startup rather than on the first user request.
- You want startup to fail loudly if a shell can't be built. Pre-warming surfaces blueprint errors at host startup; lazy activation surfaces them only on the first matching request.
- A CI smoke test wants to assert that all configured shells can be activated.

```csharp
builder.AddShells(shells => shells
    .WithHostAssemblies()
    .WithConfigurationProvider(configuration)
    .PreWarmShells("Default", "telemetry")  // <-- still works exactly as before
    .ConfigureAllShells(shell => shell.WithFeatures(typeof(MyFeature))));
```

The startup log will show `pre-warming 2 shell(s)` and the listed shells will be `Active` before the host opens its listening socket.

## 5. Diagnostics: when a request *doesn't* match

Today, an unmatched request fails silently — the resolver returns `null`, the request 404s, no log line fires. After this feature, the resolver emits a single structured log entry:

```
info: CShells.AspNetCore.Resolution.WebRoutingShellResolver
      No shell matched for request: Path=/wat, Host=localhost, Header(X-Shell)=null,
      Claim(tenant)=null. Considered 3 candidate blueprints: [Default(Path=""),
      acme(Path="acme"), contoso(Path="contoso")]
```

When the catalogue has more than `WebRoutingShellResolverOptions.NoMatchLogCandidateCap` blueprints (default: 10), the entry includes `(+N more)` rather than the full list.

To enable per-match logs (off by default to avoid log spam in production):

```csharp
builder.AddShells(shells => shells
    .WithWebRouting(o =>
    {
        o.EnablePathRouting = true;
        o.LogMatches = true;          // ← Debug-level "Resolved 'X' (mode: Y)" per request
    }));
```

## 6. Custom resolver strategies: the one-keyword migration

If you implement `IShellResolverStrategy`, the signature changes from sync to async:

```diff
 public class MyCustomResolver : IShellResolverStrategy
 {
-    public ShellId? Resolve(ShellResolutionContext context)
+    public Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken)
     {
-        return MyResolutionLogic(context);
+        return Task.FromResult<ShellId?>(MyResolutionLogic(context));
     }
 }
```

If your resolver does real async work (e.g., calls a database), you can drop any `GetAwaiter().GetResult()` or `.Result` patterns and `await` properly.

If your resolver wants to participate in blueprint-aware routing for its own routing modes, inject `IShellRouteIndex` and call `TryMatchAsync` with custom criteria — the same surface `WebRoutingShellResolver` uses.

## 7. The route index, briefly

For curious readers (you don't need to think about this to use the feature):

- The route index is a singleton in DI (`IShellRouteIndex`), populated from `IShellBlueprintProvider`.
- Path-by-name routing pays at most **one** `provider.GetAsync(name)` per cold blueprint per host process. There's no upfront catalogue scan.
- Root-path / Host / Header / Claim routing populates a small in-memory snapshot **on first use of those modes**. Subsequent lookups are O(1). The snapshot updates incrementally on `ShellAdded`/`ShellRemoved`/`ShellReloaded` lifecycle events.
- Hosts using only path-by-name routing pay zero startup cost regardless of catalogue size — `007`'s 100k-tenant scaling promise is preserved.
- The snapshot is published via `Volatile.Write` of an immutable structure; concurrent reads always observe a consistent state.

For full design details see [`research.md`](research.md), [`data-model.md`](data-model.md), and [`contracts/IShellRouteIndex.md`](contracts/IShellRouteIndex.md).
