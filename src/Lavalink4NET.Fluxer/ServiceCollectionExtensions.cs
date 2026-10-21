using Fluxer.Net;
using Fluxer.Net.Voice;
using Lavalink4NET.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Lavalink4NET.Fluxer;

/// <summary>
/// Registers Lavalink4NET for Fluxer.Net bots.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Lavalink4NET with a single node and the Fluxer audio bridge. Requires a <see cref="FluxerClient"/> in
    /// the container.
    /// </summary>
    public static IServiceCollection AddLavalink(this IServiceCollection services, Action<FluxerAudioOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLavalink<FluxerClientWrapper>();
        return services.AddFluxerAudio(configure);
    }

    /// <summary>
    /// Adds the Fluxer voice client and the audio bridge, for use with another Lavalink4NET registration such as
    /// <c>AddLavalinkCluster&lt;FluxerClientWrapper&gt;()</c>. Requires a <see cref="FluxerClient"/> in the
    /// container.
    /// </summary>
    public static IServiceCollection AddFluxerAudio(this IServiceCollection services, Action<FluxerAudioOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure != null)
            services.Configure(configure);

        services.TryAddSingleton(provider => new FluxerVoiceClient(provider.GetRequiredService<FluxerClient>())
        {
            QueueDuration = provider.GetRequiredService<IOptions<FluxerAudioOptions>>().Value.QueueDuration
        });
        services.AddHostedService<FluxerAudioBridge>();
        return services;
    }
}
