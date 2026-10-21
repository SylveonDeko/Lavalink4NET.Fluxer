namespace Lavalink4NET.Fluxer;

/// <summary>
/// Options for <see cref="FluxerAudioBridge"/>.
/// </summary>
public sealed class FluxerAudioOptions
{
    /// <summary>
    /// The Lavalink nodes to stream audio from. When empty, the node configured in Lavalink4NET's
    /// <see cref="AudioServiceOptions"/> is used.
    /// </summary>
    public List<FluxerAudioNode> Nodes { get; set; } = [];

    /// <summary>
    /// How much audio each voice connection buffers. Shorter makes skips and pauses take effect sooner.
    /// </summary>
    public TimeSpan QueueDuration { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long to wait before reconnecting to a node's audio stream after it drops.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>
/// A Lavalink node running the Fluxer plugin.
/// </summary>
/// <param name="BaseAddress">The node's HTTP address, such as http://localhost:2333.</param>
/// <param name="Passphrase">The node's password.</param>
public sealed record FluxerAudioNode(Uri BaseAddress, string Passphrase);
