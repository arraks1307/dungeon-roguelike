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
}

/// <summary>
/// Главный скрипт игры (висит на корневом узле Main.tscn).
/// Создаёт мир, управляет состояниями, очерёдностью ходов, боем, этажами и рестартом.
///
/// Класс разбит на три файла с помощью partial:
///   GameManager.cs          — ядро: состояния, ходы, бой, этажи;
///   GameManager.Items.cs    — предметы, золото, торговец;
///   GameManager.SelfTest.cs — режим самопроверки.
/// </summary>
public partial class GameManager : Node3D, IGameWorld
{
    private const int BossFloorInterval = 5;
    private const int MapWidth = 48;
    private const int MapHeight = 32;
    private const int FovRadius = 8;
    private const int ChaseDistance = 20;

    private static readonly Color DamageColor = new(1f, 0.45f, 0.4f);
    private static readonly Color GoodColor = new(0.5f, 1f, 0.5f);
    private static readonly Color InfoColor = new(1f, 0.85f, 0.4f);
    private static readonly Color CritColor = new(1f, 0.9f, 0.2f);

    private readonly Random _rng = new();
    private readonly List<Enemy> _enemies = new();
    private HashSet<Vector2I> _visible = new();
    private Dictionary<Vector2I, int> _playerDistances = new();
    private Enemy _boss;

    private DungeonMap _map;
    private DungeonRenderer _renderer;
    private Player _player;
    private CameraRig _camera;
    private Hud _hud;
    private MainMenu _mainMenu;
    private PauseMenu _pauseMenu;
    private InventoryPanel _inventoryPanel;
    private ShopPanel _shopPanel;
    private SettingsMenu _settingsMenu;
    private SoundManager _sound;
    private GameState _state;
    private DifficultyDef _difficulty = Difficulties.Normal;
    private int _floor;
    private int _kills;
    private int _turns;
    private bool _victory;

    // ---------- Реализация IGameWorld (то, что видят враги) ----------

    public DungeonMap Map => _map;
    public Vector2I PlayerPos => _player.GridPos;
    public bool PlayerInvisible => _player.IsInvisible;
    public Dictionary<Vector2I, int> PlayerDistances => _playerDistances;

    public bool IsOccupied(Vector2I cell) =>
        cell == _player.GridPos || EnemyAt(cell) != null || _chests.ContainsKey(cell) ||
        (_merchant != null && _merchant.GridPos == cell);

    public bool IsVisibleToPlayer(Vector2I cell) => _visible.Contains(cell);

    public void EnemyAttacksPlayer(Enemy enemy, bool ranged)
    {
        if (ranged)
        {
            Effects.Projectile(this, enemy.GridPos, _player.GridPos, enemy.Type.Color);
            _sound.Play(Sound.Shoot);
        }
        else
        {
            enemy.PlayAttack(_player.GridPos);
        }

        int damage = RollDamage(enemy, _player);
        _player.TakeDamage(damage);
        if (_godMode)
            _player.Heal(damage);
        Effects.FloatingText(this, _player.GridPos, $"-{damage}", DamageColor);
        _sound.Play(Sound.Hurt);
        _camera.Shake(0.12f);
        string verb = ranged ? "стреляет в тебя" : "бьёт тебя";
        _hud.AddMessage($"{enemy.DisplayName} {verb}: -{damage}.", DamageColor);

        if (_player.IsDead)
        {
            GameOver(enemy.DisplayName);
            return;
        }

        // Шипы ранят того, кто ударил вплотную
        int thorns = _player.Bonuses.Thorns;
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

        _player = new Player();
        AddChild(_player);
        _player.ActionRequested += OnPlayerPressed;
        _player.WalkRequested += OnWalkRequested;

        _camera = new CameraRig { Target = _player };
        AddChild(_camera);

        _hud = new Hud();
        AddChild(_hud);

        _inventoryPanel = new InventoryPanel();
        AddChild(_inventoryPanel);
        _inventoryPanel.UseRequested += OnInventoryUse;

        _shopPanel = new ShopPanel();
        AddChild(_shopPanel);
        _shopPanel.BuyRequested += BuyItem;
        _shopPanel.SellRequested += SellItem;

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

        _pauseMenu = new PauseMenu();
        AddChild(_pauseMenu);
        _pauseMenu.ResumeRequested += Resume;
        _pauseMenu.SettingsRequested += OpenSettings;
        _pauseMenu.SaveAndExitRequested += SaveAndExit;

        _settingsMenu = new SettingsMenu();
        AddChild(_settingsMenu);
        _settingsMenu.Closed += OnSettingsClosed;

        ShowMainMenu();
    }

