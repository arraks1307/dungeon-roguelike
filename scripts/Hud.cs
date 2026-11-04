using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Интерфейс поверх 3D-сцены: карточка героя (здоровье, опыт, характеристики, способности),
/// мини-карта, журнал сообщений, быстрые слоты, полоска босса и экран итогов забега.
/// CanvasLayer рисуется поверх всего остального, независимо от камеры.
/// </summary>
public partial class Hud : CanvasLayer
{
    private const int MaxMessages = 6;
    private static readonly Color HealthColor = new(0.9f, 0.3f, 0.35f);
    private static readonly Color BossColor = new(0.75f, 0.2f, 0.5f);

    private Label _classLabel;
    private Label _hpLabel;
    private ProgressBar _hpBar;
    private ProgressBar _xpBar;
    private Label _infoLabel;
    private Label _goldLabel;
    private Label _effectsLabel;
    private HBoxContainer _skills;
    private HBoxContainer _hotbar;
    private PanelContainer _minimapCard;
    private Minimap _minimap;
    private VBoxContainer _log;
    private VBoxContainer _bossBox;
    private Label _bossLabel;
    private ProgressBar _bossBar;
    private ColorRect _gameOverPanel;
    private Label _gameOverLabel;
    private VBoxContainer _partyBox;
    private Label _waitingLabel;
    private Label _stairsLabel;
    private LineEdit _chatEdit;
    private readonly Dictionary<ProgressBar, Tween> _barTweens = new();
    private ColorRect _fade;
    private Tween _fadeTween;
    private PanelContainer _tutorialCard;
    private Label _tutorialProgress;
    private Label _tutorialTitle;
    private Label _tutorialText;

    public event System.Action<string> ChatSubmitted;
    public event System.Action ChatCancelled;
    public event System.Action TutorialSkipRequested;

    public override void _Ready()
    {
        // MouseFilter.Ignore везде, где можно: иначе HUD «съедал» бы колёсико мыши для зума
        var root = new Control { Theme = UiTheme.Main, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // Виньетка — первой, чтобы оказаться под всеми остальными элементами
        root.AddChild(UiTheme.CreateVignette(0.4f));

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 16);
        root.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddChild(column);

        // Верхняя строка: карточка героя слева, мини-карта справа
        var topRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(topRow);
        // Слева столбик: свой герой, под ним — остальные члены отряда и кого ждём
        var leftColumn = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, MouseFilter = Control.MouseFilterEnum.Ignore };
        leftColumn.AddThemeConstantOverride("separation", 8);
        topRow.AddChild(leftColumn);
        leftColumn.AddChild(CreateHeroCard());
        _partyBox = new VBoxContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _partyBox.AddThemeConstantOverride("separation", 6);
        leftColumn.AddChild(_partyBox);
        _waitingLabel = Ui.CreateMutedLabel(14);
        _waitingLabel.Visible = false;
        leftColumn.AddChild(_waitingLabel);
        _stairsLabel = Ui.CreateLabel(14);
        _stairsLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        _stairsLabel.Visible = false;
        leftColumn.AddChild(_stairsLabel);

        topRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        _minimapCard = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, MouseFilter = Control.MouseFilterEnum.Ignore };
        _minimapCard.AddThemeStyleboxOverride("panel", UiTheme.Glass(0.55f, 12, 8f));
        topRow.AddChild(_minimapCard);
        _minimap = new Minimap();
        _minimapCard.AddChild(_minimap);

        // Растягивающийся элемент отодвигает журнал в низ экрана
        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        _log = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _log.AddThemeConstantOverride("separation", 2);
        column.AddChild(_log);

        // Строка чата (сетевая игра): появляется по Enter, Enter — отправить, Esc — закрыть
        _chatEdit = new LineEdit
        {
            Visible = false,
            MaxLength = NetSession.MaxChatLength,
            PlaceholderText = "Сообщение отряду…  Enter — отправить, Esc — закрыть",
            CustomMinimumSize = new Vector2(560f, 40f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        _chatEdit.AddThemeFontSizeOverride("font_size", 17);
        _chatEdit.TextSubmitted += text => ChatSubmitted?.Invoke(text);
        _chatEdit.GuiInput += @event =>
        {
            if (@event is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape })
            {
                _chatEdit.AcceptEvent();
                ChatCancelled?.Invoke();
            }
        };
        column.AddChild(_chatEdit);

        // Быстрые слоты в правом нижнем углу: якоря справа снизу, растут влево
        _hotbar = new HBoxContainer
        {
            AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = -400f, OffsetRight = -16f, OffsetTop = -108f, OffsetBottom = -40f,
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _hotbar.AddThemeConstantOverride("separation", 8);
        root.AddChild(_hotbar);

        CreateBossBar(root);
        CreateTutorialCard(root);
        CreateGameOverScreen(root);

        // Затемнение при смене этажа — последним, поверх всего HUD; клики сквозь него проходят
        _fade = new ColorRect { Color = Colors.Black, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_fade);
    }

    /// <summary>Карточка героя: класс и уровень, полоски здоровья и опыта, характеристики, способности.</summary>
    private Control CreateHeroCard()
    {
        var card = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, MouseFilter = Control.MouseFilterEnum.Ignore };
        card.AddThemeStyleboxOverride("panel", UiTheme.Glass(0.55f, 14, 14f));

        var stats = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        stats.AddThemeConstantOverride("separation", 6);
        card.AddChild(stats);

        _classLabel = Ui.CreateLabel(17);
        stats.AddChild(_classLabel);

        _hpLabel = Ui.CreateLabel(15);
        stats.AddChild(_hpLabel);
        _hpBar = CreateBar(280f, 10f, HealthColor);
        stats.AddChild(_hpBar);
        _xpBar = CreateBar(280f, 4f, null); // цвет — акцент темы, то есть цвет класса
        stats.AddChild(_xpBar);

        _infoLabel = Ui.CreateMutedLabel(15);
        stats.AddChild(_infoLabel);
        _goldLabel = Ui.CreateMutedLabel(15);
        stats.AddChild(_goldLabel);

        _skills = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _skills.AddThemeConstantOverride("separation", 6);
        stats.AddChild(_skills);

        _effectsLabel = Ui.CreateLabel(14);
        _effectsLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.9f, 1f));
        stats.AddChild(_effectsLabel);
        return card;
    }

    private static ProgressBar CreateBar(float width, float height, Color? fill)
    {
        var bar = new ProgressBar
        {
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(width, height),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (fill.HasValue)
        {
            var style = new StyleBoxFlat { BgColor = fill.Value };
            style.SetCornerRadiusAll(6);
            bar.AddThemeStyleboxOverride("fill", style);
        }
        return bar;
    }

    /// <summary>Полоска здоровья босса по центру сверху: якоря на середине, отступы ±240 пикселей.</summary>
    private void CreateBossBar(Control root)
    {
        _bossBox = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            OffsetLeft = -240f, OffsetRight = 240f, OffsetTop = 20f,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bossBox.AddThemeConstantOverride("separation", 6);
        root.AddChild(_bossBox);
        _bossLabel = Ui.CreateLabel(20);
        _bossLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _bossBox.AddChild(_bossLabel);
        _bossBar = CreateBar(480f, 12f, BossColor);
        _bossBox.AddChild(_bossBar);
    }

    private void CreateGameOverScreen(Control root)
    {
        _gameOverPanel = new ColorRect { Color = new Color(0.02f, 0.03f, 0.05f, 0.7f), Visible = false };
        _gameOverPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_gameOverPanel);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _gameOverPanel.AddChild(center);

        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiTheme.Glass(0.85f, 18, 36f));
        center.AddChild(card);
        _gameOverLabel = Ui.CreateLabel(26);
        _gameOverLabel.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(_gameOverLabel);
    }

    public void UpdateStats(Player player, int floor, int kills)
    {
        // В отряде у героя есть имя игрока — показываем его рядом с классом
        _classLabel.Text = player.DisplayName == player.Class.Name
            ? $"{player.Class.Name}  •  уровень {player.Level}"
            : $"{player.DisplayName}  •  {player.Class.Name}  •  уровень {player.Level}";
        _classLabel.AddThemeColorOverride("font_color", player.Class.Color.Lightened(0.35f));

        _hpLabel.Text = $"Здоровье  {player.Hp} / {player.MaxHp}";
        SetBar(_hpBar, player.Hp, player.MaxHp);
        SetBar(_xpBar, player.Experience, player.ExperienceToNextLevel);

        _infoLabel.Text = $"Этаж {floor}   •   Атака {player.Attack}   •   Защита {player.Defense}   •   Побеждено {kills}";
        _goldLabel.Text = $"Золото {player.Gold}   •   Сумка {player.Inventory.Items.Count}/{Inventory.Capacity}";

        UpdateSkills(player);

        var effects = new List<string>();
        if (player.StrengthTurns > 0)
            effects.Add($"Сила ({player.StrengthTurns})");
        if (player.IsInvisible)
            effects.Add($"Невидимость ({player.InvisibleTurns})");
        _effectsLabel.Text = effects.Count > 0 ? string.Join("   ", effects) : "";
        _effectsLabel.Visible = effects.Count > 0;

        UpdateHotbar(player.Inventory);
    }

    /// <summary>
    /// Способности — «плашки»: готовая обведена цветом класса, перезаряжающаяся — бледная
    /// и с числом ходов до готовности.
    /// </summary>
    private void UpdateSkills(Player player)
    {
        Clear(_skills);
        for (int i = 0; i < player.Class.Skills.Length; i++)
        {
            var skill = player.Class.Skills[i];
            int cooldown = player.SkillCooldowns[i];
            bool ready = cooldown == 0;

            var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            var style = UiTheme.Slot(ready ? player.Class.Color : new Color(1f, 1f, 1f, 0.1f), ready ? 0.1f : 0.03f);
            style.SetContentMarginAll(5f);
            style.ContentMarginLeft = style.ContentMarginRight = 10f;
            chip.AddThemeStyleboxOverride("panel", style);

            string key = i == 0 ? "F" : "G";
            var label = Ui.CreateLabel(14, ready ? $"{key}  {skill.Name}" : $"{key}  {skill.Name}  {cooldown}");
            label.AddThemeColorOverride("font_color", ready ? UiTheme.Text : UiTheme.TextMuted);
            chip.AddChild(label);
            _skills.AddChild(chip);
        }
    }

    /// <summary>Пять быстрых слотов: первые пять ячеек сумки, срабатывают клавишами 1–5.</summary>
    private void UpdateHotbar(Inventory inventory)
    {
        Clear(_hotbar);
        for (int i = 0; i < 5; i++)
        {
            var item = i < inventory.Items.Count ? inventory.Items[i] : null;
            var border = item == null ? new Color(1f, 1f, 1f, 0.08f)
                : item.Rarity == Rarity.Common ? new Color(1f, 1f, 1f, 0.2f)
                : item.RarityColor;
            var style = UiTheme.Slot(border, 0f, item?.Rarity > Rarity.Common ? 2 : 1);
            style.BgColor = new Color(0.06f, 0.07f, 0.1f, 0.6f);

            var slot = new PanelContainer { CustomMinimumSize = new Vector2(68f, 68f), MouseFilter = Control.MouseFilterEnum.Ignore };
            slot.AddThemeStyleboxOverride("panel", style);
            _hotbar.AddChild(slot);

            if (item != null)
            {
                slot.AddChild(new TextureRect
                {
                    Texture = ItemIcons.Get(item, 48),
                    StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }

            // Номер слота — в левом верхнем углу, количество — в правом нижнем (поверх иконки)
            var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            slot.AddChild(overlay);
            var number = Ui.CreateMutedLabel(12, $"{i + 1}");
            number.Position = new Vector2(-1f, -4f);
            overlay.AddChild(number);
            if (item is { Count: > 1 })
            {
                var count = Ui.CreateLabel(13, $"×{item.Count}");
                count.Position = new Vector2(30f, 36f);
                overlay.AddChild(count);
            }
        }
    }

    private static void Clear(Node container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>Показывает полоску босса, если он есть и уже заметил игрока.</summary>
    public void UpdateBoss(Enemy boss)
    {
        _bossBox.Visible = boss != null && boss.Alerted && !boss.IsDead;
        if (!_bossBox.Visible)
            return;
        _bossLabel.Text = $"{boss.DisplayName}   {boss.Hp} / {boss.MaxHp}";
        SetBar(_bossBar, boss.Hp, boss.MaxHp);
    }

    /// <summary>
    /// Компактные карточки остальных героев отряда: имя цветом класса, уровень, полоска здоровья
    /// и пометка «призрак» или «сходил». В одиночной игре блок скрыт.
    /// </summary>
    public void UpdateParty(List<Player> heroes, Player local)
    {
        Clear(_partyBox);
        var others = heroes.Where(hero => hero != local).ToList();
        _partyBox.Visible = others.Count > 0;
        foreach (var hero in others)
        {
            var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            card.AddThemeStyleboxOverride("panel", UiTheme.Glass(0.45f, 10, 8f));
            _partyBox.AddChild(card);

            var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            rows.AddThemeConstantOverride("separation", 3);
            card.AddChild(rows);

            string status = hero.IsGhost ? "  •  призрак" : hero.HasActed ? "  •  сходил" : "";
            var name = Ui.CreateLabel(14, $"{hero.DisplayName}  •  ур. {hero.Level}{status}");
            name.AddThemeColorOverride("font_color", hero.IsGhost ? UiTheme.TextMuted : hero.Class.Color.Lightened(0.35f));
            rows.AddChild(name);

            var bar = CreateBar(200f, 6f, hero.IsGhost ? new Color(1f, 1f, 1f, 0.15f) : HealthColor);
            bar.MaxValue = hero.MaxHp;
            bar.Value = hero.IsGhost ? 0 : hero.Hp;
            rows.AddChild(bar);
        }
    }

    /// <summary>Строка «Ждём: …» — кто из отряда ещё не сделал ход в этом раунде.</summary>
    public void SetWaiting(List<string> names)
    {
        _waitingLabel.Visible = names is { Count: > 0 };
        if (_waitingLabel.Visible)
            _waitingLabel.Text = $"Ждём: {string.Join(", ", names)}";
    }

    /// <summary>Полоска плавно доезжает до нового значения, а не прыгает к нему.</summary>
    private void SetBar(ProgressBar bar, double value, double max)
    {
        bar.MaxValue = max;
        _barTweens.TryGetValue(bar, out var running);
        running?.Kill();
        if (Settings.AnimationScale == 0f || !Effects.Enabled)
        {
            bar.Value = value;
            return;
        }
        var tween = CreateTween();
        tween.TweenProperty(bar, "value", value, 0.25f * Settings.AnimationScale)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _barTweens[bar] = tween;
    }

    /// <summary>Переход на новый этаж: экран появляется из темноты.</summary>
    public void FadeFromBlack()
    {
        if (Settings.AnimationScale == 0f || !Effects.Enabled)
            return;
        _fadeTween?.Kill();
        _fade.Modulate = Colors.White;
        _fade.Visible = true;
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(_fade, "modulate:a", 0f, 0.45f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        _fadeTween.TweenCallback(Callable.From(() => _fade.Visible = false));
    }

    /// <summary>Строка «К лестнице: …» — отряд собирается спускаться, а кто-то ещё далеко.</summary>
    public void SetStairsWaiting(List<string> names)
    {
        _stairsLabel.Visible = names != null;
        if (names != null)
            _stairsLabel.Text = names.Count > 0
                ? $"Спуск по лестнице — когда подойдут: {string.Join(", ", names)}"
                : "Отряд у лестницы";
    }

    /// <summary>Карточка текущего шага обучения: появляется только на уровне обучения.</summary>
    private void CreateTutorialCard(Control root)
    {
        _tutorialCard = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            OffsetLeft = -300f, OffsetRight = 300f, OffsetTop = 20f,
            GrowHorizontal = Control.GrowDirection.Both,
            Visible = false,
        };
        _tutorialCard.AddThemeStyleboxOverride("panel", UiTheme.Glass(0.8f, 14, 16f));
        root.AddChild(_tutorialCard);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        _tutorialCard.AddChild(rows);

        _tutorialProgress = Ui.CreateMutedLabel(14);
        rows.AddChild(_tutorialProgress);
        _tutorialTitle = Ui.CreateLabel(24);
        rows.AddChild(_tutorialTitle);
        _tutorialText = Ui.CreateWrappedLabel(17, 560f);
        rows.AddChild(_tutorialText);

        var skip = Ui.CreateButton("Пропустить обучение (Tab)", () => TutorialSkipRequested?.Invoke());
        skip.FocusMode = Control.FocusModeEnum.None; // иначе Пробел («ждать») нажимал бы эту кнопку
        skip.CustomMinimumSize = new Vector2(300f, 38f);
        skip.AddThemeFontSizeOverride("font_size", 16);
        skip.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        rows.AddChild(skip);
    }

    public void ShowTutorial(int step, int total, string title, string text)
    {
        _tutorialProgress.Text = $"Обучение  •  шаг {step} из {total}";
        _tutorialTitle.Text = title;
        _tutorialText.Text = text;
        _tutorialCard.Visible = true;
    }

    public void HideTutorial() => _tutorialCard.Visible = false;

    public bool IsChatOpen => _chatEdit.Visible;

    public void OpenChat()
    {
        _chatEdit.Text = "";
        _chatEdit.Visible = true;
        _chatEdit.CallDeferred(Control.MethodName.GrabFocus); // после этого кадра — иначе сам Enter попадёт в поле
    }

    public void CloseChat()
    {
        _chatEdit.Visible = false;
        _chatEdit.ReleaseFocus();
    }

    /// <summary>Метка союзника на мини-карте (мигает несколько секунд).</summary>
    public void AddPing(Vector2I cell, Color color) => _minimap.AddPing(cell, color);

    public void UpdateMinimap(DungeonMap map, HashSet<Vector2I> visible, Vector2I player,
        List<Vector2I> visibleEnemies, List<(Vector2I Cell, Color Color)> items, List<(Vector2I Cell, Color Color)> allies) =>
        _minimap.UpdateData(map, visible, player, visibleEnemies, items, allies);

    public void ToggleMinimap() => _minimapCard.Visible = !_minimapCard.Visible;

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

    public void ClearMessages() => Clear(_log);

    public void ShowGameOver(string text)
    {
        _gameOverLabel.Text = text;
        _gameOverPanel.Visible = true;
    }

    public void HideGameOver() => _gameOverPanel.Visible = false;
}
