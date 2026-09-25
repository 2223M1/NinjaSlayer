using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace NinjaSlayer.Code.ExternalAnimations;

internal enum BossGreetingSignal { Ready, Start, Shorten, Done, Release }

internal struct BossGreetingMessage : INetMessage
{
    internal string RoomKey;
    internal BossGreetingSignal Signal;
    internal bool Brief;
    internal bool Present;
    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => true;
    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(RoomKey);
        writer.WriteInt((int)Signal, 3);
        writer.WriteBool(Brief);
        writer.WriteBool(Present);
    }
    public void Deserialize(PacketReader reader)
    {
        RoomKey = reader.ReadString();
        Signal = (BossGreetingSignal)reader.ReadInt(3);
        Brief = reader.ReadBool();
        Present = reader.ReadBool();
    }
}

// One room's presentation barrier, not a combat action queue. Kept until room
// exit so a reconnecting client can ask for the current (or released) state.
internal sealed class BossGreetingSync : IDisposable
{
    private readonly INetGameService _network;
    private readonly string _roomKey;
    private readonly HashSet<ulong> _players;
    private readonly HashSet<ulong> _ready = [];
    private readonly HashSet<ulong> _done = [];
    private readonly HashSet<ulong> _disconnected = [];
    private bool _disposed;
    internal bool IsAuthority => _network.Type != NetGameType.Client;
    internal bool Started { get; private set; }
    internal bool Released { get; private set; }
    internal bool Brief { get; private set; }
    internal bool Present { get; private set; }
    internal bool Shortened { get; private set; }
    internal bool Cancelled => _disposed || !_network.IsConnected;

    internal BossGreetingSync(INetGameService network, string roomKey, IEnumerable<ulong> players,
        bool brief, bool present)
    {
        _network = network;
        _roomKey = roomKey;
        _players = players.ToHashSet();
        Brief = brief;
        Present = present;
        _network.RegisterMessageHandler<BossGreetingMessage>(Receive);
        _network.Disconnected += Disconnected;
        if (_network is INetHostGameService host) host.ClientDisconnected += ClientDisconnected;
    }

    internal void Ready()
    {
        if (IsAuthority)
        {
            _ready.Add(_network.NetId);
            AdvanceHost();
        }
        else Send(BossGreetingSignal.Ready);
    }

    internal void RequestBrief()
    {
        if (!IsAuthority || !Started || Released || Brief || Shortened) return;
        Shortened = true;
        Send(BossGreetingSignal.Shorten);
    }

    internal void Complete()
    {
        if (IsAuthority)
        {
            _done.Add(_network.NetId);
            AdvanceHost();
        }
        else Send(BossGreetingSignal.Done);
    }

    private IEnumerable<ulong> Participants => _players.Where(id => !_disconnected.Contains(id)
        && (id == _network.NetId
            || _network is INetHostGameService host && host.NetHost!.ConnectedPeerIds.Contains(id)));

    private void AdvanceHost()
    {
        if (!Started && Participants.All(_ready.Contains))
        {
            Started = true;
            Send(BossGreetingSignal.Start);
        }
        if (Started && !Released && Participants.All(_done.Contains))
        {
            Released = true;
            Send(BossGreetingSignal.Release);
        }
    }

    private void Receive(BossGreetingMessage message, ulong sender)
    {
        if (_disposed || message.RoomKey != _roomKey || !_players.Contains(sender)) return;
        if (IsAuthority)
        {
            if (message.Signal == BossGreetingSignal.Ready)
            {
                _disconnected.Remove(sender);
                _ready.Add(sender);
                AdvanceHost();
                if (Started) Send(Released ? BossGreetingSignal.Release : BossGreetingSignal.Start, sender);
                if (Shortened && !Released) Send(BossGreetingSignal.Shorten, sender);
            }
            else if (message.Signal == BossGreetingSignal.Done && Started && _ready.Contains(sender))
            {
                _done.Add(sender);
                AdvanceHost();
            }
            return;
        }
        if (_network is not INetClientGameService client || sender != client.NetClient?.HostNetId) return;
        switch (message.Signal)
        {
            case BossGreetingSignal.Start when !Started:
                Brief = message.Brief;
                Present = message.Present;
                Started = true;
                break;
            case BossGreetingSignal.Shorten when Started:
                Shortened = true;
                break;
            case BossGreetingSignal.Release:
                Started = Released = true;
                break;
        }
    }

    private void Send(BossGreetingSignal signal, ulong? recipient = null)
    {
        if (_disposed || !_network.IsConnected || _network.Type == NetGameType.Singleplayer) return;
        var message = new BossGreetingMessage { RoomKey = _roomKey, Signal = signal, Brief = Brief, Present = Present };
        if (recipient is { } id) _network.SendMessage(message, id);
        else _network.SendMessage(message);
    }

    private void ClientDisconnected(ulong id, NetErrorInfo _)
    {
        _disconnected.Add(id);
        _ready.Remove(id);
        _done.Remove(id);
        AdvanceHost();
    }
    private void Disconnected(NetErrorInfo _) => Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _network.UnregisterMessageHandler<BossGreetingMessage>(Receive);
        _network.Disconnected -= Disconnected;
        if (_network is INetHostGameService host) host.ClientDisconnected -= ClientDisconnected;
    }
}
