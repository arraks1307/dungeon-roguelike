using Godot;

namespace Roguelike;

/// <summary>
/// Интерфейс поверх 3D-сцены: здоровье, этаж, журнал сообщений, экран смерти.
/// CanvasLayer рисуется поверх всего остального, независимо от камеры.
/// </summary>
public partial class Hud : CanvasLayer
{
    private const int MaxMessages = 6;

    private Label _hpLabel;
    private ProgressBar _hpBar;
    private Label _infoLabel;
    private VBoxContainer _log;
    private ColorRect _gameOverPanel;
    private Label _gameOverLabel;

    public override void _Ready()
    {
        var root = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            root.AddThemeConstantOverride(side, 16);
        AddChild(root);

        var column = new VBoxContainer();
        root.AddChild(column);

        _hpLabel = CreateLabel(20);
        column.AddChild(_hpLabel);

        _hpBar = new ProgressBar
        {
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(240f, 14f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        _hpBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.2f, 0.05f, 0.05f) });
        _hpBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color(0.85f, 0.15f, 0.15f) });
        column.AddChild(_hpBar);

        _infoLabel = CreateLabel(18);
        column.AddChild(_infoLabel);

        // Пустой растягивающийся элемент отодвигает журнал в низ экрана
        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        _log = new VBoxContainer();
        column.AddChild(_log);

        var help = CreateLabel(14);
        help.Text = "WASD / стрелки — ход и атака  •  Пробел — ждать  •  Золотой круг — лестница вниз";
        help.Modulate = new Color(1f, 1f, 1f, 0.6f);
        column.AddChild(help);

        _gameOverPanel = new ColorRect { Color = new Color(0f, 0f, 0f, 0.75f), Visible = false };
        _gameOverPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_gameOverPanel);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _gameOverPanel.AddChild(center);

        _gameOverLabel = CreateLabel(32);
        _gameOverLabel.HorizontalAlignment = HorizontalAlignment.Center;
        center.AddChild(_gameOverLabel);
    }

    public void UpdateStats(Player player, int floor, int kills)
    {
        _hpLabel.Text = $"Здоровье: {player.Hp} / {player.MaxHp}";
        _hpBar.MaxValue = player.MaxHp;
        _hpBar.Value = player.Hp;
        _infoLabel.Text = $"Этаж: {floor}   Уровень: {player.Level}   Атака: {player.Attack}   Побеждено: {kills}";
    }

    public void AddMessage(string text, Color? color = null)
    {
        var label = CreateLabel(16);
        label.Text = text;
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

    private static Label CreateLabel(int fontSize)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        return label;
    }
}
