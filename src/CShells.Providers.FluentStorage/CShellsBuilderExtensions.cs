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
        /// Loads shell blueprints eagerly from blob storage and registers each as an
        /// <see cref="IShellBlueprint"/>. Each JSON blob at the configured path becomes one
        /// blueprint; reloads re-bind against a cached in-memory configuration snapshot built
        /// from the blob contents at load time.
        /// </summary>
        /// <remarks>
        /// For scenarios that need "fresh read on every reload" the host can register a
        /// custom <see cref="IShellBlueprint"/> whose <c>ComposeAsync</c> re-opens the blob;
        /// the default loader snapshot is sufficient for most use cases.
        /// </remarks>
        public CShellsBuilder WithFluentStorageBlueprints(
            IBlobStorage blobStorage,
            string? path = null,
            JsonSerializerOptions? jsonOptions = null)
        {
            Guard.Against.Null(builder);
            Guard.Against.Null(blobStorage);

            var loader = new FluentStorageShellBlueprintLoader(blobStorage, path, jsonOptions);
            var blueprints = loader.LoadAsync().GetAwaiter().GetResult();

            foreach (var blueprint in blueprints)
                builder.Services.AddSingleton(blueprint);

            return builder;
        }

        /// <summary>
        /// Overload that defers <see cref="IBlobStorage"/> creation to the caller's callback.
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
