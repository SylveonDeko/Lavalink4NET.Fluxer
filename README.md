# Lavalink4NET.Fluxer

[Lavalink4NET](https://github.com/angelobreuer/Lavalink4NET) support for [Fluxer.Net](https://github.com/Nexfinity/Fluxer.Net)
bots.

It needs a Lavalink node running [lavalink-fluxer-plugin](https://github.com/SylveonDeko/lavalink-fluxer-plugin). The
bot joins voice through `Fluxer.Net.Voice`, and `FluxerAudioBridge` streams each player's audio from the node into
that connection.

## Usage

Single node:

```csharp
services.AddSingleton(fluxerClient);
services.AddLavalink();
services.ConfigureLavalink(options =>
{
    options.BaseAddress = new Uri("http://localhost:2333");
    options.Passphrase = "youshallnotpass";
});
```

Cluster:

```csharp
services.AddLavalinkCluster<FluxerClientWrapper>();
services.AddFluxerAudio(options =>
    options.Nodes.Add(new FluxerAudioNode(new Uri("http://localhost:2333"), "youshallnotpass")));
```

Players are then used the same way as with any other Lavalink4NET client.

## Building

The project references `Fluxer.Net.Voice` from a [Fluxer.Net](https://github.com/Nexfinity/Fluxer.Net) checkout next
to this one:

```
git clone https://github.com/Nexfinity/Fluxer.Net
git clone https://github.com/SylveonDeko/Lavalink4NET.Fluxer
dotnet build Lavalink4NET.Fluxer
```
