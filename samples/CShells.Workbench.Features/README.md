# CShells.Workbench.Features
Feature library for the CShells Workbench sample application — a multi-tenant blog platform.
## Features
| Feature      | Depends On | Description                                    |
|--------------|------------|------------------------------------------------|
| `Core`       | —          | Tenant identity + `GET /` info endpoint        |
| `Posts`      | Core       | Blog posts CRUD (`/posts`)                     |
| `Comments`   | Posts      | Reader comments (`/posts/{id}/comments`)       |
| `Analytics`  | Posts      | View-count analytics (`/analytics`)            |
## Tenant Plans
| Shell    | Path      | Features                              | Description              |
|----------|-----------|---------------------------------------|--------------------------|
| Default  | `/`       | Core, Posts                           | Free tier                |
| Acme     | `/acme`   | Core, Posts, Comments                 | Pro tier                 |
| Contoso  | `/contoso`| Core, Posts, Comments, Analytics      | Enterprise tier          |
## Key Concepts Demonstrated

- **`IWebShellFeature`**: All features — service registration + endpoint mapping
- **`[ShellFeature]`**: Feature metadata + `DependsOn` chains
- **`IConfigurableFeature<T>`**: `AnalyticsFeature` — typed per-shell options
- **`IShellActivatedHandler`**: `SeedPostsHandler` — per-shell startup work
- **Feature isolation**: Each shell gets its own in-memory data stores
