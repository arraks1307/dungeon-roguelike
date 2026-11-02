using System.Collections.Generic;
using System.Linq;
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
    private Label _effectsLabel;
    private Label _skillsLabel;
    private HBoxContainer _hotbar;
    private Minimap _minimap;
    private VBoxContainer _log;
    private VBoxContainer _bossBox;
    private Label _bossLabel;
    private ProgressBar _bossBar;
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

        _skillsLabel = Ui.CreateLabel(16);
        stats.AddChild(_skillsLabel);

        _effectsLabel = Ui.CreateLabel(16);
        _effectsLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.9f, 1f));
        stats.AddChild(_effectsLabel);

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
            "F G — способности  •  I — инвентарь  •  1–5 — слоты  •  M — карта  •  Esc — пауза");
        help.Modulate = new Color(1f, 1f, 1f, 0.6f);
        column.AddChild(help);

        // Быстрые слоты в правом нижнем углу: якоря справа снизу, растут влево
        _hotbar = new HBoxContainer
        {
            AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = -400f, OffsetRight = -16f, OffsetTop = -96f, OffsetBottom = -36f,
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _hotbar.AddThemeConstantOverride("separation", 6);
        AddChild(_hotbar);

        // Полоска здоровья босса по центру сверху: якоря на середине, отступы ±220 пикселей
        _bossBox = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            OffsetLeft = -220f, OffsetRight = 220f, OffsetTop = 16f,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_bossBox);
        _bossLabel = Ui.CreateLabel(22);
        _bossLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _bossLabel.AddThemeColorOverride("font_color", Ui.TitleColor);
        _bossBox.AddChild(_bossLabel);
        _bossBar = new ProgressBar
        {
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(440f, 18f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bossBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.15f, 0.05f, 0.1f) });
        _bossBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color(0.7f, 0.1f, 0.4f) });
        _bossBox.AddChild(_bossBar);

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
        _infoLabel.Text = $"Этаж: {floor}   Уровень: {player.Level} ({player.Experience}/{player.ExperienceToNextLevel})   Атака: {player.Attack}   " +
                          $"Защита: {player.Defense}   Побеждено: {kills}";
        _gearLabel.Text = $"Золото: {player.Gold}   Сумка: {player.Inventory.Items.Count}/{Inventory.Capacity}   " +
                          $"Оружие: {player.Inventory.GetEquipped(EquipSlot.Weapon)?.Name ?? "нет"}";

        // Способности: готовая подсвечена цветом класса, перезаряжающаяся — серая с числом ходов
        var skills = player.Class.Skills.Select((skill, i) =>
        {
            int cooldown = player.SkillCooldowns[i];
            string key = i == 0 ? "F" : "G";
            return cooldown == 0 ? $"{key}: {skill.Name}" : $"{key}: {skill.Name} ({cooldown})";
        });
        _skillsLabel.Text = $"{player.Class.Name}  •  " + string.Join("   ", skills);
        bool anyReady = player.SkillCooldowns.Any(cooldown => cooldown == 0);
        _skillsLabel.AddThemeColorOverride("font_color", anyReady ? player.Class.Color.Lightened(0.4f) : new Color(0.6f, 0.6f, 0.6f));

        var effects = new List<string>();
        if (player.StrengthTurns > 0)
            effects.Add($"Сила ({player.StrengthTurns})");
        if (player.IsInvisible)
            effects.Add($"Невидимость ({player.InvisibleTurns})");
        _effectsLabel.Text = effects.Count > 0 ? "Эффекты: " + string.Join(", ", effects) : "";

        UpdateHotbar(player.Inventory);
    }

    /// <summary>Пять быстрых слотов: первые пять ячеек сумки, срабатывают клавишами 1–5.</summary>
    private void UpdateHotbar(Inventory inventory)
    {
        foreach (Node child in _hotbar.GetChildren())
        {
            _hotbar.RemoveChild(child);
            child.QueueFree();
        }

        for (int i = 0; i < 5; i++)
        {
            var item = i < inventory.Items.Count ? inventory.Items[i] : null;
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0f, 0f, 0f, 0.55f),
                BorderColor = item?.RarityColor ?? new Color(1f, 1f, 1f, 0.15f),
            };
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(4);

            var slot = new PanelContainer { CustomMinimumSize = new Vector2(64f, 58f), MouseFilter = Control.MouseFilterEnum.Ignore };
            slot.AddThemeStyleboxOverride("panel", style);
            _hotbar.AddChild(slot);

            var content = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            content.AddThemeConstantOverride("separation", 0);
            slot.AddChild(content);

            if (item != null)
            {
                content.AddChild(new TextureRect
                {
                    Texture = ItemIcons.Get(item),
                    StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }
            var caption = Ui.CreateLabel(13, item == null ? $"{i + 1}" : item.Count > 1 ? $"{i + 1}  ×{item.Count}" : $"{i + 1}");
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            content.AddChild(caption);
        }
    }

    /// <summary>Показывает полоску босса, если он есть и уже заметил игрока.</summary>
    public void UpdateBoss(Enemy boss)
    {
        _bossBox.Visible = boss != null && boss.Alerted && !boss.IsDead;
        if (!_bossBox.Visible)
            return;
        _bossLabel.Text = $"{boss.DisplayName}: {boss.Hp} / {boss.MaxHp}";
        _bossBar.MaxValue = boss.MaxHp;
        _bossBar.Value = boss.Hp;
    }

    public void UpdateMinimap(DungeonMap map, HashSet<Vector2I> visible, Vector2I player,
        List<Vector2I> visibleEnemies, List<(Vector2I Cell, Color Color)> items) =>
        _minimap.UpdateData(map, visible, player, visibleEnemies, items);

    public void ToggleMinimap() => _minimap.Visible = !_minimap.Visible;

    /// <summary>true — журнал не пополняется (быстрая симуляция).</summary>
    public bool Silent { get; set; }

    public void AddMessage(string text, Color? color = null)
    {
        if (Silent)
            return;
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
