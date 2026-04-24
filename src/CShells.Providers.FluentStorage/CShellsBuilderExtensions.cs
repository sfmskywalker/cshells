using System.Text.Json;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using FluentStorage.Blobs;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Providers.FluentStorage;

/// <summary>
/// Extension methods that register FluentStorage-backed shell blueprints.
/// </summary>
public static class CShellsBuilderExtensions
{
    /// <param name="builder">The CShells builder.</param>
    extension(CShellsBuilder builder)
    {
        /// <summary>
        /// Registers a FluentStorage blueprint provider that serves JSON blobs as shell
        /// blueprints. The same instance implements <see cref="IShellBlueprintManager"/>, so
        /// create / update / delete operations through <see cref="IShellRegistry"/> are routed
        /// back to the blob store.
        /// </summary>
        /// <remarks>
        /// All I/O is async end-to-end — blueprints are resolved lazily on
        /// <see cref="IShellBlueprintProvider.GetAsync"/>, not at DI registration. No
        /// sync-over-async, no container-build blocking.
        /// </remarks>
        public CShellsBuilder WithFluentStorageBlueprints(
            IBlobStorage blobStorage,
            string? path = null,
            JsonSerializerOptions? jsonOptions = null)
        {
            Guard.Against.Null(builder);
            Guard.Against.Null(blobStorage);

            var provider = new FluentStorageShellBlueprintProvider(blobStorage, path, jsonOptions);

            // Register once; consumed by the composite provider factory (reads via
            // IShellBlueprintProvider) and by anything that directly requests the manager.
            builder.Services.AddSingleton(provider);
            builder.Services.AddSingleton<IShellBlueprintManager>(provider);
            builder.AddBlueprintProvider(_ => provider);

            return builder;
        }

        /// <summary>
        /// Overload that defers <see cref="IBlobStorage"/> creation to the caller's callback.
        /// The callback runs synchronously at registration time; blob I/O itself is async.
        /// </summary>
        public CShellsBuilder WithFluentStorageBlueprints(
            Func<IBlobStorage> configureBlobStorage,
            string? path = null,
            JsonSerializerOptions? jsonOptions = null)
        {
            Guard.Against.Null(builder);
            Guard.Against.Null(configureBlobStorage);

            return builder.WithFluentStorageBlueprints(configureBlobStorage(), path, jsonOptions);
        }
    }
}
