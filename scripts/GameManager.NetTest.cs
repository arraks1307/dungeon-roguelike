using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Roguelike;

/// <summary>
/// Сетевая самопроверка: две (или больше) копии игры на одном компьютере подключаются друг к другу,
/// за каждого героя играет автопилот, а в конце каждая копия печатает контрольную сумму мира.
/// Если миры совпали — суммы одинаковые, а счётчик рассинхронов равен нулю.
///
/// Запуск (каждая копия — отдельным процессом):
///   -- --net-host=2 [--net-rounds=60]          хост ждёт, пока в отряде будет 2 игрока, и начинает
///   -- --net-join=127.0.0.1 [--net-quit-at=20] клиент подключается (и может «уйти» на раунде 20)
/// </summary>
public partial class GameManager
{
    private const ulong NetTestTimeoutMs = 240_000;
    private const ulong NetTestStallMs = 30_000; // столько без единого действия — значит, раунд завис
    private ulong _netTestLastActionMs;

    private string _netTestRole;            // «хост», «клиент» или null — обычная игра
    private int _netTestPlayers = 2;
    private string _netTestAddress = "127.0.0.1";
    private string _netTestName;
    private int _netTestRounds = 60;
    private int _netTestQuitAt = -1;        // клиент отключается на этом раунде (проверка ухода игрока)
    private int _netTestDesyncAt = -1;      // клиент нарочно портит свой мир на этом раунде (проверка восстановления)
    private bool _netTestDesynced;
    private bool _netTestTutorial;          // перед забегом пройти настоящее обучение (его ведёт бот)
    private bool _netTestTrade;             // боты торгуют и выбрасывают вещи
    private bool _netTestUi;                // в лобби входим настоящими кнопками
    private bool _netTestInput;             // в конце хост жмёт настоящие Enter, Esc и правую кнопку мыши
    private bool _netTestDone;
    private ulong _netTestStartMs;

    /// <summary>Случайность для решений бота — своя у каждого компьютера, мир от неё не зависит.</summary>
    private readonly Random _botRng = new();

    private void StartNetTest()
    {
        _net.LoopbackOnly = true;
        _netTestStartMs = Time.GetTicksMsec();
        _net.StatusChanged += status => GD.Print($"СЕТЬ: {_netTestRole}: {status}");

        bool host = _netTestRole == "хост";
        int classIndex = _testClass != null ? Array.IndexOf(ClassCatalog.All, _testClass) : host ? 0 : 2;
        string name = _netTestName ?? (host ? "Хост" : "Гость");
        if (_netTestUi)
        {
            OpenLobby();
            _lobby.Prefill(name, _netTestAddress);
            _lobby.ClickClass(classIndex);
            if (host)
                _lobby.ClickHost();
            else
                _lobby.ClickJoin();
            GD.Print($"СЕТЬ: {_netTestRole}: кнопка лобби нажата, сеть активна: {_net.IsActive}");
            return;
        }
        string error = host ? _net.Host(name, classIndex) : _net.Join(_netTestAddress, name, classIndex);
        GD.Print($"СЕТЬ: {_netTestRole}: {error ?? "сеть запущена"}");
        if (error != null)
            GetTree().Quit(1);
    }

