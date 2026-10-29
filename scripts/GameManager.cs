using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

public enum GameState
{
    Menu,
    Playing,
    Paused,
    GameOver,
}

/// <summary>
/// Главный скрипт игры (висит на корневом узле Main.tscn).
/// Создаёт мир, управляет состояниями (меню, игра, пауза, смерть),
/// очерёдностью ходов, этажами и рестартом.
/// </summary>
public partial class GameManager : Node3D
{
    private const int MapWidth = 48;
    private const int MapHeight = 32;
    private const int FovRadius = 8;
    private const int ChaseDistance = 20;
    private const int PotionHeal = 10;
    private const int KillsPerLevel = 4;

    private static readonly Color DamageColor = new(1f, 0.45f, 0.4f);
    private static readonly Color GoodColor = new(0.5f, 1f, 0.5f);
    private static readonly Color InfoColor = new(1f, 0.85f, 0.4f);

    private readonly Random _rng = new();
    private readonly List<Enemy> _enemies = new();
    private readonly Dictionary<Vector2I, MeshInstance3D> _potions = new();
    private HashSet<Vector2I> _visible = new();

    private DungeonMap _map;
    private DungeonRenderer _renderer;
    private Player _player;
    private CameraRig _camera;
    private Hud _hud;
    private MainMenu _mainMenu;
    private PauseMenu _pauseMenu;
    private GameState _state;
    private int _floor;
    private int _kills;

