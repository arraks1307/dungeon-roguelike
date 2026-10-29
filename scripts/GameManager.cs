using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Главный скрипт игры (висит на корневом узле Main.tscn).
/// Создаёт мир, управляет очерёдностью ходов, этажами, смертью и рестартом.
/// </summary>
public partial class GameManager : Node3D
{
    private const int MapWidth = 48;
    private const int MapHeight = 32;
    private const int FovRadius = 8;
    private const int ChaseDistance = 20;
    private const int PotionHeal = 10;
    private const int KillsPerLevel = 4;
    // Камера почти сверху (~65°), чтобы стены не загораживали игрока
    private static readonly Vector3 CameraOffset = new(0f, 17f, 8f);

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
    private Camera3D _camera;
    private Hud _hud;
    private int _floor;
    private int _kills;
    private bool _gameOver;

    // Режим самопроверки: игра делает N случайных ходов, сохраняет скриншот и закрывается
    private string _screenshotPath;
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
        _player.RestartRequested += OnRestartRequested;

        _camera = new Camera3D { Fov = 50f };
        AddChild(_camera);
        _camera.Position = CameraOffset;
        _camera.LookAt(Vector3.Zero); // поворот задаём один раз, дальше камера только сдвигается

        _hud = new Hud();
        AddChild(_hud);

        ParseCommandLine();
        StartNewGame();
    }

    public override void _Process(double delta)
    {
        // Плавное следование камеры: каждый кадр сокращаем расстояние до цели
        var target = _player.Position + CameraOffset;
        _camera.Position = _camera.Position.Lerp(target, 1f - Mathf.Exp(-8f * (float)delta));

        if (_screenshotPath != null)
            RunSelfTest();
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

    private void StartNewGame()
    {
        _floor = 0;
        _kills = 0;
        _gameOver = false;
        _player.ResetStats();
        _hud.HideGameOver();
        _hud.ClearMessages();
        NextFloor();
        _hud.AddMessage("Ты спускаешься в подземелье. Найди золотую лестницу.", InfoColor);
    }

    private void NextFloor()
    {
        _floor++;

        foreach (var enemy in _enemies)
            enemy.QueueFree();
        _enemies.Clear();
        foreach (var potion in _potions.Values)
            potion.QueueFree();
        _potions.Clear();

        _map = DungeonGenerator.Generate(MapWidth, MapHeight, _rng);
        _renderer.Build(_map);
        _player.Teleport(_map.StartPos);
        _camera.Position = _player.Position + CameraOffset;

        SpawnEnemies(Math.Min(3 + _floor * 2, 25));
        SpawnPotions(_rng.Next(1, 4));

        UpdateFov();
        UpdateHud();
        if (_floor > 1)
            _hud.AddMessage($"Этаж {_floor}. Здесь опаснее.", InfoColor);
    }

    // ---------- Ход игрока ----------

    private void OnPlayerAction(Vector2I direction)
    {
        if (_gameOver)
            return;

        if (direction != Vector2I.Zero)
        {
            var target = _player.GridPos + direction;
            var enemy = EnemyAt(target);

            if (enemy != null)
            {
                AttackEnemy(enemy);
            }
            else if (_map.IsWalkable(target))
            {
                _player.MoveTo(target);
                TryPickUpPotion(target);
                if (_map.Get(target) == Tile.Stairs)
                {
                    NextFloor();
                    return;
                }
            }
            else
            {
                return; // упёрлись в стену — ход не тратится
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

            if (IsAdjacent(enemy.GridPos, _player.GridPos))
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

            var step = enemy.ChooseStep(distances, cell => !IsOccupied(cell));
            if (step.HasValue)
                enemy.MoveTo(step.Value);
        }
    }

    private void GameOver(Enemy killer)
    {
        _gameOver = true;
        UpdateHud();
        _hud.ShowGameOver(
            $"Ты погиб на этаже {_floor}\n" +
            $"Тебя одолел: {killer.DisplayName}\n" +
            $"Побеждено врагов: {_kills}\n\n" +
            "Нажми R, чтобы начать заново");
    }

    private void OnRestartRequested()
    {
        if (_gameOver)
            StartNewGame();
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
    }

    private void UpdateHud() => _hud.UpdateStats(_player, _floor, _kills);

    private Enemy EnemyAt(Vector2I cell) => _enemies.Find(enemy => enemy.GridPos == cell);

    private bool IsOccupied(Vector2I cell) => cell == _player.GridPos || EnemyAt(cell) != null;

    private static bool IsAdjacent(Vector2I a, Vector2I b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

    // Урон = атака ± 1, но не меньше 1
    private int RollDamage(int attack) => Math.Max(1, attack + _rng.Next(-1, 2));

    private void ParseCommandLine()
    {
        // Аргументы после «--» в командной строке Godot, например: -- --screenshot=shot.png --moves=40
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot="))
                _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--moves="))
                _autoMoves = int.Parse(arg["--moves=".Length..]);
        }
    }

    private void RunSelfTest()
    {
        _frame++;
        if (_frame <= _autoMoves * 3)
        {
            // Каждые 3 кадра — случайный шаг
            if (_frame % 3 == 0)
                OnPlayerAction(DungeonMap.Directions[_rng.Next(4)]);
            return;
        }
        if (_frame == _autoMoves * 3 + 40)
        {
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
            GetTree().Quit();
        }
    }
}
