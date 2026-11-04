using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про сетевую игру: лобби, отправка своих действий, выполнение действий
/// из общего потока хоста, контрольная сумма мира и отключения игроков.
///
/// Главное правило: в сетевой игре мир меняется ТОЛЬКО через ApplyHeroAction, вызванный из
/// общего потока действий. Тогда у всех одинаковые действия в одинаковом порядке —
/// и одинаковый мир. Всё, что зависит от конкретного компьютера (окна, камера, звук,
/// предложенные улучшения), не должно трогать мир и его генератор случайности _rng.
/// </summary>
public partial class GameManager
{
    private NetSession _net;
    private LobbyPanel _lobby;
    private bool _isNetworkGame;
    private bool _netPreparing; // хост начал забег, идёт обучение; настоящего мира ещё нет

    /// <summary>Случайность «для этого компьютера»: какие улучшения предложить. Мир от неё не зависит.</summary>
    private readonly Random _uiRng = new();

    /// <summary>Своё действие отправлено хосту, ждём, когда оно вернётся в общем потоке.</summary>
    private bool _actionInFlight;
    private Action<bool> _afterLocalAction;

    /// <summary>Номер героя → сетевой номер игрока (нужно хосту, чтобы проверять, кто чем управляет).</summary>
    private readonly Dictionary<int, long> _heroOwners = new();
    private int _desyncs;

    private void SetupNetwork()
    {
        _net = new NetSession { Name = "Net" }; // имя узла одинаковое у всех — по нему Godot находит адресата RPC
        AddChild(_net);
        _net.LobbyChanged += OnLobbyChanged;
        _net.StatusChanged += OnNetStatus;
        _net.PreparingRun += OnNetPreparing;
        _net.GameStarted += OnNetGameStarted;
        _net.ActionReceived += OnNetActionReceived;
        _net.ActionBroadcast += OnNetActionBroadcast;
        _net.PeerLeft += OnNetPeerLeft;
        _net.Disconnected += OnNetDisconnected;
        _net.ResyncRequested += OnResyncRequested;
        _net.SnapshotReceived += OnSnapshotReceived;

        _lobby = new LobbyPanel();
        AddChild(_lobby);
        _lobby.HostRequested += name =>
        {
            RememberNetFields();
            string error = _net.Host(name, _lobby.SelectedClass);
            if (error != null)
                _lobby.SetConnectStatus(error);
            else
                _lobby.OpenRoom(isHost: true);
        };
        _lobby.JoinRequested += (name, address) =>
        {
            RememberNetFields();
            string error = _net.Join(address, name, _lobby.SelectedClass);
            if (error != null)
                _lobby.SetConnectStatus(error);
        };
        _lobby.ClassChosen += index => _net.SetMyClass(index);
        _lobby.StartRequested += difficulty => _net.StartGame(difficulty.Id);
        _lobby.LeaveRequested += LeaveLobby;
        _lobby.BackRequested += CloseLobby;
    }

    // ---------- Лобби ----------

    private void OpenLobby()
    {
        _mainMenu.Visible = false;
        _lobby.Prefill(Settings.Current.PlayerName, Settings.Current.LastAddress);
        _lobby.OpenConnect();
    }

    private void LeaveLobby()
    {
        _net.Close();
        _lobby.OpenConnect();
    }

    private void CloseLobby()
    {
        _net.Close();
        _lobby.Visible = false;
        _mainMenu.ShowMain();
    }

    private void RememberNetFields()
    {
        Settings.Current.PlayerName = _lobby.EnteredName;
        Settings.Current.LastAddress = _lobby.EnteredAddress;
        if (!IsTestRun)
            Settings.Save();
    }

    private void OnLobbyChanged()
    {
        if (_netPreparing || _state != GameState.Menu && !_gameOver)
            return; // во время забега и обучения лобби не показываем
        if (!_lobby.IsInRoom && _net.IsActive)
            _lobby.OpenRoom(_net.IsHost); // клиент: хост принял нас — открываем комнату
        _lobby.UpdateMembers(_net.Members, _net.MyId);
    }

