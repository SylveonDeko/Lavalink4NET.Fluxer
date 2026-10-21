using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using Fluxer.Net.Voice;
using Lavalink4NET.Clients;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lavalink4NET.Fluxer;

/// <summary>
/// Streams player audio from Lavalink nodes running the Fluxer plugin into the bot's Fluxer voice connections.
/// </summary>
/// <remarks>
/// Each node sends one binary message per 20 millisecond frame: the guild id as an unsigned 64 bit big endian
/// integer followed by 16 bit little endian stereo PCM at 48 kHz.
/// </remarks>
public sealed class FluxerAudioBridge : BackgroundService
{
    private const string StreamPath = "v4/fluxer/audio";
    private const int HeaderLength = sizeof(ulong);
    private const int PumpCapacity = 25;
    private static readonly TimeSpan PumpIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly FluxerVoiceClient _voice;
    private readonly IDiscordClientWrapper _client;
    private readonly FluxerAudioOptions _options;
    private readonly AudioServiceOptions _audioOptions;
    private readonly ILogger<FluxerAudioBridge> _logger;
    private readonly ConcurrentDictionary<ulong, Channel<short[]>> _pumps = new ConcurrentDictionary<ulong, Channel<short[]>>();

    /// <summary>
    /// Creates the bridge.
    /// </summary>
    public FluxerAudioBridge(FluxerVoiceClient voice, IDiscordClientWrapper client, IOptions<FluxerAudioOptions> options,
        IOptions<AudioServiceOptions> audioOptions, ILogger<FluxerAudioBridge> logger)
    {
        _voice = voice;
        _client = client;
        _options = options.Value;
        _audioOptions = audioOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ClientInformation client = await _client.WaitForReadyAsync(stoppingToken).ConfigureAwait(false);

        IReadOnlyList<FluxerAudioNode> nodes = _options.Nodes.Count > 0
            ? _options.Nodes
            : [new FluxerAudioNode(_audioOptions.BaseAddress, _audioOptions.Passphrase)];

        await Task.WhenAll(nodes.Select(node => RunNodeAsync(node, client.CurrentUserId, stoppingToken))).ConfigureAwait(false);
    }

    private async Task RunNodeAsync(FluxerAudioNode node, ulong userId, CancellationToken cancellationToken)
    {
        Uri uri = GetStreamUri(node.BaseAddress);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using ClientWebSocket socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", node.Passphrase);
                socket.Options.SetRequestHeader("User-Id", userId.ToString());
                await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Audio stream connected to {Node}", node.BaseAddress);

                await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Audio stream from {Node} closed: {Status} {Reason}", node.BaseAddress,
                    socket.CloseStatus, socket.CloseStatusDescription);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audio stream from {Node} failed", node.BaseAddress);
            }

            try
            {
                await Task.Delay(_options.ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];

        while (socket.State == WebSocketState.Open)
        {
            int count = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                if (count == buffer.Length)
                    Array.Resize(ref buffer, buffer.Length * 2);

                result = await socket.ReceiveAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
                count += result.Count;
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
                return;

            if (result.MessageType != WebSocketMessageType.Binary || count <= HeaderLength)
                continue;

            ulong guildId = BinaryPrimitives.ReadUInt64BigEndian(buffer);
            int sampleCount = (count - HeaderLength) / sizeof(short);
            sampleCount -= sampleCount % FluxerVoiceConnection.Channels;

            short[] samples = new short[sampleCount];
            Buffer.BlockCopy(buffer, HeaderLength, samples, 0, sampleCount * sizeof(short));

            Enqueue(guildId, samples, cancellationToken);
        }
    }

    private void Enqueue(ulong guildId, short[] samples, CancellationToken cancellationToken)
    {
        if (_voice.GetConnection(guildId) is not { IsConnected: true })
            return;

        while (true)
        {
            Channel<short[]> pump = _pumps.GetOrAdd(guildId, id => StartPump(id, cancellationToken));
            if (pump.Writer.TryWrite(samples))
                return;

            _pumps.TryRemove(new KeyValuePair<ulong, Channel<short[]>>(guildId, pump));
        }
    }

    private Channel<short[]> StartPump(ulong guildId, CancellationToken cancellationToken)
    {
        Channel<short[]> pump = Channel.CreateBounded<short[]>(new BoundedChannelOptions(PumpCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        _ = Task.Run(() => PumpAsync(guildId, pump, cancellationToken), CancellationToken.None);
        return pump;
    }

    private async Task PumpAsync(ulong guildId, Channel<short[]> pump, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                using CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                idle.CancelAfter(PumpIdleTimeout);

                bool more;
                try
                {
                    more = await pump.Reader.WaitToReadAsync(idle.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    more = false;
                }

                if (!more)
                    break;

                while (pump.Reader.TryRead(out short[]? samples))
                {
                    if (_voice.GetConnection(guildId) is not { IsConnected: true } connection)
                        continue;

                    try
                    {
                        await connection.SendAsync(samples, cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sending audio to guild {GuildId} failed", guildId);
        }
        finally
        {
            _pumps.TryRemove(new KeyValuePair<ulong, Channel<short[]>>(guildId, pump));
            pump.Writer.TryComplete();
        }
    }

    private static Uri GetStreamUri(Uri baseAddress)
    {
        UriBuilder builder = new UriBuilder(new Uri(baseAddress, StreamPath))
        {
            Scheme = baseAddress.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
        };
        return builder.Uri;
    }
}
