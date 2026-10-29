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
    Paused,
    GameOver,
}

/// <summary>
/// Главный скрипт игры (висит на корневом узле Main.tscn).
/// Создаёт мир, управляет состояниями (меню, игра, инвентарь, пауза, смерть),
/// очерёдностью ходов, предметами, этажами и рестартом.
/// </summary>
public partial class GameManager : Node3D, IGameWorld
{
    private const int BossFloorInterval = 5;
    private const int MapWidth = 48;
    private const int MapHeight = 32;
    private const int FovRadius = 8;
    private const int ChaseDistance = 20;
    private const int KillsPerLevel = 4;
    private const double DropChance = 0.25;

    private static readonly Color DamageColor = new(1f, 0.45f, 0.4f);
    private static readonly Color GoodColor = new(0.5f, 1f, 0.5f);
    private static readonly Color InfoColor = new(1f, 0.85f, 0.4f);

    private readonly Random _rng = new();
    private readonly List<Enemy> _enemies = new();
    private readonly Dictionary<Vector2I, ItemPickup> _floorItems = new();
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
    private GameState _state;
    private int _floor;
    private int _kills;

    // Режим самопроверки: игра делает N случайных ходов, сохраняет скриншот и закрывается
    private string _screenshotPath;
    private string _screenshotView = "play";
    private int _autoMoves;
    private int _frame;
    private int _startFloor = 1;
    private bool _godMode; // только для самопроверки: игрок не может погибнуть

    // ---------- Реализация IGameWorld (то, что видят враги) ----------

    public DungeonMap Map => _map;
    public Vector2I PlayerPos => _player.GridPos;
    public Dictionary<Vector2I, int> PlayerDistances => _playerDistances;

    public bool IsOccupied(Vector2I cell) => cell == _player.GridPos || EnemyAt(cell) != null;

    public bool IsVisibleToPlayer(Vector2I cell) => _visible.Contains(cell);

    public void EnemyAttacksPlayer(Enemy enemy, bool ranged)
    {
        if (ranged)
            Effects.Projectile(this, enemy.GridPos, _player.GridPos, enemy.Type.Color);
        else
            enemy.PlayAttack(_player.GridPos);

        int damage = RollDamage(enemy, _player);
        _player.TakeDamage(damage);
        if (_godMode)
            _player.Heal(damage);
        string verb = ranged ? "стреляет в тебя" : "бьёт тебя";
        _hud.AddMessage($"{enemy.DisplayName} {verb}: -{damage}.", DamageColor);

        if (_player.IsDead)
            GameOver(enemy);
    }

    public void SummonMinions(Enemy summoner, int count)
    {
        var minionTypes = Enemy.Types.Where(type => type.MinFloor == 1).ToArray();
        int summoned = 0;
        foreach (var direction in DungeonMap.Directions)
        {
            var cell = summoner.GridPos + direction;
            if (summoned == count || !_map.CanMove(summoner.GridPos, cell) || IsOccupied(cell))
                continue;
            var minion = SpawnEnemy(minionTypes[_rng.Next(minionTypes.Length)], cell);
            minion.Alerted = true;
            summoned++;
        }
        if (summoned > 0)
            _hud.AddMessage($"{summoner.DisplayName} призывает подмогу!", DamageColor);
    }

    public override void _Ready()
    {
        SetupEnvironment();

        _renderer = new DungeonRenderer();
        AddChild(_renderer);

        _player = new Player();
        AddChild(_player);
        _player.ActionRequested += OnPlayerAction;

        _camera = new CameraRig { Target = _player };
        AddChild(_camera);

        _hud = new Hud();
        AddChild(_hud);

        _inventoryPanel = new InventoryPanel();
        AddChild(_inventoryPanel);
        _inventoryPanel.UseRequested += UseItem;

        _mainMenu = new MainMenu();
        AddChild(_mainMenu);
        _mainMenu.NewGameRequested += StartNewGame;

        _pauseMenu = new PauseMenu();
        AddChild(_pauseMenu);
        _pauseMenu.ResumeRequested += Resume;
        _pauseMenu.MainMenuRequested += ShowMainMenu;

        ParseCommandLine();
        ShowMainMenu();
    }

