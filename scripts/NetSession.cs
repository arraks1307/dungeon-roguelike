using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>Игрок в лобби: сетевой номер (у хоста всегда 1), имя и выбранный класс.</summary>
public record LobbyMember(long PeerId, string Name, int ClassIndex);

/// <summary>
/// Сетевая часть игры: подключение по IP, лобби и передача действий.
///
/// Как устроена синхронизация («одинаковая симуляция», lockstep):
/// - при старте хост рассылает всем одно и то же зерно случайности, поэтому у всех строится
///   один и тот же мир;
/// - игроки не двигают героев сами, а отправляют хосту «хочу сделать вот это»;
/// - хост нумерует действия и рассылает их всем (и себе) в одном порядке;
/// - каждый компьютер выполняет одни и те же действия в одном порядке и получает одинаковый мир.
/// По сети идут только короткие действия, а не весь мир — это быстро и просто.
/// К каждому действию хост прикладывает контрольную сумму своего мира, и клиент сверяет её со своей.
///
/// RPC (Remote Procedure Call) — вызов метода на другом компьютере. В Godot метод помечается
/// атрибутом [Rpc], а вызывается через Rpc(...) (у всех) или RpcId(номер, ...) (у одного).
/// Узел должен лежать по одному и тому же пути в дереве сцены на всех компьютерах.
/// </summary>
public partial class NetSession : Node
{
    public const int DefaultPort = 7777;

    /// <summary>Хост плюс трое друзей — столько героев помещается в отряд.</summary>
    private const int MaxClients = GameManager.MaxPartySize - 1;

    public event Action LobbyChanged;
    public event Action<string> StatusChanged;                       // «подключаемся...», «отказано: ...»
    public event Action<int, int, List<LobbyMember>> GameStarted;    // зерно, сложность, состав
    public event Action<long, HeroAction> ActionReceived;            // только у хоста: кто и что хочет сделать
    public event Action<int, HeroAction, int> ActionBroadcast;       // у всех: герой, действие, контрольная сумма хоста
    public event Action<long> PeerLeft;                              // только у хоста: игрок отключился
    public event Action<string> Disconnected;                        // только у клиента: связь с хостом пропала
    public event Action<long, string> ChatReceived;                  // у всех: кто и что написал
    public event Action<long, Vector2I> PingReceived;                // у всех: кто и какую клетку отметил
    public event Action<long> ResyncRequested;                       // только у хоста: клиенту нужен снимок мира
    public event Action<byte[]> SnapshotReceived;                    // у всех: снимок мира от хоста

    private readonly List<LobbyMember> _members = new();
    private string _myName = "";
    private int _myClass;
    private bool _inGame;

    public IReadOnlyList<LobbyMember> Members => _members;
    public bool IsActive { get; private set; }
    public bool IsHost { get; private set; }
    public long MyId => IsActive ? Multiplayer.GetUniqueId() : 0;

    /// <summary>Для самопроверки: слушать только этот компьютер (так Windows не спрашивает про брандмауэр).</summary>
    public bool LoopbackOnly { get; set; }

    public override void _Ready()
    {
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += () => Fail("Не удалось подключиться: хост не отвечает.");
        Multiplayer.ServerDisconnected += () => Fail("Связь с хостом потеряна.");
    }

    // ---------- Подключение ----------

    /// <summary>Создать игру. Возвращает текст ошибки или null, если всё получилось.</summary>
    public string Host(string name, int classIndex, int port = DefaultPort)
    {
        Close();
        var peer = new ENetMultiplayerPeer();
        if (LoopbackOnly)
            peer.SetBindIP("127.0.0.1");
        var error = peer.CreateServer(port, MaxClients);
        if (error != Error.Ok)
            return $"Не удалось открыть порт {port} ({error}). Возможно, игра уже запущена.";

        Multiplayer.MultiplayerPeer = peer;
        IsActive = true;
        IsHost = true;
        _inGame = false;
        _members.Add(new LobbyMember(1, CleanName(name), classIndex));
        LobbyChanged?.Invoke();
        return null;
    }

    /// <summary>Подключиться к другу. Результат придёт позже: в LobbyChanged или в StatusChanged.</summary>
    public string Join(string address, string name, int classIndex)
    {
        Close();
        var (host, port) = ParseAddress(address);
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(host, port);
        if (error != Error.Ok)
            return $"Не удалось начать подключение ({error}).";

        Multiplayer.MultiplayerPeer = peer;
        IsActive = true;
        IsHost = false;
        _myName = CleanName(name);
        _myClass = classIndex;
        StatusChanged?.Invoke($"Подключаемся к {host}:{port}...");
        return null;
    }

