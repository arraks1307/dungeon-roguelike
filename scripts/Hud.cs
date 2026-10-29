using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Интерфейс поверх 3D-сцены: здоровье, этаж, мини-карта, журнал сообщений, экран смерти.
/// CanvasLayer рисуется поверх всего остального, независимо от камеры.
/// </summary>
public partial class Hud : CanvasLayer
{
    private const int MaxMessages = 6;

    private Label _hpLabel;
    private ProgressBar _hpBar;
    private Label _infoLabel;
    private Label _gearLabel;
    private Minimap _minimap;
    private VBoxContainer _log;
    private ColorRect _gameOverPanel;
    private Label _gameOverLabel;

    public override void _Ready()
    {
        // MouseFilter.Ignore везде, где можно: иначе HUD «съедал» бы колёсико мыши для зума
        var root = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            root.AddThemeConstantOverride(side, 16);
        AddChild(root);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(column);

        // Верхняя строка: характеристики слева, мини-карта справа
        var topRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(topRow);

        var stats = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        topRow.AddChild(stats);

        _hpLabel = Ui.CreateLabel(20);
        stats.AddChild(_hpLabel);

        _hpBar = new ProgressBar
        {
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(240f, 14f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _hpBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.2f, 0.05f, 0.05f) });
        _hpBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color(0.85f, 0.15f, 0.15f) });
        stats.AddChild(_hpBar);

        _infoLabel = Ui.CreateLabel(18);
        stats.AddChild(_infoLabel);

        _gearLabel = Ui.CreateLabel(16);
        _gearLabel.Modulate = new Color(1f, 1f, 1f, 0.8f);
        stats.AddChild(_gearLabel);

        // Пустой растягивающийся элемент отодвигает мини-карту к правому краю
        topRow.AddChild(new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        _minimap = new Minimap { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        topRow.AddChild(_minimap);

        // Такой же растягивающийся элемент отодвигает журнал в низ экрана
        column.AddChild(new Control
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        _log = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(_log);

        var help = Ui.CreateLabel(14,
            "WASD / стрелки — ход и атака  •  Q E Z C — диагонали  •  Пробел — ждать  •  " +
            "I — инвентарь  •  Колёсико — зум  •  M — карта  •  Esc — пауза");
        help.Modulate = new Color(1f, 1f, 1f, 0.6f);
        column.AddChild(help);

        _gameOverPanel = new ColorRect { Color = new Color(0f, 0f, 0f, 0.75f), Visible = false };
        _gameOverPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_gameOverPanel);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _gameOverPanel.AddChild(center);

        _gameOverLabel = Ui.CreateLabel(32);
        _gameOverLabel.HorizontalAlignment = HorizontalAlignment.Center;
        center.AddChild(_gameOverLabel);
    }

    public void UpdateStats(Player player, int floor, int kills)
    {
        _hpLabel.Text = $"Здоровье: {player.Hp} / {player.MaxHp}";
        _hpBar.MaxValue = player.MaxHp;
        _hpBar.Value = player.Hp;
        _infoLabel.Text = $"Этаж: {floor}   Уровень: {player.Level}   Атака: {player.Attack}   " +
                          $"Защита: {player.Defense}   Побеждено: {kills}";
        var inventory = player.Inventory;
        _gearLabel.Text = $"Оружие: {inventory.Weapon?.Name ?? "нет"}   Броня: {inventory.Armor?.Name ?? "нет"}   " +
                          $"Сумка: {inventory.Items.Count}/{Inventory.Capacity}";
    }

    public void UpdateMinimap(DungeonMap map, HashSet<Vector2I> visible, Vector2I player,
        List<Vector2I> visibleEnemies, List<(Vector2I Cell, Color Color)> items) =>
        _minimap.UpdateData(map, visible, player, visibleEnemies, items);

    public void ToggleMinimap() => _minimap.Visible = !_minimap.Visible;

    public void AddMessage(string text, Color? color = null)
    {
        var label = Ui.CreateLabel(16, text);
        if (color.HasValue)
            label.AddThemeColorOverride("font_color", color.Value);
        _log.AddChild(label);

        while (_log.GetChildCount() > MaxMessages)
        {
            var oldest = _log.GetChild(0);
            _log.RemoveChild(oldest);
            oldest.QueueFree();
        }

        // Старые сообщения бледнее новых
        int count = _log.GetChildCount();
        for (int i = 0; i < count; i++)
            ((Label)_log.GetChild(i)).Modulate = new Color(1f, 1f, 1f, 0.35f + 0.65f * (i + 1) / count);
    }

    public void ClearMessages()
    {
        foreach (Node child in _log.GetChildren())
        {
            _log.RemoveChild(child);
            child.QueueFree();
        }
    }

    public void ShowGameOver(string text)
    {
        _gameOverLabel.Text = text;
        _gameOverPanel.Visible = true;
    }

    public void HideGameOver() => _gameOverPanel.Visible = false;
}
