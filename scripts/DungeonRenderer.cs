using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Превращает логическую карту в 3D. Пол и стены рисуются через MultiMesh —
/// это один объект на тысячи одинаковых кубов, что намного быстрее отдельных узлов.
/// Двери, потайные двери, ловушки, факелы и декор — отдельные узлы, их немного.
/// </summary>
public partial class DungeonRenderer : Node3D
{
    private static readonly Color StairsColor = new(1f, 0.8f, 0.25f);
    private static readonly Color DoorColor = new(0.45f, 0.28f, 0.14f);

    // Насколько затемнять клетки, которые игрок уже видел, но сейчас не видит
    private const float MemoryDarkening = 0.7f;
    private const double DecorChance = 0.5;

    private DungeonMap _map;
    private DungeonTheme _theme;
    private MultiMesh _floors;
    private MultiMesh _walls;

    private const float FogFadeSeconds = 0.22f;
    private readonly Dictionary<(MultiMesh Mesh, int Index), CellFade> _fades = new();
    private Color[] _floorTargets; // к какому цвету идёт каждая клетка; null — этаж только что построен
    private Color[] _wallTargets;
    private MeshInstance3D _stairs;
    private readonly Dictionary<Vector2I, int> _floorIndex = new();
    private readonly Dictionary<Vector2I, int> _wallIndex = new();
    private readonly List<(Vector2I Wall, Torch Torch)> _torches = new();
    private readonly Dictionary<Vector2I, Door3D> _doors = new();
    private readonly Dictionary<Vector2I, MeshInstance3D> _secretWalls = new();
    private readonly Dictionary<Vector2I, Node3D> _traps = new();
    private readonly List<(Vector2I Cell, Node3D Node)> _decor = new();
    private readonly System.Random _rng = new();

