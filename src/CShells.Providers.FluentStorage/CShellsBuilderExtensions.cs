using System.Text.Json;
using CShells.DependencyInjection;
using FluentStorage.Blobs;

namespace CShells.Providers.FluentStorage;

/// <summary>
/// Extension methods for configuring FluentStorage-based shell settings provider.
/// </summary>
public static class CShellsBuilderExtensions
{
    /// <param name="builder">The CShells builder.</param>
    extension(CShellsBuilder builder)
    {
        /// <summary>
        /// Configures CShells to use FluentStorage for reading shell configurations from blob storage.
        /// </summary>
        /// <param name="blobStorage">The blob storage instance to read shell configurations from.</param>
        /// <param name="path">The path/prefix within the blob storage where shell JSON files are located. If null, reads from root.</param>
        /// <param name="jsonOptions">Optional JSON serialization options. If null, uses default options with case-insensitive property names.</param>
        /// <returns>The updated CShells builder.</returns>
        public CShellsBuilder WithFluentStorageProvider(IBlobStorage blobStorage,
            string? path = null,
            JsonSerializerOptions? jsonOptions = null)
        {
            Guard.Against.Null(builder);
            Guard.Against.Null(blobStorage);

            var provider = new FluentStorageShellSettingsProvider(blobStorage, path, jsonOptions);
            return builder.WithProvider(provider);
        }

        /// <summary>
        /// Configures CShells to use FluentStorage for reading shell configurations from blob storage.
        /// </summary>
        /// <param name="configureBlobStorage">A callback to configure the blob storage instance.</param>
        /// <param name="path">The path/prefix within the blob storage where shell JSON files are located. If null, reads from root.</param>
        /// <param name="jsonOptions">Optional JSON serialization options. If null, uses default options with case-insensitive property names.</param>
        /// <returns>The updated CShells builder.</returns>
        public CShellsBuilder WithFluentStorageProvider(Func<IBlobStorage> configureBlobStorage,
            string? path = null,
            JsonSerializerOptions? jsonOptions = null)
        {
            Guard.Against.Null(builder);
            Guard.Against.Null(configureBlobStorage);

            var blobStorage = configureBlobStorage();
            return builder.WithFluentStorageProvider(blobStorage, path, jsonOptions);
        }
    }
}