    private void RunNetTest()
    {
        ulong now = Time.GetTicksMsec();
        bool stalled = _isNetworkGame && _netTestLastActionMs > 0 && now - _netTestLastActionMs > NetTestStallMs;
        if (now - _netTestStartMs > NetTestTimeoutMs || stalled && !_netTestDone)
        {
            GD.Print($"СЕТЬ: {_netTestRole}: {(stalled ? "зависание" : "тайм-аут")}, раундов {_turns}");
            PrintNetTestState();
            GetTree().Quit(2);
            return;
        }
        if (_netTestDone)
            return;

        if (!_isNetworkGame && _net.IsHost && _net.Members.Count >= _netTestPlayers)
        {
            if (_netTestUi)
                _lobby.ClickStart();
            else
                _net.StartGame((_testDifficulty ?? Difficulties.Normal).Id); // повторные вызовы хост игнорирует
        }

        _frame++;
        if (InTutorial && _frame % 2 == 0)
        {
            TutorialBotTurn();
            return;
        }
        if (!_isNetworkGame)
            return;
        if (_frame % 2 != 0 || _actionInFlight || _gameOver || _state != GameState.Playing)
            return;
        var hero = _localHero;
        if (hero.IsGhost || hero.HasActed)
            return;
        // Одно действие за раз: отправили — ждём, пока вернётся от хоста. Узнать сразу, сработало ли оно,
        // бот не может, поэтому считает неудачи: после трёх подряд (например, союзник загородил коридор)
        // просто пропускает ход, иначе оба героя упирались бы друг в друга вечно
        if (_botFailures >= 3)
        {
            SubmitLocalAction(HeroAction.Wait, _ => _botFailures = 0);
            return;
        }
        if (_netTestTrade && TryBotTrade(hero))
            return;
        AutoPilotTurn(hero, action =>
        {
            SubmitLocalAction(action, spent => _botFailures = spent || !action.SpendsTurn ? 0 : _botFailures + 1);
            return true;
        }, _botRng);
    }

    private int _botFailures;

    /// <summary>Купить, продать или выбросить вещь: эти действия тоже идут через общий поток.</summary>
    private bool TryBotTrade(Player hero)
    {
        double roll = _botRng.NextDouble();
        int bag = hero.Inventory.Items.Count;
        var gear = hero.Inventory.Equipped.FirstOrDefault();
        if (_merchant != null && roll < 0.05 && gear != null)
            SubmitLocalAction(HeroAction.Upgrade((int)gear.Slot));
        else if (_merchant != null && roll < 0.08 && hero.IsHurt)
            SubmitLocalAction(HeroAction.Treat);
        else if (_merchant != null && roll < 0.1)
            SubmitLocalAction(HeroAction.Mystery);
        else if (_merchant != null && roll < 0.15 && bag > 0)
            SubmitLocalAction(HeroAction.Sell(0));
        else if (_merchant != null && roll < 0.3 && _merchant.Stock.Count > 0)
            SubmitLocalAction(HeroAction.Buy(_botRng.Next(_merchant.Stock.Count), _merchant.Stock.Count));
        else if (roll > 0.96 && bag > 0)
            SubmitLocalAction(HeroAction.Drop(0));
        else
            return false;
        return true;
    }

    /// <summary>Кто чего ждёт — чтобы по логу понять, почему раунд не двигается.</summary>
    private void PrintNetTestState()
    {
        GD.Print($"СЕТЬ: {_netTestRole}:   состояние {_state}, действие в пути {_actionInFlight}, забег окончен {_gameOver}, " +
                 $"сетевая игра {_isNetworkGame}, контрольная сумма {StateChecksum():X8}");
        foreach (var hero in _heroes)
            GD.Print($"СЕТЬ: {_netTestRole}:   {hero.DisplayName}{(hero == _localHero ? " (свой)" : "")}: сходил {hero.HasActed}, " +
                     $"призрак {hero.IsGhost}, улучшений ждёт {hero.PendingLevelUps}, {hero.GridPos}");
    }