    public void Build(DungeonMap map)
    {
        _map = map;
        _theme = map.Theme;
        foreach (Node child in GetChildren())
            child.QueueFree();
        _floorIndex.Clear();
        _wallIndex.Clear();
        _fades.Clear();
        _floorTargets = null;
        _wallTargets = null;
        _torches.Clear();
        _doors.Clear();
        _secretWalls.Clear();
        _traps.Clear();
        _decor.Clear();

        var floorCells = new List<Vector2I>();
        var wallCells = new List<Vector2I>();
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                var p = new Vector2I(x, y);
                var tile = map.Get(p);
                if (tile == Tile.SecretDoor)
                {
                    floorCells.Add(p); // под потайной дверью — пол, сверху — «стена»-обманка
                    CreateSecretWall(p);
                }
                else if (map.IsWalkable(p))
                {
                    floorCells.Add(p);
                    if (map.IsDoor(p))
                        CreateDoor(p);
                }
                else if (TouchesFloor(p))
                {
                    wallCells.Add(p); // стены глубоко в камне всё равно не видны — не рисуем их
                }
            }
        }

        _floors = CreateMultiMesh(new BoxMesh { Size = new Vector3(1f, 0.1f, 1f), Material = SurfaceMaterials.Floor }, floorCells, -0.05f, _floorIndex);
        _walls = CreateMultiMesh(new BoxMesh { Size = new Vector3(1f, 1f, 1f), Material = SurfaceMaterials.Wall }, wallCells, 0.5f, _wallIndex);

        _stairs = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.3f, BottomRadius = 0.4f, Height = 0.15f,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = StairsColor, EmissionEnabled = true, Emission = StairsColor, EmissionEnergyMultiplier = 0.8f,
                },
            },
            Position = new Vector3(map.StairsPos.X, 0.08f, map.StairsPos.Y),
            Visible = false,
        };
        AddChild(_stairs);

        PlaceTorches();
        PlaceDecor();
    }

    // ---------- Динамические изменения карты ----------

    /// <summary>Дверь открылась — анимируем створку.</summary>
    public void OpenDoor(Vector2I cell)
    {
        if (_doors.TryGetValue(cell, out var door))
            door.Open();
    }

    /// <summary>Потайную дверь нашли: убираем «стену»-обманку и ставим обычную дверь.</summary>
    public void RevealSecretDoor(Vector2I cell)
    {
        if (_secretWalls.Remove(cell, out var wall))
            wall.QueueFree();
        CreateDoor(cell);
    }

    // ---------- Туман войны ----------

    /// <summary>
    /// Видимые клетки — яркие, исследованные — тёмные, неизвестные — чёрные.
    /// Заодно показываем то, что игрок уже обнаружил: лестницу, факелы, двери, ловушки, декор.
    /// </summary>
    public void UpdateVisibility(HashSet<Vector2I> visible)
    {
        // Первое обновление нового этажа — сразу, без перехода: сравнивать ещё не с чем
        bool snap = _floorTargets == null;
        _floorTargets ??= new Color[_floorIndex.Count];
        _wallTargets ??= new Color[_wallIndex.Count];
        bool animate = !snap && Effects.Enabled && Settings.AnimationScale > 0f;

        foreach (var (cell, index) in _floorIndex)
        {
            // Шахматный узор на полу помогает глазу считать клетки
            var baseColor = (cell.X + cell.Y) % 2 == 0 ? _theme.Floor : _theme.Floor.Darkened(0.12f);
            SetCellColor(_floors, _floorTargets, index, CellColor(cell, baseColor, visible), animate);
        }
        foreach (var (cell, index) in _wallIndex)
            SetCellColor(_walls, _wallTargets, index, CellColor(cell, _theme.Wall, visible), animate);

        foreach (var (cell, wall) in _secretWalls)
            ((StandardMaterial3D)((BoxMesh)wall.Mesh).Material).AlbedoColor = CellColor(cell, _theme.Wall, visible);
        foreach (var (cell, door) in _doors)
        {
            door.Visible = IsExplored(cell);
            door.SetColor(CellColor(cell, DoorColor, visible));
        }

        // На этаже с боссом лестница — обычный пол, пока босс жив
        var stairs = _map.StairsPos;
        _stairs.Visible = _map.Get(stairs) == Tile.Stairs && IsExplored(stairs);

        // Факел (и его свет) появляется, когда игрок увидел стену, на которой он висит
        foreach (var (wall, torch) in _torches)
            torch.Visible = IsExplored(wall);
        foreach (var (cell, node) in _decor)
            node.Visible = IsExplored(cell);

        UpdateTraps();
    }

    /// <summary>Переход цвета одной клетки: туман войны не щёлкает, а плавно наплывает.</summary>
    private sealed class CellFade
    {
        public Color From;
        public Color To;
        public float Time; // от 0 до 1

        public Color Current => From.Lerp(To, Time * Time * (3f - 2f * Time)); // плавный разгон и торможение
    }

    /// <summary>Запоминаем цель; анимируем только изменившиеся клетки — обычно их десятки, а не тысячи.</summary>
    private void SetCellColor(MultiMesh mesh, Color[] targets, int index, Color color, bool animate)
    {
        if (targets[index] == color)
            return;
        var key = (mesh, index);
        if (!animate)
        {
            _fades.Remove(key);
            targets[index] = color;
            mesh.SetInstanceColor(index, color);
            return;
        }
        var from = _fades.TryGetValue(key, out var running) ? running.Current : targets[index];
        targets[index] = color;
        _fades[key] = new CellFade { From = from, To = color };
    }

    public override void _Process(double delta)
    {
        if (_fades.Count == 0)
            return;
        foreach (var (key, fade) in _fades.ToList())
        {
            fade.Time = Mathf.Min(fade.Time + (float)delta / (FogFadeSeconds * Settings.AnimationScale), 1f);
            key.Mesh.SetInstanceColor(key.Index, fade.Current);
            if (fade.Time >= 1f)
                _fades.Remove(key);
        }
    }

    /// <summary>Создаём узлы для только что обнаруженных ловушек и убираем сработавшие одноразовые.</summary>
    private void UpdateTraps()
    {
        foreach (var (cell, trap) in _map.Traps)
        {
            if (trap.Revealed && !_traps.ContainsKey(cell))
            {
                var node = CreateTrap(trap.Kind);
                node.Position = Actor.GridToWorld(cell);
                AddChild(node);
                _traps[cell] = node;
            }
        }
        foreach (var cell in _traps.Keys.Where(cell => !_map.Traps.ContainsKey(cell)).ToList())
        {
            _traps[cell].QueueFree();
            _traps.Remove(cell);
        }
    }

    private bool IsExplored(Vector2I cell) => _map.Explored[cell.X, cell.Y];

    private Color CellColor(Vector2I cell, Color baseColor, HashSet<Vector2I> visible)
    {
        if (visible.Contains(cell))
            return baseColor;
        if (IsExplored(cell))
            return baseColor.Darkened(MemoryDarkening);
        return Colors.Black;
    }

    // ---------- Создание объектов ----------

    private void CreateDoor(Vector2I cell)
    {
        // Если стены слева и справа — проход идёт сверху вниз, створка лежит вдоль оси X
        bool spansX = _map.Get(cell + new Vector2I(-1, 0)) == Tile.Wall && _map.Get(cell + new Vector2I(1, 0)) == Tile.Wall;
        var door = new Door3D { Position = Actor.GridToWorld(cell), SpansX = spansX, Visible = false };
        AddChild(door);
        _doors[cell] = door;
        if (_map.Get(cell) == Tile.OpenDoor)
            door.Open(instant: true);
    }

    private void CreateSecretWall(Vector2I cell)
    {
        // Копия материала настоящих стен (Duplicate) — иначе потайную дверь выдала бы другая текстура.
        // Своя копия нужна, чтобы менять ей цвет (туман войны) отдельно от всех стен.
        var material = (StandardMaterial3D)SurfaceMaterials.Wall.Duplicate();
        material.AlbedoColor = Colors.Black;
        var wall = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One, Material = material },
            Position = new Vector3(cell.X, 0.5f, cell.Y),
        };
        AddChild(wall);
        _secretWalls[cell] = wall;
    }

    private static Node3D CreateTrap(TrapKind kind)
    {
        var root = new Node3D();
        if (kind == TrapKind.Lava)
        {
            root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.96f, 0.04f, 0.96f) }, new Color(1f, 0.4f, 0.05f), new Vector3(0f, 0.02f, 0f), glow: 2.5f));
        }
        else if (kind == TrapKind.Spikes)
        {
            root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.8f, 0.04f, 0.8f) }, new Color(0.2f, 0.2f, 0.22f), new Vector3(0f, 0.02f, 0f)));
            foreach (var offset in new[] { new Vector2(-0.2f, -0.2f), new Vector2(0.2f, -0.2f), new Vector2(-0.2f, 0.2f), new Vector2(0.2f, 0.2f) })
                root.AddChild(Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.07f, Height = 0.25f },
                    new Color(0.75f, 0.75f, 0.8f), new Vector3(offset.X, 0.14f, offset.Y)));
        }
        else
        {
            // Магическая руна: светящийся диск (красный — тревога, синий — телепорт)
            var color = kind == TrapKind.Alarm ? new Color(1f, 0.25f, 0.2f) : new Color(0.4f, 0.6f, 1f);
            root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.38f, BottomRadius = 0.38f, Height = 0.02f }, color, new Vector3(0f, 0.02f, 0f), glow: 1.5f));
            root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 0.03f }, new Color(0.1f, 0.1f, 0.12f), new Vector3(0f, 0.025f, 0f)));
        }
        return root;
    }

    /// <summary>
    /// По факелу на верхнюю стену комнаты (в пещерах — не в каждой). Верхняя стена повёрнута к камере,
    /// поэтому факел на ней хорошо видно. Под факелом обязательно должен быть пол.
    /// </summary>
    private void PlaceTorches()
    {
        foreach (var room in _map.Rooms)
        {
            if (_rng.NextDouble() > _theme.TorchChance)
                continue;
            int wallY = room.Position.Y - 1;
            var candidates = new List<Vector2I>();
            for (int x = room.Position.X; x < room.End.X; x++)
            {
                var wall = new Vector2I(x, wallY);
                if (_map.Get(wall) == Tile.Wall && _map.IsWalkable(wall + new Vector2I(0, 1)))
                    candidates.Add(wall);
            }
            if (candidates.Count == 0)
                continue;

            var chosen = candidates[_rng.Next(candidates.Count)];
            var torch = new Torch { FlameColor = _theme.Torch, Position = new Vector3(chosen.X, 0.8f, chosen.Y + 0.55f), Visible = false };
            AddChild(torch);
            _torches.Add((chosen, torch));
        }
    }

    /// <summary>
    /// Декор у стен комнат: только для красоты, сквозь него можно ходить.
    /// Предмет сдвигаем к стене, чтобы он не закрывал врагов и вещи в центре клетки.
    /// </summary>
    private void PlaceDecor()
    {
        foreach (var room in _map.Rooms.Concat(_map.SecretRooms))
        {
            for (int i = 0; i < 3; i++)
            {
                if (_rng.NextDouble() > DecorChance)
                    continue;
                var cell = new Vector2I(_rng.Next(room.Position.X, room.End.X), _rng.Next(room.Position.Y, room.End.Y));
                if (_map.Get(cell) != Tile.Floor || cell == _map.StartPos)
                    continue;

                // Ищем соседнюю стену и прижимаемся к ней
                var wallDirection = DungeonMap.Directions.Take(4).FirstOrDefault(d => _map.Get(cell + d) == Tile.Wall);
                if (wallDirection == Vector2I.Zero)
                    continue;

                var kind = _theme.Decor[_rng.Next(_theme.Decor.Length)];
                var node = CreateDecor(kind);
                node.Position = Actor.GridToWorld(cell) + new Vector3(wallDirection.X, 0f, wallDirection.Y) * 0.32f;
                node.Rotation = new Vector3(0f, (float)_rng.NextDouble() * Mathf.Tau, 0f);
                node.Visible = false;
                AddChild(node);
                _decor.Add((cell, node));
            }
        }
    }

    private static Node3D CreateDecor(DecorKind kind)
    {
        var root = new Node3D();
        switch (kind)
        {
            case DecorKind.Barrel:
                root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.45f }, new Color(0.5f, 0.32f, 0.15f), new Vector3(0f, 0.22f, 0f)));
                root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.18f, Height = 0.04f }, new Color(0.3f, 0.3f, 0.32f), new Vector3(0f, 0.35f, 0f)));
                break;
            case DecorKind.Crate:
                root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.35f, 0.35f, 0.35f) }, new Color(0.62f, 0.45f, 0.25f), new Vector3(0f, 0.17f, 0f)));
                break;
            case DecorKind.Mushroom:
                root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.04f, Height = 0.2f }, new Color(0.85f, 0.85f, 0.75f), new Vector3(0f, 0.1f, 0f)));
                root.AddChild(Mesh(new SphereMesh { Radius = 0.12f, Height = 0.12f }, new Color(0.3f, 0.95f, 0.9f), new Vector3(0f, 0.22f, 0f), glow: 1.2f));
                break;
            case DecorKind.Rock:
                root.AddChild(Mesh(new SphereMesh { Radius = 0.2f, Height = 0.22f }, new Color(0.4f, 0.38f, 0.36f), new Vector3(0f, 0.08f, 0f)));
                break;
            case DecorKind.Bones:
                root.AddChild(Mesh(new SphereMesh { Radius = 0.08f, Height = 0.14f }, new Color(0.9f, 0.88f, 0.8f), new Vector3(0f, 0.06f, 0f)));
                root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.3f, 0.04f, 0.05f) }, new Color(0.9f, 0.88f, 0.8f), new Vector3(0.1f, 0.02f, 0.1f)));
                root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.05f, 0.04f, 0.28f) }, new Color(0.9f, 0.88f, 0.8f), new Vector3(-0.1f, 0.02f, 0.05f)));
                break;
            case DecorKind.Tombstone:
                root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.35f, 0.5f, 0.1f) }, new Color(0.5f, 0.52f, 0.55f), new Vector3(0f, 0.25f, 0f)));
                break;
            case DecorKind.Candle:
                root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.2f }, new Color(0.95f, 0.92f, 0.85f), new Vector3(0f, 0.1f, 0f)));
                root.AddChild(Mesh(new SphereMesh { Radius = 0.035f, Height = 0.08f }, new Color(0.5f, 1f, 0.6f), new Vector3(0f, 0.24f, 0f), glow: 3f));
                break;
        }
        return root;
    }

    private static MeshInstance3D Mesh(PrimitiveMesh mesh, Color color, Vector3 position, float glow = 0f)
    {
        var material = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.8f };
        if (glow > 0f)
        {
            material.EmissionEnabled = true;
            material.Emission = color;
            material.EmissionEnergyMultiplier = glow;
        }
        mesh.Material = material;
        return new MeshInstance3D { Mesh = mesh, Position = position };
    }

    private bool TouchesFloor(Vector2I p)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var neighbor = p + new Vector2I(dx, dy);
                if (_map.IsWalkable(neighbor) || _map.Get(neighbor) == Tile.SecretDoor)
                    return true;
            }
        return false;
    }

    private MultiMesh CreateMultiMesh(PrimitiveMesh mesh, List<Vector2I> cells, float height,
        Dictionary<Vector2I, int> index)
    {
        // Цвет каждого куба задаётся отдельно (UseColors), а материал (SurfaceMaterials) умножает его на текстуру камня
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = cells.Count, // задаётся строго после формата и UseColors
        };

        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(cell.X, height, cell.Y)));
            multiMesh.SetInstanceColor(i, Colors.Black);
            index[cell] = i;
        }

        AddChild(new MultiMeshInstance3D { Multimesh = multiMesh });
        return multiMesh;
    }
}