    /// <summary>Выйти из сетевой игры (или лобби) и закрыть соединение.</summary>
    public void Close()
    {
        if (IsActive)
            Multiplayer.MultiplayerPeer?.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        IsActive = false;
        IsHost = false;
        _inGame = false;
        _members.Clear();
    }

    /// <summary>«192.168.1.5» или «192.168.1.5:7000» → адрес и порт.</summary>
    private static (string Host, int Port) ParseAddress(string address)
    {
        address = address.Trim();
        int colon = address.LastIndexOf(':');
        if (colon > 0 && int.TryParse(address[(colon + 1)..], out int port))
            return (address[..colon], port);
        return (address.Length > 0 ? address : "127.0.0.1", DefaultPort);
    }

    /// <summary>Разделители «|» используются при передаче лобби, поэтому из имени их убираем.</summary>
    private static string CleanName(string name)
    {
        name = (name ?? "").Replace("|", "").Trim();
        return name.Length == 0 ? "Игрок" : name.Length > 16 ? name[..16] : name;
    }

    private void Fail(string reason)
    {
        bool wasInGame = _inGame;
        Close();
        if (wasInGame)
            Disconnected?.Invoke(reason);
        else
            StatusChanged?.Invoke(reason);
    }

    /// <summary>IPv4-адреса этого компьютера, чтобы хост мог продиктовать их друзьям.</summary>
    public static List<string> LocalAddresses() => IP.GetLocalAddresses()
        .Where(address => address.Count(c => c == '.') == 3 && !address.StartsWith("127.") && !address.StartsWith("169.254."))
        .Distinct()
        .ToList();

    // ---------- Лобби ----------

    private void OnConnectedToServer() => RpcId(1, MethodName.Hello, _myName, _myClass, MainMenu.Version);