    private void OnNetStatus(string status)
    {
        if (!_lobby.Visible)
            return;
        if (_lobby.IsInRoom)
            _lobby.OpenConnect(status); // связь пропала прямо в лобби
        else
            _lobby.SetConnectStatus(status);
    }

    /// <summary>
    /// Хост нажал «Начать»: каждый проходит обучение у себя. Мир обучения локальный и не сетевой,
    /// поэтому _isNetworkGame пока false: настоящий мир появится в OnNetGameStarted.
    /// </summary>
    private void OnNetPreparing()
    {
        _netPreparing = true;
        _isNetworkGame = false;
        _lobby.Visible = false;
        _state = GameState.Menu;
        _gameOver = false;
        _hud.HideGameOver();
        CloseLocalWindows();
        _hud.Visible = false;

        var me = _net.Members.First(member => member.PeerId == _net.MyId);
        var playerClass = ClassCatalog.All[me.ClassIndex];
        if (_netTestRole == null)
            OfferTutorial(playerClass, me.Name, WaitForParty);
        else if (_netTestTutorial)
            StartTutorial(playerClass, me.Name, WaitForParty);
        else
            _net.SendReady();
    }

    /// <summary>Своё обучение закончено: сообщаем хосту и ждём остальных.</summary>
    private void WaitForParty()
    {
        _state = GameState.Menu;
        _hud.Visible = false;
        if (_netTestRole == null)
            _tutorialPanel.OpenWaiting(ShowMainMenu);
        _net.SendReady();
    }

    /// <summary>Хост начал забег: у всех одно зерно — значит, один и тот же мир.</summary>
    private void OnNetGameStarted(int seed, int difficulty, List<LobbyMember> members)
    {
        _netPreparing = false;
        _tutorialPanel.Visible = false;
        _lobby.Visible = false;
        _isNetworkGame = true;
        _rng = new GameRandom(seed);
        _actionInFlight = false;
        _afterLocalAction = null;
        _desyncs = 0;
        _resyncs = 0;
        _tradeCount = 0;
        _resyncPending = false;

        _heroOwners.Clear();
        var party = new List<HeroSpec>();
        for (int i = 0; i < members.Count; i++)
        {
            _heroOwners[i] = members[i].PeerId;
            bool mine = members[i].PeerId == _net.MyId;
            party.Add(new HeroSpec(ClassCatalog.All[members[i].ClassIndex], members[i].Name, Bot: mine && _netTestRole != null));
        }
        int localIndex = Math.Max(0, members.FindIndex(member => member.PeerId == _net.MyId));
        StartRun(party, Difficulties.Get((Difficulty)difficulty), localIndex);
    }

    /// <summary>Выйти из сетевой игры (в главное меню).</summary>
    private void LeaveNetwork()
    {
        if (_net.IsActive)
            _net.Close();
        _isNetworkGame = false;
        _netPreparing = false;
        _actionInFlight = false;
        _afterLocalAction = null;
    }

    // ---------- Свои действия ----------

    /// <summary>
    /// Своё действие. В одиночной игре выполняется сразу, в сетевой — уходит хосту и выполнится,
    /// когда вернётся в общем потоке. after вызывается после выполнения (true — ход потрачен).
    /// Пока одно действие в пути, новые не отправляем: иначе быстрые нажатия «обгоняли» бы друг друга.
    /// </summary>
    private void SubmitLocalAction(HeroAction action, Action<bool> after = null)
    {
        if (_gameOver || _localHero == null)
            return;
        if (!_isNetworkGame)
        {
            bool spent = ApplyHeroAction(_localHero, action);
            after?.Invoke(spent);
            return;
        }

        if (_actionInFlight)
            return;
        if (action.SpendsTurn && (_localHero.IsGhost || _localHero.HasActed))
        {
            if (!_localHero.IsGhost)
                _hud.SetWaiting(WaitingFor()); // заведомо пустой ход по сети не гоняем
            return;
        }
        _actionInFlight = true;
        _afterLocalAction = after;
        _net.SendAction(action);
    }

