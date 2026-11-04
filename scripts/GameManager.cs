using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

public enum GameState
{
    Menu,
    Playing,
    Inventory,
    Shop,
    LevelUp,
    Paused,
    GameOver,
    Chat,      // пишем сообщение отряду: клавиши идут в строку чата, а не герою
}

/// <summary>Кто входит в отряд: класс, имя и (для самопроверки) управляет ли героем автопилот.</summary>
public record HeroSpec(ClassDef Class, string Name = null, bool Bot = false);

/// <summary>
/// Главный скрипт игры (висит на корневом узле Main.tscn).
/// Создаёт мир, управляет состояниями, раундами ходов, боем, этажами и рестартом.
///
/// Отряд — от 1 до 4 героев. Каждый раунд каждый живой герой действует ОДИН раз и сразу
/// (без ожидания), а когда сходили все — ходят враги. Одиночная игра — это отряд из одного героя,
/// поэтому для неё работает тот же самый код.
///
/// Класс разбит на части с помощью partial:
///   GameManager.cs          — ядро: отряд, раунды, бой, этажи;
///   GameManager.Items.cs    — предметы, золото, торговец;
///   GameManager.Skills.cs   — способности и улучшения;
///   GameManager.Dungeon.cs  — двери, ловушки, сундуки, темы;
///   GameManager.Save.cs     — сохранение (только одиночная игра);
///   GameManager.Graphics.cs — окружение и качество графики;
///   GameManager.SelfTest.cs — самопроверка и симуляция.
/// </summary>
public partial class GameManager : Node3D, IGameWorld
{
    public const int MaxPartySize = 4;

    private const int BossFloorInterval = 5;
    private const int MapWidth = 48;
    private const int MapHeight = 32;
    private const int FovRadius = 8;
    private const int ChaseDistance = 20;
    private const float MinDamageShare = 0.35f;
    // Сила врагов в отряде — за каждого героя сверх первого (подобрано симуляциями, см. --party-tune)
    private float _partyHpPerHero = 0.6f;      // здоровье врагов: +60%
    private float _partyEnemiesPerHero = 0.3f; // число врагов на этаже: +30%
    private float _partyDamagePerHero = 0.1f;  // урон врагов: +10%

    /// <summary>
    /// «Вес» героев сверх первого для отряда из 1, 2, 3, 4 героев. Выгода отряда растёт быстрее
    /// числа героев (больше урона, опыт — всем), но не ровно: втроём оказалось заметно легче, чем
    /// вчетвером. Поэтому вес задан таблицей, подобранной симуляциями (см. --party-weights).
    /// </summary>
    private float[] _partyWeights = { 0f, 1f, 2.7f, 3.7f };

    private float ExtraHeroWeight => _partyWeights[Math.Clamp(_heroes.Count - 1, 0, _partyWeights.Length - 1)];

    private static readonly Color DamageColor = new(1f, 0.45f, 0.4f);
    private static readonly Color GoodColor = new(0.5f, 1f, 0.5f);
    private static readonly Color InfoColor = new(1f, 0.85f, 0.4f);
    private static readonly Color CritColor = new(1f, 0.9f, 0.2f);

    /// <summary>Случайность мира. В сетевой игре — с общим зерном, поэтому у всех одинаковая.</summary>
    private GameRandom _rng = new();
    private readonly List<Enemy> _enemies = new();
    private readonly List<Player> _heroes = new();
    private HashSet<Vector2I> _visible = new();
    private Dictionary<Vector2I, int> _playerDistances = new();
    private Enemy _boss;

    /// <summary>Герой, которым управляют с этого компьютера: за ним следят камера, HUD и окна.</summary>
    private Player _localHero;
    private List<HeroSpec> _partySpec = new();

    private DungeonMap _map;
    private DungeonRenderer _renderer;
    private CameraRig _camera;
    private Hud _hud;
    private MainMenu _mainMenu;
    private PauseMenu _pauseMenu;
    private InventoryPanel _inventoryPanel;
    private ShopPanel _shopPanel;
    private SettingsMenu _settingsMenu;
    private SoundManager _sound;

    /// <summary>Состояние интерфейса ЭТОГО компьютера (меню, инвентарь, пауза...). На других героев не влияет.</summary>
    private GameState _state;

    /// <summary>Забег окончен (все погибли или победа). В отличие от _state, это общее для всего отряда.</summary>
    private bool _gameOver;

    private DifficultyDef _difficulty = Difficulties.Normal;
    private int _floor;
    private int _kills;
    private int _turns;
    private bool _victory;

    private bool IsSinglePlayer => _heroes.Count == 1;
    private IEnumerable<Player> LivingHeroes => _heroes.Where(hero => !hero.IsGhost);

    // ---------- Реализация IGameWorld (то, что видят враги) ----------

    public DungeonMap Map => _map;
    public Dictionary<Vector2I, int> PlayerDistances => _playerDistances;

    public Player NearestTarget(Vector2I from) => _heroes
        .Where(hero => !hero.IsGhost && !hero.IsInvisible && !hero.IsOffline)
        .OrderBy(hero => ChebyshevDistance(hero.GridPos, from))
        .FirstOrDefault();

    /// <summary>Клетку занимают живые герои (призраки — нет), враги, сундуки и торговец.</summary>
    public bool IsOccupied(Vector2I cell) =>
        LivingHeroes.Any(hero => hero.GridPos == cell) || EnemyAt(cell) != null || _chests.ContainsKey(cell) || _shrines.ContainsKey(cell) ||
        (_merchant != null && _merchant.GridPos == cell);

    public bool IsVisibleToPlayer(Vector2I cell) => _visible.Contains(cell);