    public override void _Process(double delta)
    {
        if (_simulateGames > 0)
            RunSimulation();
        else if (_screenshotPath != null)
            RunSelfTest();
    }

    /// <summary>
    /// Клавиши уровня всей игры. Движение обрабатывает Player, а здесь — Esc, R, M, I, X, 1–5 и колёсико.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } mouse && _state == GameState.Playing)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp)
                _camera.Zoom(-1);
            else if (mouse.ButtonIndex == MouseButton.WheelDown)
                _camera.Zoom(1);
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
            case Key.Escape when _state == GameState.Menu && _classSelect.Visible:
                if (_classSelect.IsConfirming)
                    _classSelect.CancelConfirm(); // с подтверждения — обратно к карточкам
                else
                    CloseClassSelect();
                break;
            case Key.F when _state == GameState.Playing:
                UseSkill(0);
                break;
            case Key.G when _state == GameState.Playing:
                UseSkill(1);
                break;
            case Key.Escape:
                switch (_state)
                {
                    case GameState.Menu: _mainMenu.GoBack(); break;
                    case GameState.Playing: Pause(); break;
                    case GameState.Inventory: CloseInventory(); break;
                    case GameState.Shop: CloseShop(); break;
                    case GameState.Paused: Resume(); break;
                    case GameState.GameOver: ShowMainMenu(); break;
                }
                break;
            case Key.I when _state == GameState.Playing:
                OpenInventory();
                break;
            case Key.I when _state == GameState.Inventory:
                CloseInventory();
                break;
            case Key.X when _state == GameState.Inventory:
                DropItem(_inventoryPanel.FocusedIndex);
                break;
            case >= Key.Key1 and <= Key.Key5 when _state == GameState.Playing:
                QuickUse((int)(key.PhysicalKeycode - Key.Key1));
                break;
            case Key.R when _state == GameState.GameOver:
                StartNewGame(_player.Class, _difficulty); // заново тем же классом и на той же сложности
                break;
            case Key.M when _state == GameState.Playing:
                _hud.ToggleMinimap();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    // ---------- Состояния игры ----------

    /// <summary>Главное меню поверх случайного подземелья-заставки.</summary>
    private void ShowMainMenu()
    {
        _state = GameState.Menu;
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

        _player.Visible = false;
        _hud.Visible = false;
        _pauseMenu.Visible = false;
        _inventoryPanel.Visible = false;
        _shopPanel.Visible = false;
        _levelUpPanel.Visible = false;
        _classSelect.Visible = false;
        _camera.MenuMode = true;
        _camera.MenuCenter = new Vector3(MapWidth / 2f, 0f, MapHeight / 2f);
        UiTheme.SetAccent(UiTheme.DefaultAccent);
        _camera.SnapToTarget();
        _mainMenu.ShowMain();
    }

    private void StartNewGame(ClassDef playerClass, DifficultyDef difficulty)
    {
        _state = GameState.Playing;
        _difficulty = difficulty;
        _floor = _startFloor - 1;
        _kills = 0;
        _turns = 0;
        _victory = false;
        _pendingLevelUps = 0;
        _mainMenu.Visible = false;
        _classSelect.Visible = false;
        _hud.Visible = true;
        _hud.HideGameOver();
        _hud.ClearMessages();
        _player.Visible = true;
        _player.ResetStats(playerClass);
        UiTheme.SetAccent(playerClass.Color.Lightened(0.15f)); // интерфейс окрашивается в цвет героя
        for (int i = 0; i < difficulty.BonusPotions; i++)
            _player.Inventory.TryAdd(new Item(ItemCatalog.HealthPotion));
        _camera.MenuMode = false;

        NextFloor();
        _hud.AddMessage($"{playerClass.Name} спускается в подземелье. Найди золотую лестницу. F и G — способности.", InfoColor);
    }