    // Режим самопроверки: игра делает N случайных ходов, сохраняет скриншот и закрывается
    private string _screenshotPath;
    private string _screenshotView = "play";
    private int _autoMoves;
    private int _frame;

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
    /// Клавиши уровня всей игры. Движение обрабатывает Player, а здесь — Esc, R, M и колёсико.
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
                    case GameState.Paused: Resume(); break;
                    case GameState.GameOver: ShowMainMenu(); break;
                }
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
        _camera.MenuMode = true;
        _camera.MenuCenter = new Vector3(MapWidth / 2f, 0f, MapHeight / 2f);
        _camera.SnapToTarget();
        _mainMenu.ShowMain();
    }

    private void StartNewGame()
    {
        _state = GameState.Playing;
        _floor = 0;
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

        _map = DungeonGenerator.Generate(MapWidth, MapHeight, _rng);
        _renderer.Build(_map);
        _player.Teleport(_map.StartPos);
        _camera.SnapToTarget();

        SpawnEnemies(Math.Min(3 + _floor * 2, 25));
        SpawnPotions(_rng.Next(1, 4));

        UpdateFov();
        UpdateHud();
        if (_floor > 1)
            _hud.AddMessage($"Этаж {_floor}. Здесь опаснее.", InfoColor);
    }

    private void ClearLevelObjects()
    {
        foreach (var enemy in _enemies)
            enemy.QueueFree();
        _enemies.Clear();
        foreach (var potion in _potions.Values)
            potion.QueueFree();
        _potions.Clear();
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
                TryPickUpPotion(target);
                if (_map.Get(target) == Tile.Stairs)
                {
                    NextFloor();
                    return;
                }
            }
        }

        EnemiesTurn();
        UpdateFov();
        UpdateHud();
    }

    private void AttackEnemy(Enemy enemy)
    {
        _player.PlayAttack(enemy.GridPos);
        int damage = RollDamage(_player.Attack);
        enemy.TakeDamage(damage);

        if (!enemy.IsDead)
        {
            _hud.AddMessage($"{enemy.DisplayName} получает {damage} урона.");
            return;
        }

        _hud.AddMessage($"{enemy.DisplayName} повержен!", GoodColor);
        _enemies.Remove(enemy);
        enemy.Die();
        _kills++;

        if (_kills % KillsPerLevel == 0)
        {
            _player.LevelUp();
            _hud.AddMessage($"Новый уровень: {_player.Level}! Атака и здоровье выросли.", InfoColor);
        }
    }

    private void TryPickUpPotion(Vector2I cell)
    {
        if (!_potions.Remove(cell, out var potion))
            return;
        potion.QueueFree();
        _player.Heal(PotionHeal);
        _hud.AddMessage($"Ты выпиваешь зелье: +{PotionHeal} здоровья.", GoodColor);
    }

    // ---------- Ход врагов ----------

    private void EnemiesTurn()
    {
        // Поле расстояний считаем один раз за ход — им пользуются все враги
        var distances = _map.DistanceField(_player.GridPos, ChaseDistance);

        foreach (var enemy in _enemies)
        {
            if (_visible.Contains(enemy.GridPos))
                enemy.Alerted = true;
            if (!enemy.Alerted || !enemy.ShouldActThisTurn())
                continue;

            if (_map.AreAdjacent(enemy.GridPos, _player.GridPos))
            {
                enemy.PlayAttack(_player.GridPos);
                int damage = RollDamage(enemy.Attack);
                _player.TakeDamage(damage);
                _hud.AddMessage($"{enemy.DisplayName} бьёт тебя: -{damage}.", DamageColor);

                if (_player.IsDead)
                {
                    GameOver(enemy);
                    return;
                }
                continue;
            }

            var step = enemy.ChooseStep(distances, cell => _map.CanMove(enemy.GridPos, cell) && !IsOccupied(cell));
            if (step.HasValue)
                enemy.MoveTo(step.Value);
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
            var enemy = new Enemy();
            enemy.Setup(available[_rng.Next(available.Length)], _floor);
            AddChild(enemy);
            enemy.Teleport(cell);
            _enemies.Add(enemy);
        }
    }

    private void SpawnPotions(int count)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.3f, 0.6f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.3f, 0.6f),
            EmissionEnergyMultiplier = 0.6f,
        };
        for (int i = 0; i < count; i++)
        {
            if (!TryFindFreeCell(out var cell) || _potions.ContainsKey(cell))
                continue;
            var potion = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.15f, Height = 0.3f, Material = material },
                Position = Actor.GridToWorld(cell) + new Vector3(0f, 0.15f, 0f),
            };
            AddChild(potion);
            _potions[cell] = potion;
        }
    }

    /// <summary>Случайная свободная клетка пола в любой комнате, кроме стартовой.</summary>
    private bool TryFindFreeCell(out Vector2I cell)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var room = _map.Rooms[_rng.Next(1, _map.Rooms.Count)];
            cell = new Vector2I(
                _rng.Next(room.Position.X, room.End.X),
                _rng.Next(room.Position.Y, room.End.Y));
            if (_map.Get(cell) == Tile.Floor && !IsOccupied(cell))
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
        foreach (var (cell, potion) in _potions)
            potion.Visible = _map.Explored[cell.X, cell.Y];

        var visibleEnemies = _enemies.Where(enemy => enemy.Visible).Select(enemy => enemy.GridPos).ToList();
        _hud.UpdateMinimap(_map, _visible, _player.GridPos, visibleEnemies, _potions.Keys.ToList());
    }

    private void UpdateHud() => _hud.UpdateStats(_player, _floor, _kills);

    private Enemy EnemyAt(Vector2I cell) => _enemies.Find(enemy => enemy.GridPos == cell);

    private bool IsOccupied(Vector2I cell) => cell == _player.GridPos || EnemyAt(cell) != null;

    // Урон = атака ± 1, но не меньше 1
    private int RollDamage(int attack) => Math.Max(1, attack + _rng.Next(-1, 2));

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
        }
    }

    /// <summary>
    /// --view=menu — снимок главного меню, scores — таблицы рекордов,
    /// pause — паузы после ходов, play (по умолчанию) — просто игры после ходов.
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
            // Каждые 3 кадра — случайный шаг
            if (_frame % 3 == 0)
                OnPlayerAction(DungeonMap.Directions[_rng.Next(DungeonMap.Directions.Length)]);
            return;
        }

        if (_frame == moveFrames + 1 && _screenshotView == "pause" && _state == GameState.Playing)
            Pause();

        if (_frame == moveFrames + 40)
        {
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
            GetTree().Quit();
        }
    }
}
