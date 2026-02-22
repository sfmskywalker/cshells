using Microsoft.Extensions.DependencyInjection;

namespace CShells.Features;

/// <summary>
/// Optional extension to <see cref="IShellFeature"/> that is called once after
/// <b>all</b> feature <c>ConfigureServices</c> calls have completed but before
/// <see cref="IServiceProvider"/> is built for the shell.
/// </summary>
/// <remarks>
/// This is useful when a feature needs to inspect or finalize registrations that
/// may have been added or replaced by later-running dependent features.
/// A typical use-case is a feature that owns a single framework registration call
/// (e.g. <c>AddMassTransit</c>) that must incorporate configuration contributed
/// by features that run after it (e.g. a transport-specific feature).
/// </remarks>
public interface IPostConfigureShellServices
{
    /// <summary>
    /// Called after all shell features have run their <c>ConfigureServices</c> method.
    /// </summary>
    /// <param name="services">
    /// The fully-populated shell <see cref="IServiceCollection"/>, including all
    /// registrations from every enabled feature.
    /// </param>
    void PostConfigureServices(IServiceCollection services);
}

