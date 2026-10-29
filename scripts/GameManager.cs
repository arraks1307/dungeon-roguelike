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
    private const int KillsPerLevel = 4;

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
    private SoundManager _sound;
    private GameState _state;
    private int _floor;
    private int _kills;

    // ---------- Реализация IGameWorld (то, что видят враги) ----------

    public DungeonMap Map => _map;
    public Vector2I PlayerPos => _player.GridPos;
    public bool PlayerInvisible => _player.IsInvisible;
    public Dictionary<Vector2I, int> PlayerDistances => _playerDistances;

    public bool IsOccupied(Vector2I cell) =>
        cell == _player.GridPos || EnemyAt(cell) != null || (_merchant != null && _merchant.GridPos == cell);

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
            GameOver(enemy);
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
        SetupEnvironment();

        _sound = new SoundManager();
        AddChild(_sound);

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
        _inventoryPanel.UseRequested += OnInventoryUse;

        _shopPanel = new ShopPanel();
        AddChild(_shopPanel);
        _shopPanel.BuyRequested += BuyItem;
        _shopPanel.SellRequested += SellItem;

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

        switch (key.PhysicalKeycode)
        {
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
        _shopPanel.Visible = false;
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

    private void GameOver(Enemy killer)
    {
        _state = GameState.GameOver;
        UpdateHud();
        _sound.Play(Sound.Death);
        _camera.Shake(0.4f);

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
            $"Побеждено врагов: {_kills}   Золото: {_player.Gold}\n" +
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
        SpawnGold(_rng.Next(2, 5));
        if (bossFloor)
            SpawnBoss();
        else if (ShouldSpawnMerchant())
            SpawnMerchant();

        UpdateFov();
        UpdateHud();
        if (bossFloor)
            _hud.AddMessage("Ты чувствуешь присутствие чего-то огромного... Лестница откроется после победы над боссом.", DamageColor);
        else if (_floor > 1)
            _hud.AddMessage($"Этаж {_floor}. Здесь опаснее.", InfoColor);
        if (_merchant != null)
            _hud.AddMessage("Где-то на этаже звенят монеты — здесь есть торговец.", InfoColor);
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
        _merchant?.QueueFree();
        _merchant = null;
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
            else
            {
                _player.MoveTo(target);
                _sound.Play(Sound.Step);
                TryPickUpItem(target);
                if (_map.Get(target) == Tile.Stairs)
                {
                    _sound.Play(Sound.Stairs);
                    NextFloor();
                    return;
                }
            }
        }

        EndPlayerTurn();
    }

    /// <summary>После любого действия игрока, которое тратит ход, ходят враги и тикают эффекты.</summary>
    private void EndPlayerTurn()
    {
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

        UpdateFov();
        UpdateHud();
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

        if (enemy == _boss)
            OnBossKilled(enemy);
        else
            DropLoot(enemy);

        if (_kills % KillsPerLevel == 0)
        {
            _player.LevelUp();
            _hud.AddMessage($"Новый уровень: {_player.Level}! Атака и здоровье выросли.", InfoColor);
            Effects.FloatingText(this, _player.GridPos, "Уровень!", InfoColor);
            Effects.Burst(this, _player.GridPos, InfoColor, 24);
            _sound.Play(Sound.LevelUp);
        }
    }

    private void OnBossKilled(Enemy boss)
    {
        _boss = null;
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
        enemy.Setup(type, _floor);
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
            if (_map.Get(cell) == Tile.Floor && !IsOccupied(cell) && !_floorItems.ContainsKey(cell))
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

        var visibleEnemies = _enemies.Where(enemy => enemy.Visible).Select(enemy => enemy.GridPos).ToList();
        var markers = _floorItems.Select(pair => (pair.Key, pair.Value.MapColor)).ToList();
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
