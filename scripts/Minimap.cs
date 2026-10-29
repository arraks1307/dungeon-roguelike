using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Мини-карта: рисуется вручную в _Draw, по маленькому квадрату на клетку.
/// QueueRedraw() просит Godot перерисовать её в следующем кадре.
/// </summary>
public partial class Minimap : Control
{
    private const int CellSize = 4;

    private static readonly Color BackgroundColor = new(0f, 0f, 0f, 0.55f);
    private static readonly Color SeenFloorColor = new(0.35f, 0.35f, 0.4f);
    private static readonly Color VisibleFloorColor = new(0.7f, 0.65f, 0.6f);
    private static readonly Color StairsColor = new(1f, 0.8f, 0.25f);
    private static readonly Color PotionColor = new(1f, 0.3f, 0.6f);
    private static readonly Color EnemyColor = new(1f, 0.25f, 0.2f);
    private static readonly Color PlayerColor = new(0.4f, 0.7f, 1f);

    private DungeonMap _map;
    private HashSet<Vector2I> _visible = new();
    private Vector2I _player;
    private List<Vector2I> _enemies = new();
    private List<Vector2I> _items = new();

    public Minimap()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void UpdateData(DungeonMap map, HashSet<Vector2I> visible, Vector2I player,
        List<Vector2I> visibleEnemies, List<Vector2I> items)
    {
        _map = map;
        _visible = visible;
        _player = player;
        _enemies = visibleEnemies;
        _items = items;
        CustomMinimumSize = new Vector2(map.Width * CellSize, map.Height * CellSize);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_map == null)
            return;

        DrawRect(new Rect2(Vector2.Zero, CustomMinimumSize), BackgroundColor);

        for (int x = 0; x < _map.Width; x++)
        {
            for (int y = 0; y < _map.Height; y++)
            {
                var cell = new Vector2I(x, y);
                if (!_map.Explored[x, y] || !_map.IsWalkable(cell))
                    continue;

                var color = _map.Get(cell) == Tile.Stairs ? StairsColor
                    : _visible.Contains(cell) ? VisibleFloorColor
                    : SeenFloorColor;
                DrawCell(cell, color);
            }
        }

        foreach (var item in _items)
            if (_map.Explored[item.X, item.Y])
                DrawCell(item, PotionColor);
        foreach (var enemy in _enemies)
            DrawCell(enemy, EnemyColor);

        // Игрока рисуем чуть крупнее, чтобы его было легко найти
        DrawRect(new Rect2(_player.X * CellSize - 1, _player.Y * CellSize - 1, CellSize + 2, CellSize + 2), PlayerColor);
    }

    private void DrawCell(Vector2I cell, Color color) =>
        DrawRect(new Rect2(cell.X * CellSize, cell.Y * CellSize, CellSize, CellSize), color);
}
