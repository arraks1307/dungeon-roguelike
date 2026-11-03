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

    private static readonly Color SeenFloorColor = new(0.35f, 0.35f, 0.4f);
    private static readonly Color VisibleFloorColor = new(0.7f, 0.65f, 0.6f);
    private static readonly Color StairsColor = new(1f, 0.8f, 0.25f);
    private static readonly Color EnemyColor = new(1f, 0.25f, 0.2f);
    private static readonly Color PlayerColor = new(0.4f, 0.7f, 1f);

    private DungeonMap _map;
    private HashSet<Vector2I> _visible = new();
    private Vector2I _player;
    private List<Vector2I> _enemies = new();
    private List<(Vector2I Cell, Color Color)> _items = new();
    private List<(Vector2I Cell, Color Color)> _allies = new();

    private const double PingSeconds = 4.0;
    private readonly List<(Vector2I Cell, Color Color, double Left)> _pings = new();

    public Minimap()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void AddPing(Vector2I cell, Color color)
    {
        _pings.Add((cell, color, PingSeconds));
        QueueRedraw();
    }

    /// <summary>Пока есть метки, перерисовываемся каждый кадр — они мигают и гаснут.</summary>
    public override void _Process(double delta)
    {
        if (_pings.Count == 0)
            return;
        for (int i = _pings.Count - 1; i >= 0; i--)
        {
            var ping = _pings[i];
            if (ping.Left - delta <= 0)
                _pings.RemoveAt(i);
            else
                _pings[i] = (ping.Cell, ping.Color, ping.Left - delta);
        }
        QueueRedraw();
    }

    public void UpdateData(DungeonMap map, HashSet<Vector2I> visible, Vector2I player,
        List<Vector2I> visibleEnemies, List<(Vector2I Cell, Color Color)> items, List<(Vector2I Cell, Color Color)> allies)
    {
        _map = map;
        _visible = visible;
        _player = player;
        _enemies = visibleEnemies;
        _items = items;
        _allies = allies;
        CustomMinimumSize = new Vector2(map.Width * CellSize, map.Height * CellSize);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_map == null)
            return;

        // Подложку рисует «стеклянная» карточка HUD, здесь — только сама карта

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

        foreach (var (cell, color) in _items)
            if (_map.Explored[cell.X, cell.Y])
                DrawCell(cell, color);
        foreach (var enemy in _enemies)
            DrawCell(enemy, EnemyColor);
        foreach (var (cell, color) in _allies)
            DrawCell(cell, color); // союзники — цветом своего класса

        // Метки: расходящееся кольцо, мигает примерно два раза в секунду
        foreach (var (cell, color, left) in _pings)
        {
            float pulse = (float)(left * 2.0 % 1.0);
            var center = new Vector2((cell.X + 0.5f) * CellSize, (cell.Y + 0.5f) * CellSize);
            DrawArc(center, CellSize * (1.5f + 2.5f * (1f - pulse)), 0f, Mathf.Tau, 16, color, 1.5f);
            DrawCell(cell, color);
        }

        // Игрока рисуем чуть крупнее, чтобы его было легко найти
        DrawRect(new Rect2(_player.X * CellSize - 1, _player.Y * CellSize - 1, CellSize + 2, CellSize + 2), PlayerColor);
    }

    private void DrawCell(Vector2I cell, Color color) =>
        DrawRect(new Rect2(cell.X * CellSize, cell.Y * CellSize, CellSize, CellSize), color);
}