    public void EnemyAttacksPlayer(Enemy enemy, Player target, bool ranged, float power = 1f)
    {
        target = FindCover(target);
        if (ranged && power == 1f)
        {
            Effects.Projectile(this, enemy.GridPos, target.GridPos, enemy.Type.Color);
            _sound.Play(Sound.Shoot);
        }
        else
        {
            enemy.PlayAttack(target.GridPos);
        }

        int damage = ApplyGuard(target, (int)MathF.Round(RollDamage(enemy, target) * power), ranged);
        target.TakeDamage(damage);
        if (IsInvulnerable)
            target.Heal(damage);
        Effects.FloatingText(this, target.GridPos, $"-{damage}", DamageColor);
        _sound.Play(Sound.Hurt);
        if (IsLocalHero(target))
            _camera.Shake(0.12f);
        string verb = ranged ? "стреляет в тебя" : "бьёт тебя";
        Tell(target, $"{enemy.DisplayName} {verb}: -{damage}.", $"{target.DisplayName} получает {damage} урона ({enemy.DisplayName}).", DamageColor);

        if (target.IsDead)
        {
            OnHeroDied(target, enemy.DisplayName);
            return;
        }

        // Шипы ранят того, кто ударил вплотную
        int thorns = target.Bonuses.Thorns;
        if (!ranged && thorns > 0)
        {
            enemy.TakeDamage(thorns);
            Effects.FloatingText(this, enemy.GridPos, thorns.ToString(), new Color(0.8f, 1f, 0.6f));
            if (enemy.IsDead)
                KillEnemy(enemy);
        }
    }

    public void Announce(string text)
    {
        _hud.AddMessage(text, DamageColor);
        _camera.Shake(0.3f);
        _sound.Play(Sound.Summon);
    }

    public void SummonMinions(Enemy summoner, int count, EnemyType type)
    {
        var minionTypes = Enemy.Types.Where(t => t.MinFloor == 1).ToArray();
        int summoned = 0;
        foreach (var direction in DungeonMap.Directions)
        {
            var cell = summoner.GridPos + direction;
            if (summoned == count || !_map.CanMove(summoner.GridPos, cell) || IsOccupied(cell))
                continue;
            var minion = SpawnEnemy(type ?? minionTypes[_rng.Next(minionTypes.Length)], cell);
            minion.Alerted = true;
            minion.IsSummoned = true;
            Effects.Burst(this, cell, new Color(0.6f, 0.2f, 0.8f), 14);
            summoned++;
        }
        if (summoned > 0)
        {
            _hud.AddMessage($"{summoner.DisplayName} призывает подмогу!", DamageColor);
            _sound.Play(Sound.Summon);
        }
    }

    // ---------- Создание сцены ----------

    public override void _Ready()
    {
        ParseCommandLine(); // первым делом: самопроверка подменяет путь к сохранению
        Settings.Load();
        if (_testGraphics.HasValue)
            Settings.Current.Graphics = _testGraphics.Value; // только в памяти, файл настроек не меняется
        Settings.Apply();
        SetupEnvironment();

        _sound = new SoundManager();
        AddChild(_sound);
        Ui.PlaySound = _sound.Play; // кнопки интерфейса щёлкают через общий SoundManager

        _renderer = new DungeonRenderer();
        AddChild(_renderer);

        _camera = new CameraRig();
        AddChild(_camera);

        _hud = new Hud();
        AddChild(_hud);

        _inventoryPanel = new InventoryPanel();
        AddChild(_inventoryPanel);
        _inventoryPanel.UseRequested += OnInventoryUse;

        _shopPanel = new ShopPanel();
        AddChild(_shopPanel);
        _shopPanel.BuyRequested += OnShopBuy;
        _shopPanel.SellRequested += OnShopSell;
        _shopPanel.ServiceRequested += OnShopService;
        _shopPanel.Services = BuildServices;

        _levelUpPanel = new LevelUpPanel();
        AddChild(_levelUpPanel);
        _levelUpPanel.PerkChosen += OnPerkChosen;

        _classSelect = new ClassSelectPanel();
        AddChild(_classSelect);
        _classSelect.ClassChosen += StartNewGame;

        _mainMenu = new MainMenu();
        AddChild(_mainMenu);
        _mainMenu.NewGameRequested += OpenClassSelect;
        _mainMenu.ContinueRequested += ContinueGame;
        _mainMenu.SettingsRequested += OpenSettings;
        _mainMenu.NetworkRequested += OpenLobby;

        _pauseMenu = new PauseMenu();
        AddChild(_pauseMenu);
        _pauseMenu.ResumeRequested += Resume;
        _pauseMenu.SettingsRequested += OpenSettings;
        _pauseMenu.SaveAndExitRequested += SaveAndExit;

        _settingsMenu = new SettingsMenu();
        AddChild(_settingsMenu);
        _settingsMenu.Closed += OnSettingsClosed;

        SetupNetwork();
        SetupCoop();
        SetupTutorial();
        ShowMainMenu();
        if (!IsTestRun && DisplayServer.GetName() != "headless")
            _sound.StartBackground();
        if (RunSoundTools())
            return;
        if (_netTestRole != null)
            StartNetTest();
    }

    public override void _Process(double delta)
    {
        if (_netTestRole != null)
            RunNetTest();
        else if (_simulateGames > 0)
            RunSimulation();
        else if (_screenshotPath != null)
            RunSelfTest();
    }