    /// <summary>Клиент представляется хосту. Хост проверяет версию игры и свободные места.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void Hello(string name, int classIndex, string version)
    {
        if (!IsHost)
            return;
        long sender = Multiplayer.GetRemoteSenderId();
        string refusal = version != MainMenu.Version ? $"у хоста другая версия игры ({MainMenu.Version}, у тебя {version})"
            : _inGame ? "забег уже начался"
            : _members.Count >= GameManager.MaxPartySize ? "отряд уже полный"
            : null;
        if (refusal != null)
        {
            RpcId(sender, MethodName.Rejected, refusal);
            return;
        }
        _members.Add(new LobbyMember(sender, CleanName(name), Math.Clamp(classIndex, 0, ClassCatalog.All.Length - 1)));
        SendLobby();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void Rejected(string reason) => Fail($"Хост отказал: {reason}.");

    /// <summary>Выбрать класс в лобби: хост меняет сразу, клиент просит хоста.</summary>
    public void SetMyClass(int classIndex)
    {
        if (IsHost)
            ChangeClass(1, classIndex);
        else if (IsActive)
            RpcId(1, MethodName.RequestClass, classIndex);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void RequestClass(int classIndex)
    {
        if (IsHost && !_inGame)
            ChangeClass(Multiplayer.GetRemoteSenderId(), classIndex);
    }

    private void ChangeClass(long peerId, int classIndex)
    {
        int index = _members.FindIndex(member => member.PeerId == peerId);
        if (index < 0)
            return;
        _members[index] = _members[index] with { ClassIndex = Math.Clamp(classIndex, 0, ClassCatalog.All.Length - 1) };
        SendLobby();
    }

    private void SendLobby()
    {
        LobbyChanged?.Invoke();
        Rpc(MethodName.LobbyState, EncodeMembers());
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void LobbyState(string[] members)
    {
        _members.Clear();
        _members.AddRange(DecodeMembers(members));
        LobbyChanged?.Invoke();
    }

    private string[] EncodeMembers() =>
        _members.Select(member => $"{member.PeerId}|{member.ClassIndex}|{member.Name}").ToArray();

    private static IEnumerable<LobbyMember> DecodeMembers(string[] lines) => lines
        .Select(line => line.Split('|', 3))
        .Select(parts => new LobbyMember(long.Parse(parts[0]), parts[2], int.Parse(parts[1])));

    // ---------- Забег ----------

    /// <summary>Хост начинает забег: всем (и себе) — одно зерно, сложность и состав отряда.</summary>
    public void StartGame(Difficulty difficulty)
    {
        if (!IsHost)
            return;
        int seed = (int)(GD.Randi() & 0x7fffffff);
        Rpc(MethodName.GameStart, seed, (int)difficulty, EncodeMembers());
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void GameStart(int seed, int difficultyId, string[] members)
    {
        _inGame = true;
        _members.Clear();
        _members.AddRange(DecodeMembers(members));
        GameStarted?.Invoke(seed, difficultyId, _members.ToList());
    }

    /// <summary>Свой герой хочет что-то сделать. Хост обрабатывает сразу, клиент отправляет хосту.</summary>
    public void SendAction(HeroAction action)
    {
        if (IsHost)
            ActionReceived?.Invoke(1, action);
        else if (IsActive)
            RpcId(1, MethodName.RequestAction, (int)action.Kind, action.Direction.X, action.Direction.Y, action.Index, action.Extra);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void RequestAction(int kind, int dx, int dy, int index, int extra)
    {
        if (!IsHost || kind < 0 || kind > (int)HeroActionKind.Perk)
            return; // Leave клиент прислать не может — только хост, когда игрок отключился
        var action = new HeroAction((HeroActionKind)kind, new Vector2I(Math.Clamp(dx, -1, 1), Math.Clamp(dy, -1, 1)), index, extra);
        ActionReceived?.Invoke(Multiplayer.GetRemoteSenderId(), action);
    }

    /// <summary>Хост: разослать действие всем (и выполнить у себя) вместе с контрольной суммой мира.</summary>
    public void Broadcast(int heroId, HeroAction action, int checksum)
    {
        if (IsHost)
            Rpc(MethodName.ApplyAction, heroId, (int)action.Kind, action.Direction.X, action.Direction.Y, action.Index, action.Extra, checksum);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void ApplyAction(int heroId, int kind, int dx, int dy, int index, int extra, int checksum) =>
        ActionBroadcast?.Invoke(heroId, new HeroAction((HeroActionKind)kind, new Vector2I(dx, dy), index, extra), checksum);

    // ---------- Чат и метки ----------
    // Они не меняют мир, поэтому идут мимо потока действий: хост просто пересылает их всем.

    public const int MaxChatLength = 120;

    public void SendChat(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
            return;
        if (text.Length > MaxChatLength)
            text = text[..MaxChatLength];
        if (IsHost)
            Rpc(MethodName.ChatMessage, 1L, text);
        else if (IsActive)
            RpcId(1, MethodName.RequestChat, text);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void RequestChat(string text)
    {
        if (IsHost && text.Length <= MaxChatLength)
            Rpc(MethodName.ChatMessage, (long)Multiplayer.GetRemoteSenderId(), text);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void ChatMessage(long peerId, string text) => ChatReceived?.Invoke(peerId, text);

    public void SendPing(Vector2I cell)
    {
        if (IsHost)
            Rpc(MethodName.PingMessage, 1L, cell.X, cell.Y);
        else if (IsActive)
            RpcId(1, MethodName.RequestPing, cell.X, cell.Y);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void RequestPing(int x, int y)
    {
        if (IsHost)
            Rpc(MethodName.PingMessage, (long)Multiplayer.GetRemoteSenderId(), x, y);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void PingMessage(long peerId, int x, int y) => PingReceived?.Invoke(peerId, new Vector2I(x, y));

    // ---------- Восстановление мира ----------

    /// <summary>Клиент заметил, что его мир разошёлся с хостом, и просит снимок.</summary>
    public void RequestResync()
    {
        if (IsActive && !IsHost)
            RpcId(1, MethodName.ResyncRequest);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void ResyncRequest()
    {
        if (IsHost && _inGame)
            ResyncRequested?.Invoke(Multiplayer.GetRemoteSenderId());
    }

    /// <summary>
    /// Хост рассылает снимок мира ВСЕМ, включая себя. Восстанавливаются все одновременно и в одной
    /// точке потока действий — поэтому миры совпадут даже в мелочах, которых нет в снимке.
    /// </summary>
    public void BroadcastSnapshot(byte[] snapshot)
    {
        if (IsHost)
            Rpc(MethodName.ApplySnapshot, snapshot);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void ApplySnapshot(byte[] snapshot) => SnapshotReceived?.Invoke(snapshot);

    // ---------- Отключения ----------

    private void OnPeerDisconnected(long peerId)
    {
        if (!IsHost)
            return;
        if (_inGame)
        {
            // В забеге: GameManager уберёт героя через общее действие Leave. Откладываем до конца кадра:
            // прямо сейчас ENet ещё считает ушедшего получателем и ругается на отправку ему пакета
            Callable.From(() => PeerLeft?.Invoke(peerId)).CallDeferred();
            _members.RemoveAll(member => member.PeerId == peerId);
            return;
        }
        if (_members.RemoveAll(member => member.PeerId == peerId) > 0)
            SendLobby();
    }

    /// <summary>Хост вернулся из забега в лобби (новый забег тем же составом).</summary>
    public void BackToLobby() => _inGame = false;
}
