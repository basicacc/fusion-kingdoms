using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

public enum NetRole { Local, Host, Client }

// Autoload "Net": LAN 1v1 over ENet. The host simulates everything; the client sends commands and receives state.
public partial class NetSession : Node
{
    public const int Port = 7777;
    public static NetSession I { get; private set; } = null!;

    public NetRole Role = NetRole.Local;
    public string LocalName = System.Environment.UserName;
    public string RemoteName = "";
    public byte[]? RemoteLibrary;
    public int RemotePeer;
    public string Status = "";

    public event Action? Changed;
    public event Action<byte[]>? SnapshotReceived;
    public event Action<byte[]>? MindsReceived;
    public event Action<int, byte[]>? CommandReceived;
    public event Action<int, int>? RoyalReceived;
    public event Action? Disconnected;

    int _nextRequest = 1;
    readonly Dictionary<int, Action<string?, string?, int>> _pending = new();

    public override void _EnterTree() => I = this;

    void Notify()
    {
        GD.Print($"[net] {Role}: {Status}");
        Changed?.Invoke();
    }

    public override void _Ready()
    {
        Multiplayer.PeerConnected += id => { if (Role == NetRole.Host) { RemotePeer = (int)id; Status = "A player is connecting..."; Notify(); } };
        Multiplayer.PeerDisconnected += _ => { Status = "The other player left."; RemoteName = ""; Disconnected?.Invoke(); Notify(); };
        Multiplayer.ConnectedToServer += () =>
        {
            Status = "Connected. Sending your Library of Minds...";
            var lib = MindLibrary.I;
            RpcId(1, MethodName.Hello, LocalName, Wire.WriteLibrary(lib.All, lib.BaseMinds.Values.ToHashSet()));
            Notify();
        };
        Multiplayer.ConnectionFailed += () => { Status = "Could not connect."; Close(); Notify(); };
        Multiplayer.ServerDisconnected += () => { Status = "The host closed the game."; Disconnected?.Invoke(); Close(); Notify(); };
    }

    public string? Host()
    {
        Close();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(Port, 1);
        if (err != Error.Ok) return $"Could not host on port {Port}: {err}";
        Multiplayer.MultiplayerPeer = peer;
        Role = NetRole.Host;
        Status = $"Hosting on port {Port}. Your LAN address: {string.Join(", ", LanAddresses())}. Waiting for a player...";
        Notify();
        return null;
    }

    public string? Join(string address)
    {
        Close();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(address.Trim(), Port);
        if (err != Error.Ok) return $"Could not connect: {err}";
        Multiplayer.MultiplayerPeer = peer;
        Role = NetRole.Client;
        Status = $"Connecting to {address}...";
        Notify();
        return null;
    }

    public void Close()
    {
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer p) p.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        Role = NetRole.Local;
        RemoteName = "";
        RemoteLibrary = null;
        _pending.Clear();
    }

    public static IEnumerable<string> LanAddresses() =>
        IP.GetLocalAddresses().Where(a => a.Contains('.') && !a.StartsWith("127.") && !a.StartsWith("169.254"));

    // ---------- lobby ----------

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Hello(string name, byte[] library)
    {
        RemotePeer = Multiplayer.GetRemoteSenderId();
        RemoteName = name;
        RemoteLibrary = library;
        Status = $"{name} joined with {library.Length / 1024} KB of minds. Press Start when ready.";
        Notify();
    }

    public void StartMatch()
    {
        if (Role != NetRole.Host || RemoteLibrary == null) return;
        int seed = (int)(GD.Randi() & 0x7fffffff);
        var st = MatchScene.Settings;
        RpcId(RemotePeer, MethodName.Begin, seed, LocalName, (int)st.Size, (int)st.Style, st.Resources, st.StartingStock, st.Minutes, st.Wildlife);
        MatchScene.Seed = seed;
        MatchScene.Role = NetRole.Host;
        MatchScene.ShowcaseMode = false;
        GetTree().ChangeSceneToFile("res://scenes/Match.tscn");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Begin(int seed, string hostName, int size, int style, float resources, float stock, float minutes, float wildlife)
    {
        MatchScene.Settings = new MapSettings((MapSize)size, (MapStyle)style, resources, stock, minutes, wildlife);
        RemoteName = hostName;
        MatchScene.Seed = seed;
        MatchScene.Role = NetRole.Client;
        MatchScene.ShowcaseMode = false;
        GetTree().ChangeSceneToFile("res://scenes/Match.tscn");
    }

    // ---------- in match: host -> client ----------

    public void SendState(byte[]? minds, byte[] snapshot)
    {
        if (minds != null) RpcId(RemotePeer, MethodName.Minds, minds);
        RpcId(RemotePeer, MethodName.State, snapshot);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 1)]
    void Minds(byte[] data) => MindsReceived?.Invoke(data);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 1)]
    void State(byte[] data) => SnapshotReceived?.Invoke(data);

    public void Reply(int request, CommandResult r)
    {
        RpcId(RemotePeer, MethodName.Answer, request, r.Error ?? "", r.Info ?? "", r.Mind?.NetId ?? -1);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 1)]
    void Answer(int request, string error, string info, int mindId)
    {
        if (_pending.Remove(request, out var cb)) cb(error.Length == 0 ? null : error, info.Length == 0 ? null : info, mindId);
    }

    // ---------- in match: client -> host ----------

    public void SendCommand(Command c, Action<string?, string?, int> onAnswer)
    {
        int id = _nextRequest++;
        _pending[id] = onAnswer;
        RpcId(1, MethodName.Order, id, c.ToBytes());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 1)]
    void Order(int request, byte[] command) => CommandReceived?.Invoke(request, command);

    public void SendRoyal(int move, int action) => RpcId(1, MethodName.Steer, move, action);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void Steer(int move, int action) => RoyalReceived?.Invoke(move, action);
}
