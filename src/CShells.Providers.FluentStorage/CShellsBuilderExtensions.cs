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
        /// Registers an <see cref="IShellBlueprintProvider"/> that enumerates JSON blobs at the
        /// given path asynchronously during host startup. Each blob yields a
        /// <see cref="FluentStorageShellBlueprint"/> that re-opens and re-deserializes the blob
        /// on every <see cref="IShellBlueprint.ComposeAsync"/> call — so reloads observe blob
        /// updates without a host restart.
        /// </summary>
        /// <remarks>
        /// The provider does its I/O inside the startup hosted service's <c>StartAsync</c>, not
        /// during DI registration — no sync-over-async, no container-build blocking, no
        /// deadlocks in synchronization-context environments.
        /// </remarks>
        public CShellsBuilder WithFluentStorageBlueprints(
            IBlobStorage blobStorage,
            string? path = null,
            JsonSerializerOptions? jsonOptions = null)
        {
            Guard.Against.Null(builder);
            Guard.Against.Null(blobStorage);

            builder.Services.AddSingleton<IShellBlueprintProvider>(
                new FluentStorageShellBlueprintProvider(blobStorage, path, jsonOptions));

            return builder;
        }

        /// <summary>
        /// Overload that defers <see cref="IBlobStorage"/> creation to the caller's callback.
        /// The callback is invoked synchronously at registration time; the blob I/O itself
        /// (listing + deserializing) still runs asynchronously at host start.
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