    /// <summary>Действие с клавиатуры — только когда на этом компьютере не открыто никакое окно.</summary>
    private void PerformLocalAction(HeroAction action, Action<bool> after = null)
    {
        if (_state == GameState.Playing)
            SubmitLocalAction(action, after);
    }

    /// <summary>
    /// Выполнить действие любого героя. Это единственное место, через которое меняется мир:
    /// ход (шаг, способность, предмет) или «свободное» действие (выбросить, купить, улучшение...).
    /// </summary>
    private bool ApplyHeroAction(Player hero, HeroAction action)
    {
        switch (action.Kind)
        {
            case HeroActionKind.Drop:
                DropItem(hero, action.Index);
                return false;
            case HeroActionKind.Buy:
                BuyItem(hero, action.Index, action.Extra);
                return false;
            case HeroActionKind.Sell:
                SellItem(hero, action.Index);
                return false;
            case HeroActionKind.Perk:
                ChoosePerk(hero, action.Index);
                return false;
            case HeroActionKind.Upgrade:
                UpgradeItem(hero, (EquipSlot)action.Index);
                return false;
            case HeroActionKind.Treat:
                HealAtMerchant(hero);
                return false;
            case HeroActionKind.Mystery:
                OpenMysteryBox(hero);
                return false;
            case HeroActionKind.Leave:
                RemoveHero(hero);
                return false;
            default:
                return PerformHeroAction(hero, action);
        }
    }

    // ---------- Общий поток действий ----------

    /// <summary>Хост: игрок peerId хочет сделать действие. Проверяем, что герой его, и рассылаем всем.</summary>
    private void OnNetActionReceived(long peerId, HeroAction action)
    {
        if (!_isNetworkGame || _gameOver)
            return;
        foreach (var (heroId, owner) in _heroOwners)
        {
            if (owner == peerId && _heroes.Any(hero => hero.Id == heroId))
            {
                _net.Broadcast(heroId, action, StateChecksum());
                return;
            }
        }
    }

    /// <summary>У всех (и у хоста): очередное действие из общего потока.</summary>
    private void OnNetActionBroadcast(int heroId, HeroAction action, int hostChecksum)
    {
        if (!_isNetworkGame)
            return;
        if (!_net.IsHost && hostChecksum != StateChecksum())
            ReportDesync(hostChecksum);

        var hero = _heroes.Find(other => other.Id == heroId);
        if (hero == null)
            return;
        bool spent = ApplyHeroAction(hero, action);

        if (hero == _localHero)
        {
            _actionInFlight = false;
            var after = _afterLocalAction;
            _afterLocalAction = null;
            after?.Invoke(spent);
            TryShowLevelUp(); // пока действие было в пути, окно улучшения не открывалось
        }
        else if (_state == GameState.Shop && action.Kind is HeroActionKind.Buy or HeroActionKind.Sell && _merchant != null)
        {
            _shopPanel.Refresh(_merchant.Stock, _localHero, focusBuy: true, 0); // друг купил или продал товар
        }

        CheckNetTestProgress();
    }

    private void ReportDesync(int hostChecksum)
    {
        _desyncs++;
        if (_resyncPending)
            return; // снимок уже попросили — пока он идёт, остальные расхождения ожидаемы
        GD.Print($"СЕТЬ: РАССИНХРОН на раунде {_turns}: у хоста {hostChecksum:X8}, у нас {StateChecksum():X8}");
        _hud.AddMessage("Мир на этом компьютере разошёлся с хостом — восстанавливаем по данным хоста…", DamageColor);
        _resyncPending = true;
        _net.RequestResync();
    }

    // ---------- Восстановление после рассинхрона ----------

    private bool _resyncPending;
    private int _resyncs;

    /// <summary>Хост: клиент просит снимок. Рассылаем его всем — восстановятся все одновременно.</summary>
    private void OnResyncRequested(long peerId)
    {
        if (!_isNetworkGame || _gameOver)
            return;
        byte[] snapshot = SaveSystem.Pack(CreateNetworkSnapshot());
        GD.Print($"СЕТЬ: хост: снимок мира для восстановления, {snapshot.Length} байт");
        _net.BroadcastSnapshot(snapshot);
    }

