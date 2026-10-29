using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Превращает логическую карту в 3D. Пол и стены рисуются через MultiMesh —
/// это один объект на тысячи одинаковых кубов, что намного быстрее отдельных узлов.
/// </summary>
public partial class DungeonRenderer : Node3D
{
    private static readonly Color FloorColor = new(0.42f, 0.38f, 0.35f);
    private static readonly Color WallColor = new(0.55f, 0.5f, 0.46f);
    private static readonly Color StairsColor = new(1f, 0.8f, 0.25f);

    // Насколько затемнять клетки, которые игрок уже видел, но сейчас не видит
    private const float MemoryDarkening = 0.7f;

    private DungeonMap _map;
    private MultiMesh _floors;
    private MultiMesh _walls;
    private MeshInstance3D _stairs;
    private readonly Dictionary<Vector2I, int> _floorIndex = new();
    private readonly Dictionary<Vector2I, int> _wallIndex = new();

    public void Build(DungeonMap map)
    {
        _map = map;
        foreach (Node child in GetChildren())
            child.QueueFree();
        _floorIndex.Clear();
        _wallIndex.Clear();

        var floorCells = new List<Vector2I>();
        var wallCells = new List<Vector2I>();
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                var p = new Vector2I(x, y);
                if (map.IsWalkable(p))
                    floorCells.Add(p);
                else if (TouchesFloor(p))
                    wallCells.Add(p); // стены глубоко в камне всё равно не видны — не рисуем их
            }
        }

        _floors = CreateMultiMesh(new BoxMesh { Size = new Vector3(1f, 0.1f, 1f) }, floorCells, -0.05f, _floorIndex);
        _walls = CreateMultiMesh(new BoxMesh { Size = new Vector3(1f, 1f, 1f) }, wallCells, 0.5f, _wallIndex);

        var stairsMaterial = new StandardMaterial3D
        {
            AlbedoColor = StairsColor,
            EmissionEnabled = true,
            Emission = StairsColor,
            EmissionEnergyMultiplier = 0.8f,
        };
        _stairs = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.4f, Height = 0.15f, Material = stairsMaterial },
            Position = new Vector3(map.StairsPos.X, 0.08f, map.StairsPos.Y),
            Visible = false,
        };
        AddChild(_stairs);
    }

    /// <summary>
    /// Туман войны: видимые клетки — яркие, исследованные — тёмные, неизвестные — чёрные.
    /// </summary>
    public void UpdateVisibility(HashSet<Vector2I> visible)
    {
        foreach (var (cell, index) in _floorIndex)
        {
            // Шахматный узор на полу помогает глазу считать клетки
            var baseColor = (cell.X + cell.Y) % 2 == 0 ? FloorColor : FloorColor.Darkened(0.12f);
            _floors.SetInstanceColor(index, CellColor(cell, baseColor, visible));
        }
        foreach (var (cell, index) in _wallIndex)
            _walls.SetInstanceColor(index, CellColor(cell, WallColor, visible));

        // На этаже с боссом лестница — обычный пол, пока босс жив
        var stairs = _map.StairsPos;
        _stairs.Visible = _map.Get(stairs) == Tile.Stairs && _map.Explored[stairs.X, stairs.Y];
    }

    private Color CellColor(Vector2I cell, Color baseColor, HashSet<Vector2I> visible)
    {
        if (visible.Contains(cell))
            return baseColor;
        if (_map.Explored[cell.X, cell.Y])
            return baseColor.Darkened(MemoryDarkening);
        return Colors.Black;
    }

    private bool TouchesFloor(Vector2I p)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (_map.IsWalkable(p + new Vector2I(dx, dy)))
                    return true;
        return false;
    }

    private MultiMesh CreateMultiMesh(PrimitiveMesh mesh, List<Vector2I> cells, float height,
        Dictionary<Vector2I, int> index)
    {
        // Цвет каждого куба задаётся отдельно (UseColors), а материал умножает его на белый
        mesh.Material = new StandardMaterial3D { AlbedoColor = Colors.White, VertexColorUseAsAlbedo = true, Roughness = 0.9f };

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