    private void Pause()
    {
        _state = GameState.Paused;
        _pauseMenu.Open();
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

    private void GameOver(string killerName)
    {
        _state = GameState.GameOver;
        SaveSystem.Delete(); // смерть окончательна — продолжить этот забег нельзя
        UpdateHud();
        _sound.Play(Sound.Death);
        _camera.Shake(0.4f);

        _hud.ShowGameOver(
            $"Ты погиб на этаже {_floor}\n" +
            $"Тебя одолел: {killerName}\n" +
            $"Побеждено врагов: {_kills}   Золото: {_player.Gold}\n" +
            RecordScore() + "\n" +
            "R — начать заново  •  Esc — в главное меню");
    }

    /// <summary>Финальный босс повержен — забег пройден.</summary>
    private void Victory()
    {
        _state = GameState.GameOver;
        _victory = true;
        SaveSystem.Delete();
        UpdateHud();
        _sound.Play(Sound.LevelUp);
        _camera.Shake(0.5f);

        _hud.ShowGameOver(
            "ПОБЕДА!\n\n" +
            "Владыка бездны повержен, и подземелье затихает.\n" +
            $"{_player.Class.Name} {_player.Level}-го уровня, сложность: {_difficulty.Name.ToLower()}\n" +
            $"Побеждено врагов: {_kills}   Золото: {_player.Gold}   Ходов: {_turns}\n" +
            RecordScore() + "\n" +
            "R — новый забег  •  Esc — в главное меню");
    }

    /// <summary>Записывает результат в таблицу рекордов и возвращает строку о месте в ней.</summary>
    private string RecordScore()
    {
        if (IsTestRun)
            return ""; // самопроверка и симуляция не засоряют таблицу рекордов
        int place = HighScores.Add(new ScoreEntry(_floor, _kills, _player.Level, DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
            _player.Class.Name, _difficulty.Name, _victory));
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
        ClearLevelObjects();

        bool bossFloor = _floor % BossFloorInterval == 0;
        int themeIndex = Themes.IndexFor(_floor);
        _map = DungeonGenerator.Generate(MapWidth, MapHeight, _rng, themeIndex, withBossRoom: bossFloor);
        _renderer.Build(_map);
        ApplyTheme();
        _player.Teleport(_map.StartPos);
        _camera.SnapToTarget();

        int enemyCount = Math.Min(2 + _floor * 3 / 2, 22); // подобрано по симуляциям
        SpawnEnemies(bossFloor ? enemyCount / 2 : enemyCount);
        SpawnChests();
        SpawnItems(_rng.Next(3, 6) + _difficulty.ItemsPerFloor);
        SpawnGold(_rng.Next(2, 5));
        if (!bossFloor)
            SpawnTraps();
        if (bossFloor)
            SpawnBoss();
        else if (ShouldSpawnMerchant())
            SpawnMerchant();

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
        _merchant?.QueueFree();
        _merchant = null;
        _boss = null;
    }

    // ---------- Ход игрока ----------

    /// <summary>
    /// Ручное нажатие клавиши движения. Если враг был виден до или после этого шага,
    /// удержание клавиши не запустит автоходьбу — в бою каждый ход делается осознанно.
    /// </summary>
    private void OnPlayerPressed(Vector2I direction)
    {
        bool enemyBefore = AnyEnemyVisible();
        OnPlayerAction(direction);
        if (enemyBefore || AnyEnemyVisible())
            StopWalking("враг в поле зрения при нажатии");
    }

    private void OnPlayerAction(Vector2I direction)
    {
        if (_state != GameState.Playing)
            return;

        if (direction != Vector2I.Zero)
        {
            var target = _player.GridPos + direction;
            if (!_map.CanMove(_player.GridPos, target))
                return; // упёрлись в стену — ход не тратится

            if (_merchant != null && target == _merchant.GridPos)
            {
                OpenShop(); // разговор с торговцем не тратит ход
                return;
            }

            var enemy = EnemyAt(target);
            if (enemy != null)
            {
                AttackEnemy(enemy);
            }
            else if (_chests.TryGetValue(target, out var chest))
            {
                OpenChest(chest);
            }
            else if (_map.Get(target) == Tile.Door)
            {
                OpenDoor(target); // открыть дверь — отдельное действие, сам шаг будет следующим ходом
            }
            else
            {
                _player.MoveTo(target);
                _sound.Play(Sound.Step);
                TryPickUpItem(target);
                CheckTrap(target);
                if (_state != GameState.Playing)
                    return; // погиб на шипах
                if (_map.Get(_player.GridPos) == Tile.Stairs)
                {
                    _sound.Play(Sound.Stairs);
                    NextFloor();
                    return;
                }
            }
        }

        EndPlayerTurn();
    }

    /// <summary>
    /// Умная ходьба (клавиша зажата): делаем шаг, только если это спокойное перемещение,
    /// и останавливаемся, как только происходит что-то, на что игрок должен обратить внимание.
    /// Атаковать, открывать двери и сундуки при удержании нельзя — только отдельным нажатием.
    /// </summary>
    private void OnWalkRequested(Vector2I direction)
    {
        var target = _player.GridPos + direction;
        string blocker = _state != GameState.Playing ? "открыто окно"
            : AnyEnemyVisible() ? "виден враг"
            : !_map.CanMove(_player.GridPos, target) ? "стена"
            : IsOccupied(target) ? "клетка занята"
            : _map.Get(target) == Tile.Door ? "закрытая дверь"
            : null;
        if (blocker != null)
        {
            StopWalking(blocker);
            return;
        }

        int hpBefore = _player.Hp;
        int floorBefore = _floor;
        int interestingBefore = VisibleInterestingThings();
        bool steppingOnSomething = _floorItems.ContainsKey(target) || _map.Get(target) == Tile.Stairs;

        OnPlayerAction(direction);

        string reason = _state != GameState.Playing ? "открылось окно или смерть"
            : _floor != floorBefore ? "новый этаж"
            : _player.Hp < hpBefore ? "получен урон"
            : steppingOnSomething ? "наступил на предмет или лестницу"
            : AnyEnemyVisible() ? "показался враг"
            : VisibleInterestingThings() > interestingBefore ? "показался предмет, сундук или торговец"
            : null;
        if (reason != null)
            StopWalking(reason);
    }

    /// <summary>Причина последней остановки — её печатает самопроверка, чтобы ловить ложные остановки.</summary>
    private string _lastWalkStopReason;

    private void StopWalking(string reason)
    {
        _lastWalkStopReason = reason;
        _player.StopWalking();
    }

    private bool AnyEnemyVisible() => _enemies.Any(enemy => _visible.Contains(enemy.GridPos));

    private int VisibleInterestingThings() =>
        _floorItems.Keys.Count(_visible.Contains) +
        _chests.Keys.Count(_visible.Contains) +
        (_merchant != null && _visible.Contains(_merchant.GridPos) ? 1 : 0);

    /// <summary>После любого действия игрока, которое тратит ход, ходят враги и тикают эффекты.</summary>
    private void EndPlayerTurn()
    {
        _turns++;
        EnemiesTurn();
        if (_state == GameState.GameOver)
            return;

        var (healed, strengthEnded, invisibilityEnded) = _player.TickEffects();
        if (healed > 0)
            Effects.FloatingText(this, _player.GridPos, $"+{healed}", GoodColor);
        if (strengthEnded)
            _hud.AddMessage("Действие зелья силы закончилось.");
        if (invisibilityEnded)
            _hud.AddMessage("Ты снова видим.");

        SearchSurroundings();
        UpdateFov();
        UpdateHud();
        TryShowLevelUp();
    }

    private void AttackEnemy(Enemy enemy)
    {
        if (_player.IsInvisible)
        {
            _player.BreakInvisibility();
            _hud.AddMessage("Атака выдаёт тебя — невидимость пропала.");
        }

        _player.PlayAttack(enemy.GridPos);
        var bonuses = _player.Bonuses;
        int damage = RollDamage(_player, enemy) + bonuses.FireDamage;
        bool crit = _rng.Next(100) < bonuses.Crit;
        if (crit)
            damage *= 2;

        enemy.TakeDamage(damage);
        Effects.FloatingText(this, enemy.GridPos, crit ? $"{damage}!" : damage.ToString(), crit ? CritColor : Colors.White);
        Effects.Burst(this, enemy.GridPos, bonuses.FireDamage > 0 ? new Color(1f, 0.5f, 0.1f) : enemy.Type.Color, crit ? 16 : 8);
        _sound.Play(Sound.Hit);
        if (crit)
            _camera.Shake(0.08f);

        // Вампиризм: лечимся за каждый удар
        if (bonuses.Lifesteal > 0 && _player.IsHurt)
        {
            int before = _player.Hp;
            _player.Heal(bonuses.Lifesteal);
            Effects.FloatingText(this, _player.GridPos, $"+{_player.Hp - before}", GoodColor);
        }

        if (enemy.IsDead)
            KillEnemy(enemy);
        else
            _hud.AddMessage(crit
                ? $"Критический удар! {enemy.DisplayName} получает {damage} урона."
                : $"{enemy.DisplayName} получает {damage} урона.");
    }

    private void KillEnemy(Enemy enemy)
    {
        _hud.AddMessage($"{enemy.DisplayName} повержен!", GoodColor);
        _enemies.Remove(enemy);
        enemy.Die();
        _kills++;
        Effects.Burst(this, enemy.GridPos, enemy.Type.Color, enemy.IsBoss ? 60 : 20);
        _sound.Play(Sound.Kill);

        // Опыт — раньше проверки босса: иначе экран победы показал бы уровень без опыта за финального босса
        int levels = _player.GainExperience(enemy.ExperienceReward);
        if (levels > 0)
        {
            _pendingLevelUps += levels; // окно выбора улучшения покажем в конце хода
            _hud.AddMessage($"Новый уровень: {_player.Level}!", InfoColor);
            Effects.FloatingText(this, _player.GridPos, "Уровень!", InfoColor);
            Effects.Burst(this, _player.GridPos, InfoColor, 24);
            _sound.Play(Sound.LevelUp);
        }

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

        // Награда: предмет как будто с более глубокого этажа и много золота
        var dropCell = boss.GridPos == stairs ? _player.GridPos : boss.GridPos;
        if (!_floorItems.ContainsKey(dropCell))
            PlaceItem(ItemCatalog.Roll(_rng, _floor + 3), dropCell);
        _player.Gold += 50 + _floor * 10;
        _hud.AddMessage($"Ты забираешь сокровища короля: +{50 + _floor * 10} золота.", InfoColor);
    }

    // ---------- Ход врагов ----------

    private void EnemiesTurn()
    {
        // Поле расстояний считаем один раз за ход — им пользуются все враги
        _playerDistances = _map.DistanceField(_player.GridPos, ChaseDistance);

        // ToList() — копия списка: босс может призвать новых врагов прямо во время перебора,
        // а менять коллекцию, по которой идёт foreach, нельзя
        foreach (var enemy in _enemies.ToList())
        {
            if (_state != GameState.Playing)
                return; // игрок погиб — остальные враги уже не ходят
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
            SpawnEnemy(available[_rng.Next(available.Length)], cell);
        }
    }

    private Enemy SpawnEnemy(EnemyType type, Vector2I cell)
    {
        var enemy = new Enemy();
        enemy.Setup(type, _floor, _difficulty);
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

    private void UpdateFov()
    {
        _visible = _map.ComputeFov(_player.GridPos, FovRadius + _player.Bonuses.Vision);
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

        var visibleEnemies = _enemies.Where(enemy => enemy.Visible).Select(enemy => enemy.GridPos).ToList();
        var markers = _floorItems.Select(pair => (pair.Key, pair.Value.MapColor)).ToList();
        markers.AddRange(_chests.Keys.Select(cell => (cell, new Color(0.75f, 0.5f, 0.2f))));
        markers.AddRange(_map.Traps.Where(pair => pair.Value.Revealed).Select(pair => (pair.Key, new Color(0.9f, 0.2f, 0.2f))));
        if (_merchant != null && _merchant.Visible)
            markers.Add((_merchant.GridPos, new Color(0.7f, 0.4f, 1f)));
        _hud.UpdateMinimap(_map, _visible, _player.GridPos, visibleEnemies, markers);
    }

    private void UpdateHud()
    {
        _hud.UpdateStats(_player, _floor, _kills);
        _hud.UpdateBoss(_boss);
    }

    private Enemy EnemyAt(Vector2I cell) => _enemies.Find(enemy => enemy.GridPos == cell);

    /// <summary>Урон = атака ± 1 минус защита цели, но не меньше 1.</summary>
    private int RollDamage(Actor attacker, Actor defender) =>
        Math.Max(1, attacker.Attack + _rng.Next(-1, 2) - defender.Defense);
}