    /// <summary>Снимок мира для сети: то же, что одиночное сохранение, плюс весь отряд и генератор случайности.</summary>
    private SaveData CreateNetworkSnapshot()
    {
        var data = CreateSaveData();
        data.Party = _heroes.Select(hero => new PartyHeroSave
        {
            Id = hero.Id,
            Name = hero.DisplayName,
            HasActed = hero.HasActed,
            PendingLevelUps = hero.PendingLevelUps,
            State = hero.CreateSave(),
        }).ToList();
        data.RngState = _rng.State;
        data.RngCalls = _rng.Calls;
        data.DescentRequested = _descentRequested;
        return data;
    }

    private void OnSnapshotReceived(byte[] snapshot)
    {
        if (!_isNetworkGame)
            return;
        RestoreNetworkSnapshot(SaveSystem.Unpack(snapshot));
        _resyncPending = false;
        _resyncs++;
        GD.Print($"СЕТЬ: {_netTestRole ?? "игра"}: мир восстановлен по снимку хоста, контрольная сумма {StateChecksum():X8}");
        _hud.AddMessage("Мир восстановлен по данным хоста.", GoodColor);
    }

    /// <summary>
    /// Пересобрать мир из снимка: отряд (с теми же номерами героев), карту, врагов, предметы,
    /// генератор случайности. Своё окно (инвентарь, торговец) закрываем — его содержимое устарело.
    /// </summary>
    private void RestoreNetworkSnapshot(SaveData data)
    {
        int localId = _localHero.Id;
        var bots = _heroes.ToDictionary(hero => hero.Id, hero => hero.IsBot);

        ClearLevelObjects();
        _floor = data.Floor;
        _kills = data.Kills;
        _turns = data.Turns;
        _difficulty = Difficulties.Get(data.Difficulty);
        _rng = GameRandom.FromState(data.RngState, data.RngCalls);
        _descentRequested = data.DescentRequested;

        var party = data.Party
            .Select(save => new HeroSpec(ClassCatalog.Get(save.State.Class), save.Name, bots.GetValueOrDefault(save.Id)))
            .ToList();
        CreateParty(party, Math.Max(0, data.Party.FindIndex(save => save.Id == localId)));

        _map = data.Map.ToMap();
        _renderer.Build(_map);
        ApplyTheme();
        for (int i = 0; i < _heroes.Count; i++)
        {
            var hero = _heroes[i];
            var save = data.Party[i];
            hero.Id = save.Id;
            hero.SetDisplayName(save.Name);
            hero.RestoreSave(save.State, allowDead: true);
            hero.HasActed = save.HasActed;
            hero.PendingLevelUps = save.PendingLevelUps;
        }
        RestoreLevelObjects(data);

        if (_state is GameState.Inventory or GameState.Shop or GameState.LevelUp)
        {
            _inventoryPanel.Visible = false;
            _shopPanel.Visible = false;
            _levelUpPanel.Visible = false;
            _hud.Visible = true;
            _state = GameState.Playing;
        }
        _camera.SnapToTarget();
        UpdateFov();
        UpdateHud();
        TryShowLevelUp();
    }

