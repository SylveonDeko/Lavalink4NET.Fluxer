# Lavalink4NET.Fluxer

Lavalink4NET integration for [Fluxer.Net](https://github.com/Nexfinity/Fluxer.Net) bots. It needs a Lavalink node
running [lavalink-fluxer-plugin](https://github.com/SylveonDeko/lavalink-fluxer-plugin).

Fluxer voice runs on LiveKit. The bot joins voice itself through `Fluxer.Net.Voice`, and `FluxerAudioBridge`
streams each player's audio from the node into that voice connection.

```csharp
services.AddSingleton(fluxerClient);
services.AddLavalink();
services.ConfigureLavalink(options =>
{
    options.BaseAddress = new Uri("http://localhost:2333");
    options.Passphrase = "youshallnotpass";
});
```

With a cluster, register the wrapper yourself and list the nodes for the bridge:

```csharp
services.AddLavalinkCluster<FluxerClientWrapper>();
services.AddFluxerAudio(options => options.Nodes.Add(new FluxerAudioNode(new Uri("http://localhost:2333"), "youshallnotpass")));
```