    /// <summary>Вызывается после каждого действия из общего потока — у всех копий в одной и той же точке.</summary>
    private void CheckNetTestProgress()
    {
        if (_netTestRole == null || _netTestDone)
            return;
        _netTestLastActionMs = Time.GetTicksMsec();

        if (_netTestDesyncAt >= 0 && !_netTestDesynced && !_net.IsHost && _turns >= _netTestDesyncAt)
        {
            // «Ошибка» только на этом компьютере: лишнее золото и лишнее случайное число
            _netTestDesynced = true;
            _localHero.Gold += 7;
            _rng.Next();
            GD.Print($"СЕТЬ: {_netTestRole}: портим свой мир на раунде {_turns}");
        }

        if (_netTestQuitAt >= 0 && !_net.IsHost && _turns >= _netTestQuitAt)
        {
            _netTestDone = true;
            GD.Print($"СЕТЬ: {_netTestRole}: ухожу из отряда на раунде {_turns}");
            GetTree().Quit();
            return;
        }
        if (_turns < _netTestRounds && !_gameOver)
            return;

        _netTestDone = true;
        GD.Print($"СЕТЬ: {_netTestRole}: раундов {_turns}, этаж {_floor}, убито {_kills}, героев {_heroes.Count}, " +
                 $"случайных чисел {_rng.Calls}, контрольная сумма {StateChecksum():X8}, рассинхронов {_desyncs}, " +
                 $"восстановлений {_resyncs}, сделок {_tradeCount}" +
                 (_gameOver ? " (забег окончен)" : ""));
        foreach (var hero in _heroes)
            GD.Print($"СЕТЬ: {_netTestRole}:   {hero.DisplayName} ({hero.Class.Name}) {hero.GridPos} здоровье {hero.Hp}/{hero.MaxHp} " +
                     $"ур. {hero.Level} золото {hero.Gold}{(hero.IsGhost ? " призрак" : "")}");

        if (_netTestInput && _net.IsHost)
        {
            RunInputTest();
            return;
        }
        if (_screenshotPath != null)
        {
            // Для скриншота показываем чат и метку: пишем сообщение, отмечаем клетку рядом и ждём секунду
            _net.SendChat("Иду к лестнице, прикройте!");
            var near = DungeonMap.Directions.Select(d => _localHero.GridPos + d * 2).FirstOrDefault(_map.IsWalkable, _localHero.GridPos);
            _net.SendPing(near);
            GetTree().CreateTimer(1.2).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
                GetTree().CreateTimer(2.0).Timeout += () => GetTree().Quit();
            };
            return;
        }
        // Хост ждёт подольше, чтобы последние сообщения успели дойти до клиентов
        GetTree().CreateTimer(_net.IsHost ? 3.0 : _netTestInput ? 8.0 : 0.5).Timeout += () => GetTree().Quit();
    }

    private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private static void PressKey(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private static void TypeText(string text)
    {
        foreach (char letter in text)
            Input.ParseInputEvent(new InputEventKey { Unicode = letter, Pressed = true });
    }

    /// <summary>
    /// Настоящий ввод, а не вызовы методов: события идут тем же путём, что от клавиатуры и мыши.
    /// Проверяем чат (открыть, закрыть по Esc, отправить) и метку правой кнопкой мыши.
    /// </summary>
    private async void RunInputTest()
    {
        PressKey(Key.Enter);
        await Delay(0.4);
        GD.Print($"ВВОД: Enter → чат открыт: {_hud.IsChatOpen}, состояние {_state}");

        TypeText("черновик");
        await Delay(0.2);
        PressKey(Key.Escape);
        await Delay(0.4);
        GD.Print($"ВВОД: Esc → чат открыт: {_hud.IsChatOpen}, состояние {_state}");

        PressKey(Key.Enter);
        await Delay(0.4);
        TypeText("Привет, отряд");
        await Delay(0.2);
        PressKey(Key.Enter);
        await Delay(0.6);
        GD.Print($"ВВОД: отправка → чат открыт: {_hud.IsChatOpen}, состояние {_state}");

        var cell = DungeonMap.Directions.Select(d => _localHero.GridPos + d).First(_map.IsWalkable);
        var position = _camera.UnprojectPosition(Actor.GridToWorld(cell));
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = position, GlobalPosition = position });
        await Delay(0.8);
        GD.Print($"ВВОД: правый клик по клетке {cell}");

        if (_screenshotPath != null)
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
        await Delay(1.5);
        GetTree().Quit();
    }
}