    public override void _Process(double delta)
    {
        if (_screenshotPath != null)
            RunSelfTest();
    }

    /// <summary>
    /// Клавиши уровня всей игры. Движение обрабатывает Player, а здесь — Esc, R, M, I, X и колёсико.
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

        switch (key.PhysicalKeycode)
        {
            case Key.Escape:
                switch (_state)
                {
                    case GameState.Menu: _mainMenu.GoBack(); break;
                    case GameState.Playing: Pause(); break;
                    case GameState.Inventory: CloseInventory(); break;
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
            case Key.R when _state == GameState.GameOver:
                StartNewGame();
                break;
            case Key.M when _state == GameState.Playing:
                _hud.ToggleMinimap();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void SetupEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.02f, 0.02f, 0.03f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.35f, 0.4f, 0.6f),
            AmbientLightEnergy = 0.5f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            GlowEnabled = true,
        };
        AddChild(new WorldEnvironment { Environment = environment });
    }

    // ---------- Состояния игры ----------

    /// <summary>Главное меню поверх случайного подземелья-заставки.</summary>
    private void ShowMainMenu()
    {
        _state = GameState.Menu;
        ClearLevelObjects();

        _map = DungeonGenerator.Generate(MapWidth, MapHeight, _rng);
        _renderer.Build(_map);

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
        _camera.MenuMode = true;
        _camera.MenuCenter = new Vector3(MapWidth / 2f, 0f, MapHeight / 2f);
        _camera.SnapToTarget();
        _mainMenu.ShowMain();
    }

    private void StartNewGame()
    {
        _state = GameState.Playing;
        _floor = _startFloor - 1;
        _kills = 0;
        _mainMenu.Visible = false;
        _hud.Visible = true;
        _hud.HideGameOver();
        _hud.ClearMessages();
        _player.Visible = true;
        _player.ResetStats();
        _camera.MenuMode = false;

        NextFloor();
        _hud.AddMessage("Ты спускаешься в подземелье. Найди золотую лестницу.", InfoColor);
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

    private void OpenInventory()
    {
        _state = GameState.Inventory;
        _inventoryPanel.Open(_player.Inventory);
    }

    private void CloseInventory()
    {
        _state = GameState.Playing;
        _inventoryPanel.Visible = false;
    }

    private void GameOver(Enemy killer)
    {
        _state = GameState.GameOver;
        UpdateHud();

        // В режиме самопроверки не засоряем таблицу рекордов
        int place = _screenshotPath == null
            ? HighScores.Add(new ScoreEntry(_floor, _kills, _player.Level, DateTime.Now.ToString("dd.MM.yyyy HH:mm")))
            : 0;
        string placeText = place switch
        {
            1 => "Новый рекорд!\n",
            > 1 => $"Место в таблице рекордов: {place}\n",
            _ => "",
        };

        _hud.ShowGameOver(
            $"Ты погиб на этаже {_floor}\n" +
            $"Тебя одолел: {killer.DisplayName}\n" +
            $"Побеждено врагов: {_kills}\n" +
            placeText + "\n" +
            "R — начать заново  •  Esc — в главное меню");
    }

    private void NextFloor()
    {
        _floor++;
        ClearLevelObjects();

        bool bossFloor = _floor % BossFloorInterval == 0;
        _map = DungeonGenerator.Generate(MapWidth, MapHeight, _rng, withBossRoom: bossFloor);
        _renderer.Build(_map);
        _player.Teleport(_map.StartPos);
        _camera.SnapToTarget();

        int enemyCount = Math.Min(3 + _floor * 2, 25);
        SpawnEnemies(bossFloor ? enemyCount / 2 : enemyCount);
        SpawnItems(_rng.Next(3, 6));
        if (bossFloor)
            SpawnBoss();

        UpdateFov();
        UpdateHud();
        if (bossFloor)
            _hud.AddMessage("Ты чувствуешь присутствие чего-то огромного... Лестница откроется после победы над боссом.", DamageColor);
        else if (_floor > 1)
            _hud.AddMessage($"Этаж {_floor}. Здесь опаснее.", InfoColor);
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

        _boss = SpawnEnemy(Enemy.BossType, cell);
    }

    private void ClearLevelObjects()
    {
        foreach (var enemy in _enemies)
            enemy.QueueFree();
        _enemies.Clear();
        foreach (var pickup in _floorItems.Values)
            pickup.QueueFree();
        _floorItems.Clear();
        _boss = null;
    }

    // ---------- Ход игрока ----------

    private void OnPlayerAction(Vector2I direction)
    {
        if (_state != GameState.Playing)
            return;

        if (direction != Vector2I.Zero)
        {
            var target = _player.GridPos + direction;
            if (!_map.CanMove(_player.GridPos, target))
                return; // упёрлись в стену — ход не тратится

            var enemy = EnemyAt(target);
            if (enemy != null)
            {
                AttackEnemy(enemy);
            }
            else
            {
                _player.MoveTo(target);
                TryPickUpItem(target);
                if (_map.Get(target) == Tile.Stairs)
                {
                    NextFloor();
                    return;
                }
            }
        }

        EndPlayerTurn();
    }

    /// <summary>После любого действия игрока, которое тратит ход, ходят враги.</summary>
    private void EndPlayerTurn()
    {
        EnemiesTurn();
        UpdateFov();
        UpdateHud();
    }

    private void AttackEnemy(Enemy enemy)
    {
        _player.PlayAttack(enemy.GridPos);
        int damage = RollDamage(_player, enemy);
        enemy.TakeDamage(damage);

        if (enemy.IsDead)
            KillEnemy(enemy);
        else
            _hud.AddMessage($"{enemy.DisplayName} получает {damage} урона.");
    }

    private void KillEnemy(Enemy enemy)
    {
        _hud.AddMessage($"{enemy.DisplayName} повержен!", GoodColor);
        _enemies.Remove(enemy);
        enemy.Die();
        _kills++;

        if (enemy == _boss)
        {
            _boss = null;
            var stairs = _map.StairsPos;
            _map.Tiles[stairs.X, stairs.Y] = Tile.Stairs;
            _hud.AddMessage("Босс повержен! Путь вниз открыт.", InfoColor);
            // Награда: предмет как будто с более глубокого этажа
            var dropCell = _floorItems.ContainsKey(enemy.GridPos) || enemy.GridPos == stairs ? _player.GridPos : enemy.GridPos;
            if (!_floorItems.ContainsKey(dropCell))
                PlaceItem(ItemCatalog.Roll(_rng, _floor + 2), dropCell);
        }
        else if (_rng.NextDouble() < DropChance && !_floorItems.ContainsKey(enemy.GridPos))
        {
            PlaceItem(ItemCatalog.Roll(_rng, _floor), enemy.GridPos);
            _hud.AddMessage($"{enemy.DisplayName} что-то роняет.");
        }

        if (_kills % KillsPerLevel == 0)
        {
            _player.LevelUp();
            _hud.AddMessage($"Новый уровень: {_player.Level}! Атака и здоровье выросли.", InfoColor);
        }
    }

    // ---------- Предметы ----------

    private void TryPickUpItem(Vector2I cell)
    {
        if (!_floorItems.TryGetValue(cell, out var pickup))
            return;

        if (!_player.Inventory.TryAdd(pickup.Item))
        {
            _hud.AddMessage($"Здесь лежит: {pickup.Item.Name}. Но сумка полна!", DamageColor);
            return;
        }
        _floorItems.Remove(cell);
        pickup.QueueFree();
        _hud.AddMessage($"Ты подбираешь: {pickup.Item.Name}.", GoodColor);
    }

    private void PlaceItem(Item item, Vector2I cell)
    {
        var pickup = ItemPickup.Create(item, cell);
        AddChild(pickup);
        _floorItems[cell] = pickup;
    }

    /// <summary>Применить или надеть предмет из сумки. Тратит ход.</summary>
    private void UseItem(int index)
    {
        var inventory = _player.Inventory;
        if (_state != GameState.Inventory || index < 0 || index >= inventory.Items.Count)
            return;

        var item = inventory.Items[index];
        switch (item.Kind)
        {
            case ItemKind.Weapon:
                _player.Equip(index);
                _hud.AddMessage($"Ты берёшь в руки: {item.Name}.", InfoColor);
                break;

            case ItemKind.Armor:
                _player.Equip(index);
                _hud.AddMessage($"Ты надеваешь: {item.Name}.", InfoColor);
                break;

            case ItemKind.HealthPotion:
                if (!_player.IsHurt)
                {
                    _hud.AddMessage("Ты и так полностью здоров.");
                    return; // ход не тратим, окно не закрываем
                }
                inventory.Items.RemoveAt(index);
                _player.Heal(item.Power);
                _hud.AddMessage($"Ты выпиваешь {item.Name}: +{item.Power} здоровья.", GoodColor);
                break;

            case ItemKind.TeleportScroll:
                if (!TryFindFreeCell(out var destination, includeStartRoom: true))
                    return;
                inventory.Items.RemoveAt(index);
                _player.Teleport(destination);
                _camera.SnapToTarget();
                _hud.AddMessage("Вспышка — и ты в другом месте!", InfoColor);
                break;

            case ItemKind.FireScroll:
                var targets = _enemies.Where(enemy => _visible.Contains(enemy.GridPos)).ToList();
                if (targets.Count == 0)
                {
                    _hud.AddMessage("Рядом нет врагов — свиток лучше поберечь.");
                    return;
                }
                inventory.Items.RemoveAt(index);
                int damage = item.Power + _floor;
                _hud.AddMessage($"Огненная волна! Все видимые враги получают {damage} урона.", InfoColor);
                foreach (var enemy in targets)
                {
                    enemy.TakeDamage(damage);
                    if (enemy.IsDead)
                        KillEnemy(enemy);
                }
                break;
        }

        CloseInventory();
        EndPlayerTurn();
    }

    /// <summary>Выбросить предмет под ноги. Не тратит ход.</summary>
    private void DropItem(int index)
    {
        var inventory = _player.Inventory;
        if (index < 0 || index >= inventory.Items.Count)
            return;
        if (_floorItems.ContainsKey(_player.GridPos))
        {
            _hud.AddMessage("Здесь уже что-то лежит.");
            return;
        }

        var item = inventory.Items[index];
        inventory.Items.RemoveAt(index);
        PlaceItem(item, _player.GridPos);
        _hud.AddMessage($"Ты выбрасываешь: {item.Name}.");
        _inventoryPanel.Refresh(inventory, index);
        UpdateFov();
        UpdateHud();
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
            enemy.TakeTurn(this);
        }
    }

    // ---------- Появление врагов и предметов ----------

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
        enemy.Setup(type, _floor);
        AddChild(enemy);
        enemy.Teleport(cell);
        enemy.Visible = _visible.Contains(cell);
        _enemies.Add(enemy);
        return enemy;
    }

    private void SpawnItems(int count)
    {
        for (int i = 0; i < count; i++)
            if (TryFindFreeCell(out var cell))
                PlaceItem(ItemCatalog.Roll(_rng, _floor), cell);
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
            if (_map.Get(cell) == Tile.Floor && !IsOccupied(cell) && !_floorItems.ContainsKey(cell))
                return true;
        }
        cell = default;
        return false;
    }

    // ---------- Вспомогательное ----------

    private void UpdateFov()
    {
        _visible = _map.ComputeFov(_player.GridPos, FovRadius);
        foreach (var cell in _visible)
            _map.Explored[cell.X, cell.Y] = true;

        _renderer.UpdateVisibility(_visible);
        foreach (var enemy in _enemies)
            enemy.Visible = _visible.Contains(enemy.GridPos);
        foreach (var (cell, pickup) in _floorItems)
            pickup.Visible = _map.Explored[cell.X, cell.Y];

        var visibleEnemies = _enemies.Where(enemy => enemy.Visible).Select(enemy => enemy.GridPos).ToList();
        var items = _floorItems.Select(pair => (pair.Key, pair.Value.Item.Color)).ToList();
        _hud.UpdateMinimap(_map, _visible, _player.GridPos, visibleEnemies, items);
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

    private void ParseCommandLine()
    {
        // Аргументы после «--» в командной строке Godot, например:
        // -- --screenshot=shot.png --moves=40 --view=pause
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot="))
                _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--moves="))
                _autoMoves = int.Parse(arg["--moves=".Length..]);
            else if (arg.StartsWith("--view="))
                _screenshotView = arg["--view=".Length..];
            else if (arg == "--god")
                _godMode = true;
            else if (arg.StartsWith("--start-floor="))
                _startFloor = int.Parse(arg["--start-floor=".Length..]);
        }
    }

    /// <summary>
    /// --view=menu — снимок главного меню, scores — таблицы рекордов,
    /// pause / inventory — паузы или инвентаря после ходов, play (по умолчанию) — просто игры.
    /// Во время случайных ходов иногда применяется первый предмет из сумки — так проверяются предметы.
    /// </summary>
    private void RunSelfTest()
    {
        _frame++;
        bool inMenu = _screenshotView is "menu" or "scores";

        if (_frame == 1)
        {
            if (_screenshotView == "scores")
                _mainMenu.ShowScores();
            else if (!inMenu)
                StartNewGame();
        }

        int moveFrames = inMenu ? 0 : _autoMoves * 3;
        if (_frame <= moveFrames)
        {
            if (_frame % 3 == 0)
                OnPlayerAction(AutoPilotDirection());
            if (_frame % 30 == 0 && _state == GameState.Playing && _player.Inventory.Items.Count > 0)
            {
                OpenInventory();
                UseItem(_rng.Next(_player.Inventory.Items.Count));
                if (_state == GameState.Inventory)
                    CloseInventory();
            }
            return;
        }

        if (_frame == moveFrames + 1 && _state == GameState.Playing)
        {
            if (_screenshotView == "pause")
                Pause();
            else if (_screenshotView == "inventory")
                OpenInventory();
        }

        if (_frame == moveFrames + 40)
        {
            GD.Print($"Самопроверка: этаж {_floor}, убито {_kills}, здоровье {_player.Hp}, состояние {_state}");
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
            GetTree().Quit();
        }
    }

    /// <summary>Автопилот для самопроверки: чаще идёт к лестнице, иногда шагает случайно.</summary>
    private Vector2I AutoPilotDirection()
    {
        var randomDirection = DungeonMap.Directions[_rng.Next(DungeonMap.Directions.Length)];
        if (_rng.NextDouble() < 0.3)
            return randomDirection;

        var distances = _map.DistanceField(_map.StairsPos, MapWidth * MapHeight);
        if (!distances.TryGetValue(_player.GridPos, out int best))
            return randomDirection;

        var result = randomDirection;
        foreach (var direction in DungeonMap.Directions)
        {
            var next = _player.GridPos + direction;
            if (distances.TryGetValue(next, out int distance) && distance < best && _map.CanMove(_player.GridPos, next))
            {
                best = distance;
                result = direction;
            }
        }
        return result;
    }
}