    /// <summary>
    /// Контрольная сумма мира: число, которое почти наверняка изменится, если хоть что-то
    /// в мире отличается (позиции, здоровье, золото, враги, сколько раз брали случайное число).
    /// unchecked — переполнение int здесь нормально, нам нужно просто «перемешать» числа.
    /// </summary>
    private int StateChecksum()
    {
        unchecked
        {
            long hash = 17;
            void Add(long value) => hash = hash * 31 + value;

            Add(_floor);
            Add(_turns);
            Add(_kills);
            Add(_rng.Calls);
            foreach (var hero in _heroes)
            {
                Add(hero.Id);
                Add(hero.GridPos.X);
                Add(hero.GridPos.Y);
                Add(hero.Hp);
                Add(hero.MaxHp);
                Add(hero.Gold);
                Add(hero.Level);
                Add(hero.Experience);
                foreach (var item in hero.Inventory.Items.Concat(hero.Inventory.Equipped))
                {
                    Add(item.Name.Length); // длина, а не хеш строки: хеш строк в .NET у каждого запуска свой
                    Add(item.Count);
                    Add((int)item.Rarity);
                }
                Add(hero.SkillCooldowns[0]);
                Add(hero.SkillCooldowns[1]);
                Add(hero.HasActed ? 1 : 0);
                Add(hero.PendingLevelUps);
            }
            foreach (var enemy in _enemies)
            {
                Add(enemy.GridPos.X);
                Add(enemy.GridPos.Y);
                Add(enemy.Hp);
            }
            // Сумма не зависит от порядка: словарь у всех один, но так надёжнее и после восстановления
            Add(_floorItems.Keys.Sum(cell => (long)cell.X * 64 + cell.Y));
            Add(_floorItems.Count);
            Add(_chests.Count);
            Add(_merchant?.Stock.Count ?? -1);
            Add(_merchant?.Stock.Sum(item => (long)item.Price) ?? -1);
            Add(_descentRequested ? 1 : 0);
            return (int)(hash ^ (hash >> 32));
        }
    }

    // ---------- Отключения ----------

    /// <summary>Хост: игрок отключился — его герой уходит из отряда у всех сразу (через общий поток).</summary>
    private void OnNetPeerLeft(long peerId)
    {
        if (!_isNetworkGame || _gameOver)
            return;
        foreach (var (heroId, owner) in _heroOwners)
            if (owner == peerId && _heroes.Any(hero => hero.Id == heroId))
                _net.Broadcast(heroId, HeroAction.Leave, StateChecksum());
    }

    /// <summary>Герой покидает отряд. Если остальные уже сходили, раунд заканчивается без него.</summary>
    private void RemoveHero(Player hero)
    {
        if (hero == _localHero)
            return; // своего героя по сети не убирают
        _heroes.Remove(hero);
        hero.QueueFree();
        _hud.AddMessage($"{hero.DisplayName} покидает игру.", InfoColor);

        if (!LivingHeroes.Any())
        {
            GameOver("отряд остался без живых героев");
            return;
        }
        if (TryDescend())
            return; // ушёл как раз тот, кого ждали у лестницы
        if (LivingHeroes.All(other => other.HasActed))
        {
            EndRound();
            return;
        }
        UpdateFov();
        UpdateHud();
    }

    /// <summary>Клиент: связь с хостом пропала посреди забега.</summary>
    private void OnNetDisconnected(string reason)
    {
        if (_netPreparing)
        {
            // Хост пропал, пока шло обучение: забега ещё нет, возвращаем в окно подключения
            ShowMainMenu();
            OpenLobby();
            _lobby.SetConnectStatus(reason);
            return;
        }
        if (!_isNetworkGame)
            return;
        _isNetworkGame = false;
        _gameOver = true;
        _state = GameState.GameOver;
        _pauseMenu.Visible = false;
        _inventoryPanel.Visible = false;
        _shopPanel.Visible = false;
        _levelUpPanel.Visible = false;
        _hud.Visible = true;
        _hud.ShowGameOver($"{reason}\nЗабег прерван.\n\nEsc — в главное меню");
        if (_netTestRole != null)
        {
            GD.Print($"СЕТЬ: {_netTestRole}: {reason}");
            PrintNetTestState();
            GetTree().Quit(1);
        }
    }

    /// <summary>R после конца забега: в сетевой игре новый забег начинает хост тем же составом.</summary>
    private void RestartNetworkRun()
    {
        if (!_net.IsActive)
            return;
        if (!_net.IsHost)
        {
            _hud.AddMessage("Новый забег может начать только хост.");
            return;
        }
        _net.BackToLobby();
        _net.StartGame(_difficulty.Id);
    }
}