    /// <summary>
    /// Клавиши уровня всей игры. Движение обрабатывает Player, а здесь — Esc, R, M, I, X, F, G, 1–5 и колёсико.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } mouse && _state == GameState.Playing)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp)
                _camera.Zoom(-1);
            else if (mouse.ButtonIndex == MouseButton.WheelDown)
                _camera.Zoom(1);
            else if (mouse.ButtonIndex == MouseButton.Right && _isNetworkGame)
                TryPing(mouse.Position);
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        if (_settingsMenu.Visible)
        {
            if (key.PhysicalKeycode == Key.Escape)
            {
                _settingsMenu.Close();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        switch (key.PhysicalKeycode)
        {
            case Key.Escape when _tutorialPanel.Visible:
                _tutorialPanel.Cancel();
                break;
            case Key.Tab when InTutorial && _state == GameState.Playing:
                FinishTutorial();
                break;
            case Key.Escape when _state == GameState.Menu && _lobby.Visible:
                if (_lobby.IsInRoom)
                    LeaveLobby();
                else
                    CloseLobby();
                break;
            case Key.Escape when _state == GameState.Menu && _classSelect.Visible:
                if (_classSelect.IsConfirming)
                    _classSelect.CancelConfirm(); // с подтверждения — обратно к карточкам
                else
                    CloseClassSelect();
                break;
            case Key.Enter or Key.KpEnter when _state == GameState.Playing && _isNetworkGame:
                OpenChat();
                break;
            case Key.F when _state == GameState.Playing:
                PerformLocalAction(HeroAction.Skill(0));
                break;
            case Key.G when _state == GameState.Playing:
                PerformLocalAction(HeroAction.Skill(1));
                break;
            case Key.Escape:
                switch (_state)
                {
                    case GameState.Menu: _mainMenu.GoBack(); break;
                    case GameState.Playing: Pause(); break;
                    case GameState.Inventory: CloseInventory(); break;
                    case GameState.Shop: CloseShop(); break;
                    case GameState.Paused:
                        if (!_pauseMenu.CloseControls())
                            Resume();
                        break;
                    case GameState.Chat: CloseChat(); break;
                    case GameState.GameOver: ShowMainMenu(); break;
                }
                break;
            case Key.I when _state == GameState.Playing:
                OpenInventory();
                break;
            case Key.I when _state == GameState.Inventory:
                CloseInventory();
                break;
            case Key.T when _state == GameState.Inventory:
                SubmitLocalAction(HeroAction.GiveItem(_inventoryPanel.FocusedIndex));
                break;
            case Key.Y when _state == GameState.Inventory:
                SubmitLocalAction(HeroAction.GiveGold);
                break;
            case >= Key.Key6 and <= Key.Key8 when _state == GameState.Playing && _isNetworkGame:
                _net.SendChat(QuickPhrases[key.PhysicalKeycode - Key.Key6]);
                break;
            case Key.X when _state == GameState.Inventory:
                SubmitLocalAction(HeroAction.Drop(_inventoryPanel.FocusedIndex));
                break;
            case >= Key.Key1 and <= Key.Key5 when _state == GameState.Playing:
                QuickUse((int)(key.PhysicalKeycode - Key.Key1));
                break;
            case Key.R when _state == GameState.GameOver:
                if (_net.IsActive)
                    RestartNetworkRun();
                else
                    OfferTutorial(_partySpec[0].Class, null, () => StartRun(_partySpec, _difficulty)); // тем же составом и на той же сложности
                break;
            case Key.M when _state == GameState.Playing:
                _hud.ToggleMinimap();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    // ---------- Отряд ----------

    /// <summary>
    /// Создать героев отряда. localIndex — какой из них управляется с этого компьютера.
    /// Если два героя одного класса, к имени добавляется номер: «Маг», «Маг 2».
    /// </summary>
    private void CreateParty(IReadOnlyList<HeroSpec> specs, int localIndex)
    {
        foreach (var old in _heroes)
            old.QueueFree();
        _heroes.Clear();

        for (int i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var hero = new Player { Id = i, IsLocal = i == localIndex, IsBot = spec.Bot };
            AddChild(hero);
            hero.ResetStats(spec.Class);

            string name = spec.Name ?? spec.Class.Name;
            int sameName = _heroes.Count(other => other.DisplayName == name || other.DisplayName.StartsWith(name + " "));
            hero.SetDisplayName(sameName == 0 ? name : $"{name} {sameName + 1}");
            hero.SetHighQuality(Settings.HighQuality);

            // Клавиатура: события придут только от своего героя (остальные их не посылают)
            hero.ActionRequested += direction => OnPlayerPressed(hero, direction);
            hero.WalkRequested += direction => OnWalkRequested(hero, direction);
            _heroes.Add(hero);
        }

        _localHero = _heroes[localIndex];
        _camera.Target = _localHero;
    }

    private bool IsLocalHero(Player hero) => hero == _localHero;

    /// <summary>
    /// Сообщение о личном событии героя: своему — в форме «ты», о других — с именем.
    /// Русские глаголы настоящего времени не меняются по родам («Маг подбирает»), поэтому
    /// вторую форму легко написать для любого героя.
    /// </summary>
    private void Tell(Player hero, string youText, string otherText, Color? color = null) =>
        _hud.AddMessage(IsLocalHero(hero) ? youText : otherText, color);

    /// <summary>Расставить отряд у входа на этаж: первый — на старт, остальные — в ближайшие свободные клетки.</summary>
    private void PlaceParty()
    {
        var taken = new HashSet<Vector2I>();
        var distances = _map.DistanceField(_map.StartPos, 6);
        var cells = distances.Keys
            .Where(cell => _map.Get(cell) == Tile.Floor)
            .OrderBy(cell => distances[cell])
            .ToList();
        cells.Remove(_map.StartPos);
        cells.Insert(0, _map.StartPos);

        foreach (var hero in _heroes)
        {
            var cell = cells.FirstOrDefault(c => !taken.Contains(c) && EnemyAt(c) == null, _map.StartPos);
            taken.Add(cell);
            hero.Teleport(cell);
        }
    }

    // ---------- Состояния игры ----------

    /// <summary>Главное меню поверх случайного подземелья-заставки.</summary>
    private void ShowMainMenu()
    {
        LeaveNetwork(); // из сетевой игры выход в меню — это выход из отряда
        AbandonTutorial();
        _state = GameState.Menu;
        _gameOver = false;
        ClearLevelObjects();

        _map = DungeonGenerator.Generate(MapWidth, MapHeight, _rng, themeIndex: _rng.Next(Themes.All.Length));
        _renderer.Build(_map);
        ApplyTheme();

        // На заставке показываем всю карту целиком, без тумана войны
        var everything = new HashSet<Vector2I>();
        for (int x = 0; x < _map.Width; x++)
        {
            for (int y = 0; y < _map.Height; y++)
            {
                _map.Explored[x, y] = true;
                everything.Add(new Vector2I(x, y));
            }
        }
        _renderer.UpdateVisibility(everything);

        foreach (var hero in _heroes)
            hero.Visible = false;
        _hud.Visible = false;
        _pauseMenu.Visible = false;
        _inventoryPanel.Visible = false;
        _shopPanel.Visible = false;
        _levelUpPanel.Visible = false;
        _classSelect.Visible = false;
        _lobby.Visible = false;
        _camera.MenuMode = true;
        _camera.MenuCenter = new Vector3(MapWidth / 2f, 0f, MapHeight / 2f);
        UiTheme.SetAccent(UiTheme.DefaultAccent);
        _camera.SnapToTarget();
        _mainMenu.ShowMain();
    }

    /// <summary>Одиночная игра — отряд из одного героя.</summary>
    private void StartNewGame(ClassDef playerClass, DifficultyDef difficulty)
    {
        _isNetworkGame = false;
        _classSelect.Visible = false;
        OfferTutorial(playerClass, null, () => StartRun(new List<HeroSpec> { new(playerClass) }, difficulty));
    }

    private void StartRun(List<HeroSpec> party, DifficultyDef difficulty, int localIndex = 0)
    {
        _partySpec = party;
        _state = GameState.Playing;
        _gameOver = false;
        _difficulty = difficulty;
        _floor = _startFloor - 1;
        _kills = 0;
        _turns = 0;
        _victory = false;
        _mainMenu.Visible = false;
        _classSelect.Visible = false;
        _hud.Visible = true;
        _hud.HideGameOver();
        _hud.ClearMessages();
        _hud.CloseChat();
        _hud.HideTutorial();
        _camera.MenuMode = false;

        CreateParty(party, localIndex);
        foreach (var hero in _heroes)
            for (int i = 0; i < difficulty.BonusPotions; i++)
                hero.Inventory.TryAdd(new Item(ItemCatalog.HealthPotion));
        UiTheme.SetAccent(_localHero.Class.Color.Lightened(0.15f)); // интерфейс окрашивается в цвет своего героя

        NextFloor();
        string who = IsSinglePlayer ? $"{_localHero.Class.Name} спускается" : $"Отряд из {_heroes.Count} героев спускается";
        _hud.AddMessage($"{who} в подземелье.", InfoColor);
    }

    private void Pause()
    {
        _state = GameState.Paused;
        _pauseMenu.Open(InTutorial ? "Выйти в меню" : _isNetworkGame ? "Покинуть отряд и выйти в меню" : "Сохранить и выйти в меню");
    }

    private void Resume()
    {
        _state = GameState.Playing;
        _pauseMenu.Visible = false;
    }

    private void SaveAndExit()
    {
        SaveRun();
        ShowMainMenu();
    }

    /// <summary>Настройки открываются и из главного меню, и из паузы; на время прячем меню под ними.</summary>
    private void OpenSettings()
    {
        _mainMenu.Visible = false;
        _pauseMenu.Visible = false;
        _settingsMenu.Open();
    }

    private void OnSettingsClosed()
    {
        if (_state == GameState.Menu)
            _mainMenu.ShowMain();
        else if (_state == GameState.Paused)
            _pauseMenu.Open();
    }

    /// <summary>
    /// Герой погиб. Если остальные живы — он призрак до следующего этажа;
    /// если погибли все — забег окончен.
    /// </summary>
    private void OnHeroDied(Player hero, string killerName)
    {
        if (_heroes.All(other => other.IsGhost || other.IsOffline))
        {
            GameOver(killerName);
            return;
        }
        Effects.Burst(this, hero.GridPos, new Color(0.75f, 0.85f, 1f), 30);
        _sound.Play(Sound.Death);
        Tell(hero,
            "Ты погибаешь — и становишься призраком. Оживёшь, когда отряд спустится на следующий этаж.",
            $"{hero.DisplayName} погибает и становится призраком до следующего этажа.",
            DamageColor);
    }

    private void GameOver(string killerName)
    {
        _gameOver = true;
        _state = GameState.GameOver;
        if (_isNetworkGame)
            SaveSystem.DeleteNetwork();
        else
            SaveSystem.Delete(); // смерть окончательна — продолжить этот забег нельзя
        CloseLocalWindows();
        UpdateHud();
        _sound.Play(Sound.Death);
        _camera.Shake(0.4f);

        string title = IsSinglePlayer ? $"Ты погиб на этаже {_floor}" : $"Отряд погиб на этаже {_floor}";
        _hud.ShowGameOver(
            $"{title}\n" +
            $"Последний удар: {killerName}\n" +
            $"Побеждено врагов: {_kills}   Золото: {_localHero.Gold}\n" +
            RecordScore() + PartySummary() + "\n" +
            RestartHint);
    }

    private string RestartHint => _isNetworkGame && !_net.IsHost
        ? "Новый забег начинает хост  •  Esc — в главное меню"
        : "R — начать заново  •  Esc — в главное меню";

    /// <summary>
    /// Забег закончился, пока у этого игрока было открыто окно (в сетевой игре это мог сделать друг):
    /// закрываем окна, чтобы был виден экран итогов.
    /// </summary>
    private void CloseLocalWindows()
    {
        _pauseMenu.Visible = false;
        _inventoryPanel.Visible = false;
        _shopPanel.Visible = false;
        _levelUpPanel.Visible = false;
        _settingsMenu.Visible = false;
        _hud.CloseChat();
        _hud.Visible = true;
    }

    /// <summary>Финальный босс повержен — забег пройден.</summary>
    private void Victory()
    {
        _gameOver = true;
        _state = GameState.GameOver;
        _victory = true;
        if (_isNetworkGame)
            SaveSystem.DeleteNetwork();
        else
            SaveSystem.Delete();
        CloseLocalWindows();
        UpdateHud();
        _sound.Play(Sound.LevelUp);
        _camera.Shake(0.5f);

        string heroes = IsSinglePlayer
            ? $"{_localHero.Class.Name} {_localHero.Level}-го уровня"
            : string.Join(", ", _heroes.Select(hero => $"{hero.DisplayName} ({hero.Level} ур.)"));
        _hud.ShowGameOver(
            "ПОБЕДА!\n\n" +
            "Владыка бездны повержен, и подземелье затихает.\n" +
            $"{heroes}, сложность: {_difficulty.Name.ToLower()}\n" +
            $"Побеждено врагов: {_kills}   Золото: {_localHero.Gold}   Ходов: {_turns}\n" +
            RecordScore() + PartySummary() + "\n" +
            RestartHint);
    }

    /// <summary>Записывает результат в таблицу рекордов и возвращает строку о месте в ней (только одиночная игра).</summary>
    private string RecordScore()
    {
        if (IsTestRun || !IsSinglePlayer || _isNetworkGame)
            return ""; // самопроверка, симуляция и игра отрядом не засоряют таблицу одиночных рекордов
        int place = HighScores.Add(new ScoreEntry(_floor, _kills, _localHero.Level, DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
            _localHero.Class.Name, _difficulty.Name, _victory));
        return place switch
        {
            1 => "Новый рекорд!\n",
            > 1 => $"Место в таблице рекордов: {place}\n",
            _ => "",
        };
    }

    private void NextFloor()
    {
        _floor++;
        _descentRequested = false;
        _roundActions = 0;
        ClearLevelObjects();

        bool bossFloor = _floor % BossFloorInterval == 0;
        int themeIndex = Themes.IndexFor(_floor);
        _map = DungeonGenerator.Generate(MapWidth, MapHeight, _rng, themeIndex, withBossRoom: bossFloor);
        _renderer.Build(_map);
        ApplyTheme();

        // Новый этаж: призраки оживают, у всех новый раунд
        foreach (var hero in _heroes)
        {
            if (hero.IsGhost)
                hero.Revive();
            hero.HasActed = false;
            hero.Visible = true;
        }
        PlaceParty();
        _camera.SnapToTarget();
        if (!IsTestRun)
            _hud.FadeFromBlack(); // камера перескочила на новый этаж — прячем скачок за затемнением

        int enemyCount = Math.Min(2 + _floor * 3 / 2, 22); // подобрано по симуляциям
        enemyCount = (int)MathF.Round(enemyCount * (1f + _partyEnemiesPerHero * ExtraHeroWeight + _difficulty.ExtraEnemies));
        SpawnEnemies(bossFloor ? enemyCount / 2 : enemyCount);
        SpawnChests();
        SpawnShrines();
        SpawnPlates();
        SpawnItems(_rng.Next(3, 6) + _difficulty.ItemsPerFloor + (_heroes.Count - 1));
        SpawnGold(_rng.Next(2, 5) + (_heroes.Count - 1));
        if (!bossFloor)
            SpawnTraps();
        if (bossFloor)
            SpawnBoss();
        else if (ShouldSpawnMerchant())
            SpawnMerchant();
        MaybeSplitParty(bossFloor);

        UpdateFov();
        UpdateHud();
        if (bossFloor && _boss.IsFinalBoss)
            _hud.AddMessage("Здесь, в глубине, ждёт Владыка бездны. Победи его — и подземелье будет пройдено!", DamageColor);
        else if (bossFloor)
            _hud.AddMessage($"Ты чувствуешь присутствие чего-то огромного... Здесь {_boss.DisplayName}. Лестница откроется после победы.", DamageColor);
        else if (_floor == 1 || Themes.IndexFor(_floor - 1) != themeIndex)
            _hud.AddMessage($"Этаж {_floor}: {_map.Theme.Name}.", InfoColor);
        else
            _hud.AddMessage($"Этаж {_floor}. Здесь опаснее.", InfoColor);
        if (_merchant != null)
            _hud.AddMessage("Где-то на этаже звенят монеты — здесь есть торговец.", InfoColor);

        SaveRun(); // автосохранение в начале каждого этажа
        SaveNetworkRun();
    }

    /// <summary>
    /// Босс стоит в последней комнате рядом с лестницей. Пока он жив, клетка лестницы — обычный пол.
    /// </summary>
    private void SpawnBoss()
    {
        var stairs = _map.StairsPos;
        _map.Tiles[stairs.X, stairs.Y] = Tile.Floor;

        var cell = stairs + new Vector2I(0, -2);
        if (!_map.IsWalkable(cell) || IsOccupied(cell))
            cell = stairs;
        var blocker = EnemyAt(cell);
        if (blocker != null)
        {
            // Редкий случай: на месте босса уже стоит обычный враг — убираем его
            _enemies.Remove(blocker);
            blocker.QueueFree();
        }

        _boss = SpawnEnemy(Enemy.BossForFloor(_floor), cell);
    }

    private void ClearLevelObjects()
    {
        foreach (var enemy in _enemies)
            enemy.QueueFree();
        _enemies.Clear();
        foreach (var pickup in _floorItems.Values)
            pickup.QueueFree();
        _floorItems.Clear();
        foreach (var chest in _chests.Values)
            chest.QueueFree();
        _chests.Clear();
        foreach (var shrine in _shrines.Values)
            shrine.QueueFree();
        _shrines.Clear();
        ClearPlates();
        _merchant?.QueueFree();
        _merchant = null;
        _boss = null;
    }

    // ---------- Раунд: действия героев ----------

    /// <summary>
    /// Ход ЛЮБОГО героя: клавиатура своего героя, автопилот или действие друга из сети.
    /// Возвращает true, если ход потрачен. Невалидное действие (шаг в стену, зелье при полном
    /// здоровье) ход не тратит. «Свободные» действия — в ApplyHeroAction (GameManager.Network.cs).
    /// </summary>
    private bool PerformHeroAction(Player hero, HeroAction action)
    {
        if (_gameOver)
            return false;
        if (hero.IsGhost)
            return PerformGhostAction(hero, action);

        _attacker = hero;
        hero.IsGuarding = false; // стойка держится только до следующего действия
        int floorBefore = _floor;
        bool spent = action.Kind switch
        {
            HeroActionKind.Move => TryMove(hero, action.Direction),
            HeroActionKind.Skill => TryUseSkill(hero, action.Index),
            HeroActionKind.UseItem => UseItem(hero, action.Index),
            _ => false,
        };
        _attacker = null;
        if (spent && !_gameOver && _floor == floorBefore)
            TryDescend(); // герой подошёл к лестнице, у которой его ждали

        // Не потратил ход, забег окончен или отряд уже на новом этаже (там начался новый раунд)
        if (!spent || _gameOver || _floor != floorBefore)
        {
            if (!_gameOver)
            {
                UpdateFov();
                UpdateHud();
            }
            return spent;
        }

        hero.HasActed = true;
        CountPartyAction();
        return true;
    }

    /// <summary>Шаг, атака, сундук или дверь. Возвращает true, если ход потрачен.</summary>
    private bool TryMove(Player hero, Vector2I direction)
    {
        if (direction == Vector2I.Zero)
        {
            hero.IsGuarding = true; // пропуск хода — это защитная стойка
            Effects.FloatingText(this, hero.GridPos, "Стойка", InfoColor);
            return true;
        }

        var target = hero.GridPos + direction;
        if (!_map.CanMove(hero.GridPos, target))
            return false; // упёрлись в стену — ход не тратится

        if (_merchant != null && target == _merchant.GridPos)
        {
            if (IsLocalHero(hero) && !hero.IsBot)
                OpenShop(); // разговор с торговцем не тратит ход
            return false;
        }

        var enemy = EnemyAt(target);
        if (enemy != null)
        {
            AttackEnemy(hero, enemy);
            return true;
        }
        if (_chests.TryGetValue(target, out var chest))
        {
            OpenChest(hero, chest);
            return true;
        }
        if (_shrines.TryGetValue(target, out var shrine))
            return UseShrine(hero, shrine);
        if (_map.Get(target) == Tile.Door)
        {
            OpenDoor(target); // открыть дверь — отдельное действие, сам шаг будет следующим ходом
            return true;
        }
        var ghost = _heroes.Find(other => other.IsGhost && other.GridPos == target);
        if (ghost != null)
        {
            ReviveStep(hero, ghost);
            return true;
        }
        if (LivingHeroes.Any(other => other.GridPos == target))
            return false; // там стоит союзник

        hero.MoveTo(target);
        _sound.Play(Sound.Step);
        TryPickUpItem(hero, target);
        CheckPlates(hero);
        CheckTrap(hero, target);
        if (_gameOver || hero.IsGhost)
            return true; // погиб на шипах
        if (_map.Get(hero.GridPos) == Tile.Stairs)
            OnHeroReachedStairs(hero);
        return true;
    }

    /// <summary>
    /// Ручное нажатие клавиши движения. Если враг был виден до или после этого шага,
    /// удержание клавиши не запустит автоходьбу — в бою каждый ход делается осознанно.
    /// </summary>
    private void OnPlayerPressed(Player hero, Vector2I direction)
    {
        if (!IsLocalHero(hero))
            return;
        bool enemyBefore = AnyEnemyVisible();
        // В сетевой игре шаг выполнится чуть позже (когда вернётся от хоста) — проверку делаем тогда
        PerformLocalAction(HeroAction.Move(direction), _ =>
        {
            if (enemyBefore || AnyEnemyVisible())
                StopWalking("враг в поле зрения при нажатии");
        });
    }

    /// <summary>
    /// Умная ходьба (клавиша зажата): делаем шаг, только если это спокойное перемещение,
    /// и останавливаемся, как только происходит что-то, на что игрок должен обратить внимание.
    /// Атаковать, открывать двери и сундуки при удержании нельзя — только отдельным нажатием.
    /// В отряде, пока остальные не сходили, ходьба просто ждёт следующего раунда.
    /// </summary>
    private void OnWalkRequested(Player hero, Vector2I direction)
    {
        if (!IsLocalHero(hero) || _actionInFlight)
            return; // предыдущий шаг ещё в пути — пропускаем этот «тик» ходьбы

        var target = hero.GridPos + direction;
        string blocker = _state != GameState.Playing ? "открыто окно"
            : hero.IsGhost ? (_map.InBounds(target) ? null : "край карты")
            : AnyEnemyVisible() ? "виден враг"
            : !_map.CanMove(hero.GridPos, target) ? "стена"
            : IsOccupied(target) ? "клетка занята"
            : _map.Get(target) == Tile.Door ? "закрытая дверь"
            : null;
        if (blocker != null)
        {
            StopWalking(blocker);
            return;
        }

        int hpBefore = hero.Hp;
        int floorBefore = _floor;
        int interestingBefore = VisibleInterestingThings();
        bool steppingOnSomething = _floorItems.ContainsKey(target) || _map.Get(target) == Tile.Stairs;

        PerformLocalAction(HeroAction.Move(direction), _ =>
        {
            string reason = _state != GameState.Playing ? "открылось окно или смерть"
                : _floor != floorBefore ? "новый этаж"
                : hero.Hp < hpBefore ? "получен урон"
                : steppingOnSomething ? "наступил на предмет или лестницу"
                : AnyEnemyVisible() ? "показался враг"
                : VisibleInterestingThings() > interestingBefore ? "показался предмет, сундук или торговец"
                : null;
            if (reason != null)
                StopWalking(reason);
        });
    }

    /// <summary>Причина последней остановки — её печатает самопроверка, чтобы ловить ложные остановки.</summary>
    private string _lastWalkStopReason;

    private void StopWalking(string reason)
    {
        _lastWalkStopReason = reason;
        _localHero.StopWalking();
    }

    private bool AnyEnemyVisible() => _enemies.Any(enemy => _visible.Contains(enemy.GridPos));

    private int VisibleInterestingThings() =>
        _floorItems.Keys.Count(_visible.Contains) +
        _chests.Keys.Count(_visible.Contains) +
        (_merchant != null && _visible.Contains(_merchant.GridPos) ? 1 : 0);

    /// <summary>Все живые герои сходили: ходят враги, тикают эффекты, начинается новый раунд.</summary>
    private void EndRound()
    {
        _turns++;
        _roundActions = 0;
        EnemiesTurn();
        if (_gameOver)
            return;

        foreach (var hero in LivingHeroes)
        {
            var (healed, strengthEnded, invisibilityEnded) = hero.TickEffects();
            if (healed > 0)
                Effects.FloatingText(this, hero.GridPos, $"+{healed}", GoodColor);
            if (strengthEnded && IsLocalHero(hero))
                _hud.AddMessage("Действие зелья силы закончилось.");
            if (invisibilityEnded && IsLocalHero(hero))
                _hud.AddMessage("Ты снова видим.");
        }
        foreach (var hero in _heroes)
            hero.HasActed = false;

        SearchSurroundings();
        UpdateFov();
        UpdateHud();
        TryDescend(); // дальний герой мог погибнуть — тогда ждать больше некого
        TryShowLevelUp();
    }

    private void AttackEnemy(Player hero, Enemy enemy)
    {
        if (hero.IsInvisible)
        {
            hero.BreakInvisibility();
            Tell(hero, "Атака выдаёт тебя — невидимость пропала.", $"{hero.DisplayName} выдаёт себя атакой.");
        }

        hero.PlayAttack(enemy.GridPos);
        hero.ChargeSkills();
        var bonuses = hero.Bonuses;
        int damage = RollDamage(hero, enemy) + bonuses.FireDamage + PerkBonusDamage(hero, enemy);
        bool crit = _rng.Next(100) < bonuses.Crit;
        if (crit)
            damage *= 2;
        damage = ApplyTeamwork(hero, enemy, damage);
        hero.DamageDealt += damage;

        enemy.TakeDamage(damage);
        Effects.FloatingText(this, enemy.GridPos, crit ? $"{damage}!" : damage.ToString(), crit ? CritColor : Colors.White);
        Effects.Burst(this, enemy.GridPos, bonuses.FireDamage > 0 ? new Color(1f, 0.5f, 0.1f) : enemy.Type.Color, crit ? 16 : 8);
        _sound.Play(Sound.Hit);
        if (crit && IsLocalHero(hero))
            _camera.Shake(0.08f);

        // Вампиризм: лечимся за каждый удар
        if (bonuses.Lifesteal > 0 && hero.IsHurt)
        {
            int before = hero.Hp;
            hero.Heal(bonuses.Lifesteal);
            Effects.FloatingText(this, hero.GridPos, $"+{hero.Hp - before}", GoodColor);
        }

        ApplyMeleePerks(hero, enemy, damage);
        if (enemy.IsDead)
            KillEnemy(enemy);
        else
            _hud.AddMessage(crit
                ? $"Критический удар! {enemy.DisplayName} получает {damage} урона."
                : $"{enemy.DisplayName} получает {damage} урона.");
    }

    /// <summary>Враг повержен. Опыт получают все живые герои — отряд делит победы поровну.</summary>
    private void KillEnemy(Enemy enemy)
    {
        _hud.AddMessage($"{enemy.DisplayName} повержен!", GoodColor);
        if (_attacker != null)
            _attacker.KillsMade++;
        _enemies.Remove(enemy);
        enemy.Die();
        _kills++;
        Effects.Burst(this, enemy.GridPos, enemy.Type.Color, enemy.IsBoss ? 60 : 20);
        _sound.Play(Sound.Kill);

        // Опыт — раньше проверки босса: иначе экран победы показал бы уровень без опыта за финального босса
        foreach (var hero in LivingHeroes.Where(hero => hero.Has(PerkEffect.Arcane)))
            hero.ChargeSkills();

        bool anyLevelUp = false;
        foreach (var hero in LivingHeroes)
        {
            int levels = hero.GainExperience(enemy.ExperienceReward);
            if (levels == 0)
                continue;
            hero.PendingLevelUps += levels; // окно выбора улучшения покажем в конце раунда
            anyLevelUp = true;
            Tell(hero, $"Новый уровень: {hero.Level}!", $"{hero.DisplayName}: новый уровень {hero.Level}.", InfoColor);
            Effects.Burst(this, hero.GridPos, InfoColor, 24);
            // Надпись — только над своим героем: в отряде уровень часто растёт у всех сразу, и надписи слипались
            if (IsLocalHero(hero))
                Effects.FloatingText(this, hero.GridPos, "Уровень!", InfoColor);
        }
        if (anyLevelUp)
            _sound.Play(Sound.LevelUp);

        if (enemy == _boss)
            OnBossKilled(enemy);
        else
            DropLoot(enemy);
    }

    private void OnBossKilled(Enemy boss)
    {
        _boss = null;
        if (boss.IsFinalBoss)
        {
            Victory();
            return;
        }
        var stairs = _map.StairsPos;
        _map.Tiles[stairs.X, stairs.Y] = Tile.Stairs;
        _hud.AddMessage("Босс повержен! Путь вниз открыт.", InfoColor);

        // Награда: предмет как будто с более глубокого этажа и золото каждому живому герою
        // Не _localHero: у каждого компьютера свой «свой» герой, а мир должен получиться одинаковым
        var dropCell = boss.GridPos == stairs ? LivingHeroes.First().GridPos : boss.GridPos;
        if (!_floorItems.ContainsKey(dropCell))
            PlaceItem(ItemCatalog.Roll(_rng, _floor + 3), dropCell);
        int gold = 50 + _floor * 10;
        foreach (var hero in LivingHeroes)
            hero.Gold += gold;
        _hud.AddMessage(IsSinglePlayer
            ? $"Ты забираешь сокровища короля: +{gold} золота."
            : $"Сокровища босса делятся на всех: +{gold} золота каждому.", InfoColor);
    }

    // ---------- Ход врагов ----------

    private void EnemiesTurn()
    {
        // Поле расстояний до ближайшего героя-цели считаем один раз за ход — им пользуются все враги
        var targets = _heroes.Where(hero => !hero.IsGhost && !hero.IsInvisible && !hero.IsOffline).Select(hero => hero.GridPos).ToList();
        _playerDistances = targets.Count > 0 ? _map.DistanceField(targets, ChaseDistance) : new Dictionary<Vector2I, int>();

        // ToList() — копия списка: босс может призвать новых врагов прямо во время перебора,
        // а менять коллекцию, по которой идёт foreach, нельзя
        foreach (var enemy in _enemies.ToList())
        {
            if (_gameOver)
                return; // отряд погиб — остальные враги уже не ходят
            if (enemy.IsDead)
                continue; // погиб от шипов в этот же ход
            enemy.TakeTurn(this);
        }
    }

    // ---------- Появление врагов ----------

    private void SpawnEnemies(int count)
    {
        var available = Enemy.Types.Where(type => type.MinFloor <= _floor).ToArray();
        for (int i = 0; i < count; i++)
        {
            if (!TryFindFreeCell(out var cell))
                return;
            var enemy = SpawnEnemy(available[_rng.Next(available.Length)], cell);
            if (_rng.NextDouble() < _difficulty.EliteChance)
                enemy.MakeElite(EliteHp);
        }
    }

    private Enemy SpawnEnemy(EnemyType type, Vector2I cell)
    {
        var enemy = new Enemy();
        float weight = ExtraHeroWeight;
        enemy.Setup(type, _floor, _difficulty, 1f + _partyHpPerHero * weight, 1f + _partyDamagePerHero * weight);
        AddChild(enemy);
        enemy.Teleport(cell);
        enemy.Visible = _visible.Contains(cell);
        _enemies.Add(enemy);
        return enemy;
    }

    /// <summary>Случайная свободная клетка пола в комнате (по умолчанию — не в стартовой).</summary>
    private bool TryFindFreeCell(out Vector2I cell, bool includeStartRoom = false)
    {
        int firstRoom = includeStartRoom ? 0 : 1;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var room = _map.Rooms[_rng.Next(firstRoom, _map.Rooms.Count)];
            cell = new Vector2I(
                _rng.Next(room.Position.X, room.End.X),
                _rng.Next(room.Position.Y, room.End.Y));
            if (_map.Get(cell) == Tile.Floor && !IsOccupied(cell) && !_floorItems.ContainsKey(cell) &&
                !_map.Traps.ContainsKey(cell))
                return true;
        }
        cell = default;
        return false;
    }

    // ---------- Вспомогательное ----------

    /// <summary>
    /// Обзор у отряда общий: объединяем то, что видит каждый герой. Призрак видит недалеко, зато разведывает сквозь стены.
    /// </summary>
    private void UpdateFov()
    {
        _visible = new HashSet<Vector2I>();
        foreach (var hero in _heroes)
            _visible.UnionWith(_map.ComputeFov(hero.GridPos, hero.IsGhost ? GhostFov : FovRadius + hero.Bonuses.Vision));
        foreach (var cell in _visible)
            _map.Explored[cell.X, cell.Y] = true;

        _renderer.UpdateVisibility(_visible);
        foreach (var enemy in _enemies)
            enemy.Visible = _visible.Contains(enemy.GridPos);
        foreach (var (cell, pickup) in _floorItems)
            pickup.Visible = _map.Explored[cell.X, cell.Y];
        if (_merchant != null)
            _merchant.Visible = _map.Explored[_merchant.GridPos.X, _merchant.GridPos.Y];
        foreach (var (cell, chest) in _chests)
            chest.Visible = _map.Explored[cell.X, cell.Y];
        foreach (var (cell, shrine) in _shrines)
            shrine.Visible = _map.Explored[cell.X, cell.Y];
        for (int i = 0; i < _plates.Count; i++)
            _plateNodes[i].Visible = _map.Explored[_plates[i].X, _plates[i].Y];

        var visibleEnemies = _enemies.Where(enemy => enemy.Visible).Select(enemy => enemy.GridPos).ToList();
        var markers = _floorItems.Select(pair => (pair.Key, pair.Value.MapColor)).ToList();
        markers.AddRange(_chests.Keys.Select(cell => (cell, new Color(0.75f, 0.5f, 0.2f))));
        markers.AddRange(_shrines.Values.Select(shrine => (shrine.GridPos, shrine.Glow)));
        markers.AddRange(_map.Traps.Where(pair => pair.Value.Revealed).Select(pair => (pair.Key, new Color(0.9f, 0.2f, 0.2f))));
        if (_merchant != null && _merchant.Visible)
            markers.Add((_merchant.GridPos, new Color(0.7f, 0.4f, 1f)));
        var allies = _heroes.Where(hero => !IsLocalHero(hero)).Select(hero => (hero.GridPos, hero.Class.Color)).ToList();
        _hud.UpdateMinimap(_map, _visible, _localHero.GridPos, visibleEnemies, markers, allies);
    }

    private void UpdateHud()
    {
        UpdateTutorial(); // первым: шаг обучения может дать герою зелье или рану
        _hud.UpdateStats(_localHero, _floor, _kills);
        _hud.UpdateParty(_heroes, _localHero);
        foreach (var hero in _heroes)
            hero.UpdateNameplate(!IsSinglePlayer && !IsLocalHero(hero));
        _hud.SetStairsWaiting(StairsWaitingNames());
        _hud.UpdateBoss(_boss);
    }

    private Enemy EnemyAt(Vector2I cell) => _enemies.Find(enemy => enemy.GridPos == cell);

    private static int ChebyshevDistance(Vector2I a, Vector2I b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// Урон = атака ± 1 минус защита цели. Защита снимает не всё: по герою проходит минимум треть
    /// с лишним удара, иначе броня превращала бы любого врага в безобидного (1 урона за удар).
    /// </summary>
    private int RollDamage(Actor attacker, Actor defender)
    {
        int roll = attacker.Attack + _rng.Next(-1, 2);
        int floor = defender is Player ? (int)MathF.Ceiling(roll * MinDamageShare) : 1;
        return Math.Max(Math.Max(1, floor), roll - defender.Defense);
    }
}
