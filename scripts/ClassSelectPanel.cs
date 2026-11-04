using System;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Выбор класса перед новым забегом: три карточки рядом.
/// Карточка — кнопка с подробным текстом, поэтому работает и мышью, и стрелками с Enter.
/// После выбора карточки показывается экран подтверждения, чтобы случайный клик не начал забег.
/// </summary>
public partial class ClassSelectPanel : CanvasLayer
{
    public event Action<ClassDef, DifficultyDef> ClassChosen;

    private Button _firstCard;
    private HBoxContainer _difficultyRow;
    private readonly System.Collections.Generic.List<SubViewport> _portraits = new();
    private readonly System.Collections.Generic.Dictionary<Button, string> _cardTexts = new();
    private readonly System.Collections.Generic.Dictionary<ClassDef, Button> _cards = new();
    private DifficultyDef _difficulty = Difficulties.Normal;

    // Два «экрана» внутри одного окна: выбор героя и подтверждение
    private ColorRect _selectScreen;
    private ColorRect _confirmScreen;
    private Label _confirmText;
    private Button _confirmButton;
    private ClassDef _pendingClass;
    private Button _pendingCard;

    /// <summary>Сейчас открыт экран подтверждения (Esc должен вернуть к карточкам, а не в меню).</summary>
    public bool IsConfirming => _confirmScreen.Visible;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.6f, out _selectScreen);

        var title = Ui.CreateLabel(44, "Выбери героя");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(title);

        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 20);
        column.AddChild(cards);

        foreach (var playerClass in ClassCatalog.All)
        {
            var card = CreateCard(playerClass);
            cards.AddChild(card);
            _cards[playerClass] = card;
            _firstCard ??= card;
        }

        // Сложность: три кнопки-переключателя. ButtonGroup делает так, что нажата всегда ровно одна
        var difficultyRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _difficultyRow = difficultyRow;
        difficultyRow.AddThemeConstantOverride("separation", 12);
        column.AddChild(difficultyRow);
        difficultyRow.AddChild(Ui.CreateLabel(20, "Сложность:"));
        var group = new ButtonGroup();
        foreach (var difficulty in Difficulties.All)
        {
            var toggle = new Button
            {
                Text = difficulty.Name,
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = difficulty == _difficulty,
                CustomMinimumSize = new Vector2(150f, 40f),
            };
            toggle.AddThemeFontSizeOverride("font_size", 18);
            toggle.Pressed += () => _difficulty = difficulty;
            difficultyRow.AddChild(toggle);
        }

        var hint = Ui.CreateLabel(15, "Выбери сложность, затем героя (клик или стрелки и Enter)  •  Esc — назад");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Modulate = new Color(1f, 1f, 1f, 0.6f);
        column.AddChild(hint);

        CreateConfirmScreen();
    }

    private void CreateConfirmScreen()
    {
        var column = Ui.CreateCenteredScreen(this, 0.75f, out _confirmScreen);
        _confirmScreen.Visible = false;

        var title = Ui.CreateLabel(40, "Начать забег?");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(title);

        _confirmText = Ui.CreateLabel(22);
        _confirmText.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_confirmText);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });

        _confirmButton = Ui.CreateButton("Начать", () => ClassChosen?.Invoke(_pendingClass, _difficulty));
        column.AddChild(_confirmButton);
        column.AddChild(Ui.CreateButton("Назад к выбору", CancelConfirm));
    }

    /// <summary>
    /// Портрет — настоящая модель героя. SubViewport рисует отдельный маленький мир со своей камерой
    /// и светом, а его картинка становится иконкой кнопки, поэтому герой дышит прямо на карточке.
    /// Создаём при первом открытии: в проверках и симуляциях это окно не нужно.
    /// </summary>
    private void CreatePortraits()
    {
        if (_portraits.Count > 0)
            return;
        foreach (var (playerClass, card) in _cards)
        {
            var viewport = new SubViewport
            {
                Size = new Vector2I(190, 170),
                OwnWorld3D = true,
                TransparentBg = true,
                Msaa3D = Viewport.Msaa.Msaa4X,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            AddChild(viewport);

            var hero = new Player { IsLocal = false, IsBot = true, RotationDegrees = new Vector3(0f, -22f, 0f) };
            viewport.AddChild(hero);
            hero.ResetStats(playerClass);
            hero.SetHighQuality(false);
            hero.SetTorch(false); // свет портрета свой, факел здесь пересвечивал бы модель

            var camera = new Camera3D
            {
                Fov = 34f,
                Position = new Vector3(0f, 0.95f, 2.75f),
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.ClearColor,
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = new Color(0.75f, 0.78f, 0.9f),
                    AmbientLightEnergy = 0.35f,
                    TonemapMode = Godot.Environment.ToneMapper.Aces,
                    TonemapExposure = 1f,
                },
            };
            viewport.AddChild(camera);
            camera.LookAt(new Vector3(0f, 0.62f, 0f));
            viewport.AddChild(new DirectionalLight3D { LightEnergy = 0.8f, RotationDegrees = new Vector3(-35f, -30f, 0f) });
            viewport.AddChild(new DirectionalLight3D { LightColor = playerClass.Color, LightEnergy = 1.4f, RotationDegrees = new Vector3(-15f, 150f, 0f) });

            card.Icon = viewport.GetTexture();
            card.ExpandIcon = false;
            card.VerticalIconAlignment = VerticalAlignment.Top;
            card.IconAlignment = HorizontalAlignment.Center;
            _portraits.Add(viewport);
        }
    }

    /// <summary>Закрытое окно не тратит видеокарту: портреты рисуются, только пока видны.</summary>
    public override void _Process(double delta)
    {
        var mode = Visible && _selectScreen.Visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        foreach (var viewport in _portraits)
            viewport.RenderTargetUpdateMode = mode;
    }

    /// <summary>При создании мира сложность уже выбрана, ряд с ней не нужен.</summary>
    public bool ShowDifficulty { set => _difficultyRow.Visible = value; }

    public string ConfirmText { set => _confirmButton.Text = value; }

    public void Open()
    {
        CreatePortraits();        // Закрытый класс виден, чтобы было к чему стремиться, но выбрать его нельзя
        foreach (var (playerClass, card) in _cards)
        {
            bool locked = playerClass.Unlock != null && !Worlds.LoadUnlocks().Contains(playerClass.Unlock);
            card.Disabled = locked;
            card.Modulate = locked ? new Color(1f, 1f, 1f, 0.6f) : Colors.White;
            if (!_cardTexts.ContainsKey(card))
                _cardTexts[card] = card.Text;
            string text = _cardTexts[card].Replace("[1]", KeyBindings.Name(GameKey.Skill1)).Replace("[2]", KeyBindings.Name(GameKey.Skill2));
            card.Text = locked ? $"ЗАКРЫТО\n{Profile.AchievementDescription(playerClass.Unlock)}\n\n{text}" : text;
        }
        Visible = true;        _selectScreen.Visible = true;
        _confirmScreen.Visible = false;
        Ui.FocusLater(_firstCard);
    }

    /// <summary>Показать подтверждение для выбранного класса (вызывается кнопкой-карточкой).</summary>
    public void AskConfirm(ClassDef playerClass, Button card = null)
    {
        _pendingClass = playerClass;
        _pendingCard = card;
        _confirmText.Text = $"{playerClass.Name}  •  сложность: {_difficulty.Name.ToLower()}";
        _confirmText.AddThemeColorOverride("font_color", playerClass.Color.Lightened(0.4f));

        // Прячем карточки целиком, чтобы стрелки не уводили фокус на них из окна подтверждения
        _selectScreen.Visible = false;
        _confirmScreen.Visible = true;
        Ui.FocusLater(_confirmButton);
    }

    /// <summary>Вернуться к карточкам; фокус — на ту, которую выбирали.</summary>
    public void CancelConfirm()
    {
        _confirmScreen.Visible = false;
        _selectScreen.Visible = true;
        Ui.FocusLater(_pendingCard ?? _firstCard);
    }

    private Button CreateCard(ClassDef playerClass)
    {
        var start = string.Join(", ", playerClass.StartingItems.Select(item => item.Name).Distinct());
        var skills = string.Join("\n", playerClass.Skills.Select((skill, i) =>
            $"{(i == 0 ? "[1]" : "[2]")} — {skill.Name}: {skill.Description} ({(skill.Cooldown == 0 ? "без зарядки" : $"заряжается за {ClassCatalog.Attacks(skill.Cooldown)}")})"));

        var card = new Button
        {
            CustomMinimumSize = new Vector2(208f, 380f),
            Text = $"{playerClass.Name}\n\n{playerClass.Description}\n\n" +
                   $"Здоровье: {playerClass.MaxHp}   Атака: {playerClass.Attack}\n" +
                   $"С собой: {start}\n\n{skills}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Alignment = HorizontalAlignment.Left,
            VerticalIconAlignment = VerticalAlignment.Top,
        };
        card.AddThemeFontSizeOverride("font_size", 14);
        card.AddThemeColorOverride("font_color", playerClass.Color.Lightened(0.35f));
        card.AddThemeColorOverride("font_focus_color", playerClass.Color.Lightened(0.6f));
        card.AddThemeColorOverride("font_hover_color", playerClass.Color.Lightened(0.6f));
        card.Pressed += () => AskConfirm(playerClass, card);
        card.MouseEntered += () => card.GrabFocus();
        return card;
    }
}
