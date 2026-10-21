using System.Collections.Concurrent;
using System.Collections.Immutable;
using Fluxer.Net;
using Fluxer.Net.Voice;
using Lavalink4NET.Clients;
using Lavalink4NET.Clients.Events;
using Lavalink4NET.Events;
using Microsoft.Extensions.Logging;
using VoiceServer = Lavalink4NET.Clients.VoiceServer;
using VoiceState = Lavalink4NET.Clients.VoiceState;

namespace Lavalink4NET.Fluxer;

/// <summary>
/// Connects Lavalink4NET to a <see cref="FluxerClient"/>.
/// </summary>
/// <remarks>
/// Fluxer voice runs on LiveKit, which Lavalink cannot join itself. Joining a channel opens a
/// <see cref="FluxerVoiceConnection"/> through <see cref="FluxerVoiceClient"/>, and only then tells Lavalink4NET
/// the voice server is ready so the player is created. The audio itself reaches the channel through
/// <see cref="FluxerAudioBridge"/>.
/// </remarks>
public sealed class FluxerClientWrapper : IDiscordClientWrapper, IDisposable
{
    private const string Label = "Fluxer";
    private static readonly VoiceServer BridgedVoiceServer = new VoiceServer("fluxer", "fluxer");

    private readonly FluxerClient _client;
    private readonly FluxerVoiceClient _voice;
    private readonly ILogger<FluxerClientWrapper> _logger;
    private readonly ConcurrentDictionary<(ulong GuildId, ulong UserId), VoiceState> _voiceStates = new ConcurrentDictionary<(ulong GuildId, ulong UserId), VoiceState>();
    private readonly TaskCompletionSource<ClientInformation> _ready = new TaskCompletionSource<ClientInformation>(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Creates the wrapper for a Fluxer client and its voice client.
    /// </summary>
    public FluxerClientWrapper(FluxerClient client, FluxerVoiceClient voice, ILogger<FluxerClientWrapper> logger)
    {
        _client = client;
        _voice = voice;
        _logger = logger;

        _client.Gateway.Ready += OnReady;
        _client.Gateway.VoiceStateUpdated += OnVoiceStateUpdated;

        if (_client.Gateway.CurrentUser is { } currentUser)
            _ready.TrySetResult(new ClientInformation(Label, currentUser.Id, 1));
    }

    /// <inheritdoc />
    public event AsyncEventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

    /// <inheritdoc />
    public event AsyncEventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    /// <inheritdoc />
    public ValueTask<ImmutableArray<ulong>> GetChannelUsersAsync(ulong guildId, ulong voiceChannelId,
        bool includeBots = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_client.Gateway.GetGuild(guildId) is not { } guild)
            return ValueTask.FromResult(ImmutableArray<ulong>.Empty);

        ulong? currentUserId = _client.Gateway.CurrentUser?.Id;
        ImmutableArray<ulong> users = guild.Members.Values
            .Where(member => member.Id != currentUserId)
            .Where(member => includeBots || member.User is not { IsBot: true })
            .Where(member => member.VoiceStates.Values.Any(state => state.ChannelId == voiceChannelId))
            .Select(member => member.Id)
            .ToImmutableArray();

        return ValueTask.FromResult(users);
    }

    /// <inheritdoc />
    public async ValueTask SendVoiceUpdateAsync(ulong guildId, ulong? voiceChannelId, bool selfDeaf = false,
        bool selfMute = false, CancellationToken cancellationToken = default)
    {
        if (voiceChannelId is not { } channelId)
        {
            await _voice.LeaveAsync(guildId).ConfigureAwait(false);
            return;
        }

        await _voice.JoinAsync(guildId, channelId, selfDeaf, selfMute, cancellationToken).ConfigureAwait(false);

        await VoiceServerUpdated
            .InvokeAsync(this, new VoiceServerUpdatedEventArgs(guildId, BridgedVoiceServer))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<ClientInformation> WaitForReadyAsync(CancellationToken cancellationToken = default) =>
        new ValueTask<ClientInformation>(_ready.Task.WaitAsync(cancellationToken));

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Gateway.Ready -= OnReady;
        _client.Gateway.VoiceStateUpdated -= OnVoiceStateUpdated;
    }

    private void OnReady(SocketCurrentUser currentUser) =>
        _ready.TrySetResult(new ClientInformation(Label, currentUser.Id, 1));

    private void OnVoiceStateUpdated(SocketVoiceState voiceState)
    {
        if (voiceState.GuildId is not { } guildId)
            return;

        (ulong, ulong) key = (guildId, voiceState.UserId);
        VoiceState current = new VoiceState(voiceState.ChannelId, voiceState.SessionId);
        VoiceState previous = _voiceStates.GetValueOrDefault(key);

        if (voiceState.ChannelId is null)
            _voiceStates.TryRemove(key, out _);
        else
            _voiceStates[key] = current;

        bool isCurrentUser = voiceState.UserId == _client.Gateway.CurrentUser?.Id;
        _ = RaiseVoiceStateUpdatedAsync(new VoiceStateUpdatedEventArgs(guildId, voiceState.UserId, isCurrentUser, current, previous));
    }

    private async Task RaiseVoiceStateUpdatedAsync(VoiceStateUpdatedEventArgs eventArgs)
    {
        try
        {
            await VoiceStateUpdated.InvokeAsync(this, eventArgs).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling a voice state update for guild {GuildId} failed", eventArgs.GuildId);
        }
    }
}
